//! Health API endpoints
//!
//! Provides system health status including:
//! - Docker daemon availability
//! - Disk space
//! - Memory usage
//! - Initialization progress

use axum::{extract::State, http::StatusCode, Json};
use garden_common::{ComponentHealth, DaemonHealthStatus, HealthCheck};
use crate::AppState;
use std::collections::HashMap;

/// GET /api/health - System health status
pub async fn get_health(
    State(state): State<AppState>,
) -> (StatusCode, Json<DaemonHealthStatus>) {
    // Run health checks (using domain logic where possible)
    let docker_check = check_docker_health(&state).await;
    let disk_check = crate::domain::health::check_disk_health();
    let memory_check = crate::domain::health::check_memory_health();

    // Build legacy checks HashMap for backward compatibility
    let mut checks = HashMap::new();
    checks.insert("docker".to_string(), docker_check.clone());
    checks.insert("disk".to_string(), disk_check.clone());
    checks.insert("memory".to_string(), memory_check.clone());

    // Build new components with detailed information
    let mut components = HashMap::new();

    // Docker component (AppState-dependent)
    let docker_component = build_docker_component(&state).await;
    components.insert("docker".to_string(), docker_component);

    // Disk component (pure, from domain)
    let disk_component = crate::domain::health::build_disk_component();
    components.insert("disk".to_string(), disk_component);

    // Memory component (pure, from domain)
    let memory_component = crate::domain::health::build_memory_component();
    components.insert("memory".to_string(), memory_component);

    // Initialization component (AppState-dependent)
    let init_component = build_initialization_component(&state).await;
    components.insert("initialization".to_string(), init_component);

    // Determine overall status based on worst component status
    let overall_status = crate::domain::health::determine_overall_status(&components);

    // HTTP status code based on overall status
    let http_status = match overall_status.as_str() {
        garden_common::HEALTH_UNHEALTHY => StatusCode::SERVICE_UNAVAILABLE,
        _ => StatusCode::OK,
    };

    // Legacy boolean flags for backward compatibility
    let docker_ok = docker_check.status == garden_common::CHECK_PASS;
    let disk_ok = disk_check.status != garden_common::CHECK_FAIL;
    let memory_ok = memory_check.status != garden_common::CHECK_FAIL;
    let uptime_seconds = state.start_time.elapsed().as_secs();

    (
        http_status,
        Json(DaemonHealthStatus {
            status: overall_status,
            version: crate::cli::VERSION.to_string(),
            timestamp: chrono::Utc::now().to_rfc3339(),
            components,
            docker_available: docker_ok,
            disk_space_ok: disk_ok,
            memory_ok,
            uptime_seconds,
            checks,
        }),
    )
}

// ============================================================================
// AppState-Dependent Helpers
// ============================================================================

/// Check Docker daemon health
async fn check_docker_health(state: &AppState) -> HealthCheck {
    if state.docker.is_healthy().await {
        HealthCheck {
            status: garden_common::CHECK_PASS.to_string(),
            message: None,
        }
    } else {
        HealthCheck {
            status: garden_common::CHECK_FAIL.to_string(),
            message: Some("Docker daemon unavailable".to_string()),
        }
    }
}

/// Build Docker component health with availability check
async fn build_docker_component(state: &AppState) -> ComponentHealth {
    let mut details = HashMap::new();

    if state.docker.is_healthy().await {
        details.insert("available".to_string(), serde_json::json!(true));
        ComponentHealth::healthy(details)
    } else {
        details.insert("available".to_string(), serde_json::json!(false));
        ComponentHealth::unhealthy(details)
    }
}

/// Build initialization component showing startup progress
async fn build_initialization_component(state: &AppState) -> ComponentHealth {
    let mut details = HashMap::new();

    // Check hardware detection status
    let caps_guard = state.capabilities.read().await;
    let detection_status = if let Some(caps) = caps_guard.as_ref() {
        match caps.detection_status {
            garden_common::DetectionStatus::Scanning => "scanning",
            garden_common::DetectionStatus::Partial => "partial",
            garden_common::DetectionStatus::Complete => "complete",
        }
    } else {
        "unknown"
    };
    details.insert(
        "hardware_detection".to_string(),
        serde_json::json!(detection_status),
    );

    // Check catalog build status
    let catalog_guard = state.offerings_index.read().await;
    let catalog_ready = catalog_guard.is_some();
    details.insert("catalog_ready".to_string(), serde_json::json!(catalog_ready));

    // Determine overall initialization health
    if detection_status == "complete" && catalog_ready {
        ComponentHealth::healthy(details)
    } else {
        details.insert(
            "message".to_string(),
            serde_json::json!("Initializing..."),
        );
        ComponentHealth::degraded(details)
    }
}
