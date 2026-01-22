//! Process lifecycle management
//!
//! Cross-platform utilities for managing Moss daemon processes:
//! - Graceful shutdown via HTTP API with fallback to force kill
//! - Process detection (excluding current process)
//! - Force termination of all Moss processes
//!
//! Platform Support:
//! - Windows: Uses `tasklist` and `taskkill`
//! - Linux/macOS: Uses `pgrep` and `kill`

/// Attempt graceful shutdown via HTTP, fallback to force kill
///
/// First tries to send a shutdown request to the existing Moss instance via HTTP.
/// If that fails or times out after 3 seconds, falls back to force killing the process.
///
/// This is the preferred method for stopping existing Moss instances as it allows
/// them to clean up resources properly.
pub async fn kill_existing_moss_processes_graceful() -> anyhow::Result<()> {
    // Try graceful shutdown via HTTP first
    let client = reqwest::Client::builder()
        .timeout(std::time::Duration::from_secs(3))
        .build()?;

    match client.post(format!("http://127.0.0.1:{}/admin/shutdown", garden_common::ports::MOSS_HTTP))
        .send()
        .await
    {
        Ok(response) if response.status().is_success() => {
            tracing::info!("Sent graceful shutdown request to existing moss instance");

            // Wait up to 3 seconds for graceful shutdown
            for _ in 0..30 {
                tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;

                // Check if process is still running
                let still_running = check_moss_processes_exist();
                if !still_running {
                    tracing::info!("Existing moss instance shut down gracefully");
                    return Ok(());
                }
            }

            tracing::warn!("Graceful shutdown timed out after 3s, forcing kill");
        }
        Ok(response) => {
            tracing::warn!(status = ?response.status(), "Graceful shutdown request returned non-success status");
        }
        Err(e) => {
            tracing::debug!(error = ?e, "Could not connect to existing moss instance for graceful shutdown");
        }
    }

    // Graceful shutdown failed or timed out, force kill
    kill_existing_moss_processes()
}

/// Check if any moss processes are running (excluding current)
///
/// Returns true if at least one Moss process is running besides the current process.
/// This is used to detect if we need to shut down an existing instance before starting.
///
/// Platform-specific behavior:
/// - Windows: Uses `tasklist /FI "IMAGENAME eq garden-moss.exe"`
/// - Linux/macOS: Uses `pgrep moss`
pub fn check_moss_processes_exist() -> bool {
    #[cfg(target_os = "windows")]
    {
        use std::process::Command;
        let current_pid = std::process::id();

        if let Ok(output) = Command::new("tasklist")
            .args(["/FI", "IMAGENAME eq garden-moss.exe", "/FO", "CSV", "/NH"])
            .output()
        {
            if output.status.success() {
                let stdout = String::from_utf8_lossy(&output.stdout);
                for line in stdout.lines() {
                    if let Some(pid_str) = line.split(',').nth(1) {
                        let pid_str = pid_str.trim_matches('"').trim();
                        if let Ok(pid) = pid_str.parse::<u32>() {
                            if pid != current_pid {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        false
    }

    #[cfg(not(target_os = "windows"))]
    {
        use std::process::Command;
        let current_pid = std::process::id();

        if let Ok(output) = Command::new("pgrep").arg(garden_common::names::MOSS_BINARY).output() {
            if output.status.success() {
                let stdout = String::from_utf8_lossy(&output.stdout);
                for line in stdout.lines() {
                    if let Ok(pid) = line.trim().parse::<u32>() {
                        if pid != current_pid {
                            return true;
                        }
                    }
                }
            }
        }
        false
    }
}

/// Force kill all moss processes (excluding current)
///
/// Immediately terminates all Moss processes except the current one.
/// This is used as a fallback when graceful shutdown fails or times out.
///
/// Platform-specific behavior:
/// - Windows: Uses `taskkill /F /PID <pid>`
/// - Linux/macOS: Uses `kill -9 <pid>`
pub fn kill_existing_moss_processes() -> anyhow::Result<()> {
    #[cfg(target_os = "windows")]
    {
        use std::process::Command;

        // Get current process ID to avoid killing ourselves
        let current_pid = std::process::id();

        // Use tasklist to find garden-moss.exe processes
        let output = Command::new("tasklist")
            .args(["/FI", "IMAGENAME eq garden-moss.exe", "/FO", "CSV", "/NH"])
            .output()?;

        if output.status.success() {
            let stdout = String::from_utf8_lossy(&output.stdout);
            for line in stdout.lines() {
                // Parse CSV: "garden-moss.exe","PID","..."
                if let Some(pid_str) = line.split(',').nth(1) {
                    let pid_str = pid_str.trim_matches('"').trim();
                    if let Ok(pid) = pid_str.parse::<u32>() {
                        if pid != current_pid {
                            tracing::info!("Killing existing moss process: PID {}", pid);
                            let _ = Command::new("taskkill")
                                .args(["/PID", &pid.to_string(), "/F"])
                                .output();
                        }
                    }
                }
            }
        }
    }

    #[cfg(not(target_os = "windows"))]
    {
        use std::process::Command;

        // Get current process ID
        let current_pid = std::process::id();

        // Use pgrep to find moss processes
        let output = Command::new("pgrep")
            .arg("moss")
            .output()?;

        if output.status.success() {
            let stdout = String::from_utf8_lossy(&output.stdout);
            for line in stdout.lines() {
                if let Ok(pid) = line.trim().parse::<u32>() {
                    if pid != current_pid {
                        tracing::info!("Killing existing moss process: PID {}", pid);
                        let _ = Command::new("kill")
                            .args(&["-9", &pid.to_string()])
                            .output();
                    }
                }
            }
        }
    }

    Ok(())
}
