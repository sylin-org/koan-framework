//! Administrative API endpoints
//!
//! Provides privileged operations for system administration:
//!
//! ## Moss Operations (/api/v1/admin/moss/)
//! - `POST /shutdown` - Graceful daemon exit
//! - `POST /take-root` - Install as Windows service
//!
//! ## Stone Operations (/api/v1/admin/stone/)
//! - `POST /shutdown` - Power off the machine
//! - `POST /reboot` - Restart the machine
//! - `POST /:name/wake` - Wake stone via Wake-on-LAN (rouse)
//!
//! These endpoints should be protected by authentication in production.
//! See: docs/decisions/API-0002-admin-hierarchy.md

use axum::{
    extract::{Path, State},
    http::StatusCode,
    Json,
};
use serde_json::json;

use crate::AppState;

// ============================================================================
// Moss Operations - Daemon lifecycle
// ============================================================================

/// POST /api/v1/admin/moss/shutdown - Graceful daemon exit
///
/// Triggers graceful shutdown sequence:
/// 1. Stops accepting new requests
/// 2. Allows in-flight requests to complete (5s timeout)
/// 3. Exits process
///
/// # Returns
/// - 200 OK: Shutdown initiated successfully
pub async fn moss_shutdown(
    State(state): State<AppState>,
) -> (StatusCode, Json<serde_json::Value>) {
    tracing::info!("Admin moss shutdown requested");
    state.shutdown_tx.notify_one();

    (
        StatusCode::OK,
        Json(json!({
            "success": true,
            "message": "Moss daemon shutdown initiated"
        })),
    )
}

/// POST /api/v1/admin/moss/take-root - Install as Windows service
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
/// - 400 BAD_REQUEST: Not supported on this platform
/// - 409 CONFLICT: Service already exists
/// - 500 INTERNAL_SERVER_ERROR: Installation failed
#[cfg(target_os = "windows")]
pub async fn moss_take_root() -> Result<
    (StatusCode, Json<serde_json::Value>),
    (StatusCode, Json<serde_json::Value>),
> {
    use std::path::PathBuf;
    use std::process::Command;

    tracing::info!("Admin moss take-root requested - installing as Windows service");

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
                    "Moss has taken root from removable media\nInstalled to: {}",
                    exe_path_str
                )
            } else {
                "Moss has taken root as a Windows service".to_string()
            };

            match start_output {
                Ok(start) if start.status.success() => {
                    tracing::info!("Service started successfully");
                    Ok((
                        StatusCode::OK,
                        Json(json!({
                            "success": true,
                            "message": format!("{}\nService is now running", install_message)
                        })),
                    ))
                }
                _ => {
                    tracing::warn!("Service created but failed to start");
                    Ok((
                        StatusCode::OK,
                        Json(json!({
                            "success": true,
                            "message": format!("{}\nService installed but not started. Start manually with: sc start ZenGardenMoss", install_message)
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

/// POST /api/v1/admin/moss/take-root - Not supported on Linux/Mac
#[cfg(not(target_os = "windows"))]
pub async fn moss_take_root() -> Result<
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

// ============================================================================
// Stone Operations - Machine power management
// ============================================================================

/// POST /api/v1/admin/stone/shutdown - Power off the machine
///
/// Initiates system shutdown. The response is returned before shutdown begins.
/// A goodbye announcement is sent automatically via SIGTERM handler when moss
/// receives the termination signal from the OS shutdown sequence.
///
/// # Platform Behavior
/// - Linux: `systemctl poweroff`
/// - Windows: `shutdown /s /t 0`
///
/// # Returns
/// - 200 OK: Shutdown command issued
/// - 500 INTERNAL_SERVER_ERROR: Failed to issue shutdown command
pub async fn stone_shutdown(
    State(_state): State<AppState>,
) -> (StatusCode, Json<serde_json::Value>) {
    tracing::warn!("Stone shutdown requested - initiating system poweroff");

    // Spawn shutdown command after brief delay to allow response
    tokio::spawn(async {
        tokio::time::sleep(std::time::Duration::from_millis(500)).await;

        #[cfg(unix)]
        {
            tracing::info!("Executing: systemctl poweroff");
            let result = std::process::Command::new("systemctl")
                .args(["poweroff"])
                .spawn();

            if let Err(e) = result {
                // Fallback to shutdown command
                tracing::warn!(error = ?e, "systemctl failed, trying shutdown -h now");
                let _ = std::process::Command::new("shutdown")
                    .args(["-h", "now"])
                    .spawn();
            }
        }

        #[cfg(windows)]
        {
            tracing::info!("Executing: shutdown /s /t 0");
            let _ = std::process::Command::new("shutdown")
                .args(["/s", "/t", "0"])
                .spawn();
        }
    });

    (
        StatusCode::OK,
        Json(json!({
            "success": true,
            "message": "Stone entering slumber..."
        })),
    )
}

/// POST /api/v1/admin/stone/reboot - Restart the machine
///
/// Initiates system reboot. The response is returned before reboot begins.
/// A goodbye announcement is sent automatically via SIGTERM handler when moss
/// receives the termination signal from the OS reboot sequence.
///
/// # Platform Behavior
/// - Linux: `systemctl reboot`
/// - Windows: `shutdown /r /t 0`
///
/// # Returns
/// - 200 OK: Reboot command issued
/// - 500 INTERNAL_SERVER_ERROR: Failed to issue reboot command
pub async fn stone_reboot(
    State(_state): State<AppState>,
) -> (StatusCode, Json<serde_json::Value>) {
    tracing::warn!("Stone reboot requested - initiating system restart");

    // Spawn reboot command after brief delay to allow response
    tokio::spawn(async {
        tokio::time::sleep(std::time::Duration::from_millis(500)).await;

        #[cfg(unix)]
        {
            tracing::info!("Executing: systemctl reboot");
            let result = std::process::Command::new("systemctl")
                .args(["reboot"])
                .spawn();

            if let Err(e) = result {
                // Fallback to reboot command
                tracing::warn!(error = ?e, "systemctl failed, trying reboot");
                let _ = std::process::Command::new("reboot").spawn();
            }
        }

        #[cfg(windows)]
        {
            tracing::info!("Executing: shutdown /r /t 0");
            let _ = std::process::Command::new("shutdown")
                .args(["/r", "/t", "0"])
                .spawn();
        }
    });

    (
        StatusCode::OK,
        Json(json!({
            "success": true,
            "message": "Stone stirring..."
        })),
    )
}

/// POST /api/v1/admin/stone/:name/wake - Wake a stone via Wake-on-LAN
///
/// Sends a Wake-on-LAN magic packet to the specified stone using its
/// cached MAC address from the topology cache.
///
/// The stone must have been discovered previously (either online or offline)
/// with a valid MAC address.
///
/// # Path Parameters
/// - `name`: Stone name to wake
///
/// # Returns
/// - 200 OK: WoL packet sent successfully
/// - 404 NOT_FOUND: Stone not found in topology cache
/// - 400 BAD_REQUEST: Stone has no MAC address (discovery didn't capture it)
/// - 500 INTERNAL_SERVER_ERROR: Failed to send WoL packet
pub async fn stone_wake(
    State(state): State<AppState>,
    Path(stone_name): Path<String>,
) -> (StatusCode, Json<serde_json::Value>) {
    tracing::info!(stone = %stone_name, "Wake-on-LAN requested");

    // Look up stone in topology cache (includes offline stones)
    let stone = crate::domain::topology::get_stone_by_name(&state.topology_cache, &stone_name).await;

    match stone {
        None => {
            tracing::warn!(stone = %stone_name, "Stone not found in topology cache");
            (
                StatusCode::NOT_FOUND,
                Json(json!({
                    "success": false,
                    "error": format!("Stone '{}' not found in topology cache", stone_name),
                    "hint": "The stone may not have been discovered yet. Try 'garden-rake observe' first."
                })),
            )
        }
        Some(entry) => {
            match entry.mac {
                None => {
                    tracing::warn!(
                        stone = %stone_name,
                        status = %entry.status,
                        "Stone has no MAC address cached"
                    );
                    (
                        StatusCode::BAD_REQUEST,
                        Json(json!({
                            "success": false,
                            "error": format!("Stone '{}' has no MAC address", stone_name),
                            "hint": "MAC address was not captured during discovery. The stone may be on a platform that doesn't report MAC."
                        })),
                    )
                }
                Some(ref mac) => {
                    tracing::info!(
                        stone = %stone_name,
                        mac = %mac,
                        status = %entry.status,
                        "Sending Wake-on-LAN magic packet"
                    );

                    match crate::infra::network::send_wol_packet(mac).await {
                        Ok(()) => {
                            (
                                StatusCode::OK,
                                Json(json!({
                                    "success": true,
                                    "message": format!("Rousing {}...", stone_name),
                                    "stone": stone_name,
                                    "mac": mac,
                                    "status": entry.status.to_string(),
                                    "last_seen": entry.last_seen.to_rfc3339()
                                })),
                            )
                        }
                        Err(e) => {
                            tracing::error!(
                                stone = %stone_name,
                                mac = %mac,
                                error = ?e,
                                "Failed to send WoL packet"
                            );
                            (
                                StatusCode::INTERNAL_SERVER_ERROR,
                                Json(json!({
                                    "success": false,
                                    "error": format!("Failed to send WoL packet: {}", e)
                                })),
                            )
                        }
                    }
                }
            }
        }
    }
}
