//! Adoption API endpoints
//!
//! Endpoints for managing adopted and borrowed offerings:
//! - List adoptable offerings (detected but not yet adopted)
//! - Adopt offerings manually
//! - List adopted/borrowed offerings
//! - Remove adopted/borrowed offerings

use axum::{
    extract::{Path, State},
    http::{HeaderMap, StatusCode},
    Json,
};
use crate::api::responses::ApiResponse;
use crate::api::suggestions::{generate_suggestions, SuggestionContext};
use crate::{error_response, AppState};
use garden_common::{
    api_utils::ApiErrorResponse,
    AdoptedOfferingInfo, BorrowedOfferingInfo,
};
use serde::{Deserialize, Serialize};

/// GET /api/v1/offerings/adoptable - List offerings available for adoption
///
/// Returns list of services detected on the host that can be adopted.
/// These are services detected but not yet managed by Moss.
pub async fn list_adoptable_v1(
    State(state): State<AppState>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<Vec<AdoptableOffering>>>, (StatusCode, Json<ApiErrorResponse>)> {
    // Load manifests that support adopted mode
    let manifests = state.manifests.read().await;

    let mut adoptable = Vec::new();

    for manifest in manifests.iter() {
        // Only check manifests with adopted mode
        if !manifest.modes.iter().any(|m| matches!(m, garden_common::OfferingMode::Adopted)) {
            continue;
        }

        // Check if already adopted
        let already_adopted = {
            let adopted = state.adopted_offerings.read().await;
            adopted.iter().any(|a| a.offering == manifest.name)
        };

        if already_adopted {
            continue;
        }

        // Try detection (this will use cached results if available)
        let orchestrator = crate::domain::DetectionOrchestrator::new(state.docker.clone());
        match orchestrator.detect(manifest).await {
            Ok(result) if result.detected && result.stable => {
                adoptable.push(AdoptableOffering {
                    name: manifest.name.clone(),
                    category: manifest.category.clone(),
                    description: manifest.description.clone(),
                    version: result.version,
                    detection_method: "auto".to_string(), // Could track actual method used
                });
            }
            _ => {
                // Not detected or not stable yet
            }
        }
    }

    let ctx = SuggestionContext::from_headers(&headers, "list_adoptable");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: adoptable,
        suggestions,
    }))
}

/// POST /api/v1/offerings/:offering/adopt - Manually adopt an offering
///
/// Attempts to detect and adopt a specific offering.
/// Returns the adopted offering info if successful.
pub async fn adopt_offering_v1(
    State(state): State<AppState>,
    Path(offering): Path<String>,
    headers: HeaderMap,
    Json(req): Json<AdoptOfferingRequest>,
) -> Result<Json<ApiResponse<AdoptedOfferingInfo>>, (StatusCode, Json<ApiErrorResponse>)> {
    // Check if already adopted
    {
        let adopted = state.adopted_offerings.read().await;
        if adopted.iter().any(|a| a.offering == offering) {
            return Err(error_response(
                StatusCode::CONFLICT,
                "ALREADY_ADOPTED",
                format!("Offering '{}' is already adopted", offering),
                None,
            ));
        }
    }

    // Find manifest for offering
    let manifest = {
        let manifests = state.manifests.read().await;
        manifests.iter()
            .find(|m| m.name == offering)
            .cloned()
            .ok_or_else(|| error_response(
                StatusCode::NOT_FOUND,
                "OFFERING_NOT_FOUND",
                format!("Offering '{}' not found", offering),
                None,
            ))?
    };

    // Verify manifest supports adopted mode
    if !manifest.modes.iter().any(|m| matches!(m, garden_common::OfferingMode::Adopted)) {
        return Err(error_response(
            StatusCode::BAD_REQUEST,
            "NOT_ADOPTABLE",
            format!("Offering '{}' does not support adopted mode", offering),
            None,
        ));
    }

    // Detect offering
    let orchestrator = crate::domain::DetectionOrchestrator::new(state.docker.clone());
    let detection_result = orchestrator.detect(&manifest).await
        .map_err(|e| error_response(
            StatusCode::INTERNAL_SERVER_ERROR,
            "DETECTION_FAILED",
            format!("Detection failed: {}", e),
            None,
        ))?;

    if !detection_result.detected {
        return Err(error_response(
            StatusCode::NOT_FOUND,
            "NOT_DETECTED",
            format!("Offering '{}' not detected on this system", offering),
            None,
        ));
    }

    // Extract location from manifest or detection result
    // For now, use placeholder - real implementation would extract from detection
    let location = garden_common::ServiceLocation {
        host: req.location.unwrap_or_else(|| "localhost".to_string()),
        port: req.port.unwrap_or(0),
        protocol: manifest.category.clone(),
    };

    let control_level = req.control_level
        .and_then(|s| match s.as_str() {
            "full" => Some(garden_common::AdoptedControlLevel::Full),
            "monitor" => Some(garden_common::AdoptedControlLevel::Monitor),
            "announce" => Some(garden_common::AdoptedControlLevel::Announce),
            _ => None,
        })
        .unwrap_or_default();

    let adopted_info = AdoptedOfferingInfo {
        name: format!("{}@adopted", offering),
        offering: offering.clone(),
        mode: garden_common::OfferingMode::Adopted,
        location,
        control_level,
        health: garden_common::ServiceHealthStatus::Healthy,
        detected_at: chrono::Utc::now().to_rfc3339(),
        version: detection_result.version,
        start_command: manifest.control.as_ref().and_then(|c| c.start_command.clone()),
        stop_command: manifest.control.as_ref().and_then(|c| c.stop_command.clone()),
        restart_command: manifest.control.as_ref().and_then(|c| c.restart_command.clone()),
        health_check_url: manifest.control.as_ref().and_then(|c| c.health_check_url.clone()),
        container_name: None,
    };

    // Add to registry
    {
        let mut adopted = state.adopted_offerings.write().await;
        adopted.push(adopted_info.clone());
    }

    // Persist adopted offerings registry
    // TODO: Add persistence for adopted offerings

    let ctx = SuggestionContext::from_headers(&headers, "adopt_offering");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: adopted_info,
        suggestions,
    }))
}

/// GET /api/v1/offerings/adopted - List adopted offerings
pub async fn list_adopted_v1(
    State(state): State<AppState>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<Vec<AdoptedOfferingInfo>>>, (StatusCode, Json<ApiErrorResponse>)> {
    let adopted = state.adopted_offerings.read().await;
    let offerings = adopted.clone();
    drop(adopted);

    let ctx = SuggestionContext::from_headers(&headers, "list_adopted");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: offerings,
        suggestions,
    }))
}

/// GET /api/v1/offerings/borrowed - List borrowed offerings
pub async fn list_borrowed_v1(
    State(state): State<AppState>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<Vec<BorrowedOfferingInfo>>>, (StatusCode, Json<ApiErrorResponse>)> {
    let borrowed = state.borrowed_offerings.read().await;
    let offerings = borrowed.clone();
    drop(borrowed);

    let ctx = SuggestionContext::from_headers(&headers, "list_borrowed");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: offerings,
        suggestions,
    }))
}

/// DELETE /api/v1/offerings/:offering/adopt - Remove adopted offering
///
/// Removes an adopted offering from management (doesn't stop/delete the service).
pub async fn unadopt_offering_v1(
    State(state): State<AppState>,
    Path(offering): Path<String>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<String>>, (StatusCode, Json<ApiErrorResponse>)> {
    let mut adopted = state.adopted_offerings.write().await;

    let initial_len = adopted.len();
    adopted.retain(|a| a.offering != offering);

    if adopted.len() == initial_len {
        return Err(error_response(
            StatusCode::NOT_FOUND,
            "NOT_ADOPTED",
            format!("Offering '{}' is not currently adopted", offering),
            None,
        ));
    }

    drop(adopted);

    // TODO: Persist adopted offerings registry

    let ctx = SuggestionContext::from_headers(&headers, "unadopt_offering");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: format!("Offering '{}' unadopted successfully", offering),
        suggestions,
    }))
}

/// Adoptable offering information
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AdoptableOffering {
    pub name: String,
    pub category: String,
    pub description: String,
    pub version: Option<String>,
    pub detection_method: String,
}

/// Adopt offering request
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AdoptOfferingRequest {
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub control_level: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub location: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub port: Option<u16>,
}

/// Borrow offering request
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BorrowOfferingRequest {
    /// Name for this borrowed service
    pub name: String,

    /// URL/connection string for the external service
    pub url: String,

    /// Optional category (e.g., "Database", "Cache")
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub category: Option<String>,

    /// Optional description
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub description: Option<String>,
}

/// POST /api/v1/adoption/borrow - Register an external (borrowed) service
///
/// Borrowed services are external network services not managed by this stone,
/// but registered for reference and service discovery.
pub async fn borrow_service_v1(
    State(state): State<AppState>,
    headers: HeaderMap,
    Json(req): Json<BorrowOfferingRequest>,
) -> Result<Json<ApiResponse<BorrowedOfferingInfo>>, (StatusCode, Json<ApiErrorResponse>)> {
    // Check if already borrowed with this name
    {
        let borrowed = state.borrowed_offerings.read().await;
        if borrowed.iter().any(|b| b.name == req.name) {
            return Err(error_response(
                StatusCode::CONFLICT,
                "ALREADY_BORROWED",
                format!("Service '{}' is already registered as borrowed", req.name),
                None,
            ));
        }
    }

    // Parse URL to extract host/port/protocol
    let url_parsed = url::Url::parse(&req.url).map_err(|e| {
        error_response(
            StatusCode::BAD_REQUEST,
            "INVALID_URL",
            format!("Invalid URL: {}", e),
            None,
        )
    })?;

    let host = url_parsed.host_str().unwrap_or("localhost").to_string();
    let port = url_parsed.port().unwrap_or(0);
    let protocol = url_parsed.scheme().to_string();

    let location = garden_common::ServiceLocation {
        host,
        port,
        protocol,
    };

    let borrowed_info = BorrowedOfferingInfo {
        name: req.name.clone(),
        offering: req.name.clone(), // For borrowed, name and offering are the same
        mode: garden_common::OfferingMode::Borrowed,
        location,
        announced_at: chrono::Utc::now().to_rfc3339(),
        health_method: None,
        credentials_key: None,
        connection_template: Some(req.url.clone()),
    };

    // Add to registry
    {
        let mut borrowed = state.borrowed_offerings.write().await;
        borrowed.push(borrowed_info.clone());
    }

    // TODO: Persist borrowed offerings registry

    let ctx = SuggestionContext::from_headers(&headers, "borrow_service");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: borrowed_info,
        suggestions,
    }))
}

/// DELETE /api/v1/adoption/borrow/:name - Unregister a borrowed service
///
/// Removes a borrowed service registration (doesn't affect the external service).
pub async fn unborrow_service_v1(
    State(state): State<AppState>,
    Path(name): Path<String>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<String>>, (StatusCode, Json<ApiErrorResponse>)> {
    let mut borrowed = state.borrowed_offerings.write().await;

    let initial_len = borrowed.len();
    borrowed.retain(|b| b.name != name);

    if borrowed.len() == initial_len {
        return Err(error_response(
            StatusCode::NOT_FOUND,
            "NOT_BORROWED",
            format!("Service '{}' is not currently registered as borrowed", name),
            None,
        ));
    }

    drop(borrowed);

    // TODO: Persist borrowed offerings registry

    let ctx = SuggestionContext::from_headers(&headers, "unborrow_service");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: format!("Borrowed service '{}' unregistered successfully", name),
        suggestions,
    }))
}
