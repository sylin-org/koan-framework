use axum::{
    extract::{Path, State},
    http::{HeaderMap, StatusCode},
    Json,
};
use crate::api::responses::{CreateServiceRequest, ServiceActionResponse, ApiResponse};
use crate::api::suggestions::{generate_suggestions, SuggestionContext};
use crate::{error_response, AppState};
use garden_common::{
    api_utils::{ApiErrorResponse, sanitize_query, sanitize_name, sanitize_tag, is_suspicious},
    ServiceInfo, ServiceStatus,
};

/// Query parameters for GET /api/v1/services
///
/// Unified endpoint behavior:
/// - No params: lists all local services (fast, local-only)
/// - With params: searches/filters across garden (may query remote stones)
///
/// Query parameters:
/// - `q`: Search query (supports prefixes: c:, cat:, category:, t:, tag:, tags:)
/// - `name`: Search by exact service name
/// - `category`: Search by category
/// - `tag`: Search by tag
/// - `fresh`: Force fresh discovery (bypass cache)
#[derive(Debug, serde::Deserialize)]
pub struct ServicesQuery {
    /// Search query with optional prefix
    #[serde(default)]
    pub q: Option<String>,

    /// Filter by name
    #[serde(default)]
    pub name: Option<String>,

    /// Filter by category
    #[serde(default)]
    pub category: Option<String>,

    /// Filter by tag
    #[serde(default)]
    pub tag: Option<String>,

    /// Force fresh discovery
    #[serde(default)]
    pub fresh: bool,
}

impl ServicesQuery {
    /// Check if any search/filter params are provided
    fn has_search_params(&self) -> bool {
        self.q.is_some() || self.name.is_some() || self.category.is_some() || self.tag.is_some()
    }
}

/// GET /api/v1/services - List or search services
///
/// Unified endpoint:
/// - No params: returns all local services (backward compatible with list)
/// - With ?q=, ?name=, etc.: searches/filters across garden (replaces /find)
///
/// Response: ServiceDiscoveryResponse with found services
pub async fn list_services_v1(
    State(state): State<AppState>,
    axum::extract::Query(query): axum::extract::Query<ServicesQuery>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<crate::domain::ServiceDiscoveryResponse>>, (StatusCode, Json<ApiErrorResponse>)> {
    use crate::domain::{ServiceSearchCriteria, find_services, list_all_local_services};

    tracing::debug!(
        q = ?query.q,
        name = ?query.name,
        category = ?query.category,
        tag = ?query.tag,
        fresh = query.fresh,
        has_params = query.has_search_params(),
        "list_services_v1: unified handler invoked"
    );

    // Sanitize and validate inputs - reject suspicious patterns
    if let Some(ref q) = query.q {
        if is_suspicious(q) {
            tracing::warn!(query = %q, "Suspicious query pattern detected");
            return Err(error_response(
                StatusCode::BAD_REQUEST,
                "INVALID_QUERY",
                "Query contains invalid patterns".to_string(),
                None,
            ));
        }
    }

    let response = if query.has_search_params() {
        // Search mode: filter/search across garden
        let criteria = if let Some(ref q) = query.q {
            let sanitized = sanitize_query(q).into_value();
            ServiceSearchCriteria::parse(&sanitized)
        } else if let Some(ref name) = query.name {
            let sanitized = sanitize_name(name).into_value();
            ServiceSearchCriteria::by_name(&sanitized)
        } else if let Some(ref category) = query.category {
            let sanitized = sanitize_tag(category).into_value();
            ServiceSearchCriteria::by_category(&sanitized)
        } else if let Some(ref tag) = query.tag {
            let sanitized = sanitize_tag(tag).into_value();
            ServiceSearchCriteria::by_tag(&sanitized)
        } else {
            unreachable!("has_search_params() returned true but no params found")
        };

        find_services(&criteria, &state, query.fresh).await
    } else {
        // List mode: return all local services
        list_all_local_services(&state).await
    };

    let ctx = SuggestionContext::from_headers(&headers, "list_services");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: response,
        suggestions,
    }))
}

/// GET /api/v1/services/:service - Get specific service
pub async fn get_service_v1(
    State(state): State<AppState>,
    Path(service): Path<String>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<ServiceInfo>>, (StatusCode, Json<ApiErrorResponse>)> {
    tracing::debug!(
        service = %service,
        "get_service_v1: handler invoked for /api/v1/services/:service"
    );

    let registry = state.registry.read().await;
    let service_info = registry
        .iter()
        .find(|s| s.name == service)
        .cloned()
        .ok_or_else(|| {
            tracing::warn!(
                service = %service,
                "get_service_v1: service not found in registry"
            );
            error_response(
                StatusCode::NOT_FOUND,
                "SERVICE_NOT_FOUND",
                format!("Service '{}' not found", service),
                None,
            )
        })?;
    drop(registry);

    let ctx = SuggestionContext::from_headers(&headers, "get_service");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: service_info,
        suggestions,
    }))
}

/// POST /api/v1/services - Create service (zen: offer)
pub async fn create_service_v1(
    State(state): State<AppState>,
    headers: HeaderMap,
    Json(payload): Json<CreateServiceRequest>,
) -> Result<Json<ApiResponse<ServiceActionResponse>>, (StatusCode, Json<ApiErrorResponse>)> {
    let offering = payload.offering.clone();

    // Self-heal: if the container exists but registry forgot it (e.g. after restart), adopt it.
    if state
        .docker
        .zen_container_exists(&offering)
        .await
        .unwrap_or(false)
    {
        let in_registry = {
            let reg = state.registry.read().await;
            reg.iter().any(|s| s.name == offering)
        };

        if !in_registry {
            if let Ok(Some(info)) = crate::adopt_offering_container(&state.docker, &state.templates, &offering).await {
                let mut reg = state.registry.write().await;
                reg.push(info);
                drop(reg);
                let _ = state.persist_registry().await;

                let ctx = SuggestionContext::from_headers(&headers, "create_service");
                let suggestions = generate_suggestions(&ctx);

                return Ok(Json(ApiResponse {
                    data: ServiceActionResponse {
                        service: offering,
                        action: "create".to_string(),
                        status: "adopted".to_string(),
                        message: "Existing container adopted into registry".to_string(),
                    },
                    suggestions,
                }));
            }
        }
    }

    let compiled = match crate::get_compiled_offering(&state, &offering).await {
        Ok(Some(o)) => o,
        Ok(None) => {
            return Err(error_response(
                StatusCode::NOT_FOUND,
                "TEMPLATE_NOT_FOUND",
                format!("Unknown offering: {}", offering),
                None,
            ));
        }
        Err(e) => {
            return Err(error_response(
                StatusCode::INTERNAL_SERVER_ERROR,
                "INTERNAL_ERROR",
                format!("Failed to read offerings index: {}", e),
                None,
            ));
        }
    };

    if compiled.compatibility.decision == garden_common::COMPAT_FAIL {
        let reason = compiled.compatibility.reason.unwrap_or_else(|| "Unknown reason".to_string());
        return Err(error_response(
            StatusCode::BAD_REQUEST,
            "COMPATIBILITY_FAILED",
            format!("Offering is incompatible with this stone: {}", reason),
            None,
        ));
    }

    // Check if already running/maintenance
    let registry = state.registry.read().await;
    if let Some(existing) = registry.iter().find(|svc| svc.name == offering) {
        if existing.status == ServiceStatus::Maintenance {
            drop(registry);
            let ctx = SuggestionContext::from_headers(&headers, "create_service");
            let suggestions = generate_suggestions(&ctx);
            return Ok(Json(ApiResponse {
                data: ServiceActionResponse {
                    service: offering,
                    action: "create".to_string(),
                    status: "maintenance".to_string(),
                    message: "Service under maintenance, retry later".to_string(),
                },
                suggestions,
            }));
        }
    }
    drop(registry);

    // Create job
    let job_id = uuid::Uuid::now_v7().to_string();
    let job = crate::Job {
        id: job_id.clone(),
        offerings: vec![offering.clone()],
        status: crate::JobStatus::Pending,
        completed: vec![],
        failed: std::collections::HashMap::new(),
        started_at: std::time::SystemTime::now(),
        completed_at: None,
    };

    state.jobs.write().await.insert(job_id.clone(), job);

    // Spawn async installation task
    let state_clone = state.clone();
    let offering_clone = offering.clone();
    let job_id_clone = job_id.clone();
    tokio::spawn(async move {
        crate::install_service_task(&state_clone, &job_id_clone, &offering_clone).await;
    });

    let ctx = SuggestionContext::from_headers(&headers, "create_service");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: ServiceActionResponse {
            service: offering,
            action: "create".to_string(),
            status: "accepted".to_string(),
            message: format!("Installation started, check /api/jobs/{} for status", job_id),
        },
        suggestions,
    }))
}

/// POST /api/v1/services/:service/rest - Rest (stop) service
pub async fn rest_service_v1(
    State(state): State<AppState>,
    Path(service): Path<String>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<ServiceActionResponse>>, (StatusCode, Json<ApiErrorResponse>)> {
    let mut registry = state.registry.write().await;
    
    let service_info = registry
        .iter_mut()
        .find(|s| s.name == service)
        .ok_or_else(|| {
            error_response(
                StatusCode::NOT_FOUND,
                "SERVICE_NOT_FOUND",
                format!("Service '{}' not found", service),
                None,
            )
        })?;

    // Stop the Docker container
    if let Err(e) = state.docker.stop_service(&service, Some(&state.console)).await {
        tracing::error!(error = ?e, service = %service, "Failed to stop container");
        return Err(error_response(
            StatusCode::INTERNAL_SERVER_ERROR,
            "STOP_FAILED",
            format!("Failed to stop service: {}", e),
            None,
        ));
    }

    service_info.status = ServiceStatus::Stopped;
    drop(registry);

    if let Err(e) = state.persist_registry().await {
        tracing::warn!(error = ?e, "Failed to persist registry after rest");
    }

    let ctx = SuggestionContext::from_headers(&headers, "rest_service");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: ServiceActionResponse {
            service,
            action: "rest".to_string(),
            status: "stopped".to_string(),
            message: "Service stopped successfully".to_string(),
        },
        suggestions,
    }))
}

/// POST /api/v1/services/:service/wake - Wake (start) service
pub async fn wake_service_v1(
    State(state): State<AppState>,
    Path(service): Path<String>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<ServiceActionResponse>>, (StatusCode, Json<ApiErrorResponse>)> {
    let mut registry = state.registry.write().await;
    
    let service_info = registry
        .iter_mut()
        .find(|s| s.name == service)
        .ok_or_else(|| {
            error_response(
                StatusCode::NOT_FOUND,
                "SERVICE_NOT_FOUND",
                format!("Service '{}' not found", service),
                None,
            )
        })?;

    // Start the Docker container
    if let Err(e) = state.docker.start_service(&service, Some(&state.console)).await {
        tracing::error!(error = ?e, service = %service, "Failed to start container");
        return Err(error_response(
            StatusCode::INTERNAL_SERVER_ERROR,
            "START_FAILED",
            format!("Failed to start service: {}", e),
            None,
        ));
    }

    service_info.status = ServiceStatus::Running;
    drop(registry);

    if let Err(e) = state.persist_registry().await {
        tracing::warn!(error = ?e, "Failed to persist registry after wake");
    }

    let ctx = SuggestionContext::from_headers(&headers, "wake_service");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: ServiceActionResponse {
            service,
            action: "wake".to_string(),
            status: "running".to_string(),
            message: "Service started successfully".to_string(),
        },
        suggestions,
    }))
}

/// POST /api/v1/services/:service/nourish - Nourish (upgrade) service
pub async fn nourish_service_v1(
    State(state): State<AppState>,
    Path(service): Path<String>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<ServiceActionResponse>>, (StatusCode, Json<ApiErrorResponse>)> {
    let service_name = service.clone();
    let mut registry = state.registry.write().await;

    let svc = registry
        .iter_mut()
        .find(|s| s.name == service_name)
        .ok_or_else(|| {
            error_response(
                StatusCode::NOT_FOUND,
                "SERVICE_NOT_FOUND",
                format!("Service '{}' not found", service_name),
                None,
            )
        })?;

    if svc.status == ServiceStatus::Maintenance {
        let ctx = SuggestionContext::from_headers(&headers, "nourish_service");
        let suggestions = generate_suggestions(&ctx);
        return Ok(Json(ApiResponse {
            data: ServiceActionResponse {
                service: service_name,
                action: "nourish".to_string(),
                status: "maintenance".to_string(),
                message: "Service under maintenance, retry later".to_string(),
            },
            suggestions,
        }));
    }

    svc.status = ServiceStatus::Maintenance;
    let offering = svc.offering.clone();
    drop(registry);

    // Load template for upgrade
    let template = state.templates.load(&offering).map_err(|e| {
        // Restore status on error
        let state_clone = state.clone();
        let service_clone = service_name.clone();
        tokio::spawn(async move {
            let mut registry = state_clone.registry.write().await;
            if let Some(svc) = registry.iter_mut().find(|s| s.name == service_clone) {
                svc.status = ServiceStatus::Running;
            }
        });
        error_response(
            StatusCode::INTERNAL_SERVER_ERROR,
            "TEMPLATE_LOAD_FAILED",
            format!("Failed to load template: {}", e),
            None,
        )
    })?;

    // Perform Docker upgrade
    if let Err(e) = state
        .docker
        .upgrade_service(
            &service_name,
            &template.image,
            template.ports,
            template.environment,
            template.volumes,
            Some(&state.console),
        )
        .await
    {
        tracing::error!(error = ?e, service = %service_name, "Docker upgrade failed");
        let mut registry = state.registry.write().await;
        if let Some(svc) = registry.iter_mut().find(|s| s.name == service_name) {
            svc.status = ServiceStatus::Running;
        }
        return Err(error_response(
            StatusCode::INTERNAL_SERVER_ERROR,
            "UPGRADE_FAILED",
            format!("Failed to upgrade: {}", e),
            None,
        ));
    }

    let mut registry = state.registry.write().await;
    if let Some(svc) = registry.iter_mut().find(|s| s.name == service_name) {
        svc.status = ServiceStatus::Running;
        svc.version = template.image.split(':').next_back().unwrap_or("latest").into();
    }

    drop(registry);

    if let Err(e) = state.persist_registry().await {
        tracing::warn!(error = ?e, "Failed to persist registry after nourish");
    }

    let ctx = SuggestionContext::from_headers(&headers, "nourish_service");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: ServiceActionResponse {
            service: service_name,
            action: "nourish".to_string(),
            status: "upgraded".to_string(),
            message: "Service upgraded successfully".to_string(),
        },
        suggestions,
    }))
}

/// DELETE /api/v1/services/:service - Soft delete (remove from registry, preserve container)
/// The container becomes a "stray" that can be re-adopted later.
/// Use POST /api/v1/services/:service/destroy for hard delete (uproot).
pub async fn delete_service_v1(
    State(state): State<AppState>,
    Path(service): Path<String>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<ServiceActionResponse>>, (StatusCode, Json<ApiErrorResponse>)> {
    let mut registry = state.registry.write().await;

    let pos = registry
        .iter()
        .position(|svc| svc.name == service)
        .ok_or_else(|| {
            error_response(
                StatusCode::NOT_FOUND,
                "SERVICE_NOT_FOUND",
                format!("Service '{}' not found", service),
                None,
            )
        })?;

    // Soft delete: remove from registry only, container remains (becomes stray)
    registry.remove(pos);
    drop(registry);

    if let Err(e) = state.persist_registry().await {
        tracing::warn!(error = ?e, "Failed to persist registry after delete");
    }

    let ctx = SuggestionContext::from_headers(&headers, "delete_service");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: ServiceActionResponse {
            service,
            action: "delete".to_string(),
            status: "removed".to_string(),
            message: "Service removed from registry (container preserved as stray)".to_string(),
        },
        suggestions,
    }))
}

/// POST /api/v1/services/:service/destroy - Hard delete (uproot: remove from registry AND destroy container)
pub async fn destroy_service_v1(
    State(state): State<AppState>,
    Path(service): Path<String>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<ServiceActionResponse>>, (StatusCode, Json<ApiErrorResponse>)> {
    let mut registry = state.registry.write().await;

    let pos = registry
        .iter()
        .position(|svc| svc.name == service)
        .ok_or_else(|| {
            error_response(
                StatusCode::NOT_FOUND,
                "SERVICE_NOT_FOUND",
                format!("Service '{}' not found", service),
                None,
            )
        })?;

    // Hard delete: destroy Docker container first
    if let Err(e) = state.docker.remove_service(&service, Some(&state.console)).await {
        tracing::error!(error = ?e, service = %service, "Docker remove failed");
        return Err(error_response(
            StatusCode::INTERNAL_SERVER_ERROR,
            "DESTROY_FAILED",
            format!("Failed to destroy service container: {}", e),
            None,
        ));
    }

    // Then remove from registry
    registry.remove(pos);
    drop(registry);

    if let Err(e) = state.persist_registry().await {
        tracing::warn!(error = ?e, "Failed to persist registry after destroy");
    }

    let ctx = SuggestionContext::from_headers(&headers, "destroy_service");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: ServiceActionResponse {
            service,
            action: "destroy".to_string(),
            status: "uprooted".to_string(),
            message: "Service destroyed (container removed)".to_string(),
        },
        suggestions,
    }))
}

// ============================================================================
// Services API - Technical Layer (New Endpoints)
// ============================================================================

/// GET /api/v1/services/manifests - List all service manifests
pub async fn list_manifests_v1(
    State(state): State<AppState>,
) -> Result<(StatusCode, Json<ApiResponse<Vec<crate::templates::TemplateInfo>>>), (StatusCode, Json<ApiErrorResponse>)> {
    let manifests = state.templates.list_templates().map_err(|e| {
        error_response(
            StatusCode::INTERNAL_SERVER_ERROR,
            "MANIFEST_LIST_FAILED",
            format!("Failed to list manifests: {}", e),
            None,
        )
    })?;
    
    Ok((
        StatusCode::OK,
        Json(ApiResponse {
            data: manifests,
            suggestions: None,
        }),
    ))
}

/// GET /api/v1/services/:name/manifest - Get specific manifest YAML
pub async fn get_manifest_v1(
    State(state): State<AppState>,
    Path(name): Path<String>,
) -> Result<(StatusCode, String), (StatusCode, Json<ApiErrorResponse>)> {
    let content = state.templates.get_template_content(&name).map_err(|e| {
        error_response(
            StatusCode::NOT_FOUND,
            "MANIFEST_NOT_FOUND",
            format!("Manifest for '{}' not found: {}", name, e),
            None,
        )
    })?;
    
    Ok((StatusCode::OK, content))
}

/// GET /api/v1/services/:service/logs - Stream service logs (SSE)
pub async fn stream_service_logs_v1(
    Path(service): Path<String>,
    State(_state): State<AppState>,
) -> Result<axum::response::sse::Sse<impl futures_util::stream::Stream<Item = Result<axum::response::sse::Event, std::convert::Infallible>>>, (StatusCode, Json<ApiErrorResponse>)> {
    // TODO: Implement log streaming from Docker container
    use axum::response::sse::{Event, KeepAlive, Sse};
    use async_stream::stream;

    let log_stream = stream! {
        yield Ok(Event::default().data(format!("Log streaming for '{}' not yet implemented", service)));
    };

    Ok(Sse::new(log_stream).keep_alive(KeepAlive::default()))
}

/// POST /api/v1/services/:service:restart - Restart service
pub async fn restart_service_v1(
    State(state): State<AppState>,
    Path(service): Path<String>,
) -> Result<(StatusCode, Json<serde_json::Value>), (StatusCode, Json<ApiErrorResponse>)> {
    // Stop then start
    state.docker.stop_service(&service, Some(&state.console)).await.map_err(|e| {
        error_response(
            StatusCode::INTERNAL_SERVER_ERROR,
            "RESTART_FAILED",
            format!("Failed to stop service: {}", e),
            None,
        )
    })?;
    
    state.docker.start_service(&service, Some(&state.console)).await.map_err(|e| {
        error_response(
            StatusCode::INTERNAL_SERVER_ERROR,
            "RESTART_FAILED",
            format!("Failed to start service: {}", e),
            None,
        )
    })?;
    
    Ok((
        StatusCode::OK,
        Json(serde_json::json!({
            "service": service,
            "action": "restart",
            "status": "restarted",
            "message": "Service restarted successfully"
        })),
    ))
}

/// POST /api/v1/services/:service:cordon - Mark service unavailable
pub async fn cordon_service_v1(
    State(_state): State<AppState>,
    Path(service): Path<String>,
) -> (StatusCode, Json<serde_json::Value>) {
    // TODO: Implement cordon logic (mark in registry, update status)
    (
        StatusCode::NOT_IMPLEMENTED,
        Json(serde_json::json!({
            "error": "NOT_IMPLEMENTED",
            "message": "Cordon operation not yet implemented",
            "service": service
        })),
    )
}

/// POST /api/v1/services:reconcile - Reconcile container inventory
#[derive(Debug, serde::Deserialize)]
pub struct ReconcileRequest {
    #[serde(default)]
    pub drop_invalid: bool,
}

pub async fn reconcile_inventory_v1(
    State(state): State<AppState>,
    Json(payload): Json<ReconcileRequest>,
) -> Result<(StatusCode, Json<serde_json::Value>), (StatusCode, Json<ApiErrorResponse>)> {
    use crate::domain::reconcile_services;

    let result = reconcile_services(&state, payload.drop_invalid).await;

    // Persist changes if any adoptions or drops occurred
    if result.has_changes() {
        let _ = state.persist_registry().await;
    }

    Ok((
        StatusCode::OK,
        Json(serde_json::json!({
            "status": "ok",
            "adopted": result.adopted,
            "dropped_invalid": result.dropped_invalid,
            "skipped_existing": result.skipped_existing,
            "left_unregistered": result.left_unregistered,
            "error": result.error,
        })),
    ))
}

/// POST /api/v1/services:refresh - Refresh manifests catalog
pub async fn refresh_manifests_v1(
    State(state): State<AppState>,
) -> Result<(StatusCode, Json<serde_json::Value>), (StatusCode, Json<ApiErrorResponse>)> {
    // Rebuild offerings index (which includes manifest validation)
    crate::ensure_offerings_index(&state, true).await.map_err(|e| {
        error_response(
            StatusCode::INTERNAL_SERVER_ERROR,
            "REFRESH_FAILED",
            format!("Failed to refresh manifests: {}", e),
            None,
        )
    })?;
    
    let idx_guard = state.offerings_index.read().await;
    let idx = idx_guard.as_ref().ok_or_else(|| {
        error_response(
            StatusCode::INTERNAL_SERVER_ERROR,
            "INDEX_UNAVAILABLE",
            "Manifests index unavailable after refresh".to_string(),
            None,
        )
    })?;
    
    Ok((
        StatusCode::OK,
        Json(serde_json::json!({
            "status": "refreshed",
            "count": idx.offerings.len(),
            "fingerprint": idx.fingerprint,
            "generated_at": idx.generated_at
        })),
    ))
}
