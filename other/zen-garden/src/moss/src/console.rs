//! Console output module for first-boot initialization
//! 
//! Provides functions to write formatted output to TTY console during system setup.
//! Design: Box frames for headers only, simple indentation for content, no emojis.

use std::fs::OpenOptions;
use std::io::Write;
use anyhow::{Context, Result};

/// Ensure /etc is writable with retries for early-boot timing issues
/// Returns Ok(true) if writeable, Ok(false) if permanently read-only
pub async fn ensure_etc_writable() -> Result<bool> {
    const MAX_RETRIES: u32 = 10;
    const RETRY_DELAY_MS: u64 = 500;
    
    let test_path = "/etc/.moss-write-test";
    
    for attempt in 1..=MAX_RETRIES {
        match std::fs::write(test_path, "test") {
            Ok(_) => {
                // Writable - cleanup test file
                let _ = std::fs::remove_file(test_path);
                if attempt > 1 {
                    tracing::info!(attempt, "/ etc became writable after retries");
                }
                return Ok(true);
            }
            Err(e) if e.kind() == std::io::ErrorKind::PermissionDenied || 
                       e.raw_os_error() == Some(30) => { // EROFS = 30
                
                if attempt == 1 {
                    tracing::warn!("/etc is not yet writable, will retry (may be early boot timing)");
                }
                
                // On first attempt, try remounting
                if attempt == 1 {
                    let output = tokio::process::Command::new("mount")
                        .args(["-o", "remount,rw", "/"])
                        .output()
                        .await;
                    
                    if let Ok(result) = output {
                        if result.status.success() {
                            tracing::info!("Attempted remount of root filesystem as read-write");
                        }
                    }
                }
                
                // Wait before retry unless it's the last attempt
                if attempt < MAX_RETRIES {
                    tokio::time::sleep(tokio::time::Duration::from_millis(RETRY_DELAY_MS)).await;
                } else {
                    tracing::error!(
                        attempts = MAX_RETRIES,
                        "/ etc remained read-only after all retries"
                    );
                    return Ok(false);
                }
            }
            Err(e) => {
                return Err(anyhow::anyhow!("Unexpected error testing /etc writability: {}", e));
            }
        }
    }
    
    Ok(false)
}

/// Write text directly to TTY1 console
/// Falls back to stdout if TTY not available
pub fn tty_write(text: &str) -> Result<()> {
    // Try to open /dev/tty1 for writing
    match OpenOptions::new()
        
        .append(true)
        .open("/dev/tty1")
    {
        Ok(mut tty) => {
            writeln!(tty, "{}", text)
                .context("Failed to write to /dev/tty1")?;
            tty.flush()
                .context("Failed to flush TTY")?;
        }
        Err(_) => {
            // Fallback to stdout (for testing or non-Linux systems)
            println!("{}", text);
        }
    }
    Ok(())
}

/// Display a header with box frame
/// Example:
/// ╔══════════════════════════════════════╗
/// ║       Zen Garden - First Boot        ║
/// ╚══════════════════════════════════════╝
pub fn display_header(title: &str) -> Result<()> {
    let width = 40;
    let padding = (width - title.len() - 2) / 2;
    let extra = if (width - title.len() - 2) % 2 == 1 { 1 } else { 0 };
    
    let top = format!("╔{}╗", "═".repeat(width - 2));
    let middle = format!("║{}{}{}║", 
        " ".repeat(padding),
        title,
        " ".repeat(padding + extra)
    );
    let bottom = format!("╚{}╝", "═".repeat(width - 2));
    
    tty_write("")?;
    tty_write(&top)?;
    tty_write(&middle)?;
    tty_write(&bottom)?;
    tty_write("")?;
    Ok(())
}

/// Display an item with simple indentation
/// Example: "  Stone Name: stone-meadow-42"
pub fn display_item(label: &str, value: &str) -> Result<()> {
    tty_write(&format!("  {}: {}", label, value))
}

/// Display a success message with [OK] indicator
/// Example: "  [OK] Docker daemon connected"
pub fn display_success(message: &str) -> Result<()> {
    tty_write(&format!("  [OK] {}", message))
}

/// Display an error message with [FAIL] indicator
/// Example: "  [FAIL] Failed to generate name"
pub fn display_error(message: &str) -> Result<()> {
    tty_write(&format!("  [FAIL] {}", message))
}

/// Display a waiting/progress message with [WAIT] indicator
/// Example: "  [WAIT] Checking name availability..."
pub fn display_wait(message: &str) -> Result<()> {
    tty_write(&format!("  [WAIT] {}", message))
}

/// Check if this is a first run (stone name matches "stone-new-*")
/// Check if this is the first run by looking for the initialization flag file
pub fn is_first_run() -> bool {
    !std::path::Path::new(garden_common::names::FIRST_RUN_FLAG).exists()
}

/// Mark first-run initialization as complete
pub async fn mark_first_run_complete() -> Result<()> {
    tokio::fs::write(garden_common::names::FIRST_RUN_FLAG, "")
        .await
        .context("Failed to create first-run completion flag")?;
    Ok(())
}

/// Generate a unique stone name with collision detection
/// 
/// Uses adjective-noun pattern with mDNS collision checking (10 attempts).
/// Falls back to hex suffix if all attempts collide.
pub async fn generate_unique_name() -> Result<String> {
    const ADJECTIVES: &[&str] = &[
        "azure", "bronze", "coral", "crimson", "emerald", "golden", "indigo",
        "jade", "lunar", "marble", "obsidian", "pearl", "quartz", "ruby",
        "silver", "topaz", "turquoise", "violet", "amber", "crystal"
    ];
    
    const NOUNS: &[&str] = &[
        "meadow", "summit", "river", "forest", "canyon", "valley", "harbor",
        "glacier", "prairie", "desert", "delta", "ridge", "plateau", "grove",
        "basin", "stream", "cliff", "shore", "peak", "dune"
    ];
    
    use rand::seq::SliceRandom;
    use rand::SeedableRng;
    // Use StdRng which is Send-safe for background tasks
    let mut rng = rand::rngs::StdRng::from_entropy();
    
    // Try 10 random combinations
    for attempt in 1..=10 {
        let adjective = ADJECTIVES.choose(&mut rng).unwrap();
        let noun = NOUNS.choose(&mut rng).unwrap();
        let candidate = format!("stone-{}-{}", adjective, noun);
        
        display_wait(&format!("Checking availability: {} (attempt {}/10)", candidate, attempt))?;
        
        // Check mDNS collision
        if !check_mdns_collision(&candidate).await {
            display_success(&format!("Name available: {}", candidate))?;
            return Ok(candidate);
        }
        
        display_wait(&format!("Name collision detected: {}", candidate))?;
    }
    
    // All attempts failed, use hex suffix
    let hex_suffix = format!("{:04x}", rand::random::<u16>());
    let fallback = format!("stone-{}", hex_suffix);
    display_wait(&format!("Using fallback name: {}", fallback))?;
    Ok(fallback)
}

/// Check if a stone name already exists on the network via mDNS
/// Returns true if collision detected, false if available
async fn check_mdns_collision(name: &str) -> bool {
    // Query mDNS for _moss._tcp.local with instance name matching stone name
    // Timeout after 2 seconds
    let mdns_name = format!("{}._moss._tcp.local", name);
    
    // Use avahi-browse to check for existing service
    match tokio::process::Command::new("avahi-browse")
        .args(["-t", "-r", "-p", "_moss._tcp"])
        .output()
        .await
    {
        Ok(output) => {
            let stdout = String::from_utf8_lossy(&output.stdout);
            // Check if our stone name appears in the output
            stdout.contains(&mdns_name) || stdout.contains(name)
        }
        Err(_) => {
            // avahi-browse not available or failed, assume no collision
            false
        }
    }
}

/// Set system hostname by writing directly to /etc/hostname
pub async fn set_hostname(name: &str) -> Result<()> {
    display_wait(&format!("Setting hostname to {}", name))?;
    
    // Write directly to /etc/hostname (more reliable than hostnamectl with NoNewPrivileges)
    tokio::fs::write("/etc/hostname", format!("{}\n", name))
        .await
        .context("Failed to write /etc/hostname")?;
    
    // Also set the running hostname using sethostname syscall
    // This requires the CAP_SYS_ADMIN capability but works with NoNewPrivileges
    let output = tokio::process::Command::new("hostname")
        .arg(name)
        .output()
        .await
        .context("Failed to execute hostname command")?;
    
    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        display_error(&format!("Warning: hostname command failed: {}", stderr))?;
        // Don't fail completely - the file write succeeded
    }
    
    display_success(&format!("Hostname set to {}", name))?;
    Ok(())
}

/// Read the system hostname from /etc/hostname.
///
/// This is the source of truth for what will be announced over mDNS (`<hostname>.local`).
pub async fn get_hostname() -> Result<String> {
    let content = tokio::fs::read_to_string("/etc/hostname")
        .await
        .context("Failed to read /etc/hostname")?;
    let hostname = content.trim().to_string();
    if hostname.is_empty() {
        anyhow::bail!("/etc/hostname was empty");
    }
    Ok(hostname)
}

/// Update /etc/hosts to reflect a hostname change.
pub async fn update_hosts_file(old_name: &str, new_name: &str) -> Result<()> {
    display_wait("Updating /etc/hosts")?;
    
    // Read current hosts file
    let hosts_content = tokio::fs::read_to_string("/etc/hosts")
        .await
        .context("Failed to read /etc/hosts")?;
    
    // Replace explicit old hostname entries, plus legacy stone-new-* entries.
    let updated_content = hosts_content
        .lines()
        .map(|line| {
            if line.contains(old_name) {
                line.replace(old_name, new_name)
            } else if line.contains("stone-new-") {
                // Back-compat for older installers that used stone-new-<guid>
                line.replace("stone-new-", new_name.strip_prefix("stone-").unwrap_or(new_name))
            } else {
                line.to_string()
            }
        })
        .collect::<Vec<_>>()
        .join("\n");
    
    // Write back
    tokio::fs::write("/etc/hosts", updated_content)
        .await
        .context("Failed to write /etc/hosts")?;
    
    display_success("Updated /etc/hosts")?;
    Ok(())
}

/// Restart avahi-daemon to update mDNS announcements
pub async fn restart_avahi() -> Result<()> {
    display_wait("Restarting avahi-daemon")?;
    
    let output = tokio::process::Command::new("systemctl")
        .args(["restart", "avahi-daemon"])
        .output()
        .await
        .context("Failed to restart avahi-daemon")?;
    
    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        // Don't fail - avahi restart is optional
        display_error(&format!("Warning: avahi restart failed: {}", stderr))?;
        tty_write("  (mDNS will update on next system reboot)")?;
    } else {
        display_success("Avahi daemon restarted")?;
    }
    Ok(())
}

/// Test mDNS resolution by pinging the stone's hostname
pub async fn test_mdns_resolution(stone_name: &str) -> Result<()> {
    display_wait(&format!("Testing mDNS resolution for {}.local", stone_name))?;
    
    // Wait a moment for avahi to propagate the announcement
    tokio::time::sleep(tokio::time::Duration::from_secs(2)).await;
    
    // Try to ping the .local hostname (single ping, 2 second timeout)
    let hostname = format!("{}.local", stone_name);
    let output = tokio::process::Command::new("ping")
        .args(["-c", "1", "-W", "2", &hostname])
        .output()
        .await
        .context("Failed to execute ping command")?;
    
    if output.status.success() {
        display_success(&format!("mDNS resolution confirmed: {}.local is reachable", stone_name))?;
    } else {
        display_error(&format!("Warning: {}.local not yet reachable via mDNS", stone_name))?;
        tty_write("  (May take a few moments for network propagation)")?;
    }
    
    Ok(())
}

/// Write MOTD (Message of the Day) file
pub fn write_motd(stone_name: &str, url: &str) -> Result<()> {
    display_wait("Creating message of the day")?;
    
    let motd_content = format!(
r#"
╔══════════════════════════════════════╗
║       Zen Garden Stone Ready         ║
╚══════════════════════════════════════╝

  Stone Name: {}
  Management URL: {}
  Username: stone
  Password: garden

  Run 'systemctl status garden-moss' to check service status
  Visit {} to manage services

"#,
        stone_name,
        url,
        url
    );
    
    std::fs::write("/etc/motd", motd_content)
        .context("Failed to write /etc/motd")?;
    
    display_success("Message of the day created")?;
    Ok(())
}

/// Update Moss configuration file with new stone name
pub async fn update_moss_config(new_name: &str) -> Result<()> {
    display_wait("Updating Moss configuration")?;
    
    let config_path = format!("{}/{}", garden_common::names::CONFIG_DIR, garden_common::names::MOSS_CONFIG);
    
    // Read current config
    let config_content = tokio::fs::read_to_string(&config_path)
        .await
        .context(format!("Failed to read {}", garden_common::names::MOSS_CONFIG))?;
    
    let mut found = false;
    let mut updated_lines: Vec<String> = Vec::new();
    for line in config_content.lines() {
        let trimmed = line.trim();

        // Preferred modern key
        if trimmed.starts_with("stone_name") {
            let indent = line.len() - line.trim_start().len();
            updated_lines.push(format!("{}stone_name = \"{}\"", " ".repeat(indent), new_name));
            found = true;
            continue;
        }

        // Legacy key used in older templates
        if trimmed.starts_with("name =") || trimmed.starts_with("name=") {
            let indent = line.len() - line.trim_start().len();
            updated_lines.push(format!("{}name = \"{}\"", " ".repeat(indent), new_name));
            found = true;
            continue;
        }

        updated_lines.push(line.to_string());
    }

    // If neither key existed, insert a modern stone_name near the top (after any header comments).
    if !found {
        let mut inserted = false;
        let mut with_insert: Vec<String> = Vec::new();
        for line in &updated_lines {
            if !inserted {
                let t = line.trim();
                if t.is_empty() || t.starts_with('#') {
                    with_insert.push(line.clone());
                    continue;
                }
                with_insert.push(format!("stone_name = \"{}\"", new_name));
                inserted = true;
            }
            with_insert.push(line.clone());
        }
        if !inserted {
            with_insert.push(format!("stone_name = \"{}\"", new_name));
        }
        updated_lines = with_insert;
    }

    let updated_content = updated_lines.join("\n");
    
    // Write back
    tokio::fs::write(&config_path, updated_content)
        .await
        .context(format!("Failed to write {}", garden_common::names::MOSS_CONFIG))?;
    
    display_success("Configuration updated")?;
    Ok(())
}

/// Get local IP address synchronously (for use in non-async contexts)
pub fn get_local_ip_sync() -> String {
    use std::net::IpAddr;
    
    if let Ok(addrs) = local_ip_address::list_afinet_netifas() {
        for (_, ip) in addrs {
            if let IpAddr::V4(ipv4) = ip {
                // Skip loopback and link-local addresses
                if !ipv4.is_loopback() && !ipv4.is_link_local() {
                    return ipv4.to_string();
                }
            }
        }
    }
    
    // Fallback to hostname-based lookup
    "127.0.0.1".to_string()
}

