//! Command dispatch with middleware
//!
//! Handles common pre/post logic for command execution:
//! - Endpoint resolution (if required by command)
//! - Stone header display (if requested)
//! - Error handling and formatting

use garden_common::{GardenApiResponse, HardwareCapabilities};
use garden_rake::client::{resolve_target_endpoint, CachedStoneOps};
use garden_rake::commands::Command;
use garden_rake::context::CommandContext;
use garden_rake::discovery;
use garden_rake::tending;
use garden_rake::ui::{self, TerminalInfo};
use std::time::Duration;

/// Dispatch a command with standard middleware
///
/// This function:
/// 1. Resolves endpoint if required by command
/// 2. Displays stone header if requested
/// 3. Executes the command
pub async fn dispatch(
    cmd: &dyn Command,
    client: &reqwest::Client,
    at: Option<String>,
    quiet_mode: bool,
    fresh_mode: bool,
    cache: Option<&dyn CachedStoneOps>,
) -> anyhow::Result<()> {
    // Resolve endpoint if command requires it
    let (endpoint, stone_name) = if cmd.requires_endpoint() {
        let ep = resolve_endpoint(client, at, cache).await?;

        // Show stone header if requested
        if cmd.show_stone_header() {
            print_stone_header(client, &ep).await;
        }

        // Try to get stone name from capabilities
        let name = fetch_stone_name(client, &ep).await;
        (Some(ep), name)
    } else {
        (None, None)
    };

    // Build context
    let ctx = CommandContext::with_endpoint(
        client.clone(),
        endpoint.unwrap_or_default(),
        stone_name,
        quiet_mode,
        fresh_mode,
    );

    // Execute command
    cmd.execute(&ctx).await
}

/// Dispatch a local command (no endpoint needed)
#[allow(dead_code)]
pub async fn dispatch_local(
    cmd: &dyn Command,
    client: &reqwest::Client,
    quiet_mode: bool,
    fresh_mode: bool,
) -> anyhow::Result<()> {
    let ctx = CommandContext::without_endpoint(
        client.clone(),
        quiet_mode,
        fresh_mode,
    );

    cmd.execute(&ctx).await
}

/// Resolve endpoint with priority: --at > env var > cached tending > auto-discover
///
/// This is the authoritative endpoint resolution logic used throughout rake.
/// Includes reachability checking for cached tending and automatic fallback.
pub async fn resolve_endpoint(
    client: &reqwest::Client,
    at: Option<String>,
    cache: Option<&dyn CachedStoneOps>,
) -> anyhow::Result<String> {
    let term = TerminalInfo::detect();

    // Priority 1: --at flag (explicit override, deterministic)
    if let Some(explicit) = at {
        let endpoint = resolve_target_endpoint(client, &explicit, cache).await?;
        return Ok(endpoint);
    }

    // Priority 2: GARDEN_STONE environment variable
    if let Ok(env_endpoint) = std::env::var(garden_common::ENV_GARDEN_STONE) {
        tracing::info!(endpoint = %env_endpoint, "Using GARDEN_STONE environment variable");
        let endpoint = resolve_target_endpoint(client, &env_endpoint, cache).await?;
        return Ok(endpoint);
    }

    // Priority 3: Cached tending state (no TTL - persists until stone unreachable)
    if let Ok(tending) = tending::read_tending() {
        tracing::debug!(
            stone = %tending.stone_name,
            endpoint = %tending.endpoint,
            age_secs = tending.age_seconds(),
            "Checking cached tending state"
        );

        // Check if stone is reachable before using cached endpoint
        if is_stone_reachable(client, &tending.endpoint).await {
            tracing::info!(
                stone = %tending.stone_name,
                endpoint = %tending.endpoint,
                "Using cached tending state"
            );
            return Ok(tending.endpoint);
        } else {
            // Stone is offline - warn user and fall through to discovery
            println!(
                "{}{} Stone \"{}\" is sleeping (offline). Picking a new stone...",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("warn", term.supports_color),
                tending.stone_name
            );
            tracing::warn!(
                stone = %tending.stone_name,
                endpoint = %tending.endpoint,
                "Tended stone unreachable, falling back to discovery"
            );
            // Don't clear tending - user might want to return to this stone later
        }
    }

    // Priority 4: Auto-discover via UDP broadcast + cache result
    tracing::debug!("No cached tending, attempting auto-discovery");
    println!(
        "{}{} Discovering stones...",
        " ".repeat(ui::constants::DEFAULT_INDENT),
        ui::status_indicator("info", term.supports_color)
    );

    match discovery::discover_moss() {
        Ok(endpoint) => {
            tracing::info!(endpoint = %endpoint, "Auto-discovered stone");

            // Fetch capabilities to get stone name for cache and display
            let caps_url = format!("{}/capabilities", endpoint.trim_end_matches('/'));
            if let Ok(resp) = client
                .get(&caps_url)
                .timeout(Duration::from_secs(5))
                .send()
                .await
            {
                if let Ok(response) = resp.json::<GardenApiResponse<HardwareCapabilities>>().await {
                    let stone_name = &response.data.stone_name;
                    let _ = tending::write_tending(stone_name.clone(), endpoint.clone());

                    // Show which stone was picked
                    println!(
                        "{}{} Now tending to \"{}\"",
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("success", term.supports_color),
                        stone_name
                    );
                }
            }

            Ok(endpoint)
        }
        Err(_) => Err(anyhow::anyhow!(
            "No Zen Garden stones discovered.\n\n\
            Possible causes:\n\
              • No stones present on your network\n\
              • Firewall is blocking UDP broadcast (port 7184)\n\
              • Stone's garden-moss service is not running\n\n\
            To fix:\n\
              • Create a new stone: Run installer/NewStone.ps1\n\
              • Set tending: garden-rake tend <endpoint>\n\
              • Specify endpoint manually: garden-rake <command> --at http://<IP>:7185\n\
              • Or use a stone name: garden-rake <command> --at <stone-name>\n\
              • Check stone status: ssh stone@<ip> systemctl status garden-moss.service"
        )),
    }
}

/// Check if a stone is reachable (quick health check)
async fn is_stone_reachable(client: &reqwest::Client, endpoint: &str) -> bool {
    let health_url = format!("{}/health", endpoint.trim_end_matches('/'));
    match client
        .get(&health_url)
        .timeout(Duration::from_secs(2))
        .send()
        .await
    {
        Ok(resp) => resp.status().is_success(),
        Err(_) => false,
    }
}

/// Print stone header banner
async fn print_stone_header(client: &reqwest::Client, endpoint: &str) {
    let term = TerminalInfo::detect();

    // Fetch stone capabilities to get name and health
    let caps_url = format!("{}/capabilities", endpoint.trim_end_matches('/'));
    if let Ok(resp) = client
        .get(&caps_url)
        .timeout(Duration::from_secs(3))
        .send()
        .await
    {
        if let Ok(response) = resp.json::<GardenApiResponse<HardwareCapabilities>>().await {
            let stone_name = &response.data.stone_name;

            // Fetch health to get status
            let health_url = format!("{}/health", endpoint.trim_end_matches('/'));
            let health_status = if let Ok(health_resp) = client
                .get(&health_url)
                .timeout(Duration::from_secs(2))
                .send()
                .await
            {
                if let Ok(health_json) = health_resp.json::<serde_json::Value>().await {
                    // Map health to vitality language
                    if let Some(status) = health_json.get("status").and_then(|v| v.as_str()) {
                        match status {
                            garden_common::HEALTH_HEALTHY => garden_common::VITALITY_THRIVING,
                            garden_common::HEALTH_DEGRADED => garden_common::VITALITY_NEEDS_ATTENTION,
                            garden_common::HEALTH_UNHEALTHY => garden_common::VITALITY_WITHERING,
                            _ => garden_common::VITALITY_DORMANT,
                        }
                    } else {
                        garden_common::VITALITY_THRIVING
                    }
                } else {
                    garden_common::VITALITY_DORMANT
                }
            } else {
                garden_common::VITALITY_DORMANT
            };

            println!(
                "{}",
                ui::stone_banner(stone_name, health_status, term.supports_color)
            );
            println!();
        }
    }
}

/// Fetch stone name from capabilities
async fn fetch_stone_name(client: &reqwest::Client, endpoint: &str) -> Option<String> {
    let caps_url = format!("{}/capabilities", endpoint.trim_end_matches('/'));
    if let Ok(resp) = client
        .get(&caps_url)
        .timeout(Duration::from_secs(3))
        .send()
        .await
    {
        if let Ok(response) = resp.json::<GardenApiResponse<HardwareCapabilities>>().await {
            return Some(response.data.stone_name);
        }
    }
    None
}
