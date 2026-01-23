// Stone Operations API
//
// Purpose: System-level operations on the stone itself
// Custom actions using single-colon format: :upgrade, :shutdown

use axum::{
    extract::{Json, State},
    http::StatusCode,
};
use base64::Engine;
use serde::Deserialize;
use serde_json::json;

use crate::AppState;
use crate::domain::validate_binary_architecture;
use garden_common::names::{MOSS_BINARY, RAKE_BINARY};

/// POST /api/v1/stone:upgrade
/// Upgrade stone software (moss/rake binaries)
#[derive(Debug, Deserialize)]
pub struct UpgradeRequest {
    pub component: String,
    pub binary_data: String, // base64-encoded
}

pub async fn upgrade_stone_v1(
    State(_state): State<AppState>,
    Json(payload): Json<UpgradeRequest>,
) -> (StatusCode, Json<serde_json::Value>) {
    tracing::info!(
        component = %payload.component,
        base64_size = payload.binary_data.len(),
        "Binary upgrade requested - payload received successfully"
    );

    // Decode base64 binary data
    let binary_data = match base64::engine::general_purpose::STANDARD.decode(&payload.binary_data) {
        Ok(data) => data,
        Err(e) => {
            tracing::error!(error = ?e, "Failed to decode base64 binary data");
            return (
                StatusCode::BAD_REQUEST,
                Json(json!({
                    "status": "error",
                    "message": "Invalid base64 encoding",
                    "error": format!("{}", e),
                })),
            );
        }
    };

    // Validate architecture
    let arch = match validate_binary_architecture(&binary_data) {
        Ok(a) => a,
        Err(e) => {
            tracing::error!(error = ?e, "Binary validation failed");
            return (
                StatusCode::BAD_REQUEST,
                Json(json!({
                    "status": "error",
                    "message": "Binary validation failed",
                    "error": format!("{}", e),
                })),
            );
        }
    };

    tracing::info!(
        component = %payload.component,
        architecture = %arch,
        size = binary_data.len(),
        "Binary validated successfully"
    );

    // Determine target path based on component
    // Write to root-owned staging directory to avoid permission conflicts with SSH deployments
    // SSH deployments write to /home/stone/bin (stone-owned)
    // HTTP API deployments write to /var/lib/zen-garden/staging (root-owned)
    // moss-update-helper.sh checks both locations
    let staging_dir = if cfg!(windows) {
        std::env::var("GARDEN_STAGING_DIR")
            .unwrap_or_else(|_| "C:\\ProgramData\\ZenGarden\\staging".to_string())
    } else {
        std::env::var("GARDEN_STAGING_DIR")
            .unwrap_or_else(|_| "/var/lib/zen-garden/staging".to_string())
    };

    let target_path = match payload.component.as_str() {
        MOSS_BINARY => {
            if cfg!(windows) {
                format!("{}\\{}.staged", staging_dir, MOSS_BINARY)
            } else {
                format!("{}/{}.staged", staging_dir, MOSS_BINARY)
            }
        }
        "garden-rake" => {
            if cfg!(windows) {
                format!("{}\\{}.staged", staging_dir, RAKE_BINARY)
            } else {
                format!("{}/{}.staged", staging_dir, RAKE_BINARY)
            }
        }
        _ => {
            tracing::warn!(component = %payload.component, "Unknown component");
            return (
                StatusCode::BAD_REQUEST,
                Json(json!({
                    "status": "error",
                    "message": format!("Unknown component: {}", payload.component),
                    "valid_components": [MOSS_BINARY, RAKE_BINARY],
                })),
            );
        }
    };

    // Ensure staging directory exists
    if let Err(e) = std::fs::create_dir_all(&staging_dir) {
        tracing::error!(error = ?e, dir = %staging_dir, "Failed to create staging directory");
        return (
            StatusCode::INTERNAL_SERVER_ERROR,
            Json(json!({
                "status": "error",
                "message": "Failed to create staging directory",
                "directory": staging_dir,
                "error": format!("{}", e),
            })),
        );
    }

    // Write to temporary location
    let temp_path = format!("{}.tmp", target_path);
    if let Err(e) = std::fs::write(&temp_path, &binary_data) {
        tracing::error!(error = ?e, temp_path = %temp_path, "Failed to write binary");
        return (
            StatusCode::INTERNAL_SERVER_ERROR,
            Json(json!({
                "status": "error",
                "message": "Failed to write binary file",
                "temp_path": temp_path,
                "error": format!("{}", e),
            })),
        );
    }

    // Make executable (Unix only)
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        if let Err(e) = std::fs::set_permissions(&temp_path, std::fs::Permissions::from_mode(0o755)) {
            tracing::error!(error = ?e, temp_path = %temp_path, "Failed to set permissions");
            let _ = std::fs::remove_file(&temp_path);
            return (
                StatusCode::INTERNAL_SERVER_ERROR,
                Json(json!({
                    "status": "error",
                    "message": "Failed to set binary permissions",
                    "temp_path": temp_path,
                    "error": format!("{}", e),
                })),
            );
        }
    }

    // Atomic rename to staging location
    if let Err(e) = std::fs::rename(&temp_path, &target_path) {
        tracing::error!(error = ?e, target_path = %target_path, "Failed to stage binary");
        let _ = std::fs::remove_file(&temp_path);
        return (
            StatusCode::INTERNAL_SERVER_ERROR,
            Json(json!({
                "status": "error",
                "message": "Failed to stage binary",
                "target_path": target_path,
                "error": format!("{}", e),
            })),
        );
    }

    tracing::info!(component = %payload.component, path = %target_path, "Binary staged successfully");

    // If updating moss itself, trigger graceful shutdown for restart
    if payload.component == MOSS_BINARY {
        tracing::info!("Moss binary staged, initiating graceful shutdown for upgrade");

        // Signal graceful shutdown - systemd will restart us (Restart=always)
        // and ExecStartPre will run moss-update-helper.sh to copy the staged binary
        _state.shutdown_tx.notify_one();

        (
            StatusCode::ACCEPTED,
            Json(json!({
                "status": "accepted",
                "message": format!("{} binary staged successfully. Service restart initiated.", payload.component),
                "component": payload.component,
                "architecture": arch,
                "staged_path": target_path,
            })),
        )
    } else {
        // For rake or other components, just confirm staging
        (
            StatusCode::OK,
            Json(json!({
                "status": "success",
                "message": format!("{} binary staged successfully", payload.component),
                "component": payload.component,
                "architecture": arch,
                "staged_path": target_path,
            })),
        )
    }
}

/// POST /api/v1/stone:shutdown
/// Gracefully shutdown the Moss daemon
pub async fn shutdown_stone_v1(
    State(state): State<AppState>,
) -> (StatusCode, Json<serde_json::Value>) {
    tracing::info!("Stone shutdown endpoint called");
    state.shutdown_tx.notify_one();

    (
        StatusCode::OK,
        Json(serde_json::json!({
            "success": true,
            "message": "Shutdown initiated"
        })),
    )
}
