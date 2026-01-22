//! Observe command - garden overview with progressive stone discovery
//!
//! Displays a comprehensive view of all stones in the garden:
//! - Uses Lantern registry if available for instant topology
//! - Falls back to UDP discovery with progressive display
//! - Shows stone capabilities, AI devices, and offerings

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::discovery;
use crate::stone_cache::{CachedStone, GLOBAL_CACHE};
use crate::suggestions;
use crate::ui;
use async_trait::async_trait;
use garden_common::{DiscoveryResponse, GardenApiResponse, HardwareCapabilities, ServiceInfo};
use std::time::Duration;

/// Internal struct for stone data during observation
struct StoneData {
    capabilities: HardwareCapabilities,
    services: Vec<ServiceInfo>,
    endpoint: String,
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
    // Keep offering_filter as-is for Lantern call, create offerings_filter for legacy code
    let offerings_filter: Option<Vec<String>> = offering_filter.as_ref().map(|s| {
        s.split(',')
            .map(|o| o.trim().to_lowercase())
            .collect()
    });

    // Start background Lantern discovery immediately (non-blocking)
    discovery::discover_lantern_background();

    // Display header immediately (no waiting for discovery)
    let term = &ctx.term;
    let supports_unicode = term.supports_unicode;
    if let Some(ref filter) = offerings_filter {
        println!("{}", ui::section_header(&format!("GARDEN OVERVIEW (filtered: {})", filter.join(", ")), term));
    } else {
        println!("{}", ui::section_header("GARDEN OVERVIEW", term));
    }
    println!();

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
                    display_lantern_topology(&topology, offering_filter.as_deref(), supports_unicode);
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

    // Fallback: Hot cache architecture - Check cache FIRST (zero discovery for common case)
    let cached_stones = GLOBAL_CACHE.get_all();

    let mut found_any_stone = false;

    if !cached_stones.is_empty() {
        tracing::info!(count = cached_stones.len(), "Using cached stone discovery (cache hit)");

        // Stream cached results progressively - spawn concurrent fetch tasks
        let mut fetch_handles = vec![];

        for cached in cached_stones {
            // Filter by name if specified
            if let Some(ref filter_name) = stone_filter {
                if cached.capabilities.stone_name.to_lowercase() != filter_name.to_lowercase() {
                    continue;
                }
            }

            // Spawn concurrent fetch task - display as data arrives
            let client_clone = ctx.client.clone();
            let offerings_filter_clone = offerings_filter.clone();
            let handle = tokio::spawn(async move {
                fetch_and_display_stone(&client_clone, &cached, &offerings_filter_clone).await
            });
            fetch_handles.push(handle);
        }

        // Wait for all concurrent fetches to complete
        for handle in fetch_handles {
            if let Ok(success) = handle.await {
                if success {
                    found_any_stone = true;
                }
            }
        }

        if stone_filter.is_some() && !found_any_stone {
            println!("{}x Stone '{}' not found in cache", " ".repeat(ui::constants::DEFAULT_INDENT), stone_filter.as_ref().unwrap());
            return Ok(());
        }
    } else {
        tracing::info!("Cache miss - performing stone discovery");

        // Create channel for streaming discovered stones
        let (stone_tx, mut stone_rx) = tokio::sync::mpsc::unbounded_channel();

        // Spawn discovery in blocking task (UDP operations are sync)
        let stone_filter_clone = stone_filter.clone();
        let mut discovery_handle = tokio::task::spawn_blocking(move || {
            discovery::discover_all_moss_stream(
                Duration::from_secs(5),
                move |response, _discovery_instant| {
                    // Filter by name if specified
                    if let Some(ref filter_name) = stone_filter_clone {
                        if response.stone_name.to_lowercase() != filter_name.to_lowercase() {
                            return;
                        }
                    }

                    // Send to async channel for immediate processing
                    let _ = stone_tx.send(response);
                }
            )
        });

        println!();

        // Process stones as they arrive - spawn concurrent fetch tasks immediately
        let mut fetch_handles = vec![];
        let client = ctx.client.clone();

        loop {
            tokio::select! {
                Some(response) = stone_rx.recv() => {
                    // Spawn fetch task immediately when stone discovered
                    let client_clone = client.clone();
                    let offerings_filter_clone = offerings_filter.clone();

                    let handle = tokio::spawn(async move {
                        fetch_and_display_discovered_stone(&client_clone, &response, &offerings_filter_clone).await
                    });
                    fetch_handles.push(handle);
                }
                _ = &mut discovery_handle => {
                    // Discovery finished
                    break;
                }
            }
        }

        // Wait for all concurrent fetches to complete
        for handle in fetch_handles {
            if let Ok(success) = handle.await {
                if success {
                    found_any_stone = true;
                }
            }
        }

        // Fallback to localhost if no stones discovered
        if !found_any_stone {
            found_any_stone = try_localhost_fallback(&ctx.client, &offerings_filter).await;
        }
    }

    if !found_any_stone {
        println!("{}No reachable stones found", " ".repeat(ui::constants::DEFAULT_INDENT));
    }

    Ok(())
}

/// Fetch and display a cached stone
async fn fetch_and_display_stone(
    client: &reqwest::Client,
    cached: &CachedStone,
    offerings_filter: &Option<Vec<String>>,
) -> bool {
    let services_url = format!("{}/api/v1/services", cached.endpoint.trim_end_matches('/'));
    if let Ok(resp) = client.get(&services_url).timeout(Duration::from_secs(5)).send().await {
        if let Ok(services_json) = resp.json::<serde_json::Value>().await {
            let services: Vec<ServiceInfo> = serde_json::from_value(
                services_json.get("data").cloned().unwrap_or(services_json)
            ).unwrap_or_default();

            let stone_data = StoneData {
                capabilities: cached.capabilities.clone(),
                services,
                endpoint: cached.endpoint.clone(),
            };

            // Display as soon as data arrives
            let _ = display_stone(&stone_data, offerings_filter);
            return true;
        }
    }
    false
}

/// Fetch and display a newly discovered stone
async fn fetch_and_display_discovered_stone(
    client: &reqwest::Client,
    response: &DiscoveryResponse,
    offerings_filter: &Option<Vec<String>>,
) -> bool {
    let caps_url = format!("{}/capabilities", response.stone_endpoint.trim_end_matches('/'));
    if let Ok(resp) = client.get(&caps_url).timeout(Duration::from_secs(5)).send().await {
        if let Ok(caps_response) = resp.json::<GardenApiResponse<HardwareCapabilities>>().await {
            let services_url = format!("{}/api/v1/services", response.stone_endpoint.trim_end_matches('/'));
            if let Ok(svc_resp) = client.get(&services_url).timeout(Duration::from_secs(5)).send().await {
                if let Ok(services_json) = svc_resp.json::<serde_json::Value>().await {
                    let services: Vec<ServiceInfo> = serde_json::from_value(
                        services_json.get("data").cloned().unwrap_or(services_json)
                    ).unwrap_or_default();

                    let stone_data = StoneData {
                        capabilities: caps_response.data,
                        services,
                        endpoint: response.stone_endpoint.clone(),
                    };

                    // Display as soon as data arrives
                    let _ = display_stone(&stone_data, offerings_filter);
                    return true;
                }
            }
        }
    }
    false
}

/// Try localhost as fallback when no stones discovered
async fn try_localhost_fallback(
    client: &reqwest::Client,
    offerings_filter: &Option<Vec<String>>,
) -> bool {
    let localhost = format!("http://127.0.0.1:{}", garden_common::ports::MOSS_HTTP);

    let caps_url = format!("{}/capabilities", localhost);
    if let Ok(resp) = client.get(&caps_url).timeout(Duration::from_secs(5)).send().await {
        if let Ok(caps_response) = resp.json::<GardenApiResponse<HardwareCapabilities>>().await {
            let services_url = format!("{}/api/v1/services", localhost);
            if let Ok(svc_resp) = client.get(&services_url).timeout(Duration::from_secs(5)).send().await {
                if let Ok(services_json) = svc_resp.json::<serde_json::Value>().await {
                    let services: Vec<ServiceInfo> = serde_json::from_value(
                        services_json.get("data").cloned().unwrap_or(services_json)
                    ).unwrap_or_default();

                    let stone_data = StoneData {
                        capabilities: caps_response.data,
                        services,
                        endpoint: localhost,
                    };

                    let _ = display_stone(&stone_data, offerings_filter);
                    return true;
                }
            }
        }
    }
    false
}

/// Display topology from Lantern registry
fn display_lantern_topology(topology: &garden_common::LanternTopology, offering_filter: Option<&str>, supports_unicode: bool) {
    println!("\n=== GARDEN OVERVIEW (via Lantern) ===\n");

    if topology.stones.is_empty() {
        println!("No stones registered");
        return;
    }

    for stone in &topology.stones {
        let status_marker = match stone.status.as_str() {
            "online" => ui::bullet(supports_unicode),
            "offline" => ui::hollow_bullet(supports_unicode),
            _ => if supports_unicode { "o" } else { "~" },
        };

        // Display endpoint without http:// prefix for cleaner output
        let endpoint_display = stone.endpoint.trim_start_matches("http://").trim_end_matches('/');
        println!("{}  {} ({})  {}", status_marker, stone.name, endpoint_display, stone.status);

        // Filter services if needed
        let filtered_services: Vec<_> = if let Some(filter) = offering_filter {
            stone.services.iter()
                .filter(|s| s.name.to_lowercase().contains(&filter.to_lowercase()) ||
                           s.service_type.to_lowercase().contains(&filter.to_lowercase()))
                .collect()
        } else {
            stone.services.iter().collect()
        };

        if filtered_services.is_empty() && offering_filter.is_some() {
            println!("   +-- No matching offerings");
        } else if filtered_services.is_empty() {
            println!("   +-- No offerings");
        } else {
            println!("   OFFERINGS:");
            for (idx, svc) in filtered_services.iter().enumerate() {
                let is_last = idx == filtered_services.len() - 1;
                let branch = if is_last { "+--" } else { "|--" };
                println!("   {} {:<12}  {} ({})", branch, svc.name, svc.status, svc.service_type);
            }
        }

        println!(); // Blank line between stones
    }

    println!("Last updated: {}", topology.last_updated);
}

/// Display a single stone with its offerings
fn display_stone(stone: &StoneData, offering_filter: &Option<Vec<String>>) -> anyhow::Result<()> {
    let caps = &stone.capabilities;
    let term = ui::TerminalInfo::detect();

    // Determine stone status from detection_status
    let status_text = match caps.detection_status {
        garden_common::DetectionStatus::Scanning => "waking up",
        garden_common::DetectionStatus::Partial => "initializing",
        garden_common::DetectionStatus::Complete => "thriving",
    };

    // Stone name with status aligned at column 35
    let stone_name_display = if term.supports_color {
        use colored::Colorize;
        caps.stone_name.to_uppercase().bold().to_string()
    } else {
        caps.stone_name.to_uppercase()
    };
    let status_indicator = ui::status_indicator(status_text, term.supports_color);

    // Pad stone name to 35 characters, then add status
    let name_visible_len = caps.stone_name.len();
    let status_col = 35;
    let padding_needed = if status_col > name_visible_len {
        status_col - name_visible_len
    } else {
        1
    };

    println!("\n{}{}{}", stone_name_display, " ".repeat(padding_needed), status_indicator);

    // Underline
    let underline_len = status_col + 12;
    println!("{}", "-".repeat(underline_len));

    // === SYSTEM SECTION ===
    let endpoint_display = stone.endpoint.trim_start_matches("http://").trim_end_matches('/');
    println!("{}", ui::kv_line("ADDRESS", endpoint_display, ui::constants::DEFAULT_INDENT));
    println!("{}", ui::kv_line("ARCH", &caps.hardware.cpu.architecture, ui::constants::DEFAULT_INDENT));
    println!("{}", ui::kv_line("CPU", &format!("{} cores", caps.hardware.cpu.cores), ui::constants::DEFAULT_INDENT));
    println!("{}", ui::kv_line("MEMORY", &format!("{} GB", caps.hardware.memory.total_mb / 1024), ui::constants::DEFAULT_INDENT));

    // Show primary storage if available
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
            println!("{}", ui::kv_line("STORAGE", &storage_value, ui::constants::DEFAULT_INDENT));
        }
    }

    // === AI SECTION ===
    if let Some(ref ai_caps) = caps.hardware.ai_capabilities {
        if ai_caps.gpu_count > 0 {
            let gpu_text = if ai_caps.gpu_count == 1 {
                "1 GPU".to_string()
            } else {
                format!("{} GPUs", ai_caps.gpu_count)
            };

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

                if !base_runtimes.is_empty() {
                    format!(" - {}", base_runtimes.join(", "))
                } else {
                    String::new()
                }
            } else {
                String::new()
            };

            println!("{}", ui::kv_line("AI", &format!("{}{}{}", gpu_text, vram_text, runtime_text), ui::constants::DEFAULT_INDENT));
        }
    } else {
        // Fallback to old behavior
        let ai_devices: Vec<&garden_common::GpuInfo> = caps.hardware.gpus.iter()
            .filter(|gpu| {
                !gpu.ai_runtimes.is_empty() ||
                gpu.capabilities.iter().any(|c| c == "cuda" || c == "rocm" || c == "vulkan" || c == "directml")
            })
            .collect();

        if !ai_devices.is_empty() {
            println!("{}", ui::kv_line("AI", &format!("{} device(s)", ai_devices.len()), ui::constants::DEFAULT_INDENT));
        }
    }

    // Filter services if needed
    let filtered_services: Vec<&ServiceInfo> = if let Some(ref filters) = offering_filter {
        stone.services.iter()
            .filter(|s| filters.contains(&s.name.to_lowercase()))
            .collect()
    } else {
        stone.services.iter().collect()
    };

    if filtered_services.is_empty() && offering_filter.is_some() {
        println!("{}", ui::empty_state("No matching offerings", None));
        let hidden = stone.services.len();
        if hidden > 0 {
            println!("{}  {} other service{}", " ".repeat(ui::constants::DEFAULT_INDENT), hidden, if hidden == 1 { "" } else { "s" });
        }
    } else if filtered_services.is_empty() {
        println!("{}", ui::empty_state("No offerings", None));
    } else {
        // Display offerings as bracketed category
        println!("\n{}[offerings]", " ".repeat(ui::constants::DEFAULT_INDENT));

        let mut table = ui::TableBuilder::new()
            .with_indent(ui::constants::DEFAULT_INDENT * 2)
            .add_column(24, ui::Align::Left)
            .add_column(12, ui::Align::Left)
            .add_column(10, ui::Align::Right)
            .add_column(10, ui::Align::Right)
            .add_column(10, ui::Align::Right)
            .add_column(10, ui::Align::Right);

        for svc in filtered_services.iter() {
            let status_indicator = ui::status_indicator(&format!("{:?}", svc.status).to_lowercase(), term.supports_color);

            if let Some(ref res) = svc.resources {
                table.add_row(vec![
                    ui::truncate_name(&svc.name, ui::constants::MAX_SERVICE_NAME_LEN),
                    status_indicator,
                    res.cpu_friendly.clone(),
                    res.memory_friendly.clone(),
                    res.network_rx_friendly.clone(),
                    res.uptime_friendly.clone(),
                ]);
            } else {
                table.add_row(vec![
                    ui::truncate_name(&svc.name, ui::constants::MAX_SERVICE_NAME_LEN),
                    status_indicator,
                    "-".to_string(),
                    "-".to_string(),
                    "-".to_string(),
                    "-".to_string(),
                ]);
            }
        }

        println!("{}", table.render());

        // Show hidden count if filtered
        if offering_filter.is_some() {
            let hidden = stone.services.len() - filtered_services.len();
            if hidden > 0 {
                println!("      + {} other service{}", hidden, if hidden == 1 { "" } else { "s" });
            }
        }
    }

    println!(); // Blank line between stones
    Ok(())
}
