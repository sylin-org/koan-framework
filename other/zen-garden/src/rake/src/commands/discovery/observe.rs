//! Observe command - garden overview with topology cache
//!
//! Displays a comprehensive view of all stones in the garden:
//! - Default: Queries tended Moss's topology cache (populated by chirps)
//! - Fresh mode (--fresh): Triggers UDP discovery for real-time network scan
//! - Fallback to Lantern registry if available

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::discovery;
use crate::layout::{IndentLevel, Layout};
use crate::suggestions;
use crate::tending;
use crate::ui;
use async_trait::async_trait;
use garden_common::{CliFormatter, GardenApiResponse, TopologyEntry};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::Duration;

/// Global counter for stones displayed (for footer)
static STONE_COUNT: AtomicUsize = AtomicUsize::new(0);

/// Stone data from topology cache (lightweight, no HTTP calls)
struct TopologyStoneData<'a> {
    entry: &'a TopologyEntry,
}

/// Observe command for garden overview
pub struct ObserveCommand {
    pub stone_filter: Option<String>,
    pub offering_filter: Option<String>,
    pub quiet_mode: bool,
}

impl ObserveCommand {
    pub fn new(stone_filter: Option<String>, offering_filter: Option<String>, quiet_mode: bool) -> Self {
        Self {
            stone_filter,
            offering_filter,
            quiet_mode,
        }
    }
}

#[async_trait]
impl Command for ObserveCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        observe_garden(ctx, self.stone_filter.clone(), self.offering_filter.clone()).await?;

        // Self-teaching suggestions
        suggestions::print_suggestions(cmd::OBSERVE, self.quiet_mode);

        Ok(())
    }

    fn requires_endpoint(&self) -> bool {
        false // Observe discovers all stones, doesn't need a specific endpoint
    }

    fn name(&self) -> &'static str {
        cmd::OBSERVE
    }
}

/// Main observe implementation
async fn observe_garden(
    ctx: &CommandContext,
    stone_filter: Option<String>,
    offering_filter: Option<String>,
) -> anyhow::Result<()> {
    // Reset stone counter
    STONE_COUNT.store(0, Ordering::SeqCst);

    // Keep offering_filter as-is for Lantern call, create offerings_filter for legacy code
    let offerings_filter: Option<Vec<String>> = offering_filter.as_ref().map(|s| {
        s.split(',')
            .map(|o| o.trim().to_lowercase())
            .collect()
    });

    // Get currently tended stone name for marking (compare by name, not endpoint)
    let tended_state = tending::read_tending().ok();
    let tended_stone_name = tended_state
        .as_ref()
        .filter(|s| s.is_valid())
        .map(|s| s.stone_name.clone());

    // Display header immediately (no waiting for discovery)
    let layout = Layout::new();

    // Verbose mode diagnostics
    if ctx.verbose > 0 {
        layout.blank();
        layout.line(&format!("Verbose mode enabled (level {})", ctx.verbose))
            .level(IndentLevel::Card)
            .tag("verbose")
            .print();
        if let Some(ref tended) = tended_state {
            layout.field("Tending")
                .value(format!("{} at {}", tended.stone_name, tended.endpoint))
                .level(IndentLevel::Card)
                .tag("verbose")
                .print();
        } else {
            layout.field("Tending")
                .value("none")
                .level(IndentLevel::Card)
                .tag("verbose")
                .print();
        }
        layout.field("Fresh mode")
            .value(if ctx.fresh_mode { "enabled" } else { "disabled" })
            .level(IndentLevel::Card)
            .tag("verbose")
            .print();
    }

    // Main header
    layout.blank();
    layout.header("GARDEN OBSERVE")
        .level(IndentLevel::Card)
        .underline()
        .underline_len(47)
        .print();

    // Track if we need to update tending (when falling back from unreachable stone)
    let mut should_update_tending = false;

    // Fresh mode: For detailed stone info with resource metrics
    // Note: Fresh mode requires UDP discovery + HTTP fetches (not yet refactored)
    if ctx.fresh_mode {
        // TODO: Implement fresh mode with detailed ServiceInfo fetches
        // For now, just note that fresh mode is ignored and continue to topology view
        if ctx.verbose > 0 {
            layout.line("Note: Fresh mode ignored, using topology cache")
                .level(IndentLevel::Card)
                .print();
        }
    }

    // Default path: Query topology from tended Moss
    if let Some(ref tended) = tending::read_tending().ok() {
        layout.line(&format!("querying topology from {}...", tended.stone_name))
            .level(IndentLevel::Card)
            .print();
        layout.blank();

        // Try to get topology from tended Moss
        let topology_url = format!("{}/api/v1/garden/topology", tended.endpoint.trim_end_matches('/'));

        if ctx.verbose > 0 {
            layout.field("GET")
                .value(&topology_url)
                .level(IndentLevel::Card)
                .tag("verbose")
                .print();
        }

        match ctx.client.get(&topology_url).timeout(Duration::from_secs(5)).send().await {
            Ok(resp) if resp.status().is_success() => {
                match resp.json::<GardenApiResponse<Vec<TopologyEntry>>>().await {
                    Ok(api_response) => {
                        // Display topology from cache
                        let stones = api_response.data;

                        if ctx.verbose > 0 {
                            layout.field("Response")
                                .value(format!("{} stones in topology", stones.len()))
                                .level(IndentLevel::Card)
                                .tag("verbose")
                                .print();
                            for stone in &stones {
                                layout.line(&format!("- {} (id: {}, endpoint: {}, health: {})",
                                    stone.stone_name, stone.stone_id, stone.endpoint, stone.health))
                                    .level(IndentLevel::Section)
                                    .tag("verbose")
                                    .print();
                            }
                            layout.blank();
                        }

                        display_topology_entries(&stones, &stone_filter, &offerings_filter, tended_stone_name.as_deref(), ctx.verbose);
                        display_footer();
                        return Ok(());
                    }
                    Err(e) => {
                        tracing::warn!(error = ?e, "Failed to parse topology JSON");
                        if ctx.verbose > 0 {
                            layout.field("JSON parse error")
                                .value(e.to_string())
                                .level(IndentLevel::Card)
                                .tag("verbose")
                                .print();
                        }
                        // Fall through to Lantern/fallback
                    }
                }
            }
            Ok(resp) => {
                let status = resp.status();
                tracing::warn!(status = ?status, "Tended stone returned error, falling back to discovery");
                if ctx.verbose > 0 {
                    layout.field("Response status")
                        .value(status.to_string())
                        .level(IndentLevel::Card)
                        .tag("verbose")
                        .print();
                }
                layout.status(&format!("Stone \"{}\" is sleeping (offline). Picking a new stone...", tended.stone_name))
                    .level(IndentLevel::Card)
                    .warn()
                    .print();
                should_update_tending = true;
            }
            Err(e) => {
                tracing::warn!(error = ?e, "Failed to reach tended stone, falling back to discovery");
                if ctx.verbose > 0 {
                    layout.field("Connection error")
                        .value(e.to_string())
                        .level(IndentLevel::Card)
                        .tag("verbose")
                        .print();
                }
                layout.status(&format!("Stone \"{}\" is sleeping (offline). Picking a new stone...", tended.stone_name))
                    .level(IndentLevel::Card)
                    .warn()
                    .print();
                should_update_tending = true;
            }
        }
    }

    // Fallback: Start background Lantern discovery
    discovery::discover_lantern_background();

    // Check if Lantern was discovered in background (will be instant if cached)
    let lantern_endpoint = discovery::get_cached_lantern();

    if let Some(ref lantern) = lantern_endpoint {
        tracing::info!(endpoint = %lantern, "Using cached Lantern endpoint for topology queries");

        // Fetch topology from Lantern
        let topology_url = format!("{}/api/v1/stones", lantern);
        match ctx.client.get(&topology_url).timeout(Duration::from_secs(5)).send().await {
            Ok(resp) if resp.status().is_success() => {
                if let Ok(topology) = resp.json::<garden_common::LanternTopology>().await {
                    // Display Lantern-sourced topology
                    display_lantern_topology(&topology, offering_filter.as_deref(), tended_stone_name.as_deref());
                    display_footer();
                    return Ok(());
                }
            }
            Ok(resp) => {
                tracing::warn!(status = ?resp.status(), "Lantern returned error, falling back to UDP discovery");
            }
            Err(e) => {
                tracing::warn!(error = ?e, "Failed to reach Lantern, falling back to UDP discovery");
            }
        }
    }

    layout.line("discovering stones...")
        .level(IndentLevel::Card)
        .print();
    layout.blank();
    layout.line("No Lantern registry found")
        .level(IndentLevel::Card)
        .print();
    layout.blank();
    layout.line("Try: garden-rake observe --tend <stone>  (to specify a tended stone)")
        .level(IndentLevel::Card)
        .tag("hint")
        .print();

    display_footer();
    Ok(())
}



/// Display stones from topology API response (from tended Moss's cache)
/// 
/// This function uses data already in the topology cache - NO HTTP calls per stone.
/// The TopologyEntry already contains services and capabilities from the chirp broadcasts.
fn display_topology_entries(
    stones: &[TopologyEntry],
    stone_filter: &Option<String>,
    offerings_filter: &Option<Vec<String>>,
    tended_stone_name: Option<&str>,
    verbose: u8,
) {
    let layout = Layout::new();

    if stones.is_empty() {
        layout.line("No stones in topology cache")
            .level(IndentLevel::Card)
            .print();
        layout.blank();
        layout.line("Try: garden-rake observe --fresh  (to scan network)")
            .level(IndentLevel::Card)
            .tag("hint")
            .print();
        return;
    }

    // Filter stones if name specified
    let filtered_stones: Vec<&TopologyEntry> = if let Some(ref filter_name) = stone_filter {
        stones.iter()
            .filter(|s| s.stone_name.eq_ignore_ascii_case(filter_name))
            .collect()
    } else {
        stones.iter().collect()
    };

    if filtered_stones.is_empty() && stone_filter.is_some() {
        layout.status(&format!("Stone '{}' not found in topology", stone_filter.as_ref().unwrap()))
            .level(IndentLevel::Card)
            .error()
            .print();
        return;
    }

    // Display each stone using TopologyEntry data directly (no HTTP calls, no conversion)
    for stone in filtered_stones {
        // Skip stones without capabilities
        if stone.capabilities.is_none() {
            if verbose > 0 {
                layout.status(&format!("Stone {} has no capabilities data (may be offline)", stone.stone_name))
                    .level(IndentLevel::Card)
                    .tag("verbose")
                    .print();
            }
            continue;
        }

        // Compare stone names case-insensitively for tended marker
        let is_tended = tended_stone_name
            .map(|t| t.eq_ignore_ascii_case(&stone.stone_name))
            .unwrap_or(false);

        // Display directly from TopologyEntry - no conversion needed
        let topology_data = TopologyStoneData { entry: stone };
        let _ = display_topology_stone(&topology_data, offerings_filter, is_tended);
    }
}



/// Display topology from Lantern registry
fn display_lantern_topology(topology: &garden_common::LanternTopology, offering_filter: Option<&str>, tended_stone_name: Option<&str>) {
    let fmt = CliFormatter::new();
    let indent = " ".repeat(ui::constants::DEFAULT_INDENT);
    let term = ui::TerminalInfo::detect();

    if topology.stones.is_empty() {
        println!("{}No stones registered", indent);
        return;
    }

    for stone in &topology.stones {
        STONE_COUNT.fetch_add(1, Ordering::SeqCst);

        // Compare stone names case-insensitively
        let is_tended = tended_stone_name
            .map(|t| t.eq_ignore_ascii_case(&stone.name))
            .unwrap_or(false);
        let tended_marker = if is_tended { ui::tended_marker(term.supports_color) } else { String::new() };

        // Stone name with status and tended marker on same line - preserve original case
        let status_indicator = ui::status_indicator(&stone.status, term.supports_color);
        let status_with_tended = format!("{}{}", status_indicator, tended_marker);
        let name_display = fmt.title(&stone.name);
        println!("{}{}", indent, ui::place_value(&name_display, &status_with_tended));

        // Stone ID if available
        if let Some(ref stone_id) = stone.stone_id {
            println!("{}{}", indent, fmt.hint(&format!("id: {}", stone_id)));
        }

        println!("{}{}", indent, fmt.divider(&"─".repeat(47)));

        // ACCESS section
        println!();
        println!("{}    {}", indent, fmt.group("ACCESS"));
        let endpoint_display = stone.endpoint.trim_start_matches("http://").trim_end_matches('/');
        println!("{}        {}", indent, ui::place_value("ENDPOINT", endpoint_display));
        println!();

        // Filter services if needed
        let filtered_services: Vec<_> = if let Some(filter) = offering_filter {
            stone.services.iter()
                .filter(|s| s.name.to_lowercase().contains(&filter.to_lowercase()) ||
                           s.service_type.to_lowercase().contains(&filter.to_lowercase()))
                .collect()
        } else {
            stone.services.iter().collect()
        };

        // OFFERINGS section
        println!("{}    {}", indent, fmt.group("OFFERINGS"));
        if filtered_services.is_empty() && offering_filter.is_some() {
            println!("{}        {}", indent, ui::place_value("", "No matching offerings"));
        } else if filtered_services.is_empty() {
            println!("{}        {}", indent, ui::place_value("", "No offerings installed"));
        } else {
            for svc in filtered_services.iter() {
                let status = ui::status_indicator(&svc.status, term.supports_color);
                println!("{}        {}", indent, ui::place_value(&svc.name, &status));
            }
        }

        println!(); // Blank line between stones
    }
}

/// Display stone from topology cache (lightweight view, no HTTP calls)
fn display_topology_stone(stone: &TopologyStoneData, offering_filter: &Option<Vec<String>>, is_tended: bool) -> anyhow::Result<()> {
    let entry = stone.entry;
    let caps = entry.capabilities.as_ref().unwrap(); // Already filtered out None in caller
    let term = ui::TerminalInfo::detect();
    let fmt = CliFormatter::new();
    let indent = " ".repeat(ui::constants::DEFAULT_INDENT);

    // Increment stone counter
    STONE_COUNT.fetch_add(1, Ordering::SeqCst);

    // Stone health from topology
    let status_text = entry.health.as_str();

    // Stone name with status and tended marker
    let status_indicator = ui::status_indicator(status_text, term.supports_color);
    let tended_marker = if is_tended { ui::tended_marker(term.supports_color) } else { String::new() };
    let status_with_tended = format!("{}{}", status_indicator, tended_marker);
    let name_display = fmt.title(&entry.stone_name);
    println!("{}{}", indent, ui::place_value(&name_display, &status_with_tended));

    // Stone ID
    println!("{}{}", indent, fmt.hint(&format!("id: {}", entry.stone_id)));
    println!("{}{}", indent, fmt.divider(&"─".repeat(47)));

    // === ACCESS SECTION ===
    println!();
    println!("{}    {}", indent, fmt.group("ACCESS"));
    let endpoint_clean = entry.endpoint.trim_start_matches("http://").trim_end_matches('/');
    let (ip_addr, port) = if let Some(colon_pos) = endpoint_clean.rfind(':') {
        (&endpoint_clean[..colon_pos], &endpoint_clean[colon_pos + 1..])
    } else {
        (endpoint_clean, "7185")
    };
    let mdns_name = format!("{}.local", entry.stone_name.to_lowercase());
    println!("{}        {}", indent, ui::place_value("HTTP", &format!("http://{}:{}", ip_addr, port)));
    println!("{}        {}", indent, ui::place_value("MDNS", &mdns_name));
    println!("{}        {}", indent, ui::place_value("IP", ip_addr));

    // === HARDWARE SECTION ===
    println!();
    println!("{}    {}", indent, fmt.group("HARDWARE"));
    println!("{}        {}", indent, ui::place_value("ARCH", &caps.hardware.cpu.architecture));
    println!("{}        {}", indent, ui::place_value("CPU", &format!("{} cores", caps.hardware.cpu.cores)));
    println!("{}        {}", indent, ui::place_value("MEMORY", &format!("{} GB", caps.hardware.memory.total_mb / 1024)));

    // Storage
    if !caps.hardware.storage.is_empty() {
        if let Some(largest) = caps.hardware.storage.iter().max_by_key(|d| d.size_gb) {
            let disk_type_str = match largest.disk_type {
                garden_common::DiskType::NVMe => "NVMe",
                garden_common::DiskType::SSD => "SSD",
                garden_common::DiskType::HDD => "HDD",
                garden_common::DiskType::Unknown => "",
            };
            let storage_value = if disk_type_str.is_empty() {
                format!("{} GB ({:.0}% used)", largest.size_gb, largest.used_percent)
            } else {
                format!("{} GB {} ({:.0}% used)", largest.size_gb, disk_type_str, largest.used_percent)
            };
            println!("{}        {}", indent, ui::place_value("STORAGE", &storage_value));
        }
    }

    // AI capabilities
    if let Some(ref ai_caps) = caps.hardware.ai_capabilities {
        if ai_caps.gpu_count > 0 {
            let gpu_text = if ai_caps.gpu_count == 1 { "1 GPU".to_string() } else { format!("{} GPUs", ai_caps.gpu_count) };
            let vram_text = if ai_caps.total_vram_mb >= 1024 {
                format!(" ({} GB)", ai_caps.total_vram_mb / 1024)
            } else if ai_caps.total_vram_mb > 0 {
                format!(" ({} MB)", ai_caps.total_vram_mb)
            } else {
                String::new()
            };
            let runtime_text = if !ai_caps.runtimes.is_empty() {
                let base_runtimes: Vec<String> = ai_caps.runtimes.iter()
                    .filter(|r| !r.contains(':'))
                    .map(|r| match r.as_str() {
                        "cuda" => "CUDA".to_string(),
                        "rocm" => "ROCm".to_string(),
                        "directml" => "DirectML".to_string(),
                        "openvino" => "OpenVINO".to_string(),
                        _ => r.to_uppercase(),
                    })
                    .collect();
                if !base_runtimes.is_empty() { format!(" - {}", base_runtimes.join(", ")) } else { String::new() }
            } else {
                String::new()
            };
            println!("{}        {}", indent, ui::place_value("AI", &format!("{}{}{}", gpu_text, vram_text, runtime_text)));
        }
    }

    // === OFFERINGS SECTION (lightweight from topology) ===
    println!();
    println!("{}    {}", indent, fmt.group("OFFERINGS"));

    // Filter services from topology
    let filtered_services: Vec<_> = if let Some(ref filters) = offering_filter {
        entry.services.iter()
            .filter(|s| filters.contains(&s.offering.to_lowercase()))
            .collect()
    } else {
        entry.services.iter().collect()
    };

    if filtered_services.is_empty() && offering_filter.is_some() {
        println!("{}        {}", indent, ui::place_value("", "No matching offerings"));
        let hidden = entry.services.len();
        if hidden > 0 {
            println!("{}        ({} other service{})", indent, hidden, if hidden == 1 { "" } else { "s" });
        }
    } else if filtered_services.is_empty() {
        println!("{}        {}", indent, ui::place_value("", "No offerings installed"));
    } else {
        // Simple list (no resource metrics in topology cache)
        for svc in filtered_services {
            let status_indicator = ui::status_indicator(&svc.status, term.supports_color);
            println!("{}        {}", indent, ui::place_value(&format!("{} ({})", svc.name, svc.offering), &status_indicator));
        }
    }

    println!(); // Blank line between stones
    Ok(())
}

/// Display footer with stone count and hints
fn display_footer() {
    let layout = Layout::new();
    let fmt = CliFormatter::new();
    let count = STONE_COUNT.load(Ordering::SeqCst);

    // Footer at Card level (matching stone header level)
    let indent_card = IndentLevel::Card.indent();

    println!("{}{}", indent_card, fmt.divider(&"─".repeat(47)));
    println!("{}{} stone{} discovered", indent_card, count, if count == 1 { "" } else { "s" });
    layout.blank();
    println!("{}{}", indent_card, fmt.hint("For stone details:      garden-rake <stone>?"));
    println!("{}{}", indent_card, fmt.hint("To tend a stone:        garden-rake tend <stone>"));
    // Related commands are printed by suggestions::print_suggestions() after execute()
}
