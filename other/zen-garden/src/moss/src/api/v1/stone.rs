// Stone Software Operations API
//
// Purpose: Software-level operations on the stone (upgrade, deploy, info)
// Custom actions using single-colon format: :upgrade, :deploy
//
// Note: Machine power operations (shutdown, reboot) moved to /api/v1/admin/stone/
// See: docs/decisions/API-0002-admin-hierarchy.md

use axum::{
    body::Bytes,
    extract::{Json, State},
    http::{HeaderMap, StatusCode},
};
use base64::Engine;
use serde::{Deserialize, Serialize};
use serde_json::json;
use sha2::{Sha256, Digest};

use crate::AppState;
use crate::api::responses::ApiResponse;
use garden_common::api_utils::ApiErrorResponse;
use crate::domain::validate_binary_architecture;
use garden_common::{names::{MOSS_BINARY, RAKE_BINARY}, HardwareCapabilities, ServiceInfo};

// ============================================================================
// Stone Info Endpoint (for observe command)
// ============================================================================

/// Combined stone information response
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StoneInfoResponse {
    /// Hardware capabilities (CPU, memory, GPU, storage)
    pub capabilities: HardwareCapabilities,
    /// Active services on this stone
    pub services: Vec<ServiceInfo>,
    /// Stone endpoint (for reference)
    pub endpoint: String,
}

/// GET /api/v1/stone/info - Get complete stone information
///
/// Returns everything needed for `observe` command in one response.
/// Optimized for garden-wide discovery and status displays.
/// Eliminates multiple round-trips (capabilities + services).
///
/// # Response
/// - 200: StoneInfoResponse with capabilities + services + endpoint
/// - 500: Internal error
pub async fn get_stone_info_v1(
    State(state): State<AppState>,
    _headers: HeaderMap,
) -> Result<Json<ApiResponse<StoneInfoResponse>>, (StatusCode, Json<ApiErrorResponse>)> {
    // Get capabilities from cached state
    let capabilities = {
        let caps_guard = state.capabilities.read().await;
        caps_guard.as_ref().cloned().unwrap_or_else(|| {
            crate::infra::hardware::create_skeleton(state.stone_name.clone())
        })
    };

    // Get services (reuse existing registry logic)
    let services: Vec<ServiceInfo> = {
        let registry = state.registry.read().await;
        registry.clone()
    };

    // Build endpoint
    let current_ip = state.network_monitor.get_ip().await;
    let endpoint = format!("http://{}:{}", current_ip, state.api_port);

    let response = StoneInfoResponse {
        capabilities,
        services,
        endpoint,
    };

    Ok(Json(ApiResponse {
        data: response,
        suggestions: None,
    }))
}

// ============================================================================
// Stone Upgrade Endpoint
// ============================================================================

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

    // Extract and validate package
    let staging_base = if cfg!(windows) {
        std::env::var("GARDEN_STAGING_DIR")
            .unwrap_or_else(|_| "C:\\ProgramData\\ZenGarden\\staging".to_string())
    } else {
        std::env::var("GARDEN_STAGING_DIR")
            .unwrap_or_else(|_| "/var/lib/zen-garden/staging".to_string())
    };

    // Create temporary extraction directory
    let temp_dir = format!("{}/extract-{}", staging_base, actual_hash[..8].to_string());
    if let Err(e) = std::fs::create_dir_all(&temp_dir) {
        tracing::error!(error = ?e, dir = %temp_dir, "Failed to create extraction directory");
        return (
            StatusCode::INTERNAL_SERVER_ERROR,
            Json(json!({
                "status": "error",
                "message": "Failed to create extraction directory",
                "error": format!("{}", e),
            })),
        );
    }

    // Write package to temp file for extraction
    let temp_package = format!("{}/package.tar.gz", temp_dir);
    if let Err(e) = std::fs::write(&temp_package, &body) {
        tracing::error!(error = ?e, "Failed to write temporary package");
        let _ = std::fs::remove_dir_all(&temp_dir);
        return (
            StatusCode::INTERNAL_SERVER_ERROR,
            Json(json!({
                "status": "error",
                "message": "Failed to write temporary package",
            })),
        );
    }

    // Extract package (tar.gz on Linux/Windows for now)
    tracing::info!(path = %temp_package, "Extracting package...");
    let extract_result = if cfg!(windows) {
        std::process::Command::new("tar")
            .args(&["-xzf", &temp_package, "-C", &temp_dir])
            .output()
    } else {
        std::process::Command::new("tar")
            .args(&["-xzf", &temp_package, "-C", &temp_dir])
            .output()
    };

    if let Err(e) = extract_result {
        tracing::error!(error = ?e, "Failed to extract package");
        let _ = std::fs::remove_dir_all(&temp_dir);
        return (
            StatusCode::INTERNAL_SERVER_ERROR,
            Json(json!({
                "status": "error",
                "message": "Failed to extract package",
            })),
        );
    }

    // Find extracted directory (zen-garden-*)
    let package_dir = match std::fs::read_dir(&temp_dir) {
        Ok(entries) => {
            entries
                .filter_map(|e| e.ok())
                .find(|e| {
                    e.file_name()
                        .to_str()
                        .map(|n| n.starts_with("zen-garden-"))
                        .unwrap_or(false)
                })
                .map(|e| e.path())
        }
        Err(e) => {
            tracing::error!(error = ?e, "Failed to read extraction directory");
            let _ = std::fs::remove_dir_all(&temp_dir);
            return (
                StatusCode::INTERNAL_SERVER_ERROR,
                Json(json!({
                    "status": "error",
                    "message": "Failed to locate extracted package",
                })),
            );
        }
    };

    let package_dir = match package_dir {
        Some(dir) => dir,
        None => {
            tracing::error!("No zen-garden-* directory found in package");
            let _ = std::fs::remove_dir_all(&temp_dir);
            return (
                StatusCode::BAD_REQUEST,
                Json(json!({
                    "status": "error",
                    "message": "Invalid package structure - no zen-garden-* directory",
                })),
            );
        }
    };

    // Read and parse package.json
    let manifest_path = package_dir.join("package.json");
    let manifest: serde_json::Value = match std::fs::read_to_string(&manifest_path) {
        Ok(content) => match serde_json::from_str(&content) {
            Ok(json) => json,
            Err(e) => {
                tracing::error!(error = ?e, "Failed to parse package.json");
                let _ = std::fs::remove_dir_all(&temp_dir);
                return (
                    StatusCode::BAD_REQUEST,
                    Json(json!({
                        "status": "error",
                        "message": "Invalid package.json format",
                    })),
                );
            }
        },
        Err(e) => {
            tracing::error!(error = ?e, "Failed to read package.json");
            let _ = std::fs::remove_dir_all(&temp_dir);
            return (
                StatusCode::BAD_REQUEST,
                Json(json!({
                    "status": "error",
                    "message": "Missing package.json",
                })),
            );
        }
    };

    // Validate platform
    let platform = manifest.get("platform").and_then(|v| v.as_str()).unwrap_or("unknown");
    let expected_platform = if cfg!(windows) { "windows" } else { "linux" };
    if platform != expected_platform {
        tracing::error!(expected = expected_platform, actual = platform, "Platform mismatch");
        let _ = std::fs::remove_dir_all(&temp_dir);
        return (
            StatusCode::BAD_REQUEST,
            Json(json!({
                "status": "error",
                "message": format!("Platform mismatch - expected {}, got {}", expected_platform, platform),
            })),
        );
    }

    // Create validated staging directory
    let validated_dir = format!("{}/validated", staging_base);
    let _ = std::fs::remove_dir_all(&validated_dir); // Clear old staging
    if let Err(e) = std::fs::create_dir_all(&format!("{}/bin", validated_dir)) {
        tracing::error!(error = ?e, "Failed to create validated staging");
        let _ = std::fs::remove_dir_all(&temp_dir);
        return (
            StatusCode::INTERNAL_SERVER_ERROR,
            Json(json!({
                "status": "error",
                "message": "Failed to create validated staging directory",
            })),
        );
    }

    // Copy validated binaries to staging
    let bin_dir = package_dir.join("bin");
    if !bin_dir.exists() {
        tracing::error!("Package missing bin/ directory");
        let _ = std::fs::remove_dir_all(&temp_dir);
        return (
            StatusCode::BAD_REQUEST,
            Json(json!({
                "status": "error",
                "message": "Invalid package - missing bin/ directory",
            })),
        );
    }

    let mut contains_moss = false;
    match std::fs::read_dir(&bin_dir) {
        Ok(entries) => {
            for entry in entries.filter_map(|e| e.ok()) {
                let file_name = entry.file_name();
                let name = file_name.to_string_lossy();
                if name.starts_with("garden-moss") {
                    contains_moss = true;
                }
                let dest = format!("{}/bin/{}", validated_dir, name);
                if let Err(e) = std::fs::copy(entry.path(), &dest) {
                    tracing::error!(error = ?e, file = %name, "Failed to copy binary");
                } else {
                    tracing::info!(file = %name, "Staged validated binary");
                }
            }
        }
        Err(e) => {
            tracing::error!(error = ?e, "Failed to read bin directory");
            let _ = std::fs::remove_dir_all(&temp_dir);
            let _ = std::fs::remove_dir_all(&validated_dir);
            return (
                StatusCode::INTERNAL_SERVER_ERROR,
                Json(json!({
                    "status": "error",
                    "message": "Failed to read binaries",
                })),
            );
        }
    }

    // Copy scripts if present
    let scripts_dir = package_dir.join("scripts");
    if scripts_dir.exists() {
        if let Err(e) = std::fs::create_dir_all(&format!("{}/scripts", validated_dir)) {
            tracing::warn!(error = ?e, "Failed to create scripts staging");
        } else if let Ok(entries) = std::fs::read_dir(&scripts_dir) {
            for entry in entries.filter_map(|e| e.ok()) {
                let file_name = entry.file_name();
                let name = file_name.to_string_lossy();
                let dest = format!("{}/scripts/{}", validated_dir, name);
                if let Err(e) = std::fs::copy(entry.path(), &dest) {
                    tracing::warn!(error = ?e, file = %name, "Failed to copy script");
                } else {
                    tracing::info!(file = %name, "Staged validated script");
                }
            }
        }
    }

    // Cleanup extraction directory
    let _ = std::fs::remove_dir_all(&temp_dir);

    tracing::info!(path = %validated_dir, "Package validated and staged");

    if contains_moss {
        tracing::info!("Package contains garden-moss, initiating graceful shutdown for upgrade");
        state.shutdown_tx.notify_one();

        (
            StatusCode::ACCEPTED,
            Json(json!({
                "status": "accepted",
                "message": "Package validated and staged. Service restart initiated.",
                "staged_path": validated_dir,
                "sha256": actual_hash,
                "size": body.len(),
            })),
        )
    } else {
        (
            StatusCode::OK,
            Json(json!({
                "status": "success",
                "message": "Package validated and staged. Restart required to apply.",
                "staged_path": validated_dir,
                "sha256": actual_hash,
                "size": body.len(),
            })),
        )
    }
}
