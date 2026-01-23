// Stone Operations API
//
// Purpose: System-level operations on the stone itself
// Custom actions using single-colon format: :upgrade, :shutdown, :deploy

use axum::{
    body::Bytes,
    extract::{Json, State},
    http::{HeaderMap, StatusCode},
};
use base64::Engine;
use serde::Deserialize;
use serde_json::json;
use sha2::{Sha256, Digest};

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

/// POST /api/v1/stone:deploy
/// Deploy a complete upgrade package (.tar.gz for Linux, .zip for Windows)
///
/// Headers:
///   X-Package-SHA256: Expected SHA256 hash of the package
///
/// Body: Raw package bytes (application/octet-stream)
///
/// The package is staged and will be processed on next restart.
/// If the package contains garden-moss, a restart is initiated automatically.
pub async fn deploy_stone_v1(
    State(state): State<AppState>,
    headers: HeaderMap,
    body: Bytes,
) -> (StatusCode, Json<serde_json::Value>) {
    tracing::info!(size = body.len(), "Package deploy requested");

    // Get expected hash from header
    let expected_hash = match headers.get("x-package-sha256") {
        Some(v) => match v.to_str() {
            Ok(s) => s.to_lowercase(),
            Err(_) => {
                return (
                    StatusCode::BAD_REQUEST,
                    Json(json!({
                        "status": "error",
                        "message": "Invalid X-Package-SHA256 header encoding",
                    })),
                );
            }
        },
        None => {
            return (
                StatusCode::BAD_REQUEST,
                Json(json!({
                    "status": "error",
                    "message": "Missing X-Package-SHA256 header",
                })),
            );
        }
    };

    // Compute actual hash
    let mut hasher = Sha256::new();
    hasher.update(&body);
    let actual_hash = format!("{:x}", hasher.finalize());

    if expected_hash != actual_hash {
        tracing::error!(
            expected = %expected_hash,
            actual = %actual_hash,
            "Package checksum mismatch"
        );
        return (
            StatusCode::BAD_REQUEST,
            Json(json!({
                "status": "error",
                "message": "Package checksum mismatch",
                "expected": expected_hash,
                "actual": actual_hash,
            })),
        );
    }

    tracing::info!(hash = %actual_hash, size = body.len(), "Package checksum verified");

    // Determine staging path based on platform
    let (staging_dir, package_name) = if cfg!(windows) {
        (
            std::env::var("GARDEN_STAGING_DIR")
                .unwrap_or_else(|_| "C:\\ProgramData\\ZenGarden\\staging".to_string()),
            "pending-upgrade.zip",
        )
    } else {
        (
            std::env::var("GARDEN_STAGING_DIR")
                .unwrap_or_else(|_| "/var/lib/zen-garden/staging".to_string()),
            "pending-upgrade.tar.gz",
        )
    };

    // Ensure staging directory exists
    if let Err(e) = std::fs::create_dir_all(&staging_dir) {
        tracing::error!(error = ?e, dir = %staging_dir, "Failed to create staging directory");
        return (
            StatusCode::INTERNAL_SERVER_ERROR,
            Json(json!({
                "status": "error",
                "message": "Failed to create staging directory",
                "error": format!("{}", e),
            })),
        );
    }

    let target_path = if cfg!(windows) {
        format!("{}\\{}", staging_dir, package_name)
    } else {
        format!("{}/{}", staging_dir, package_name)
    };

    // Write to temporary location first
    let temp_path = format!("{}.tmp", target_path);
    if let Err(e) = std::fs::write(&temp_path, &body) {
        tracing::error!(error = ?e, path = %temp_path, "Failed to write package");
        return (
            StatusCode::INTERNAL_SERVER_ERROR,
            Json(json!({
                "status": "error",
                "message": "Failed to write package file",
                "error": format!("{}", e),
            })),
        );
    }

    // Atomic rename to final location
    if let Err(e) = std::fs::rename(&temp_path, &target_path) {
        tracing::error!(error = ?e, path = %target_path, "Failed to stage package");
        let _ = std::fs::remove_file(&temp_path);
        return (
            StatusCode::INTERNAL_SERVER_ERROR,
            Json(json!({
                "status": "error",
                "message": "Failed to stage package",
                "error": format!("{}", e),
            })),
        );
    }

    tracing::info!(path = %target_path, "Package staged successfully");

    // Check if package contains moss by peeking at package.json
    // For simplicity, we always trigger a restart since the upgrade script
    // will determine what to do based on package contents
    let contains_moss = peek_package_for_moss(&target_path);

    if contains_moss {
        tracing::info!("Package contains garden-moss, initiating graceful shutdown for upgrade");
        state.shutdown_tx.notify_one();

        (
            StatusCode::ACCEPTED,
            Json(json!({
                "status": "accepted",
                "message": "Package staged successfully. Service restart initiated.",
                "staged_path": target_path,
                "sha256": actual_hash,
                "size": body.len(),
            })),
        )
    } else {
        (
            StatusCode::OK,
            Json(json!({
                "status": "success",
                "message": "Package staged successfully. Restart required to apply.",
                "staged_path": target_path,
                "sha256": actual_hash,
                "size": body.len(),
            })),
        )
    }
}

/// Peek into a package to check if it contains garden-moss
fn peek_package_for_moss(package_path: &str) -> bool {
    // For .tar.gz, we could parse the archive, but for simplicity
    // we'll just assume packages with moss should trigger restart.
    // The upgrade script will handle the actual extraction and validation.
    //
    // A more robust implementation would:
    // 1. Extract package.json from the archive
    // 2. Parse it and check if components.garden-moss exists
    //
    // For now, we always return true to trigger restart,
    // letting the upgrade script decide what to do.
    let _ = package_path;
    true
}
