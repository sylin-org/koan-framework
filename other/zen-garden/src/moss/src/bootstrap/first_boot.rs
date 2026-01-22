//! First-time stone initialization
//!
//! Handles one-time setup for new stone installations:
//! - Generates unique stone name with collision detection
//! - Configures system hostname
//! - Updates configuration files
//! - Creates MOTD (message of the day)
//!
//! This runs when a stone boots with the default "stone-01" name.

/// Run first-boot initialization sequence
///
/// Displays progress on console, generates unique name, configures hostname, and creates MOTD.
///
/// # Arguments
/// * `old_name` - Current temporary stone name (e.g., "stone-01")
/// * `port` - HTTP server port for management URL
///
/// # Returns
/// The newly generated stone name
///
/// # Process
/// 1. Generate unique stone name with collision detection
/// 2. Configure system hostname (updates /etc/hostname, /etc/hosts)
/// 3. Restart Avahi mDNS service
/// 4. Test mDNS resolution
/// 5. Update Moss configuration file
/// 6. Create MOTD with management URL
pub async fn run_first_boot_initialization(old_name: &str, port: u16) -> anyhow::Result<String> {
    use crate::console;

    console::display_header("Zen Garden - First Boot")?;
    console::tty_write("")?;
    console::display_item("Temporary Name", old_name)?;
    console::display_wait("Starting first-time setup")?;
    console::tty_write("")?;

    // Generate unique name with collision detection
    console::display_header("Name Generation")?;
    let new_name = console::generate_unique_name().await?;
    console::tty_write("")?;

    // Configure system hostname
    console::display_header("System Configuration")?;
    console::set_hostname(&new_name).await?;
    console::update_hosts_file(old_name, &new_name).await?;
    console::restart_avahi().await?;
    console::test_mdns_resolution(&new_name).await?;
    console::tty_write("")?;

    // Update Moss configuration
    console::display_header("Moss Configuration")?;
    console::update_moss_config(&new_name).await?;
    console::tty_write("")?;

    // Create MOTD
    let url = format!("http://{}:{}", console::get_local_ip_sync(), port);
    console::write_motd(&new_name, &url)?;
    console::tty_write("")?;

    // Final summary
    console::display_header("Setup Complete")?;
    console::display_item("Stone Name", &new_name)?;
    console::display_item("Management URL", &url)?;
    console::display_item("Username", garden_common::constants::STONE_USER)?;
    console::display_item("Password", garden_common::constants::STONE_PASSWORD)?;
    console::tty_write("")?;
    console::display_success("Stone is ready for use")?;
    console::tty_write("")?;

    Ok(new_name)
}
