//! Service installation and update management
//!
//! Windows-specific service management:
//! - Installing Moss as a Windows service
//! - Handling service updates
//! - Cleaning up after updates
//!
//! Future: Add Linux systemd and macOS launchd support

/// Install Moss as a Windows service
///
/// Handles both installation from removable media (copies to ProgramData) and
/// permanent locations. Creates the ZenGardenMoss service and starts it.
///
/// This implements both "take-root" (zen) and "install-service" (normative) commands.
///
/// # Windows Service Commands
/// - `sc query ZenGardenMoss` - View status
/// - `sc stop ZenGardenMoss` - Stop service
/// - `sc start ZenGardenMoss` - Start service
/// - `sc delete ZenGardenMoss` - Remove service (uproot)
#[cfg(target_os = "windows")]
pub async fn install_windows_service() -> anyhow::Result<()> {
    use std::process::Command;
    use std::path::PathBuf;

    println!("🌱 Taking root as Windows service...");
    println!();

    let current_exe = std::env::current_exe()?;

    // Check if running from removable media
    let is_removable = crate::infra::is_running_from_removable_media(&current_exe)?;

    let install_exe = if is_removable {
        println!("⚠️  Detected execution from removable media");
        println!("   Installing to permanent location...");
        println!();

        // Copy to ProgramData (system-wide, admin-accessible)
        let install_dir = PathBuf::from(r"C:\ProgramData\ZenGarden");
        std::fs::create_dir_all(&install_dir)?;

        let target_exe = install_dir.join("garden-moss.exe");

        // Copy executable
        std::fs::copy(&current_exe, &target_exe)?;
        println!("✓ Copied to: {}", target_exe.display());
        println!();

        target_exe
    } else {
        current_exe
    };

    let exe_path_str = install_exe.to_string_lossy();

    // Check if service already exists
    let check_output = Command::new("sc")
        .args(["query", "ZenGardenMoss"])
        .output()?;

    if check_output.status.success() {
        println!("⚠️  Service already exists");
        println!("   To reinstall, first remove: sc delete ZenGardenMoss");
        return Err(anyhow::anyhow!("Service already installed"));
    }

    // Create service using sc.exe with proper arguments
    // Note: sc.exe requires space after = in key=value pairs
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
        .output()?;

    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        let stdout = String::from_utf8_lossy(&output.stdout);
        eprintln!("Failed to create service:");
        eprintln!("  {}", stderr);
        eprintln!("  {}", stdout);
        return Err(anyhow::anyhow!("Service creation failed"));
    }

    println!("✓ Service rooted successfully");
    println!();

    // Set service description
    let _ = Command::new("sc")
        .args([
            "description",
            "ZenGardenMoss",
            "Zen Garden stone orchestration daemon - manages container services",
        ])
        .output();

    // Start the service
    println!("🌅 Waking the service...");
    let output = Command::new("sc")
        .args(["start", "ZenGardenMoss"])
        .output()?;

    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        let stdout = String::from_utf8_lossy(&output.stdout);
        eprintln!("⚠️  Failed to start service:");
        eprintln!("  {}", stderr);
        eprintln!("  {}", stdout);
        println!();
        println!("The service is installed but not running.");
        println!("Start it manually with: sc start ZenGardenMoss");
    } else {
        println!("✓ Service is awake and thriving");
    }

    println!();
    println!("🌿 Moss has taken root as a Windows service");
    println!();
    println!("Installation path: {}", exe_path_str);
    println!();
    println!("Management commands:");
    println!("  sc query ZenGardenMoss      View status");
    println!("  sc stop ZenGardenMoss       Stop service");
    println!("  sc start ZenGardenMoss      Start service");
    println!("  sc delete ZenGardenMoss     Remove service (uproot)");
    println!();
    println!("Access the web UI at: http://localhost:7185");

    Ok(())
}

/// Finalize Windows service update
///
/// Called when running as garden-moss-new.exe after an update.
/// Waits for the old process to exit, replaces the binary, and restarts the service.
#[cfg(target_os = "windows")]
pub async fn finalize_service_update() -> anyhow::Result<()> {
    use std::process::Command;

    println!("Finalizing Moss update...");

    let current_exe = std::env::current_exe()?;
    let exe_dir = current_exe.parent().ok_or_else(|| anyhow::anyhow!("No parent directory"))?;
    let target_exe = exe_dir.join("garden-moss.exe");

    // Wait for old process to exit (up to 30 seconds)
    println!("Waiting for old Moss process to exit...");
    for attempt in 1..=60 {
        let output = Command::new("tasklist")
            .args(["/FI", "IMAGENAME eq garden-moss.exe"])
            .output()?;

        let stdout = String::from_utf8_lossy(&output.stdout);
        if !stdout.contains("garden-moss.exe") {
            break;
        }

        if attempt == 60 {
            eprintln!("Timeout waiting for old process to exit");
            return Err(anyhow::anyhow!("Old process did not exit"));
        }

        tokio::time::sleep(tokio::time::Duration::from_millis(500)).await;
    }

    println!("Old process exited. Replacing binary...");
    std::fs::copy(&current_exe, &target_exe)?;
    println!("✓ Binary replaced successfully");

    // Check if running as service
    let is_service = std::env::var("RUNNING_AS_SERVICE").is_ok();

    if is_service {
        println!("Starting Moss service...");
        let _ = Command::new("sc")
            .args(["start", "ZenGardenMoss"])
            .output()?;
        println!("✓ Service start triggered");
    } else {
        println!("Launching new Moss...");
        Command::new(&target_exe)
            .arg("--cleanup-old")
            .spawn()?;
        println!("✓ New Moss launched");
    }

    println!("Update complete. This process will now exit.");
    Ok(())
}

/// Cleanup old binary after service update
///
/// Removes the garden-moss-new.exe file after a successful update.
/// Waits for the update process to exit before removing.
#[cfg(target_os = "windows")]
pub async fn cleanup_after_service_update() -> anyhow::Result<()> {
    use std::process::Command;

    let current_exe = std::env::current_exe()?;
    let exe_dir = current_exe.parent().ok_or_else(|| anyhow::anyhow!("No parent directory"))?;
    let old_exe = exe_dir.join("garden-moss-new.exe");

    if old_exe.exists() {
        // Wait for garden-moss-new.exe process to exit
        for _ in 1..=20 {
            let output = Command::new("tasklist")
                .args(["/FI", "IMAGENAME eq garden-moss-new.exe"])
                .output()?;

            let stdout = String::from_utf8_lossy(&output.stdout);
            if !stdout.contains("garden-moss-new.exe") {
                break;
            }

            tokio::time::sleep(tokio::time::Duration::from_millis(500)).await;
        }

        // Remove old binary
        std::fs::remove_file(&old_exe).ok();
    }

    // Continue with normal startup (fall through to main logic)
    Ok(())
}
