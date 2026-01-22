//! Administrative API endpoints
//!
//! Provides privileged operations for system administration:
//! - Graceful daemon shutdown
//! - Windows service installation
//!
//! These endpoints should be protected by authentication in production.

use axum::{
    extract::State,
    http::StatusCode,
    Json,
};
use serde_json::json;

use crate::AppState;

/// POST /admin/shutdown - Initiate graceful daemon shutdown
///
/// Triggers graceful shutdown sequence:
/// 1. Stops accepting new requests
/// 2. Allows in-flight requests to complete (5s timeout)
/// 3. Exits process
///
/// # Returns
/// - 200 OK: Shutdown initiated successfully
///
/// # Example Response
/// ```json
/// {
///   "success": true,
///   "message": "Shutdown initiated"
/// }
/// ```
///
/// # Note
/// This endpoint returns immediately. The actual shutdown happens asynchronously.
/// Connected clients may see connection closed errors as the server stops.
pub async fn admin_shutdown(
    State(state): State<AppState>,
) -> (StatusCode, Json<serde_json::Value>) {
    tracing::info!("Admin shutdown endpoint called");
    state.shutdown_tx.notify_one();

    (
        StatusCode::OK,
        Json(json!({
            "success": true,
            "message": "Shutdown initiated"
        })),
    )
}

/// POST /admin/take-root - Install moss as Windows system service
///
/// **Windows only** - Installs moss as a Windows service using sc.exe.
///
/// # Behavior
/// - Detects if running from removable media (USB drive)
/// - If removable: Copies executable to C:\ProgramData\ZenGarden
/// - Creates Windows service named "ZenGardenMoss"
/// - Sets service to auto-start
/// - Starts the service immediately
///
/// # Returns
/// - 200 OK: Service installed and started
/// - 409 CONFLICT: Service already exists
/// - 500 INTERNAL_SERVER_ERROR: Installation failed
///
/// # Windows Example Response
/// ```json
/// {
///   "success": true,
///   "message": "🌿 Moss has taken root as a Windows service\n✓ Service is now awake and thriving"
/// }
/// ```
///
/// # Linux/Mac Response
/// Returns 400 BAD_REQUEST with message to use systemd instead.
#[cfg(target_os = "windows")]
pub async fn admin_take_root() -> Result<
    (StatusCode, Json<serde_json::Value>),
    (StatusCode, Json<serde_json::Value>),
> {
    use std::path::PathBuf;
    use std::process::Command;

    tracing::info!("Admin take-root endpoint called - installing as Windows service");

    let current_exe = match std::env::current_exe() {
        Ok(path) => path,
        Err(e) => {
            tracing::error!(error = ?e, "Failed to get current executable path");
            return Err((
                StatusCode::INTERNAL_SERVER_ERROR,
                Json(json!({
                    "success": false,
                    "error": "Failed to determine executable path"
                })),
            ));
        }
    };

    // Check if running from removable media (USB drive)
    let is_removable = match crate::infra::is_running_from_removable_media(&current_exe) {
        Ok(removable) => removable,
        Err(e) => {
            tracing::warn!(error = ?e, "Failed to detect drive type, assuming fixed");
            false
        }
    };

    // If removable, copy to permanent location
    let install_exe = if is_removable {
        tracing::info!("Detected execution from removable media, copying to permanent location");

        let install_dir = PathBuf::from(r"C:\ProgramData\ZenGarden");
        if let Err(e) = std::fs::create_dir_all(&install_dir) {
            tracing::error!(error = ?e, "Failed to create installation directory");
            return Err((
                StatusCode::INTERNAL_SERVER_ERROR,
                Json(json!({
                    "success": false,
                    "error": format!("Failed to create installation directory: {}", e)
                })),
            ));
        }

        let target_exe = install_dir.join("garden-moss.exe");

        if let Err(e) = std::fs::copy(&current_exe, &target_exe) {
            tracing::error!(error = ?e, "Failed to copy executable");
            return Err((
                StatusCode::INTERNAL_SERVER_ERROR,
                Json(json!({
                    "success": false,
                    "error": format!("Failed to copy executable to permanent location: {}", e)
                })),
            ));
        }

        tracing::info!(target = %target_exe.display(), "Copied executable to permanent location");
        target_exe
    } else {
        current_exe
    };

    let exe_path_str = install_exe.to_string_lossy();

    // Check if service already exists
    let check_output = Command::new("sc").args(["query", "ZenGardenMoss"]).output();

    if let Ok(output) = check_output {
        if output.status.success() {
            tracing::warn!("Service already exists");
            return Err((
                StatusCode::CONFLICT,
                Json(json!({
                    "success": false,
                    "error": "Service already installed. Remove with: sc delete ZenGardenMoss"
                })),
            ));
        }
    }

    // Create service with proper sc.exe syntax (requires space after =)
    let bin_path = format!("binPath= {}", exe_path_str);
    let output = Command::new("sc")
        .args([
            "create",
            "ZenGardenMoss",
            &bin_path,
            "start=",
            "auto",
            "DisplayName=",
            "Zen Garden Moss",
        ])
        .output();

    match output {
        Ok(output) if output.status.success() => {
            tracing::info!("Service created successfully");

            // Set description (best effort - ignore failures)
            let _ = Command::new("sc")
                .args([
                    "description",
                    "ZenGardenMoss",
                    "Zen Garden stone orchestration daemon - manages container services",
                ])
                .output();

            // Start the service
            let start_output = Command::new("sc")
                .args(["start", "ZenGardenMoss"])
                .output();

            let install_message = if is_removable {
                format!(
                    "🌿 Moss has taken root from removable media\nInstalled to: {}",
                    exe_path_str
                )
            } else {
                "🌿 Moss has taken root as a Windows service".to_string()
            };

            match start_output {
                Ok(start) if start.status.success() => {
                    tracing::info!("Service started successfully");
                    Ok((
                        StatusCode::OK,
                        Json(json!({
                            "success": true,
                            "message": format!("{}\n✓ Service is now awake and thriving", install_message)
                        })),
                    ))
                }
                _ => {
                    tracing::warn!("Service created but failed to start");
                    Ok((
                        StatusCode::OK,
                        Json(json!({
                            "success": true,
                            "message": format!("{}\n⚠️  Service installed but not started. Start manually with: sc start ZenGardenMoss", install_message)
                        })),
                    ))
                }
            }
        }
        Ok(output) => {
            let stderr = String::from_utf8_lossy(&output.stderr);
            let stdout = String::from_utf8_lossy(&output.stdout);
            tracing::error!(stderr = %stderr, stdout = %stdout, "Failed to create service");
            Err((
                StatusCode::INTERNAL_SERVER_ERROR,
                Json(json!({
                    "success": false,
                    "error": format!("Service creation failed: {} {}", stderr, stdout)
                })),
            ))
        }
        Err(e) => {
            tracing::error!(error = ?e, "Failed to execute sc.exe");
            Err((
                StatusCode::INTERNAL_SERVER_ERROR,
                Json(json!({
                    "success": false,
                    "error": format!("Failed to execute service installer: {}", e)
                })),
            ))
        }
    }
}

/// POST /admin/take-root - Not supported on Linux/Mac
///
/// Returns 400 BAD_REQUEST with guidance to use systemd on Linux.
#[cfg(not(target_os = "windows"))]
pub async fn admin_take_root() -> Result<
    (StatusCode, Json<serde_json::Value>),
    (StatusCode, Json<serde_json::Value>),
> {
    Err((
        StatusCode::BAD_REQUEST,
        Json(json!({
            "success": false,
            "error": "take-root is only supported on Windows. Use systemd on Linux."
        })),
    ))
}
