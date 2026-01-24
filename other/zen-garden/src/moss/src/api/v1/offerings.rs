// Offerings API - Human Layer
// 
// Purpose: Simplified, beginner-friendly API for managing offerings
// Target audience: 90% of users - scripters, beginners, simple automation
// Philosophy: Hide Docker complexity, provide safety rails, optimize for common case

use axum::{
    extract::{Path, Query, State},
    http::StatusCode,
    Json,
};
use crate::api::responses::ApiResponse;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;

use crate::{error_codes, error_response, AppState};

/// Query parameters for filtering offerings
#[derive(Debug, Deserialize)]
pub struct OfferingsQuery {
    /// Filter by state: available, installing, installed
    #[serde(default)]
    state: Option<String>,
}

/// Simplified offering view for human layer
#[derive(Debug, Serialize)]
pub struct OfferingView {
    pub name: String,
    pub state: String,
    pub category: String,
    pub description: String,
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub tags: Vec<String>,
    pub image: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub compatibility: Option<CompatibilityView>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub health: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub uptime: Option<String>,
}

#[derive(Debug, Serialize)]
pub struct CompatibilityView {
    pub decision: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub reason: Option<String>,
}

/// GET /api/v1/offerings
/// List all offerings (available + installed), optionally filtered by state
pub async fn list_offerings_v1(
    State(state): State<AppState>,
    Query(query): Query<OfferingsQuery>,
) -> Result<(StatusCode, Json<ApiResponse<Vec<OfferingView>>>), (StatusCode, Json<garden_common::api_utils::ApiErrorResponse>)> {
    // Get installed services from registry
    let registry = state.registry.read().await;
    let installed: HashMap<String, &crate::ServiceInfo> = registry
        .iter()
        .map(|s| (s.name.clone(), s))
        .collect();
    
    // Get available offerings from index (may still be building)
    let idx_guard = state.offerings_index.read().await;
    let offerings_index = idx_guard.as_ref();
    let catalog_building = offerings_index.is_none();
    
    let mut offerings: Vec<OfferingView> = Vec::new();
    
    // Add installed offerings with runtime details
    if query.state.as_deref() != Some("available") {
        for service in registry.iter() {
            let image = state.docker.get_service_image(&service.name).await.unwrap_or_else(|_| "<unknown>".to_string());
            offerings.push(OfferingView {
                name: service.name.clone(),
                state: "installed".to_string(),
                category: service.offering.clone(),
                description: format!("{} service", service.offering),
                tags: vec![],
                image,
                compatibility: None,
                health: Some(simplify_health(&service.status)),
                uptime: None, // TODO: Track uptime in ServiceInfo
            });
        }
    }
    
    // Add available offerings (not yet installed) - only if catalog loaded
    if query.state.as_deref() != Some("installed") {
        if let Some(offerings_index) = offerings_index {
            for offering in &offerings_index.offerings {
                if !installed.contains_key(&offering.name) {
                    offerings.push(OfferingView {
                        name: offering.name.clone(),
                        state: "available".to_string(),
                        category: offering.category.clone(),
                        description: offering.description.clone(),
                        tags: offering.tags.clone(),
                        image: offering.image.clone(),
                        compatibility: Some(CompatibilityView {
                            decision: offering.compatibility.decision.to_string(),
                            reason: offering.compatibility.reason.clone(),
                        }),
                        health: None,
                        uptime: None,
                    });
                }
            }
        }
    }
    
    let suggestions = if catalog_building {
        Some(vec!["Catalog still building - available offerings may be incomplete".to_string()])
    } else {
        None
    };
    
    Ok((
        StatusCode::OK,
        Json(ApiResponse {
            data: offerings,
            suggestions,
        }),
    ))
}

/// GET /api/v1/offerings/:name
/// Get details about a specific offering
pub async fn get_offering_v1(
    State(state): State<AppState>,
    Path(name): Path<String>,
) -> Result<(StatusCode, Json<ApiResponse<serde_json::Value>>), (StatusCode, Json<garden_common::api_utils::ApiErrorResponse>)> {
    // Check if installed
    let registry = state.registry.read().await;
    if let Some(service) = registry.iter().find(|s| s.name == name) {
        return Ok((
            StatusCode::OK,
            Json(ApiResponse {
                data: serde_json::json!({
                    "name": service.name,
                    "state": "installed",
                    "category": service.offering,
                    "health": simplify_health(&service.status),
                    "version": service.version,
                }),
                suggestions: None,
            }),
        ));
    }
    
    // Check if available
    let idx_guard = state.offerings_index.read().await;
    let offerings_index = idx_guard.as_ref().ok_or_else(|| {
        error_response(
            StatusCode::SERVICE_UNAVAILABLE,
            "INDEX_UNAVAILABLE",
            "Offerings catalog not yet loaded".to_string(),
            None,
        )
    })?;
    
    if let Some(offering) = offerings_index.offerings.iter().find(|o| o.name == name) {
        return Ok((
            StatusCode::OK,
            Json(ApiResponse {
                data: serde_json::json!({
                    "name": offering.name,
                    "state": "available",
                    "category": offering.category,
                    "description": offering.description,
                    "tags": offering.tags,
                    "compatibility": {
                        "decision": offering.compatibility.decision.to_string(),
                        "reason": offering.compatibility.reason,
                    },
                }),
                suggestions: None,
            }),
        ));
    }
    
    // Not found
    let mut details = HashMap::new();
    details.insert("name".to_string(), serde_json::json!(name));
    Err(error_response(
        StatusCode::NOT_FOUND,
        error_codes::OFFERING_NOT_FOUND,
        format!("Offering '{}' not found in catalog", name),
        Some(details),
    ))
}

/// GET /api/v1/offerings/:name/manifest
/// Get raw YAML manifest for an offering
pub async fn get_offering_manifest_v1(
    State(state): State<AppState>,
    Path(name): Path<String>,
) -> Result<(StatusCode, String), (StatusCode, Json<garden_common::api_utils::ApiErrorResponse>)> {
    match state.templates.get_template_content(&name) {
        Ok(content) => Ok((StatusCode::OK, content)),
        Err(e) => {
            let mut details = HashMap::new();
            details.insert("name".to_string(), serde_json::json!(name));
            details.insert("error".to_string(), serde_json::json!(e.to_string()));
            Err(error_response(
                StatusCode::NOT_FOUND,
                error_codes::TEMPLATE_NOT_FOUND,
                format!("Manifest for '{}' not found", name),
                Some(details),
            ))
        }
    }
}

/// POST /api/v1/offerings
/// Plant an offering (simplified installation)
#[derive(Debug, Deserialize)]
pub struct PlantOfferingRequest {
    pub name: String,
    // Future: config field for environment overrides
}

pub async fn plant_offering_v1(
    State(_state): State<AppState>,
    Json(payload): Json<PlantOfferingRequest>,
) -> Result<(StatusCode, Json<serde_json::Value>), (StatusCode, Json<garden_common::api_utils::ApiErrorResponse>)> {
    // Forward to services API with simplified configuration
    // TODO: Transform simplified config to full service creation request
    tracing::info!(offering = %payload.name, "Planting offering (simplified)");
    
    let mut details = HashMap::new();
    details.insert("offering".to_string(), serde_json::json!(payload.name));
    Err(error_response(
        StatusCode::NOT_IMPLEMENTED,
        "NOT_IMPLEMENTED",
        "Offering planting not yet implemented - use POST /api/v1/services for now".to_string(),
        Some(details),
    ))
}

/// DELETE /api/v1/offerings/:name
/// Take away an offering (uninstall)
pub async fn take_away_offering_v1(
    State(state): State<AppState>,
    Path(name): Path<String>,
) -> Result<(StatusCode, Json<serde_json::Value>), (StatusCode, Json<garden_common::api_utils::ApiErrorResponse>)> {
    use axum::http::HeaderMap;
    // Forward to services delete
    let result = crate::api::v1::services::delete_service_v1(State(state), Path(name), HeaderMap::new()).await;
    match result {
        Ok(Json(response)) => Ok((StatusCode::OK, Json(serde_json::json!(response.data)))),
        Err((status, error)) => Err((status, error)),
    }
}

/// POST /api/v1/offerings:heal
/// Heal the garden by discovering and adopting orphaned containers
#[derive(Debug, Deserialize)]
pub struct HealRequest {
    #[serde(default)]
    pub drop_invalid: bool,
}

pub async fn heal_garden_v1(
    State(state): State<AppState>,
    Json(payload): Json<HealRequest>,
) -> Result<(StatusCode, Json<serde_json::Value>), (StatusCode, Json<garden_common::api_utils::ApiErrorResponse>)> {
    // Forward to services reconcile (same operation, zen terminology)
    crate::api::v1::services::reconcile_inventory_v1(
        State(state),
        Json(crate::api::v1::services::ReconcileRequest {
            drop_invalid: payload.drop_invalid,
        }),
    )
    .await
}

/// POST /api/v1/offerings:refresh
/// Refresh the offerings catalog from disk
pub async fn refresh_catalog_v1(
    State(state): State<AppState>,
) -> Result<(StatusCode, Json<serde_json::Value>), (StatusCode, Json<garden_common::api_utils::ApiErrorResponse>)> {
    // Rebuild offerings index
    crate::ensure_offerings_index(&state, true).await.map_err(|e| {
        tracing::error!(error = ?e, "Failed to rebuild offerings catalog");
        let mut details = HashMap::new();
        details.insert("error".to_string(), serde_json::json!(format!("{}", e)));
        error_response(
            StatusCode::INTERNAL_SERVER_ERROR,
            error_codes::INTERNAL_ERROR,
            "Failed to rebuild offerings catalog".to_string(),
            Some(details),
        )
    })?;
    
    let idx_guard = state.offerings_index.read().await;
    let idx = idx_guard.as_ref().ok_or_else(|| {
        error_response(
            StatusCode::INTERNAL_SERVER_ERROR,
            error_codes::INTERNAL_ERROR,
            "Offerings catalog unavailable after rebuild".to_string(),
            None,
        )
    })?;
    
    Ok((
        StatusCode::OK,
        Json(serde_json::json!({
            "status": "refreshed",
            "count": idx.offerings.len(),
            "fingerprint": idx.fingerprint,
            "generated_at": idx.generated_at,
        })),
    ))
}

// Helper functions

fn simplify_health(status: &garden_common::ServiceStatus) -> String {
    use garden_common::{ServiceStatus, constants};
    match status {
        ServiceStatus::Running => constants::HEALTH_HEALTHY.to_string(),
        ServiceStatus::Stopped | ServiceStatus::Unknown => constants::HEALTH_UNHEALTHY.to_string(),
        ServiceStatus::Maintenance | ServiceStatus::Degraded => constants::HEALTH_DEGRADED.to_string(),
        ServiceStatus::Installing => constants::HEALTH_INSTALLING.to_string(),
    }
}

// Utility functions removed - add back if needed for uptime display
