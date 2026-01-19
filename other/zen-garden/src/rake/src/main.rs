mod parser;
mod stone_cache;
mod tending;
mod ui;

#[cfg(test)]
mod discovery_tests;

#[cfg(test)]
mod recommendation_tests;

use base64::Engine;
use clap::{Parser, Subcommand};
use std::collections::BTreeMap;
use std::time::Duration;
use tracing_subscriber::EnvFilter;
use garden_common::{GardenApiResponse, HardwareCapabilities, ServiceInfo, GardenHttpClient};
use garden_rake::client::{resolve_target_endpoint, CachedStoneOps, CachedStoneInfo};
use garden_rake::discovery;
use stone_cache::StoneCache;

// Global stone cache (hot cache architecture per design philosophy)
static STONE_CACHE: once_cell::sync::Lazy<StoneCache> = once_cell::sync::Lazy::new(StoneCache::new);

// Implement the trait for StoneCache
impl CachedStoneOps for StoneCache {
    fn get(&self, stone_name: &str) -> Option<CachedStoneInfo> {
        self.get(stone_name).map(|cached| CachedStoneInfo {
            endpoint: cached.endpoint,
        })
    }
    
    fn insert(&self, stone_name: String, endpoint: String, capabilities: HardwareCapabilities) {
        self.insert(stone_name, endpoint, capabilities);
    }
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct TemplateInfo {
    pub name: String,
    pub category: String,
    pub description: String,
    #[serde(default)]
    pub tags: Vec<String>,
}

async fn resolve_endpoint(client: &reqwest::Client, at: Option<String>) -> anyhow::Result<String> {
    let term = ui::TerminalInfo::detect();
    // Priority 1: --at flag (explicit override, deterministic)
    if let Some(explicit) = at {
        let endpoint = resolve_target_endpoint(client, &explicit, Some(&*STONE_CACHE)).await?;
        print_stone_header(client, &endpoint).await;
        return Ok(endpoint);
    }

    // Priority 2: GARDEN_STONE environment variable
    if let Ok(env_endpoint) = std::env::var(garden_common::ENV_GARDEN_STONE) {
        tracing::info!(endpoint = %env_endpoint, "Using GARDEN_STONE environment variable");
        let endpoint = resolve_target_endpoint(client, &env_endpoint, Some(&*STONE_CACHE)).await?;
        print_stone_header(client, &endpoint).await;
        return Ok(endpoint);
    }

    // Priority 3: Cached tending state (90s TTL)
    if let Ok(tending) = tending::read_tending() {
        if tending.is_valid() {
            tracing::info!(
                stone = %tending.stone_name,
                endpoint = %tending.endpoint,
                age_secs = tending.age_seconds(),
                "Using cached tending state"
            );
            print_stone_header(client, &tending.endpoint).await;
            return Ok(tending.endpoint);
        } else {
            tracing::debug!("Tending state expired, clearing cache");
            let _ = tending::clear_tending();
        }
    }

    // Priority 4: Auto-discover via UDP broadcast + cache result
    tracing::debug!("No cached tending, attempting auto-discovery");
    println!("{}{} Discovering stones...", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("info", term.supports_color));
    match discovery::discover_moss() {
        Ok(endpoint) => {
            tracing::info!(endpoint = %endpoint, "Auto-discovered stone");
            
            // Fetch capabilities to get stone name for cache
            let caps_url = format!("{}/capabilities", endpoint.trim_end_matches('/'));
            if let Ok(resp) = client.get(&caps_url).timeout(Duration::from_secs(5)).send().await {
                if let Ok(response) = resp.json::<GardenApiResponse<HardwareCapabilities>>().await {
                    let _ = tending::write_tending(response.data.stone_name.clone(), endpoint.clone());
                }
            }
            
            print_stone_header(client, &endpoint).await;
            Ok(endpoint)
        }
        Err(_) => {
            Err(anyhow::anyhow!(
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
            ))
        }
    }
}

/// Print stone header banner if tending to a valid stone
/// This should be called at the start of every command
async fn print_stone_header(client: &reqwest::Client, endpoint: &str) {
    let term = ui::TerminalInfo::detect();
    
    // Fetch stone capabilities to get name and health
    let caps_url = format!("{}/capabilities", endpoint.trim_end_matches('/'));
    if let Ok(resp) = client.get(&caps_url).timeout(Duration::from_secs(3)).send().await {
        if let Ok(response) = resp.json::<GardenApiResponse<HardwareCapabilities>>().await {
            let stone_name = &response.data.stone_name;
            
            // Fetch health to get status
            let health_url = format!("{}/health", endpoint.trim_end_matches('/'));
            let health_status = if let Ok(health_resp) = client.get(&health_url).timeout(Duration::from_secs(2)).send().await {
                if let Ok(health_json) = health_resp.json::<serde_json::Value>().await {
                    // Map health to vitality language
                    if let Some(status) = health_json.get("status").and_then(|v| v.as_str()) {
                        match status {
                            garden_common::HEALTH_HEALTHY => garden_common::VITALITY_THRIVING,
                            garden_common::HEALTH_DEGRADED => garden_common::VITALITY_NEEDS_ATTENTION,
                            garden_common::HEALTH_UNHEALTHY => garden_common::VITALITY_WITHERING,
                            _ => garden_common::VITALITY_DORMANT
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
            
            println!("{}", ui::stone_banner(stone_name, health_status, term.supports_color));
            println!();
        }
    }
}

#[derive(Parser)]
#[command(name = "garden-rake")]
#[command(about = "Zen Garden management CLI")]
#[command(version = concat!(env!("CARGO_PKG_VERSION"), ".", env!("BUILD_NUMBER")))]
struct Cli {
    /// Suppress suggestions (zen: quietly, env: GARDEN_QUIET)
    #[arg(short, long, global = true)]
    quiet: bool,

    #[command(subcommand)]
    command: Commands,
}

#[derive(Subcommand)]
enum Commands {
    /// Get Stone status (DEPRECATED: use 'observe' or 'tend' instead)
    #[command(hide = false)]
    Status {
        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Offer a service
    /// Offerings: list validated offerings, install, or inspect.
    ///
    /// Examples:
    ///   garden-rake offer                # List validated offerings by category
    ///   garden-rake offer mongodb        # Install mongodb (with compatibility fallback if needed)
    ///   garden-rake offer mongodb info   # Show offering details + compatibility decision
    Offer {
        /// Offering name (omit to list all offerings)
        offering: Option<String>,

        /// Optional action for a specific offering
        #[command(subcommand)]
        action: Option<OfferAction>,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,

        /// Bias recommendations (non-blocking). Examples: --prefer ssd, --prefer nvme
        #[arg(long, value_delimiter = ',', action = clap::ArgAction::Append)]
        prefer: Vec<String>,

        /// If an install fails due to compatibility, automatically recommend across all discovered stones.
        #[arg(long)]
        anywhere_on_fail: bool,
    },

    /// List services
    List {
        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Remove a service
    Remove {
        /// Service name to remove
        service: String,

        /// Skip confirmation prompt
        #[arg(long)]
        force: bool,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Upgrade a service
    Upgrade {
        /// Service name to upgrade (omit for all services)
        service: Option<String>,

        /// Upgrade all services on stone
        #[arg(long)]
        all: bool,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Stop a service (rest mode)
    Rest {
        /// Service name to stop
        service: String,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Start a service (wake from rest)
    Wake {
        /// Service name to start
        service: String,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Phase 3 scaffolding: Place keystone or stone (zen syntax)
    #[command(
        long_about = "Initialize pond or join pond (zen syntax for 'pond init' or 'pond join').\n\n\
        Examples:\n  \
        garden-rake place keystone              # Initialize pond\n  \
        garden-rake place stone --code ABC123 # Join pond with invitation\n\n\
        Note: Pond security implementation pending (Phase 3b)."
    )]
    Place {
        /// Target: "keystone" or "stone"
        target: String,

        /// Invitation code (required for "stone")
        #[arg(long)]
        code: Option<String>,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
        
        /// Passphrase for encrypting pond certificate (keystone only)
        #[arg(long)]
        passphrase: Option<String>,
    },

    /// Phase 3 scaffolding: Invite a Stone (zen syntax)
    #[command(
        long_about = "Generate pond invitation code (zen syntax for 'pond invite').\n\n\
        Example:\n  \
        garden-rake invite\n\n\
        Note: Pond security implementation pending (Phase 3b)."
    )]
    Invite {
        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Observe garden state (all stones or filtered)
    #[command(
        long_about = "Observe garden state with optional filtering.\n\n\
        Examples:\n  \
        garden-rake observe                    # All stones\n  \
        garden-rake observe stone-01           # Specific stone\n  \
        garden-rake observe --offering mongodb,redis  # Filter by offerings"
    )]
    Observe {
        /// Specific stone name (omit for all stones)
        stone: Option<String>,

        /// Filter by offerings (comma-separated, e.g., "mongodb,redis")
        #[arg(long)]
        offering: Option<String>,
    },

    /// Watch real-time events from a Stone
    #[command(
        long_about = "Stream real-time events from moss operations.\n\n\
        Examples:\n  \
        garden-rake watch stone-01                        # Watch all events\n  \
        garden-rake watch stone-01 until 'completed'     # Exit when string appears\n  \
        garden-rake watch --at http://stone-01:7185      # Explicit endpoint
        garden-rake watch offering mongodb logs          # Watch offering logs"
    )]
    Watch {
        /// Subcommand: offering, stone, or none for events
        #[command(subcommand)]
        target: Option<WatchTarget>,

        /// Exit when this string appears in event stream
        #[arg(long)]
        until: Option<String>,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Refresh (update) garden-moss or garden-rake binary on stone
    #[command(
        long_about = "Update garden-moss or garden-rake binary on a stone (development use).\n\n\
        Examples:\n  \
        garden-rake refresh garden-moss --from ./target/release/garden-moss\n  \
        garden-rake refresh rake --from ./dist/linux-x64/garden-rake\n\n\
        The binary will be validated for architecture compatibility before installation.\n\
        Garden-Moss will automatically restart after update."
    )]
    Refresh {
        /// Component to refresh: "moss" or "rake"
        component: String,

        /// Path to binary file
        #[arg(long)]
        from: std::path::PathBuf,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Reconcile moss registry with existing containers (adopt now)
    #[command(
        long_about = "Force moss to reconcile its registry with existing zen-offering containers.\n\n\
        This is useful after a moss restart/update, or if containers were created externally.\n\n\
        Examples:\n  \
        garden-rake reconcile                         # Adopt any missing containers\n  \
        garden-rake reconcile --drop-invalid          # Also remove invalid zen-offering-* containers\n  \
        garden-rake reconcile --at http://stone-01:7185"
    )]
    Reconcile {
        /// Remove zen-offering-* containers that don't map to a known template
        #[arg(long)]
        drop_invalid: bool,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Manage offering templates
    Template {
        #[command(subcommand)]
        command: TemplateCommands,
    },

    /// Manage tending state (which stone rake commands target)
    #[command(
        long_about = "Manage which stone garden-rake commands target.\n\n\
        Examples:\n  \
        garden-rake tend                      # Show current tending state\n  \
        garden-rake tend this                 # Tend to localhost\n  \
        garden-rake tend auto                 # Auto-discover and set\n  \
        garden-rake tend http://192.168.1.108:7185  # Set explicit endpoint\n  \
        garden-rake tend --clear              # Stop tending\n\n\
        Tending state is cached for 90 seconds and automatically refreshed."
    )]
    Tend {
        /// Target: 'this', 'local', 'auto', or explicit endpoint URL
        target: Option<String>,

        /// Clear tending state
        #[arg(long)]
        clear: bool,

        /// Show verbose tending information
        #[arg(long, short)]
        verbose: bool,
    },

    /// Manage pond security (normative syntax)
    #[command(
        long_about = "Manage pond security for multi-stone trust.\n\n\
        Examples:\n  \
        garden-rake pond init                 # Initialize pond (place keystone)\n  \
        garden-rake pond status               # Show pond status\n  \
        garden-rake pond invite               # Generate invitation code\n  \
        garden-rake pond join <code>          # Join pond with code\n  \
        garden-rake pond remove               # Remove pond from stone\n  \
        garden-rake pond untrust stone-02     # Remove stone from pond\n\n\
        Note: Pond security implementation pending (Phase 3b)."
    )]
    Pond {
        #[command(subcommand)]
        action: PondAction,
        
        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Remove a stone from the pond (zen syntax)
    #[command(
        long_about = "Remove a stone from the pond (zen syntax for 'pond untrust').\n\n\
        Example:\n  \
        garden-rake lift stone stone-02\n\n\
        Note: Pond security implementation pending (Phase 3b)."
    )]
    Lift {
        /// Target type: 'keystone' or 'stone'
        target_type: String,
        
        /// Stone name (required if target_type is 'stone')
        stone_name: Option<String>,
        
        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },
}

#[derive(Debug, Subcommand)]
enum OfferAction {
    /// Show offering details and compatibility decision
    Info,
}

#[derive(Debug, Subcommand)]
enum PondAction {
    /// Initialize pond security (place keystone)
    Init {
        /// Passphrase for encrypting pond certificate
        #[arg(long)]
        passphrase: Option<String>,
    },
    /// Show pond status
    Status,
    /// Generate invitation code
    Invite,
    /// Join pond with invitation code
    Join {
        /// TOTP invitation code
        code: String,
    },
    /// Remove pond from this stone
    Remove,
    /// Remove a stone from the pond
    Untrust {
        /// Stone name to remove
        stone_name: String,
    },
}

#[derive(Debug, serde::Deserialize)]
struct OfferingEntry {
    name: String,
    category: String,
    description: String,
    #[serde(default)]
    tags: Vec<String>,
    #[allow(dead_code)] // Used for deserialization
    image: String,
    #[serde(default)]
    compatibility: OfferingCompatibility,
}

#[derive(Debug, Default, serde::Deserialize)]
struct OfferingCompatibility {
    #[serde(default)]
    decision: String,
    #[allow(dead_code)] // Used for deserialization
    reason: Option<String>,
    #[allow(dead_code)] // Used for deserialization
    original_image: Option<String>,
    #[allow(dead_code)] // Used for deserialization
    fallback_image: Option<String>,
    #[allow(dead_code)] // Used for deserialization
    suggestion: Option<String>,
}

async fn fetch_offerings(
    client: &reqwest::Client,
    endpoint: &str,
) -> anyhow::Result<Vec<OfferingEntry>> {
    let moss = GardenHttpClient::new(client, endpoint);
    let response = moss.get_raw("/api/v1/offerings").await?;
    
    if response.status() == reqwest::StatusCode::NOT_FOUND {
        anyhow::bail!("This stone's moss does not support validated offerings. Upgrade moss and retry.");
    }
    
    let api_response: GardenApiResponse<Vec<OfferingEntry>> = response.error_for_status()?.json().await?;
    Ok(api_response.data)
}

#[derive(Debug, serde::Deserialize)]
struct TaxonomyDictionary {
    #[serde(default)]
    map: std::collections::HashMap<String, String>,
}

fn load_taxonomy_dictionary() -> TaxonomyDictionary {
    // Repo-owned dictionary (compiled into rake) so query behavior is stable and portable.
    let raw = include_str!(concat!(env!("CARGO_MANIFEST_DIR"), "/../../manifests/taxonomy.dictionary.yaml"));
    serde_yaml::from_str::<TaxonomyDictionary>(raw).unwrap_or(TaxonomyDictionary {
        map: Default::default(),
    })
}

fn normalize_tokens(raw: &str, dict: &TaxonomyDictionary) -> Vec<String> {
    raw.split([',', ' ', '\t', '\n', '\r'])
        .map(|t| t.trim().to_lowercase())
        .filter(|t| !t.is_empty())
        .map(|t| dict.map.get(&t).cloned().unwrap_or(t))
        .collect()
}

fn token_matches_category(token: &str, category: &str) -> bool {
    let token = token.to_lowercase();
    let category = category.to_lowercase();

    match token.as_str() {
        // user intent -> canonical category
        "database" => matches!(category.as_str(), "data" | "cache" | "search" | "vector"),
        "vector" => category == "vector",
        "messaging" => category == "messaging",
        "observability" => category == "observability",
        "secrets" => category == "secrets",
        "cache" => category == "cache",
        "search" => category == "search",
        // direct category match
        _ => token == category,
    }
}

fn offering_relevance_score(tokens: &[String], offering: &OfferingEntry) -> i32 {
    let name_lc = offering.name.to_lowercase();
    let desc_lc = offering.description.to_lowercase();
    let tags_lc = offering
        .tags
        .iter()
        .map(|t| t.to_lowercase())
        .collect::<std::collections::HashSet<_>>();

    let mut score = 0i32;
    for token in tokens {
        let t = token.as_str();
        if token_matches_category(t, &offering.category) {
            score += 10;
        }
        if tags_lc.contains(t) {
            score += 6;
        }
        if name_lc == t {
            score += 8;
        } else if name_lc.contains(t) {
            score += 2;
        }
        if desc_lc.contains(t) {
            score += 1;
        }
    }
    score
}

fn stone_prefer_score(prefer: &[String], caps: Option<&HardwareCapabilities>) -> i32 {
    let Some(caps) = caps else { return 0; };
    let disk_type = caps
        .hardware
        .disk
        .as_ref()
        .and_then(|d| d.disk_type.as_ref())
        .map(|s| s.to_lowercase());

    let mut score = 0i32;
    for p in prefer {
        match p.to_lowercase().as_str() {
            "ssd" => {
                if matches!(disk_type.as_deref(), Some("ssd") | Some("nvme")) {
                    score += 10;
                }
            }
            "nvme" => {
                if disk_type.as_deref() == Some("nvme") {
                    score += 12;
                }
            }
            "hdd" => {
                if disk_type.as_deref() == Some("hdd") {
                    score += 6;
                }
            }
            _ => {}
        }
    }
    score
}

async fn fetch_capabilities(client: &reqwest::Client, endpoint: &str) -> anyhow::Result<HardwareCapabilities> {
    let moss = GardenHttpClient::new(client, endpoint);
    let response: GardenApiResponse<HardwareCapabilities> = moss.get("/capabilities").await?;
    Ok(response.data)
}

async fn print_offer_query_recommendations(
    client: &reqwest::Client,
    endpoint: &str,
    query: &str,
    prefer: &[String],
) -> anyhow::Result<()> {
    let dict = load_taxonomy_dictionary();
    let tokens = normalize_tokens(query, &dict);
    if tokens.is_empty() {
        anyhow::bail!("Query is empty");
    }

    let offerings = fetch_offerings(client, endpoint).await?;
    let mut ranked = offerings
        .into_iter()
        .filter(|o| o.compatibility.decision.as_str() != garden_common::COMPAT_FAIL)
        .map(|o| {
            let s = offering_relevance_score(&tokens, &o);
            (s, o)
        })
        .filter(|(s, _)| *s > 0)
        .collect::<Vec<_>>();

    ranked.sort_by(|(sa, a), (sb, b)| sb.cmp(sa).then_with(|| a.name.cmp(&b.name)));

    println!("Query: {}", query);
    if !prefer.is_empty() {
        println!("Prefer: {}", prefer.join(", "));
    }

    if ranked.is_empty() {
        println!("No matching offerings found on this stone.");
        return Ok(());
    }

    println!("Top recommendations:");
    for (idx, (_score, o)) in ranked.into_iter().take(3).enumerate() {
        let flag = format_offering_flag(&o.compatibility);
        let prefix = if flag.is_empty() { "" } else { "(!) " };
        println!("  {}. {} - {}{}", idx + 1, o.name, prefix, o.description);
        println!("     Run: garden-rake offer {} --at {}", o.name, endpoint);
    }

    Ok(())
}

async fn print_offer_anywhere_recommendations(
    client: &reqwest::Client,
    query: &str,
    prefer: &[String],
) -> anyhow::Result<()> {
    let dict = load_taxonomy_dictionary();
    let tokens = normalize_tokens(query, &dict);
    if tokens.is_empty() {
        anyhow::bail!("Query is empty");
    }

    // Collect endpoints using streaming API
    let mut endpoints = Vec::new();
    let _ = discovery::discover_all_moss_stream(
        std::time::Duration::from_secs(2),
        |response, _instant| {
            endpoints.push(response.stone_endpoint.clone());
        },
    );

    if endpoints.is_empty() {
        anyhow::bail!("No stones discovered");
    }

    let mut candidates: Vec<(i32, String, String, OfferingEntry)> = Vec::new();
    for ep in endpoints {
        let caps = fetch_capabilities(client, &ep).await.ok();
        let stone_bonus = stone_prefer_score(prefer, caps.as_ref());
        let stone_name = caps
            .as_ref()
            .map(|c| c.stone_name.clone())
            .unwrap_or_else(|| "<unknown>".to_string());

        let offerings = match fetch_offerings(client, &ep).await {
            Ok(o) => o,
            Err(_) => continue,
        };

        for o in offerings.into_iter().filter(|o| o.compatibility.decision.as_str() != garden_common::COMPAT_FAIL) {
            let rel = offering_relevance_score(&tokens, &o);
            if rel <= 0 {
                continue;
            }
            let combined = rel * 100 + stone_bonus;
            candidates.push((combined, stone_name.clone(), ep.clone(), o));
        }
    }

    candidates.sort_by(|(sa, an, ae, ao), (sb, bn, be, bo)| {
        sb.cmp(sa)
            .then_with(|| an.cmp(bn))
            .then_with(|| ae.cmp(be))
            .then_with(|| ao.name.cmp(&bo.name))
    });

    println!("Query: {}", query);
    if !prefer.is_empty() {
        println!("Prefer: {}", prefer.join(", "));
    }

    if candidates.is_empty() {
        println!("No matching offerings found on any discovered stone.");
        return Ok(());
    }

    println!("Top recommendations across stones:");
    for (idx, (_score, stone_name, ep, o)) in candidates.into_iter().take(3).enumerate() {
        let flag = format_offering_flag(&o.compatibility);
        let prefix = if flag.is_empty() { "" } else { "(!) " };
        println!("  {}. {} @ {} - {}{}", idx + 1, o.name, stone_name, prefix, o.description);
        println!("     Run: garden-rake offer {} --at {}", o.name, ep);
    }

    Ok(())
}

/// Render a table of services with status indicators and counts
/// Extracted to avoid code duplication
fn render_services_table(services: &[ServiceInfo], term: &ui::TerminalInfo) {
    let mut table = ui::TableBuilder::new()
        .add_column(ui::constants::MAX_SERVICE_NAME_LEN, ui::Align::Left)
        .add_column(20, ui::Align::Left)
        .add_column(16, ui::Align::Left);
    
    let mut running_count = 0;
    let mut stopped_count = 0;
    
    for svc in services {
        let status_str = format!("{:?}", svc.status);
        if status_str.to_lowercase().contains(garden_common::SERVICE_RUNNING) {
            running_count += 1;
        } else {
            stopped_count += 1;
        }
        
        let status_display = ui::status_indicator(&status_str.to_lowercase(), term.supports_color);
        table.add_row(vec![
            ui::truncate_name(&svc.name, ui::constants::MAX_SERVICE_NAME_LEN),
            status_display,
            if svc.offering.is_empty() { garden_common::VALUE_UNKNOWN.to_string() } else { svc.offering.clone() },
        ]);
    }
    
    println!("{}", table.render());
    println!();
    println!("{}  {} services ({} running, {} stopped)", 
        " ".repeat(ui::constants::DEFAULT_INDENT),
        services.len(), 
        running_count, 
        stopped_count
    );
}

fn format_offering_flag(compat: &OfferingCompatibility) -> &'static str {
    match compat.decision.as_str() {
        garden_common::COMPAT_FALLBACK | garden_common::COMPAT_FAIL => "(!)",
        _ => "",
    }
}

async fn print_offerings_index(
    client: &reqwest::Client,
    endpoint: &str,
) -> anyhow::Result<()> {
    let term = ui::TerminalInfo::detect();
    
    // Fetch running services
    let services_url = format!("{}/api/v1/services", endpoint.trim_end_matches('/'));
    let services: Vec<ServiceInfo> = if let Ok(response) = client.get(&services_url).send().await {
        if let Ok(json) = response.json::<serde_json::Value>().await {
            serde_json::from_value(json.get("data").cloned().unwrap_or(json)).unwrap_or_default()
        } else {
            Vec::new()
        }
    } else {
        Vec::new()
    };
    
    // Display running services if any
    if !services.is_empty() {
        println!("{}", ui::section_header("SERVICES", &term));
        println!();
        render_services_table(&services, &term);
        println!();
        println!();
    }
    
    // Fetch and display available offerings
    let offerings = fetch_offerings(client, endpoint).await?;
    if offerings.is_empty() {
        println!("{}", ui::empty_state("No offerings available", Some("Try: garden-rake offer refresh")));
        return Ok(());
    }

    // Filter out incompatible offerings (decision = "fail")
    let compatible_offerings: Vec<OfferingEntry> = offerings
        .into_iter()
        .filter(|o| o.compatibility.decision != garden_common::COMPAT_FAIL)
        .collect();

    if compatible_offerings.is_empty() {
        let _term = ui::TerminalInfo::detect();
        println!("{}", ui::empty_state("No compatible offerings", Some("All offerings are incompatible with this stone")));
        return Ok(());
    }

    // Group by category
    let mut by_category: BTreeMap<String, Vec<OfferingEntry>> = BTreeMap::new();
    let mut restricted_offerings: Vec<String> = Vec::new();
    for o in compatible_offerings {
        if o.compatibility.decision == garden_common::COMPAT_FALLBACK {
            restricted_offerings.push(o.name.clone());
        }
        by_category.entry(o.category.clone()).or_default().push(o);
    }

    let term = ui::TerminalInfo::detect();
    println!("{}", ui::section_header("AVAILABLE OFFERINGS", &term));
    println!();

    let grid = ui::CategoryGrid::new(&term);
    
    for (category, mut items) in by_category {
        items.sort_by(|a, b| a.name.cmp(&b.name));
        
        // Convert to name strings for CategoryGrid
        let grid_items: Vec<String> = items.iter().map(|o| {
            let flag = if o.compatibility.decision == garden_common::COMPAT_FALLBACK {
                format!("{}{}", o.name, ui::constants::LEGEND_SYMBOL)
            } else {
                o.name.clone()
            };
            flag
        }).collect();
        
        print!("{}", grid.render_category(&category, &grid_items));
        println!();
    }

    if !restricted_offerings.is_empty() {
        println!("{}  {} restricted (uses compatibility fallback)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::constants::LEGEND_SYMBOL);
        println!();
        println!("{}View compatibility details:", " ".repeat(ui::constants::DEFAULT_INDENT));
        for name in &restricted_offerings {
            println!("{}  garden-rake offer {} info", " ".repeat(ui::constants::DEFAULT_INDENT * 2), name);
        }
    }
    
    Ok(())
}

async fn print_offering_info(
    client: &reqwest::Client,
    endpoint: &str,
    offering: &str,
) -> anyhow::Result<()> {
    let moss = GardenHttpClient::new(client, endpoint);
    let path = format!("/api/v1/offerings/{}", offering);
    let response = moss.get_raw(&path).await?;
    
    if response.status() == reqwest::StatusCode::NOT_FOUND {
        anyhow::bail!("Unknown offering: {}", offering);
    }
    
    let api_response: GardenApiResponse<serde_json::Value> = response.error_for_status()?.json().await?;
    let body = api_response.data;

    let name = body.get("name").and_then(|v| v.as_str()).unwrap_or(offering);
    let image = body.get("image").and_then(|v| v.as_str()).unwrap_or("<unknown>");

    println!("Offering: {}", name);
    println!("Image: {}", image);

    if let Some(compat) = body.get("compatibility") {
        let decision = compat.get("decision").and_then(|v| v.as_str()).unwrap_or(garden_common::COMPAT_PASS);
        match decision {
            garden_common::COMPAT_PASS => println!("Compatibility: pass"),
            garden_common::COMPAT_FALLBACK => {
                let reason = compat.get("reason").and_then(|v| v.as_str()).unwrap_or("<unspecified>");
                let original = compat.get("original_image").and_then(|v| v.as_str());
                let fallback = compat.get("fallback_image").and_then(|v| v.as_str());
                println!("Compatibility: fallback");
                if let (Some(o), Some(f)) = (original, fallback) {
                    println!("  From: {}", o);
                    println!("  To:   {}", f);
                }
                println!("  Reason: {}", reason);
            }
            garden_common::COMPAT_FAIL => {
                let reason = compat.get("reason").and_then(|v| v.as_str()).unwrap_or("<unspecified>");
                println!("Compatibility: fail");
                println!("  Reason: {}", reason);
                if let Some(s) = compat.get("suggestion").and_then(|v| v.as_str()) {
                    println!("  Suggestion: {}", s);
                }
                println!("  Result: this offering cannot be installed on this stone");
            }
            other => println!("Compatibility: {}", other),
        }
    }

    if let Some(ports) = body.get("ports").and_then(|v| v.as_array()) {
        if !ports.is_empty() {
            println!("Ports:");
            for p in ports {
                if let (Some(host), Some(container)) = (p.get(0).and_then(|v| v.as_u64()), p.get(1).and_then(|v| v.as_u64())) {
                    println!("  - {}:{}", host, container);
                }
            }
        }
    }

    Ok(())
}

/// Stream job progress updates from Moss stone's /api/v1/events endpoint.
/// Falls back to elapsed-time display if endpoint unavailable (older stones).
///
/// Implements golden standard: Physicality Over Theater
/// - Shows real timing, no fake progress bars
/// - Polls every 500ms for container operations (seconds/minutes duration)
/// - Displays percentage when stone reports it, elapsed time always
async fn stream_job_progress(
    client: &reqwest::Client,
    endpoint: &str,
    job_id: &str,
    service_name: &str,
    quiet_mode: bool,
) -> anyhow::Result<()> {
    let events_url = format!("{}/api/v1/events?job_id={}", endpoint.trim_end_matches('/'), job_id);
    let term = ui::TerminalInfo::detect();
    let start_time = std::time::Instant::now();
    
    // Check if stone supports /api/v1/events (probe with HEAD request)
    let probe = client.head(&events_url).send().await;
    let events_supported = matches!(probe, Ok(resp) if resp.status() != reqwest::StatusCode::NOT_FOUND);
    
    if !events_supported {
        // Fallback: show elapsed time without progress details
        if !quiet_mode {
            println!("{}{} Installing... (progress endpoint unavailable)", 
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::progress_step(true, "")
            );
        }
        
        // Simple elapsed time loop (5 minute timeout)
        let mut interval = tokio::time::interval(std::time::Duration::from_millis(500));
        let timeout = std::time::Duration::from_secs(300);
        
        loop {
            interval.tick().await;
            let elapsed = start_time.elapsed();
            
            if elapsed >= timeout {
                println!("\n{}⏱  Operation timeout ({})", 
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::format_elapsed_time(timeout)
                );
                println!("{}Check status: garden-rake list", " ".repeat(ui::constants::DEFAULT_INDENT));
                break;
            }
            
            // Check completion by querying service list
            let list_url = format!("{}/api/v1/services", endpoint.trim_end_matches('/'));
            if let Ok(response) = client.get(&list_url).send().await {
                if let Ok(value) = response.json::<serde_json::Value>().await {
                    let services: Vec<ServiceInfo> = serde_json::from_value(
                        value.get("data").cloned().unwrap_or(value)
                    ).unwrap_or_default();
                    
                    if services.iter().any(|s| s.name == service_name) {
                        if !quiet_mode {
                            println!("\n{}{} Installation complete [{}]", 
                                " ".repeat(ui::constants::DEFAULT_INDENT),
                                ui::status_indicator("ok", term.supports_color),
                                ui::format_elapsed_time(elapsed)
                            );
                        }
                        break;
                    }
                }
            }
            
            // Update progress display every 2 seconds
            if elapsed.as_secs() % 2 == 0 && !quiet_mode {
                print!("\r{}Installing... [{}]", 
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::format_elapsed_time(elapsed)
                );
                use std::io::Write;
                std::io::stdout().flush().ok();
            }
        }
        
        return Ok(());
    }
    
    // Full progress streaming from /api/v1/events
    if !quiet_mode {
        println!("{}{} Installation started", 
            " ".repeat(ui::constants::DEFAULT_INDENT),
            ui::progress_step(true, "")
        );
    }
    
    let mut interval = tokio::time::interval(std::time::Duration::from_millis(500));
    let timeout = std::time::Duration::from_secs(300); // 5 minutes
    let mut last_message = String::new();
    
    loop {
        interval.tick().await;
        let elapsed = start_time.elapsed();
        
        if elapsed >= timeout {
            println!("\n{}⏱  Operation timeout ({})", 
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::format_elapsed_time(timeout)
            );
            println!("{}Check status: garden-rake list", " ".repeat(ui::constants::DEFAULT_INDENT));
            break;
        }
        
        // Poll /api/v1/events for job updates
        match client.get(&events_url).send().await {
            Ok(response) if response.status().is_success() => {
                if let Ok(event) = response.json::<serde_json::Value>().await {
                    let status = event.get("status").and_then(|v| v.as_str()).unwrap_or("unknown");
                    let message = event.get("message").and_then(|v| v.as_str()).unwrap_or("");
                    let progress = event.get("progress").and_then(|v| v.as_u64());
                    
                    // Display new status updates
                    if !message.is_empty() && message != last_message && !quiet_mode {
                        if let Some(pct) = progress {
                            println!("\r{}{}% {} [{}]", 
                                " ".repeat(ui::constants::DEFAULT_INDENT),
                                pct,
                                message,
                                ui::format_elapsed_time(elapsed)
                            );
                        } else {
                            println!("\r{}{} [{}]", 
                                " ".repeat(ui::constants::DEFAULT_INDENT),
                                message,
                                ui::format_elapsed_time(elapsed)
                            );
                        }
                        last_message = message.to_string();
                    }
                    
                    // Check for completion
                    if status == garden_common::STATUS_COMPLETED || status == garden_common::STATUS_SUCCESS {
                        if !quiet_mode {
                            println!("\n{}{} Installation complete [{}]", 
                                " ".repeat(ui::constants::DEFAULT_INDENT),
                                ui::status_indicator("ok", term.supports_color),
                                ui::format_elapsed_time(elapsed)
                            );
                        }
                        break;
                    } else if status == garden_common::STATUS_FAILED || status == garden_common::STATUS_ERROR {
                        println!("\n{}{} Installation failed: {}", 
                            " ".repeat(ui::constants::DEFAULT_INDENT),
                            ui::status_indicator("error", term.supports_color),
                            message
                        );
                        break;
                    }
                }
            }
            Ok(response) if response.status() == reqwest::StatusCode::NOT_FOUND => {
                // Job completed or not found
                if !quiet_mode {
                    println!("\n{}{} Installation complete (job finished) [{}]", 
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("ok", term.supports_color),
                        ui::format_elapsed_time(elapsed)
                    );
                }
                break;
            }
            _ => {
                // Network error or server issue, continue polling
                if elapsed.as_secs() % 5 == 0 && !quiet_mode {
                    print!("\r{}Checking progress... [{}]", 
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::format_elapsed_time(elapsed)
                    );
                    use std::io::Write;
                    std::io::stdout().flush().ok();
                }
            }
        }
    }
    
    Ok(())
}

async fn fetch_offering_info_json(
    client: &reqwest::Client,
    endpoint: &str,
    offering: &str,
) -> anyhow::Result<serde_json::Value> {
    let moss = GardenHttpClient::new(client, endpoint);
    let path = format!("/api/v1/offerings/{}", offering);
    let response = moss.get_raw(&path).await?;
    
    if response.status() == reqwest::StatusCode::NOT_FOUND {
        anyhow::bail!("Unknown offering: {}", offering);
    }
    
    let api_response: GardenApiResponse<serde_json::Value> = response.error_for_status()?.json().await?;
    Ok(api_response.data)
}

async fn print_alternatives_for_failed_install(
    client: &reqwest::Client,
    endpoint: &str,
    offering: &str,
    prefer: &[String],
) -> anyhow::Result<Option<String>> {
    // Use the offering's own tags/category as an intent query.
    let info = fetch_offering_info_json(client, endpoint, offering).await?;

    let mut seed_tokens: Vec<String> = Vec::new();
    if let Some(category) = info.get("category").and_then(|v| v.as_str()) {
        seed_tokens.push(category.to_string());
    }
    if let Some(tags) = info.get("tags").and_then(|v| v.as_array()) {
        for t in tags.iter().filter_map(|v| v.as_str()) {
            seed_tokens.push(t.to_string());
        }
    }

    // Normalize via dictionary and de-dup.
    let dict = load_taxonomy_dictionary();
    let mut normalized = Vec::new();
    for t in seed_tokens {
        for nt in normalize_tokens(&t, &dict) {
            if !normalized.contains(&nt) {
                normalized.push(nt);
            }
        }
    }

    if normalized.is_empty() {
        return Ok(None);
    }

    let offerings = fetch_offerings(client, endpoint).await?;
    let mut ranked = offerings
        .into_iter()
        .filter(|o| o.compatibility.decision.as_str() != "fail")
        .filter(|o| o.name != offering)
        .map(|o| {
            let s = offering_relevance_score(&normalized, &o);
            (s, o)
        })
        .filter(|(s, _)| *s > 0)
        .collect::<Vec<_>>();

    ranked.sort_by(|(sa, a), (sb, b)| sb.cmp(sa).then_with(|| a.name.cmp(&b.name)));
    if ranked.is_empty() {
        return Ok(Some(normalized.join(",")));
    }

    println!("\nAlternatives:");
    for (idx, (_score, o)) in ranked.into_iter().take(3).enumerate() {
        let flag = format_offering_flag(&o.compatibility);
        let prefix = if flag.is_empty() { "" } else { "(!) " };
        println!("  {}. {} - {}{}", idx + 1, o.name, prefix, o.description);
        println!("     Run: garden-rake offer {} --at {}", o.name, endpoint);
    }

    let q = normalized.join(",");
    if !prefer.is_empty() {
        println!("\nTo search across stones: garden-rake offer {} --at anywhere --prefer {}", q, prefer.join(","));
    } else {
        println!("\nTo search across stones: garden-rake offer {} --at anywhere", q);
    }

    Ok(Some(q))
}

async fn refresh_offerings_index(
    client: &reqwest::Client,
    endpoint: &str,
) -> anyhow::Result<()> {
    let moss = GardenHttpClient::new(client, endpoint);
    let response = moss.post_empty("/api/v1/offerings/refresh").await?;
    if response.status() == reqwest::StatusCode::NOT_FOUND {
        anyhow::bail!(
            "This stone's moss does not support offerings refresh. Upgrade moss and retry."
        );
    }

    let body = response.error_for_status()?.json::<serde_json::Value>().await?;

    let count = body.get("count").and_then(|v| v.as_u64()).unwrap_or(0);
    let generated_at = body
        .get("generated_at")
        .and_then(|v| v.as_str())
        .unwrap_or("<unknown>");

    println!("✓ Offerings index rebuilt");
    println!("  Count: {}", count);
    println!("  Generated: {}", generated_at);

    if let Some(fp) = body.get("fingerprint") {
        // Keep this flexible; moss fingerprint shape may evolve.
        println!("  Fingerprint: {}", fp);
    }

    Ok(())
}

#[derive(Debug, Subcommand)]
enum TemplateCommands {
    /// List available offering templates
    List {
        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },
    
    /// Show template YAML content
    Show {
        /// Template name
        name: String,
        
        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },
}

#[derive(Debug, Subcommand)]
enum WatchTarget {
    /// Watch offering logs
    Offering {
        /// Offering name
        name: String,
        
        /// Subcommand (logs)
        #[command(subcommand)]
        mode: WatchOfferingMode,
    },
    /// Watch stone logs
    Stone {
        /// Stone name
        name: String,
        
        /// Subcommand (logs)
        #[command(subcommand)]
        mode: WatchStoneMode,
    },
}

#[derive(Debug, Subcommand)]
enum WatchOfferingMode {
    /// Stream logs in real-time
    Logs {
        /// Show timestamps
        #[arg(long)]
        timestamps: bool,
    },
}

#[derive(Debug, Subcommand)]
enum WatchStoneMode {
    /// Stream logs from all offerings
    Logs {
        /// Show timestamps
        #[arg(long)]
        timestamps: bool,
    },
}

struct StoneData {
    capabilities: HardwareCapabilities,
    services: Vec<ServiceInfo>,
}

/// Auto-discover a stone and set up tending relationship
/// Tries localhost first, then network discovery
async fn auto_discover_and_tend(client: &reqwest::Client) -> anyhow::Result<()> {
    // Try localhost first
    let local_endpoint = format!("http://127.0.0.1:{}", garden_common::ports::MOSS_HTTP);
    let health_url = format!("{}/health", local_endpoint);
    
    if let Ok(resp) = client.get(&health_url).timeout(Duration::from_millis(200)).send().await {
        if resp.status().is_success() {
            // Get stone name from capabilities
            let caps_url = format!("{}/capabilities", local_endpoint);
            if let Ok(response) = client.get(&caps_url).timeout(Duration::from_secs(5)).send().await {
                if let Ok(response) = response.json::<GardenApiResponse<HardwareCapabilities>>().await {
                    let caps = response.data;
                    tending::write_tending(caps.stone_name.clone(), local_endpoint.clone())?;
                    println!("{}.local (localhost)", caps.stone_name);
                    return Ok(());
                }
            }
        }
    }
    
    // Try network discovery
    match discovery::discover_moss() {
        Ok(endpoint) => {
            // Get capabilities for stone name
            let caps_url = format!("{}/capabilities", endpoint.trim_end_matches('/'));
            let response: GardenApiResponse<HardwareCapabilities> = client
                .get(&caps_url)
                .timeout(Duration::from_secs(5))
                .send()
                .await?
                .json()
                .await?;
            let caps = response.data;
            tending::write_tending(caps.stone_name.clone(), endpoint.clone())?;
            println!("{}.local ({})", caps.stone_name, endpoint.trim_start_matches("http://"));
            Ok(())
        }
        Err(e) => {
            Err(anyhow::anyhow!("No stones found on network or localhost: {}", e))
        }
    }
}

async fn observe_garden(
    client: &reqwest::Client,
    stone_filter: Option<String>,
    offering_filter: Option<String>,
) -> anyhow::Result<()> {
    // Keep offering_filter as-is for Lantern call, create offerings_filter for legacy code
    let offerings_filter: Option<Vec<String>> = offering_filter.as_ref().map(|s| {
        s.split(',')
            .map(|o| o.trim().to_lowercase())
            .collect()
    });

    // Try to discover Lantern first for enhanced topology view
    let term = ui::TerminalInfo::detect();
    let lantern_endpoint = discovery::discover_lantern();
    
    if let Some(ref lantern) = lantern_endpoint {
        tracing::info!(endpoint = %lantern, "Discovered Lantern - using for topology queries");
        
        // Fetch topology from Lantern
        let topology_url = format!("{}/api/v1/stones", lantern);
        match client.get(&topology_url).timeout(Duration::from_secs(5)).send().await {
            Ok(resp) if resp.status().is_success() => {
                if let Ok(topology) = resp.json::<garden_common::LanternTopology>().await {
                    // Display Lantern-sourced topology
                    display_lantern_topology(&topology, offering_filter.as_deref(), term.supports_unicode);
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

    // Display header first
    let supports_unicode = term.supports_unicode; // Capture for closures
    if let Some(ref filter) = offerings_filter {
        println!("{}", ui::section_header(&format!("GARDEN OVERVIEW (filtered: {})", filter.join(", ")), &term));
    } else {
        println!("{}", ui::section_header("GARDEN OVERVIEW", &term));
    }
    println!();

    let discovery_start = std::time::Instant::now();
    let mut stone_data = Vec::new();
    
    // Fallback: Hot cache architecture - Check cache FIRST (zero discovery for common case)
    let cached_stones = STONE_CACHE.get_all();
    
    if !cached_stones.is_empty() {
        tracing::info!(count = cached_stones.len(), "Using cached stone discovery (cache hit)");
        
        // ✅ Stream cached results progressively (Container Infrastructure: "cache hits should also stream")
        for cached in &cached_stones {
            // Filter by name if specified
            if let Some(ref filter_name) = stone_filter {
                if cached.capabilities.stone_name.to_lowercase() != filter_name.to_lowercase() {
                    continue;
                }
            }
            
            let elapsed = discovery_start.elapsed();
            println!("{}{} {} [{}]", 
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::bullet(supports_unicode),
                cached.capabilities.stone_name,
                ui::format_elapsed_time(elapsed)
            );
            
            // Fetch services for cached stone
            let services_url = format!("{}/api/v1/services", cached.endpoint.trim_end_matches('/'));
            if let Ok(resp) = client.get(&services_url).timeout(Duration::from_secs(5)).send().await {
                if let Ok(services_json) = resp.json::<serde_json::Value>().await {
                    let services: Vec<ServiceInfo> = serde_json::from_value(
                        services_json.get("data").cloned().unwrap_or(services_json)
                    ).unwrap_or_default();
                    
                    stone_data.push(StoneData {
                        capabilities: cached.capabilities.clone(),
                        services,
                    });
                }
            }
        }
        
        if stone_filter.is_some() && stone_data.is_empty() {
            println!("{}✗ Stone '{}' not found in cache", " ".repeat(ui::constants::DEFAULT_INDENT), stone_filter.as_ref().unwrap());
            return Ok(());
        }
    } else {
        tracing::info!("Cache miss - performing stone discovery");
        
        // Collect discovered responses for later fetching
        let mut discovered_responses = Vec::new();
        
        // ✅ PROGRESSIVE DISCLOSURE - Stream as stones respond (5 seconds for network discovery)
        let stone_count = discovery::discover_all_moss_stream(
            Duration::from_secs(5),
            |response, discovery_instant| {
                // Filter by name if specified
                if let Some(ref filter_name) = stone_filter {
                    if response.stone_name.to_lowercase() != filter_name.to_lowercase() {
                        return;
                    }
                }
                
                let elapsed = discovery_instant.duration_since(discovery_start);
                println!("{}{} {} [{}]", 
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::bullet(supports_unicode),
                    response.stone_name,
                    ui::format_elapsed_time(elapsed)
                );
                
                // Collect for later fetching
                discovered_responses.push(response);
            }
        );
        
        println!();
        
        // Fetch full data for discovered stones
        for response in discovered_responses {
            let caps_url = format!("{}/capabilities", response.stone_endpoint.trim_end_matches('/'));
            if let Ok(resp) = client.get(&caps_url).timeout(Duration::from_secs(5)).send().await {
                if let Ok(caps_response) = resp.json::<GardenApiResponse<HardwareCapabilities>>().await {
                    let services_url = format!("{}/api/v1/services", response.stone_endpoint.trim_end_matches('/'));
                    if let Ok(svc_resp) = client.get(&services_url).timeout(Duration::from_secs(5)).send().await {
                        if let Ok(services_json) = svc_resp.json::<serde_json::Value>().await {
                            let services: Vec<ServiceInfo> = serde_json::from_value(
                                services_json.get("data").cloned().unwrap_or(services_json)
                            ).unwrap_or_default();
                            
                            stone_data.push(StoneData {
                                capabilities: caps_response.data,
                                services,
                            });
                        }
                    }
                }
            }
        }
        
        if stone_count.is_err() || stone_count.as_ref().ok() == Some(&0) {
            // Fallback to localhost (silent attempt first)
            let localhost = format!("http://127.0.0.1:{}", garden_common::ports::MOSS_HTTP);
            
            // Fetch localhost capabilities and services
            let caps_url = format!("{}/capabilities", localhost);
            if let Ok(resp) = client.get(&caps_url).timeout(Duration::from_secs(5)).send().await {
                if let Ok(caps_response) = resp.json::<GardenApiResponse<HardwareCapabilities>>().await {
                    let services_url = format!("{}/api/v1/services", localhost);
                    if let Ok(svc_resp) = client.get(&services_url).timeout(Duration::from_secs(5)).send().await {
                        if let Ok(services_json) = svc_resp.json::<serde_json::Value>().await {
                            let services: Vec<ServiceInfo> = serde_json::from_value(
                                services_json.get("data").cloned().unwrap_or(services_json)
                            ).unwrap_or_default();
                            
                            stone_data.push(StoneData {
                                capabilities: caps_response.data,
                                services,
                            });
                        }
                    }
                }
            }
        }
    }

    if stone_data.is_empty() {
        println!("{}No reachable stones found", " ".repeat(ui::constants::DEFAULT_INDENT));
        return Ok(());
    }

    // Display detailed stone information
    for stone in &stone_data {
        display_stone(stone, &offerings_filter)?;
    }

    Ok(())
}

/// Display topology from Lantern registry
fn display_lantern_topology(topology: &garden_common::LanternTopology, offering_filter: Option<&str>, supports_unicode: bool) {
    println!("\n═══ GARDEN OVERVIEW (via Lantern) ═══\n");
    
    if topology.stones.is_empty() {
        println!("No stones registered");
        return;
    }

    for stone in &topology.stones {
        let status_marker = match stone.status.as_str() {
            "online" => ui::bullet(supports_unicode),
            "offline" => ui::hollow_bullet(supports_unicode),
            _ => if supports_unicode { "◐" } else { "~" },
        };
        
        println!("{}  {} ({})", status_marker, stone.name, stone.status);

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
            println!("   └─ No matching offerings");
        } else if filtered_services.is_empty() {
            println!("   └─ No offerings");
        } else {
            println!("   OFFERINGS:");
            for (idx, svc) in filtered_services.iter().enumerate() {
                let is_last = idx == filtered_services.len() - 1;
                let branch = if is_last { "└─" } else { "├─" };
                println!("   {} {:<12}  {} ({})", branch, svc.name, svc.status, svc.service_type);
            }
        }

        println!(); // Blank line between stones
    }

    println!("Last updated: {}", topology.last_updated);
}

fn display_stone(stone: &StoneData, offering_filter: &Option<Vec<String>>) -> anyhow::Result<()> {
    let caps = &stone.capabilities;
    let term = ui::TerminalInfo::detect();
    
    // Stone banner
    println!("{}", ui::stone_banner(&caps.stone_name, "online", term.supports_color));
    
    // System info
    println!("{}", ui::label_value_line("ARCH", &caps.hardware.cpu.architecture, ui::constants::DEFAULT_INDENT));
    println!("{}", ui::label_value_line("CPU", &format!("{} cores", caps.hardware.cpu.cores), ui::constants::DEFAULT_INDENT));
    println!("{}", ui::label_value_line("MEMORY", &format!("{} MB", caps.hardware.memory.total_mb), ui::constants::DEFAULT_INDENT));
    if !caps.hardware.gpus.is_empty() {
        println!("{}", ui::label_value_line("GPU", &format!("{} device(s)", caps.hardware.gpus.len()), ui::constants::DEFAULT_INDENT));
    }
    println!();

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
        // Display offerings table
        println!("{}", ui::section_header("OFFERINGS", &term));
        println!();
        
        let mut table = ui::TableBuilder::new()
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

async fn list_templates(
    client: &reqwest::Client,
    endpoint: &str,
) -> anyhow::Result<()> {
    let url = format!("{}/api/v1/services/manifests", endpoint.trim_end_matches('/'));
    let response = client.get(&url).send().await?;
    
    if !response.status().is_success() {
        println!("✗ Failed to retrieve templates: {}", response.status());
        return Ok(());
    }
    
    let api_response: GardenApiResponse<Vec<TemplateInfo>> = response.json().await?;
    let templates = api_response.data;
    if templates.is_empty() {
        println!("\nNo templates available");
    } else {
        println!("\nAvailable Templates:\n");
        
        // Group templates by category
        let mut categories: std::collections::HashMap<String, Vec<&TemplateInfo>> = 
            std::collections::HashMap::new();
        
        for template in &templates {
            let category = template.category.clone();
            categories.entry(category).or_default().push(template);
        }
            
        // Sort categories
        let mut category_names: Vec<String> = categories.keys().cloned().collect();
        category_names.sort();
        
        // Display templates grouped by category
        for category in category_names {
            if let Some(items) = categories.get(&category) {
                let mut sorted_items = items.clone();
                sorted_items.sort_by_key(|t| t.name.as_str());
                
                println!("{}:", category.to_uppercase());
                for template in sorted_items {
                    println!("  {:<18} {}", template.name, template.description);
                }
                println!();
            }
        }
    }
    
    Ok(())
}

async fn show_template(
    client: &reqwest::Client,
    endpoint: &str,
    name: &str,
) -> anyhow::Result<()> {
    let term = ui::TerminalInfo::detect();
    let url = format!("{}/api/v1/services/{}/manifest", endpoint.trim_end_matches('/'), name);
    let response = client.get(&url).send().await?;
    
    // Check status and exit early if not successful
    if !response.status().is_success() {
        eprintln!("✗ Template '{}' not found (HTTP {})", name, response.status());
        std::process::exit(1);
    }
    
    // Only proceed to parsing if status is OK
    match response.status() {
        reqwest::StatusCode::OK => {
            let body: serde_json::Value = response.json().await?;
            if let Some(content) = body.get("content").and_then(|c| c.as_str()) {
                // Parse the YAML to extract metadata
                let parsed: Result<serde_yaml::Value, _> = serde_yaml::from_str(content);
                
                println!("\nTemplate: {}", name);
                
                // Try to extract info from the service snippet
                if let Ok(yaml) = &parsed {
                    // Image/Version
                    if let Some(image) = yaml.get("image").and_then(|i| i.as_str()) {
                        println!("Image: {}", image);
                        if let Some((_, version)) = image.rsplit_once(':') {
                            println!("Version: {}", version);
                        }
                    }
                    
                    // Container name
                    if let Some(container) = yaml.get("container_name").and_then(|c| c.as_str()) {
                        println!("Container: {}", container);
                    }
                    
                    // Ports
                    if let Some(ports) = yaml.get("ports").and_then(|p| p.as_sequence()) {
                        if !ports.is_empty() {
                            println!("\nPorts:");
                            for port in ports {
                                if let Some(port_str) = port.as_str() {
                                    // Parse port mapping (e.g., "27017:27017")
                                    if let Some((host, container)) = port_str.split_once(':') {
                                        let host_clean = host.trim_matches('"');
                                        let container_clean = container.trim_matches('"');
                                        println!("  {} → {}", host_clean, container_clean);
                                    } else {
                                        println!("  {}", port_str);
                                    }
                                }
                            }
                        }
                    }
                    
                    // Environment variables
                    if let Some(env) = yaml.get("environment") {
                        let env_vars = match env {
                            serde_yaml::Value::Sequence(seq) => {
                                seq.iter()
                                    .filter_map(|v| v.as_str())
                                    .map(|s| s.to_string())
                                    .collect::<Vec<_>>()
                            }
                            serde_yaml::Value::Mapping(map) => {
                                map.iter()
                                    .map(|(k, v)| {
                                        format!("{}={}", 
                                            k.as_str().unwrap_or("?"),
                                            v.as_str().unwrap_or("?"))
                                    })
                                    .collect::<Vec<_>>()
                            }
                            _ => vec![],
                        };
                        
                        if !env_vars.is_empty() {
                            println!("\nEnvironment Variables:");
                            for var in env_vars {
                                if let Some((key, value)) = var.split_once('=') {
                                    println!("  {:<30} {}", key, value);
                                } else {
                                    println!("  {}", var);
                                }
                            }
                        }
                    }
                    
                    // Volumes
                    if let Some(volumes) = yaml.get("volumes").and_then(|v| v.as_sequence()) {
                        if !volumes.is_empty() {
                            println!("\nVolumes:");
                            for vol in volumes {
                                if let Some(vol_str) = vol.as_str() {
                                    // Parse volume mapping (e.g., "./data:/data/db" or "mongo-data:/data/db")
                                    if let Some((source, target)) = vol_str.split_once(':') {
                                        let source_clean = source.trim_matches('"');
                                        let target_clean = target.trim_matches('"');
                                        println!("  {} → {}", source_clean, target_clean);
                                    } else {
                                        println!("  {}", vol_str);
                                    }
                                }
                            }
                        }
                    }
                    
                    // Networks
                    if let Some(networks) = yaml.get("networks").and_then(|n| n.as_sequence()) {
                        if !networks.is_empty() {
                            println!("\nNetworks:");
                            for net in networks {
                                if let Some(net_str) = net.as_str() {
                                    println!("  {}", net_str);
                                }
                            }
                        }
                    }
                }
                
                // Show raw YAML content
                println!("\nDocker Compose:");
                println!("───────────────────────────────────────────────────");
                println!("{}", content);
                println!();
            } else {
                eprintln!("{}{} Invalid response format", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color));
            }
        }
        reqwest::StatusCode::NOT_FOUND => {
            eprintln!("{}{} Template '{}' not found", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), name);
            eprintln!("{}Use 'garden-rake template list' to see available templates", " ".repeat(ui::constants::DEFAULT_INDENT));
        }
        status => {
            eprintln!("{}{} Failed to retrieve template: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), status);
        }
    }
    
    Ok(())
}

/// Convert zen syntax to normative args for Clap
fn normalize_zen_to_clap(parsed: &parser::ParsedCommand) -> anyhow::Result<Vec<String>> {
    let mut args = Vec::new();

    // Map zen verbs to Commands
    match parsed.verb.as_str() {
        "offer" => {
            args.push("offer".to_string());
            args.extend(parsed.args.clone());
        }
        "rest" => {
            args.push("rest".to_string());
            args.extend(parsed.args.clone());
        }
        "wake" => {
            args.push("wake".to_string());
            args.extend(parsed.args.clone());
        }
        "nourish" => {
            args.push("upgrade".to_string());
            args.extend(parsed.args.clone());
        }
        "release" => {
            args.push("remove".to_string());
            args.extend(parsed.args.clone());
        }
        "observe" => {
            args.push("observe".to_string());
            args.extend(parsed.args.clone());
        }
        "watch" => {
            args.push("watch".to_string());
            args.extend(parsed.args.clone());
        }
        "touch" => {
            // touch = inspect (deep inspection)
            args.push("status".to_string());
            args.extend(parsed.args.clone());
        }
        "tend" => {
            args.push("tend".to_string());
            args.extend(parsed.args.clone());
        }
        "place" => {
            args.push("place".to_string());
            args.extend(parsed.args.clone());
        }
        "lift" => {
            // lift = remove stone from pond or remove keystone
            // For now, map to place (Phase 3)
            args.push("place".to_string());
            args.extend(parsed.args.clone());
        }
        "invite" => {
            args.push("invite".to_string());
            args.extend(parsed.args.clone());
        }
        "explore" => {
            // explore = list offerings
            args.push("offer".to_string());
        }
        "garden" => {
            // garden = observe all
            args.push("observe".to_string());
        }
        _ => {
            return Err(anyhow::anyhow!("Unknown zen verb: {}", parsed.verb));
        }
    }

    // Add --at flag if at_stone keyword was used
    if let Some(stone) = &parsed.keywords.at_stone {
        args.push("--at".to_string());
        args.push(stone.clone());
    }

    // Note: quietly is handled via quiet_mode in main, not passed to Clap
    // Note: until is handled by the watch command itself

    Ok(args)
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(EnvFilter::from_default_env())
        .init();

    // Pre-parse for zen syntax (before Clap)
    let raw_args: Vec<String> = std::env::args().skip(1).collect();
    let (cli, parsed_keywords) = if !raw_args.is_empty() {
        match parser::parse_args(raw_args.clone()) {
            Ok(parsed) if parsed.style == parser::CommandStyle::Zen => {
                // Convert zen to normative args for Clap
                let normalized = normalize_zen_to_clap(&parsed)?;
                let cli = Cli::parse_from(std::iter::once("garden-rake".to_string()).chain(normalized));
                (cli, Some(parsed.keywords))
            }
            Ok(_) => {
                // Normative style, use Clap normally
                (Cli::parse(), None)
            }
            Err(e) => {
                eprintln!("Error: {}", e);
                std::process::exit(1);
            }
        }
    } else {
        (Cli::parse(), None)
    };

    // Determine if quiet mode is active
    let quiet_mode = cli.quiet 
        || parsed_keywords.as_ref().map(|k| k.quietly).unwrap_or(false)
        || std::env::var("GARDEN_QUIET").is_ok();
    
    // Create pooled HTTP client with connection reuse (hot cache architecture)
    // Configuration optimized for long-running commands (watch/observe):
    // - pool_idle_timeout: 90 seconds (matches stone cache TTL)
    // - pool_max_idle_per_host: 10 (handle multiple concurrent operations)
    // - tcp_keepalive: 60 seconds (prevent connection drops during streams)
    // - timeout: 30 seconds (default per-request timeout, overridable)
    // 
    // This eliminates repeated TCP handshakes and TLS negotiations,
    // reducing latency for sequential requests in watch/observe loops.
    let mut client_builder = reqwest::Client::builder()
        .pool_max_idle_per_host(10)
        .pool_idle_timeout(Duration::from_secs(90))
        .tcp_keepalive(Duration::from_secs(60))
        // .http2_prior_knowledge()  // Disabled: causes connection issues on Windows
        .timeout(Duration::from_secs(30));

    // Add X-Quiet header if quiet mode is active
    if quiet_mode {
        let mut headers = reqwest::header::HeaderMap::new();
        headers.insert("X-Quiet", "true".parse().unwrap());
        client_builder = client_builder.default_headers(headers);
    }

    let client = client_builder.build()?;
    let term = ui::TerminalInfo::detect();

    match cli.command {
        Commands::Status { at } => {
            // Deprecation notice (unless quiet mode)
            if !quiet_mode {
                eprintln!("{}⚠️  'status' command is deprecated", " ".repeat(ui::constants::DEFAULT_INDENT));
                eprintln!("{}Use 'observe' for garden overview or 'tend' to set default stone", " ".repeat(ui::constants::DEFAULT_INDENT));
                eprintln!();
            }
            
            let endpoint = resolve_endpoint(&client, at).await?;
            
            let caps_url = format!("{}/capabilities", endpoint.trim_end_matches('/'));
            let health_url = format!("{}/health", endpoint.trim_end_matches('/'));
            let response: GardenApiResponse<HardwareCapabilities> = client.get(caps_url).send().await?.json().await?;
            let caps = response.data;
            let health_resp: String = client.get(health_url).send().await?.text().await?;
            
            // Parse health response
            let health_json: serde_json::Value = serde_json::from_str(&health_resp).unwrap_or_else(|_| serde_json::json!({}));
            let health_status = health_json.get("status").and_then(|v| v.as_str()).unwrap_or("unknown");
            
            // Display stone banner
            let term = ui::TerminalInfo::detect();
            println!("{}", ui::stone_banner(&caps.stone_name, health_status, term.supports_color));
            println!();
            
            // System info section
            println!("{}", ui::section_header("SYSTEM", &term));
            println!("{}", ui::label_value_line("ARCH", &caps.hardware.cpu.architecture, ui::constants::DEFAULT_INDENT));
            println!("{}", ui::label_value_line("CPU", &format!("{} cores", caps.hardware.cpu.cores), ui::constants::DEFAULT_INDENT));
            println!("{}", ui::label_value_line("MEMORY", &format!("{} MB", caps.hardware.memory.total_mb), ui::constants::DEFAULT_INDENT));
            
            // GPU section
            if !caps.hardware.gpus.is_empty() {
                println!();
                println!("{}", ui::section_header("GPU", &term));
                for gpu in &caps.hardware.gpus {
                    println!("{}", ui::label_value_line("DEVICE", &format!("{} {}", gpu.vendor, gpu.model), ui::constants::DEFAULT_INDENT));
                    println!("{}", ui::label_value_line("CAPABILITIES", &gpu.capabilities.join(", "), ui::constants::DEFAULT_INDENT));
                }
            }
            
            // Health details if available
            if let Some(checks) = health_json.get("checks").and_then(|v| v.as_object()) {
                println!();
                println!("{}", ui::section_header("HEALTH", &term));
                for (check_name, check_data) in checks {
                    let status = check_data.get("status").and_then(|v| v.as_str()).unwrap_or("unknown");
                    let indicator = ui::status_indicator(status, term.supports_color);
                    println!("{}{} {}", " ".repeat(ui::constants::DEFAULT_INDENT), indicator, check_name.to_uppercase());
                }
            }
        }

        Commands::Offer { offering, action, at, prefer, anywhere_on_fail } => {
            if at.as_deref() == Some("anywhere") {
                match (offering.as_deref(), action) {
                    (Some("refresh"), None) => {
                        anyhow::bail!("'offer refresh' requires a specific stone (remove --at anywhere)");
                    }
                    (Some(q), None) => {
                        print_offer_anywhere_recommendations(&client, q, &prefer).await?;
                    }
                    _ => {
                        anyhow::bail!("Usage with --at anywhere: garden-rake offer <query> --at anywhere [--prefer <token>]");
                    }
                }
                return Ok(());
            }

            let endpoint = resolve_endpoint(&client, at).await?;

            // If the user provided a non-offering token, treat it as a query.
            // We detect this by fetching offerings and checking for an exact name match.
            if let (Some(name), None) = (offering.as_deref(), action.as_ref()) {
                if name != "refresh" {
                    let offerings = fetch_offerings(&client, &endpoint).await?;
                    let is_known_offering = offerings.iter().any(|o| o.name == name);
                    if !is_known_offering {
                        print_offer_query_recommendations(&client, &endpoint, name, &prefer).await?;
                        return Ok(());
                    }
                }
            }

            match (offering.as_deref(), action) {
                (None, None) => {
                    print_offerings_index(&client, &endpoint).await?;
                }
                (Some("refresh"), None) => {
                    refresh_offerings_index(&client, &endpoint).await?;
                }
                (Some(name), Some(OfferAction::Info)) => {
                    print_offering_info(&client, &endpoint, name).await?;
                }
                (Some(name), None) => {
                    // Check if service is already installed
                    let services_url = format!("{}/api/v1/services", endpoint.trim_end_matches('/'));
                    if let Ok(response) = client.get(&services_url).send().await {
                        if let Ok(json) = response.json::<serde_json::Value>().await {
                            let services: Vec<ServiceInfo> = serde_json::from_value(json.get("data").cloned().unwrap_or(json)).unwrap_or_default();
                            if let Some(existing) = services.iter().find(|s| s.offering == name) {
                                let term = ui::TerminalInfo::detect();
                                let status_str = format!("{:?}", existing.status).to_lowercase();
                                let status_icon = ui::status_indicator(&status_str, term.supports_color);
                                
                                println!("{}{} Service '{}' is already installed ({})", 
                                    " ".repeat(ui::constants::DEFAULT_INDENT),
                                    status_icon,
                                    existing.name,
                                    status_str
                                );
                                println!();
                                println!("{}Options:", " ".repeat(ui::constants::DEFAULT_INDENT));
                                println!("{}  • View details:  garden-rake show {}", " ".repeat(ui::constants::DEFAULT_INDENT * 2), existing.name);
                                println!("{}  • Remove service: garden-rake remove {}", " ".repeat(ui::constants::DEFAULT_INDENT * 2), existing.name);
                                if status_str.contains(garden_common::SERVICE_STOPPED) {
                                    println!("{}  • Start service:  garden-rake start {}", " ".repeat(ui::constants::DEFAULT_INDENT * 2), existing.name);
                                } else if status_str.contains(garden_common::SERVICE_RUNNING) {
                                    println!("{}  • Stop service:   garden-rake stop {}", " ".repeat(ui::constants::DEFAULT_INDENT * 2), existing.name);
                                    println!("{}  • Restart service: garden-rake restart {}", " ".repeat(ui::constants::DEFAULT_INDENT * 2), existing.name);
                                }
                                return Ok(());
                            }
                        }
                    }
                    
                    // v1 API: POST /api/v1/services with JSON body
                    let url = format!("{}/api/v1/services", endpoint.trim_end_matches('/'));
                    let payload = serde_json::json!({
                        "offering": name,
                        "ports": [],
                        "environment": {}
                    });
                    
                    let response = client.post(url).json(&payload).send().await?;
                    let status = response.status();
                    let body = response.json::<serde_json::Value>().await.ok();

                    match status {
                        reqwest::StatusCode::ACCEPTED | reqwest::StatusCode::OK => {
                            let term = ui::TerminalInfo::detect();
                            if let Some(body) = body {
                                // Parse ServiceActionResponse from v1 API
                                let service_name = body.get("service").and_then(|v| v.as_str()).unwrap_or(name);
                                let action = body.get("action").and_then(|v| v.as_str()).unwrap_or("create");
                                let api_status = body.get("status").and_then(|v| v.as_str()).unwrap_or("pending");
                                let message = body.get("message").and_then(|v| v.as_str()).unwrap_or("");
                                
                                println!("{}", ui::stone_banner(service_name, api_status, term.supports_color));
                                println!();
                                
                                // Extract job_id from message if present
                                let job_id = if message.contains("Job ID:") || message.contains("job:") {
                                    // Try to parse job_id from message (format: "Job ID: abc123" or similar)
                                    message
                                        .split_whitespace()
                                        .skip_while(|s| !s.contains("ID") && !s.contains("job"))
                                        .nth(1)
                                        .map(|s| s.trim_end_matches(&['.', ',', '!'][..]).to_string())
                                } else {
                                    None
                                };

                                if let Some(job_id) = job_id {
                                    // Stream progress updates if stone supports /api/v1/events
                                    stream_job_progress(&client, &endpoint, &job_id, service_name, quiet_mode).await?;
                                } else if message.contains("Adopted") {
                                    println!("{}{} Service already exists (adopted)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color));
                                    println!("{}{}", " ".repeat(ui::constants::DEFAULT_INDENT), message);
                                } else if message.contains("maintenance") {
                                    println!("{}{} Under maintenance, retry later", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("pending", term.supports_color));
                                } else {
                                    println!("{}{} {} ({})", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), action, api_status);
                                    if !message.is_empty() {
                                        println!("{}{}", " ".repeat(ui::constants::DEFAULT_INDENT), message);
                                    }
                                }
                                
                                // Display suggestions from v1 API (if not quiet)
                                if !quiet_mode {
                                    if let Some(suggestions) = body.get("suggestions").and_then(|v| v.as_array()) {
                                        if !suggestions.is_empty() {
                                            println!();
                                            println!("{}", ui::section_header("SUGGESTIONS", &term));
                                            for suggestion in suggestions {
                                                if let Some(s) = suggestion.as_str() {
                                                    println!("{}  • {}", " ".repeat(ui::constants::DEFAULT_INDENT), s);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        reqwest::StatusCode::BAD_REQUEST => {
                            let term = ui::TerminalInfo::detect();
                            // Moss v1 uses structured ApiError
                            if let Some(body) = body {
                                let code = body
                                    .get("error")
                                    .and_then(|e| e.get("code"))
                                    .and_then(|v| v.as_str())
                                    .unwrap_or("<unknown>");
                                let msg = body
                                    .get("error")
                                    .and_then(|e| e.get("message"))
                                    .and_then(|v| v.as_str())
                                    .unwrap_or("Request failed");

                                println!("{}{} {} ({})", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), msg, code);

                                if let Some(details) = body.get("error").and_then(|e| e.get("details")) {
                                    if let Some(reason) = details.get("reason").and_then(|v| v.as_str()) {
                                        println!("{}Reason: {}", " ".repeat(ui::constants::DEFAULT_INDENT * 2), reason);
                                    }
                                    if let Some(suggestion) = details.get("suggestion").and_then(|v| v.as_str()) {
                                        println!("{}Suggestion: {}", " ".repeat(ui::constants::DEFAULT_INDENT * 2), suggestion);
                                    }
                                }

                                if code == garden_common::error_codes::COMPATIBILITY_FAILED {
                                    let derived_query = print_alternatives_for_failed_install(&client, &endpoint, name, &prefer)
                                        .await
                                        .ok()
                                        .flatten();

                                    if anywhere_on_fail {
                                        if let Some(q) = derived_query {
                                            println!("\n{}Searching across stones...", " ".repeat(ui::constants::DEFAULT_INDENT));
                                            let _ = print_offer_anywhere_recommendations(&client, &q, &prefer).await;
                                        }
                                    }
                                }
                            } else {
                                println!("{}{} Failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), status);
                            }
                        }
                        reqwest::StatusCode::NOT_FOUND => {
                            let term = ui::TerminalInfo::detect();
                            // Unknown offering name? Treat it like a query on this stone.
                            println!("{}{} Unknown offering: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), name);
                            let _ = print_offer_query_recommendations(&client, &endpoint, name, &prefer).await;
                        }
                        s if s.is_success() => {
                            let term = ui::TerminalInfo::detect();
                            println!("{}{} Offered {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), name);
                        }
                        reqwest::StatusCode::NOT_IMPLEMENTED => {
                            println!("{}ℹ️  Offer not implemented on server", " ".repeat(ui::constants::DEFAULT_INDENT));
                        }
                        _ => {
                            let term = ui::TerminalInfo::detect();
                            println!("{}{} Failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), status);
                        }
                    }
                }
                (None, Some(_)) => {
                    anyhow::bail!("Usage: garden-rake offer <offering> info");
                }
            }
        }

        Commands::List { at } => {
            let endpoint = resolve_endpoint(&client, at).await?;
            
            let url = format!("{}/api/v1/services", endpoint.trim_end_matches('/'));
            let response: serde_json::Value = client.get(url).send().await?.json().await?;
            let services: Vec<ServiceInfo> = serde_json::from_value(response.get("data").cloned().unwrap_or(response))?;
            
            let term = ui::TerminalInfo::detect();
            
            if services.is_empty() {
                println!("{}", ui::empty_state("No services installed", Some("Use: garden-rake offer <service>")));
            } else {
                println!("{}", ui::section_header("SERVICES", &term));
                println!();
                render_services_table(&services, &term);
            }
        }

        Commands::Remove { service, at, force } => {
            let endpoint = resolve_endpoint(&client, at).await?;
            print_stone_header(&client, &endpoint).await;
            
            // Confirmation prompt (unless --force or quiet mode)
            if !force && !quiet_mode {
                println!("{}⚠️  This will permanently remove service '{}'", " ".repeat(ui::constants::DEFAULT_INDENT), service);
                println!("{}Container and any associated volumes will be deleted.", " ".repeat(ui::constants::DEFAULT_INDENT));
                print!("{}Continue? [y/N]: ", " ".repeat(ui::constants::DEFAULT_INDENT));
                use std::io::Write;
                std::io::stdout().flush()?;
                
                let mut input = String::new();
                std::io::stdin().read_line(&mut input)?;
                
                if !input.trim().eq_ignore_ascii_case("y") {
                    println!("{}Cancelled", " ".repeat(ui::constants::DEFAULT_INDENT));
                    return Ok(());
                }
                println!();
            }
            
            // v1 API: DELETE /api/v1/services/:service
            let url = format!(
                "{}/api/v1/services/{}",
                endpoint.trim_end_matches('/'),
                service
            );
            let response = client.delete(url).send().await?;
            let status = response.status();
            
            match status {
                s if s.is_success() => {
                    // Parse v1 API response
                    if let Ok(body) = response.json::<serde_json::Value>().await {
                        let message = body.get("message").and_then(|v| v.as_str()).unwrap_or("");
                        
                        println!("{}{} Removed {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), service);
                        if !message.is_empty() {
                            println!("{}   {}", " ".repeat(ui::constants::DEFAULT_INDENT), message);
                        }
                        
                        // Display suggestions if present and not in quiet mode
                        if !quiet_mode {
                            if let Some(suggestions) = body.get("suggestions").and_then(|v| v.as_array()) {
                                if !suggestions.is_empty() {
                                    println!("\nSuggestions:");
                                    for suggestion in suggestions {
                                        if let Some(s) = suggestion.as_str() {
                                            println!("  • {}", s);
                                        }
                                    }
                                }
                            }
                        }
                    } else {
                        println!("{}{} Removed {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), service);
                    }
                }
                reqwest::StatusCode::NOT_FOUND => {
                    eprintln!("{}{} Service '{}' not found", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), service);
                }
                _ => {
                    eprintln!("{}{} Failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), status);
                }
            }
        }

        Commands::Upgrade { service, all, at } => {
            let endpoint = resolve_endpoint(&client, at).await?;

            if all || service.is_none() {
                // Batch upgrade all services (iterate v1 nourish endpoints)
                // First, get list of all services
                let list_url = format!("{}/api/v1/services", endpoint.trim_end_matches('/'));
                let list_response = client.get(&list_url).send().await?;
                
                if !list_response.status().is_success() {
                    eprintln!("{}{} Failed to retrieve service list: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), list_response.status());
                    return Ok(());
                }
                
                let services_body: serde_json::Value = list_response.json().await?;
                let services = services_body.as_array().or_else(|| services_body.get("data").and_then(|d| d.as_array()));
                
                if let Some(service_list) = services {
                    let mut upgraded = Vec::new();
                    let mut failed = Vec::new();
                    
                    for svc in service_list {
                        if let Some(name) = svc.get("name").and_then(|n| n.as_str()) {
                            let nourish_url = format!("{}/api/v1/services/{}/nourish", endpoint.trim_end_matches('/'), name);
                            let response = client.post(&nourish_url).send().await?;
                            
                            if response.status().is_success() {
                                upgraded.push(name.to_string());
                            } else {
                                failed.push(name.to_string());
                            }
                        }
                    }
                    
                    if !upgraded.is_empty() {
                        println!("{}{} Upgraded {} service(s)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), upgraded.len());
                        for name in &upgraded {
                            println!("{}  - {}", " ".repeat(ui::constants::DEFAULT_INDENT), name);
                        }
                    }
                    
                    if !failed.is_empty() {
                        eprintln!("{}{} Failed to upgrade {} service(s)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), failed.len());
                        for name in &failed {
                            eprintln!("{}  - {}", " ".repeat(ui::constants::DEFAULT_INDENT), name);
                        }
                    }
                } else {
                    eprintln!("{}{} No services found", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color));
                }
            } else if let Some(svc_name) = service {
                // v1 API: POST /api/v1/services/:service/nourish
                let url = format!(
                    "{}/api/v1/services/{}/nourish",
                    endpoint.trim_end_matches('/'),
                    svc_name
                );
                let response = client.post(url).send().await?;
                let status = response.status();
                
                match status {
                    s if s.is_success() => {
                        // Parse v1 API response
                        if let Ok(body) = response.json::<serde_json::Value>().await {
                            let message = body.get("message").and_then(|v| v.as_str()).unwrap_or("");
                            let api_status = body.get("status").and_then(|v| v.as_str()).unwrap_or("upgraded");
                            
                            println!("{}{} Upgraded {} ({})", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), svc_name, api_status);
                            if !message.is_empty() {
                                println!("{}   {}", " ".repeat(ui::constants::DEFAULT_INDENT), message);
                            }
                            
                            // Display suggestions if present and not in quiet mode
                            if !quiet_mode {
                                if let Some(suggestions) = body.get("suggestions").and_then(|v| v.as_array()) {
                                    if !suggestions.is_empty() {
                                        println!("\nSuggestions:");
                                        for suggestion in suggestions {
                                            if let Some(s) = suggestion.as_str() {
                                                println!("  • {}", s);
                                            }
                                        }
                                    }
                                }
                            }
                        } else {
                            println!("{}{} Upgraded {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), svc_name);
                        }
                    }
                    reqwest::StatusCode::ACCEPTED => {
                        println!("{}{} Service under maintenance, retry later", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("pending", term.supports_color));
                    }
                    reqwest::StatusCode::NOT_FOUND => {
                        eprintln!("{}{} Service '{}' not found", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), svc_name);
                    }
                    _ => {
                        eprintln!("{}{} Failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), status);
                    }
                }
            }
        }

        Commands::Rest { service, at } => {
            let endpoint = resolve_endpoint(&client, at).await?;
            // v1 API: POST /api/v1/services/:service/rest
            let url = format!(
                "{}/api/v1/services/{}/rest",
                endpoint.trim_end_matches('/'),
                service
            );
            let response = client.post(url).send().await?;
            let status = response.status();
            
            match status {
                s if s.is_success() => {
                    // Parse v1 API response
                    if let Ok(body) = response.json::<serde_json::Value>().await {
                        let message = body.get("message").and_then(|v| v.as_str()).unwrap_or("");
                        let api_status = body.get("status").and_then(|v| v.as_str()).unwrap_or("stopped");
                        
                        println!("{}{} Stopped {} ({})", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), service, api_status);
                        if !message.is_empty() {
                            println!("{}   {}", " ".repeat(ui::constants::DEFAULT_INDENT), message);
                        }
                        
                        // Display suggestions if present and not in quiet mode
                        if !quiet_mode {
                            if let Some(suggestions) = body.get("suggestions").and_then(|v| v.as_array()) {
                                if !suggestions.is_empty() {
                                    println!("\nSuggestions:");
                                    for suggestion in suggestions {
                                        if let Some(s) = suggestion.as_str() {
                                            println!("  • {}", s);
                                        }
                                    }
                                }
                            }
                        }
                    } else {
                        println!("{}{} Stopped {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), service);
                    }
                }
                reqwest::StatusCode::NOT_FOUND => {
                    eprintln!("{}{} Service '{}' not found", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), service);
                }
                _ => {
                    eprintln!("{}{} Failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), status);
                }
            }
        }

        Commands::Wake { service, at } => {
            let endpoint = resolve_endpoint(&client, at).await?;
            // v1 API: POST /api/v1/services/:service/wake
            let url = format!(
                "{}/api/v1/services/{}/wake",
                endpoint.trim_end_matches('/'),
                service
            );
            let response = client.post(url).send().await?;
            let status = response.status();
            
            match status {
                s if s.is_success() => {
                    // Parse v1 API response
                    if let Ok(body) = response.json::<serde_json::Value>().await {
                        let message = body.get("message").and_then(|v| v.as_str()).unwrap_or("");
                        let api_status = body.get("status").and_then(|v| v.as_str()).unwrap_or("running");
                        
                        println!("{}{} Started {} ({})", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), service, api_status);
                        if !message.is_empty() {
                            println!("{}   {}", " ".repeat(ui::constants::DEFAULT_INDENT), message);
                        }
                        
                        // Display suggestions if present and not in quiet mode
                        if !quiet_mode {
                            if let Some(suggestions) = body.get("suggestions").and_then(|v| v.as_array()) {
                                if !suggestions.is_empty() {
                                    println!("\nSuggestions:");
                                    for suggestion in suggestions {
                                        if let Some(s) = suggestion.as_str() {
                                            println!("  • {}", s);
                                        }
                                    }
                                }
                            }
                        }
                    } else {
                        println!("{}{} Started {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), service);
                    }
                }
                reqwest::StatusCode::NOT_FOUND => {
                    eprintln!("{}{} Service '{}' not found", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), service);
                }
                _ => {
                    eprintln!("{}{} Failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), status);
                }
            }
        }

        Commands::Place {
            target,
            code,
            passphrase,
            at,
        } => {
            let endpoint = resolve_endpoint(&client, at).await?;
            
            match target.as_str() {
                "keystone" => {
                    // Initialize pond (pond init)
                    let pass = passphrase.clone().unwrap_or_else(|| {
                        // In a real implementation, prompt for passphrase
                        "changeme".to_string()
                    });
                    
                    let url = format!("{}/api/v1/pond/init", endpoint.trim_end_matches('/'));
                    let payload = serde_json::json!({ "passphrase": pass });
                    
                    match client.post(&url).json(&payload).send().await {
                        Ok(response) if response.status() == reqwest::StatusCode::NOT_IMPLEMENTED => {
                            println!("{}{} Pond security not yet implemented (Phase 3b)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("info", term.supports_color));
                            println!("{}This command will initialize pond security with encrypted certificates.", " ".repeat(ui::constants::DEFAULT_INDENT));
                        }
                        Ok(response) if response.status().is_success() => {
                            println!("{}{} Pond initialized (keystone placed)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color));
                        }
                        Ok(response) => {
                            eprintln!("{}{} Failed to initialize pond: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), response.status());
                        }
                        Err(e) => {
                            eprintln!("{}{} Request failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), e);
                        }
                    }
                }
                "stone" => {
                    // Join pond (pond join)
                    if code.is_none() {
                        eprintln!("{}{} Error: --code required for placing a stone", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color));
                        eprintln!("{}Example: garden-rake place stone --code ABC123", " ".repeat(ui::constants::DEFAULT_INDENT));
                        return Ok(());
                    }
                    
                    let url = format!("{}/api/v1/pond/join", endpoint.trim_end_matches('/'));
                    let payload = serde_json::json!({ "code": code.unwrap() });
                    
                    match client.post(&url).json(&payload).send().await {
                        Ok(response) if response.status() == reqwest::StatusCode::NOT_IMPLEMENTED => {
                            println!("{}{} Pond security not yet implemented (Phase 3b)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("info", term.supports_color));
                            println!("{}This command will join an existing pond using an invitation code.", " ".repeat(ui::constants::DEFAULT_INDENT));
                        }
                        Ok(response) if response.status().is_success() => {
                            println!("{}{} Joined pond successfully", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color));
                        }
                        Ok(response) => {
                            eprintln!("{}{} Failed to join pond: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), response.status());
                        }
                        Err(e) => {
                            eprintln!("{}{} Request failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), e);
                        }
                    }
                }
                _ => {
                    eprintln!("{}{} Invalid target: '{}'. Use 'keystone' or 'stone'", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), target);
                }
            }
        }

        Commands::Invite { at } => {
            let endpoint = resolve_endpoint(&client, at).await?;
            let url = format!("{}/api/v1/pond/invite", endpoint.trim_end_matches('/'));
            
            match client.post(&url).send().await {
                Ok(response) if response.status() == reqwest::StatusCode::NOT_IMPLEMENTED => {
                    println!("{}{} Pond security not yet implemented (Phase 3b)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("info", term.supports_color));
                    println!("{}This command will generate a time-limited TOTP invitation code.", " ".repeat(ui::constants::DEFAULT_INDENT));
                }
                Ok(response) if response.status().is_success() => {
                    if let Ok(body) = response.json::<serde_json::Value>().await {
                        if let Some(code) = body.get("data").and_then(|d| d.get("code")).and_then(|c| c.as_str()) {
                            println!("{}{} Invitation code: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), code);
                            if let Some(ttl) = body.get("data").and_then(|d| d.get("ttl_seconds")).and_then(|t| t.as_u64()) {
                                println!("   Valid for {} seconds", ttl);
                            }
                        }
                    }
                }
                Ok(response) => {
                    eprintln!("{}{} Failed to generate invitation: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), response.status());
                }
                Err(e) => {
                    eprintln!("{}{} Request failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), e);
                }
            }
        }

        Commands::Observe { stone, offering } => {
            observe_garden(&client, stone, offering).await?;
        }

        Commands::Watch { target, until, at } => {
            match target {
                Some(WatchTarget::Offering { name, mode }) => {
                    let WatchOfferingMode::Logs { timestamps } = mode;
                    let endpoint = resolve_endpoint(&client, at).await?;
                    watch_offering_logs(&client, &endpoint, &name, timestamps).await?;
                }
                Some(WatchTarget::Stone { name, mode }) => {
                    let WatchStoneMode::Logs { timestamps } = mode;
                    // For stone logs, we need to resolve the specific stone endpoint
                    let endpoint = if let Some(at_endpoint) = at {
                        at_endpoint.clone()
                    } else {
                        // Try to resolve by stone name
                        let mut endpoints = Vec::new();
                        let _ = discovery::discover_all_moss_stream(
                            Duration::from_secs(2),
                            |response, _instant| {
                                endpoints.push(response);
                            },
                        );
                        
                        if endpoints.is_empty() {
                            eprintln!("{}{} No stones discovered", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color));
                            return Ok(());
                        }
                        
                        let mut found = None;
                        for response in endpoints {
                            let ep = &response.stone_endpoint;
                            let caps_url = format!("{}/capabilities", ep.trim_end_matches('/'));
                            if let Ok(resp) = client.get(&caps_url).send().await {
                                if let Ok(caps_response) = resp.json::<GardenApiResponse<HardwareCapabilities>>().await {
                                    if caps_response.data.stone_name.to_lowercase() == name.to_lowercase() {
                                        found = Some(ep.clone());
                                        break;
                                    }
                                }
                            }
                        }
                        
                        match found {
                            Some(ep) => ep,
                            None => {
                                eprintln!("{}{} Stone '{}' not found", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), name);
                                return Ok(());
                            }
                        }
                    };
                    watch_stone_logs(&endpoint, &name, timestamps).await?;
                }
                None => {
                    // Original behavior: watch events
                    let endpoint = resolve_endpoint(&client, at).await?;
                    watch_events(&client, &endpoint, until).await?;
                }
            }
        }

        Commands::Template { command } => {
            match command {
                TemplateCommands::List { at } => {
                    let endpoint = resolve_endpoint(&client, at).await?;
                    
                    list_templates(&client, &endpoint).await?;
                }
                
                TemplateCommands::Show { name, at } => {
                    let endpoint = resolve_endpoint(&client, at).await?;
                    
                    show_template(&client, &endpoint, &name).await?;
                }
            }
        }

        Commands::Tend { target, clear, verbose } => {
            if clear {
                tending::clear_tending()?;
                println!("Tending state cleared.");
                return Ok(());
            }

            if let Some(target_value) = target {
                match target_value.as_str() {
                    "this" | "local" => {
                        // Tend to localhost - validate moss is running
                        let local_endpoint = format!("http://127.0.0.1:{}", garden_common::ports::MOSS_HTTP);
                        let health_url = format!("{}/health", local_endpoint);
                        
                        match client.get(&health_url).timeout(Duration::from_millis(200)).send().await {
                            Ok(resp) if resp.status().is_success() => {
                                // Get stone name from capabilities
                                let caps_url = format!("{}/capabilities", local_endpoint);
                                let response: GardenApiResponse<HardwareCapabilities> = client.get(&caps_url).timeout(Duration::from_secs(5)).send().await?.json().await?;
                                let caps = response.data;
                                tending::write_tending(caps.stone_name.clone(), local_endpoint.clone())?;
                                println!("Now tending to: {} (localhost)", caps.stone_name);
                            }
                            _ => {
                                return Err(anyhow::anyhow!(
                                    "No local moss detected.\n\n\
                                    Options:\n\
                                    • Auto-discover stone: garden-rake tend auto\n\
                                    • Explicit endpoint: garden-rake tend http://<ip>:7185"
                                ));
                            }
                        }
                    }
                    "auto" => {
                        // Force fresh discovery
                        tending::clear_tending()?;
                        println!("Discovering stones...");
                        match discovery::discover_moss() {
                            Ok(endpoint) => {
                                // Get capabilities for stone name
                                let caps_url = format!("{}/capabilities", endpoint.trim_end_matches('/'));
                                let response: GardenApiResponse<HardwareCapabilities> = client.get(&caps_url).timeout(Duration::from_secs(5)).send().await?.json().await?;
                                let caps = response.data;
                                tending::write_tending(caps.stone_name.clone(), endpoint.clone())?;
                                println!("  Found {}.local ({})", caps.stone_name, endpoint.trim_start_matches("http://"));
                                println!("  Now tending to {}.local", caps.stone_name);
                            }
                            Err(_) => {
                                return Err(anyhow::anyhow!("No stones discovered on network"));
                            }
                        }
                    }
                    url if url.starts_with("http://") || url.starts_with("https://") => {
                        // Explicit endpoint - validate it
                        let health_url = format!("{}/health", url.trim_end_matches('/'));
                        match client.get(&health_url).timeout(Duration::from_secs(3)).send().await {
                            Ok(resp) if resp.status().is_success() => {
                                let caps_url = format!("{}/capabilities", url.trim_end_matches('/'));
                                let response: GardenApiResponse<HardwareCapabilities> = client.get(&caps_url).timeout(Duration::from_secs(5)).send().await?.json().await?;
                                let caps = response.data;
                                tending::write_tending(caps.stone_name.clone(), url.to_string())?;
                                println!("Now tending to: {} ({})", caps.stone_name, url);
                            }
                            _ => {
                                return Err(anyhow::anyhow!("Could not connect to endpoint: {}", url));
                            }
                        }
                    }
                    stone_name => {
                        // Resolve stone name (or simple host) to an endpoint
                        let endpoint = resolve_target_endpoint(&client, stone_name, Some(&*STONE_CACHE)).await?;

                        // Validate it and store tending state
                        let health_url = format!("{}/health", endpoint.trim_end_matches('/'));
                        match client.get(&health_url).timeout(Duration::from_secs(3)).send().await {
                            Ok(resp) if resp.status().is_success() => {
                                let caps_url = format!("{}/capabilities", endpoint.trim_end_matches('/'));
                                let caps: HardwareCapabilities = client
                                    .get(&caps_url)
                                    .timeout(Duration::from_secs(5))
                                    .send()
                                    .await?
                                    .json::<GardenApiResponse<HardwareCapabilities>>()
                                    .await?
                                    .data;
                                tending::write_tending(caps.stone_name.clone(), endpoint.to_string())?;
                                println!("Now tending to: {}.local ({})", caps.stone_name, endpoint.trim_start_matches("http://"));
                            }
                            _ => {
                                return Err(anyhow::anyhow!("Could not connect to stone '{}' ({})", stone_name, endpoint));
                            }
                        }
                    }
                }
            } else {
                // Show current tending state - auto-discover if needed
                match tending::read_tending() {
                    Ok(state) if state.is_valid() => {
                        // Valid cached state - verify stone is still reachable
                        let health_url = format!("{}/health", state.endpoint.trim_end_matches('/'));
                        match client.get(&health_url).timeout(Duration::from_millis(500)).send().await {
                            Ok(resp) if resp.status().is_success() => {
                                if verbose {
                                    println!("Tending to: {}.local ({})", state.stone_name, state.endpoint);
                                    println!("Last seen: {} seconds ago", state.age_seconds());
                                    println!("Status: Active ({} seconds remaining in cache)", state.ttl_remaining_seconds());
                                } else {
                                    println!("{}.local ({})", state.stone_name, state.endpoint.trim_start_matches("http://"));
                                }
                            }
                            _ => {
                                // Stone no longer reachable - try to rediscover
                                tracing::debug!("Tended stone {} no longer reachable, attempting rediscovery", state.stone_name);
                                if let Err(e) = auto_discover_and_tend(&client).await {
                                    return Err(anyhow::anyhow!(
                                        "Previously tended stone '{}' is no longer reachable.\n\n{}\n\n\
                                        Options:\n\
                                        • Auto-discover stone: garden-rake tend auto\n\
                                        • Explicit endpoint: garden-rake tend http://<ip>:7185",
                                        state.stone_name, e
                                    ));
                                }
                            }
                        }
                    }
                    Ok(state) => {
                        // Expired state - try to rediscover
                        tracing::debug!("Tending state expired, attempting rediscovery");
                        if let Err(e) = auto_discover_and_tend(&client).await {
                            return Err(anyhow::anyhow!(
                                "Tending state expired (last tended: {}).\n\n{}\n\n\
                                Options:\n\
                                • Auto-discover stone: garden-rake tend auto\n\
                                • Explicit endpoint: garden-rake tend http://<ip>:7185",
                                state.stone_name, e
                            ));
                        }
                    }
                    Err(_) => {
                        // No cached state - auto-discover
                        tracing::debug!("No tending state found, attempting auto-discovery");
                        if let Err(e) = auto_discover_and_tend(&client).await {
                            return Err(anyhow::anyhow!(
                                "Not tending to any stone.\n\n{}\n\n\
                                Options:\n\
                                • Auto-discover stone: garden-rake tend auto\n\
                                • Explicit endpoint: garden-rake tend http://<ip>:7185",
                                e
                            ));
                        }
                    }
                }
            }
        }

        Commands::Pond { action, at } => {
            let endpoint = resolve_endpoint(&client, at).await?;
            
            match action {
                PondAction::Init { passphrase } => {
                    let pass = passphrase.clone().unwrap_or_else(|| {
                        // In a real implementation, prompt for passphrase securely
                        println!("{}{} Using default passphrase. Use --passphrase for custom encryption.", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("info", term.supports_color));
                        "changeme".to_string()
                    });
                    
                    let url = format!("{}/api/v1/pond/init", endpoint.trim_end_matches('/'));
                    let payload = serde_json::json!({ "passphrase": pass });
                    
                    match client.post(&url).json(&payload).send().await {
                        Ok(response) if response.status() == reqwest::StatusCode::NOT_IMPLEMENTED => {
                            println!("{}{} Pond security not yet implemented (Phase 3b)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("info", term.supports_color));
                            println!("{}This command will initialize pond security with encrypted certificates.", " ".repeat(ui::constants::DEFAULT_INDENT));
                            println!("{}Future: Creates cornerstone and keystone for multi-stone trust.", " ".repeat(ui::constants::DEFAULT_INDENT));
                        }
                        Ok(response) if response.status().is_success() => {
                            println!("{}{} Pond initialized successfully", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color));
                            if let Ok(body) = response.json::<serde_json::Value>().await {
                                if let Some(cornerstone) = body.get("data").and_then(|d| d.get("cornerstone")).and_then(|c| c.as_str()) {
                                    println!("   Cornerstone: {}", cornerstone);
                                }
                            }
                        }
                        Ok(response) => {
                            eprintln!("{}{} Failed to initialize pond: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), response.status());
                        }
                        Err(e) => {
                            eprintln!("{}{} Request failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), e);
                        }
                    }
                }
                PondAction::Status => {
                    let url = format!("{}/api/v1/pond/status", endpoint.trim_end_matches('/'));
                    
                    match client.get(&url).send().await {
                        Ok(response) if response.status().is_success() => {
                            if let Ok(body) = response.json::<serde_json::Value>().await {
                                if let Some(data) = body.get("data") {
                                    let active = data.get("active").and_then(|a| a.as_bool()).unwrap_or(false);
                                    let tier = data.get("tier").and_then(|t| t.as_str()).unwrap_or("unknown");
                                    let note = data.get("note").and_then(|n| n.as_str()).unwrap_or("");
                                    
                                    if active {
                                        println!("{}{} Pond active", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color));
                                        if let Some(cornerstone) = data.get("cornerstone").and_then(|c| c.as_str()) {
                                            println!("   Cornerstone: {}", cornerstone);
                                        }
                                        if let Some(stones) = data.get("stones").and_then(|s| s.as_array()) {
                                            println!("   Stones: {}", stones.len());
                                            for stone in stones {
                                                if let Some(name) = stone.get("name").and_then(|n| n.as_str()) {
                                                    let is_cornerstone = stone.get("is_cornerstone").and_then(|i| i.as_bool()).unwrap_or(false);
                                                    let marker = if is_cornerstone { " (cornerstone)" } else { "" };
                                                    println!("     • {}{}", name, marker);
                                                }
                                            }
                                        }
                                    } else {
                                        println!("○ Pond not active");
                                        if !note.is_empty() {
                                            println!("   {}", note);
                                        }
                                    }
                                    println!("   Tier: {}", tier);
                                }
                            }
                        }
                        Ok(response) => {
                            eprintln!("{}{} Failed to get pond status: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), response.status());
                        }
                        Err(e) => {
                            eprintln!("{}{} Request failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), e);
                        }
                    }
                }
                PondAction::Invite => {
                    let url = format!("{}/api/v1/pond/invite", endpoint.trim_end_matches('/'));
                    
                    match client.post(&url).send().await {
                        Ok(response) if response.status() == reqwest::StatusCode::NOT_IMPLEMENTED => {
                            println!("{}{} Pond security not yet implemented (Phase 3b)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("info", term.supports_color));
                            println!("{}This command will generate a time-limited TOTP invitation code.", " ".repeat(ui::constants::DEFAULT_INDENT));
                        }
                        Ok(response) if response.status().is_success() => {
                            if let Ok(body) = response.json::<serde_json::Value>().await {
                                if let Some(data) = body.get("data") {
                                    if let Some(code) = data.get("code").and_then(|c| c.as_str()) {
                                        println!("{}{} Invitation code: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), code);
                                        if let Some(ttl) = data.get("ttl_seconds").and_then(|t| t.as_u64()) {
                                            println!("   Valid for {} seconds", ttl);
                                        }
                                        if let Some(inviter) = data.get("inviter_stone").and_then(|i| i.as_str()) {
                                            println!("   From: {}", inviter);
                                        }
                                    }
                                }
                            }
                        }
                        Ok(response) => {
                            eprintln!("{}{} Failed to generate invitation: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), response.status());
                        }
                        Err(e) => {
                            eprintln!("{}{} Request failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), e);
                        }
                    }
                }
                PondAction::Join { code } => {
                    let url = format!("{}/api/v1/pond/join", endpoint.trim_end_matches('/'));
                    let payload = serde_json::json!({ "code": code });
                    
                    match client.post(&url).json(&payload).send().await {
                        Ok(response) if response.status() == reqwest::StatusCode::NOT_IMPLEMENTED => {
                            println!("{}{} Pond security not yet implemented (Phase 3b)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("info", term.supports_color));
                            println!("{}This command will join an existing pond using an invitation code.", " ".repeat(ui::constants::DEFAULT_INDENT));
                        }
                        Ok(response) if response.status().is_success() => {
                            println!("{}{} Joined pond successfully", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color));
                            if let Ok(body) = response.json::<serde_json::Value>().await {
                                if let Some(data) = body.get("data") {
                                    if let Some(stone_name) = data.get("stone_name").and_then(|s| s.as_str()) {
                                        println!("   Stone: {}", stone_name);
                                    }
                                    if let Some(cornerstone) = data.get("cornerstone").and_then(|c| c.as_str()) {
                                        println!("   Cornerstone: {}", cornerstone);
                                    }
                                }
                            }
                        }
                        Ok(response) => {
                            eprintln!("{}{} Failed to join pond: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), response.status());
                        }
                        Err(e) => {
                            eprintln!("{}{} Request failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), e);
                        }
                    }
                }
                PondAction::Remove => {
                    let url = format!("{}/api/v1/pond", endpoint.trim_end_matches('/'));
                    
                    match client.delete(&url).send().await {
                        Ok(response) if response.status() == reqwest::StatusCode::NOT_IMPLEMENTED => {
                            println!("{}{} Pond security not yet implemented (Phase 3b)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("info", term.supports_color));
                            println!("{}This command will remove pond security from this stone.", " ".repeat(ui::constants::DEFAULT_INDENT));
                        }
                        Ok(response) if response.status().is_success() => {
                            println!("{}{} Pond removed from this stone", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color));
                        }
                        Ok(response) => {
                            eprintln!("{}{} Failed to remove pond: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), response.status());
                        }
                        Err(e) => {
                            eprintln!("{}{} Request failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), e);
                        }
                    }
                }
                PondAction::Untrust { stone_name } => {
                    let url = format!("{}/api/v1/pond/stones/{}", endpoint.trim_end_matches('/'), stone_name);
                    
                    match client.delete(&url).send().await {
                        Ok(response) if response.status() == reqwest::StatusCode::NOT_IMPLEMENTED => {
                            println!("{}{} Pond security not yet implemented (Phase 3b)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("info", term.supports_color));
                            println!("{}This command will remove a stone from the pond trust network.", " ".repeat(ui::constants::DEFAULT_INDENT));
                        }
                        Ok(response) if response.status().is_success() => {
                            println!("{}{} Removed {} from pond", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), stone_name);
                        }
                        Ok(response) => {
                            eprintln!("{}{} Failed to untrust stone: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), response.status());
                        }
                        Err(e) => {
                            eprintln!("{}{} Request failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), e);
                        }
                    }
                }
            }
        }

        Commands::Lift { target_type, stone_name, at } => {
            let endpoint = resolve_endpoint(&client, at).await?;
            
            match target_type.as_str() {
                "keystone" => {
                    // Remove pond (pond remove)
                    let url = format!("{}/api/v1/pond", endpoint.trim_end_matches('/'));
                    
                    match client.delete(&url).send().await {
                        Ok(response) if response.status() == reqwest::StatusCode::NOT_IMPLEMENTED => {
                            println!("{}{} Pond security not yet implemented (Phase 3b)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("info", term.supports_color));
                            println!("{}This command will remove pond security (lift the keystone).", " ".repeat(ui::constants::DEFAULT_INDENT));
                        }
                        Ok(response) if response.status().is_success() => {
                            println!("{}{} Keystone lifted (pond removed)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color));
                        }
                        Ok(response) => {
                            eprintln!("{}{} Failed to lift keystone: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), response.status());
                        }
                        Err(e) => {
                            eprintln!("{}{} Request failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), e);
                        }
                    }
                }
                "stone" => {
                    // Untrust stone (pond untrust)
                    if stone_name.is_none() {
                        eprintln!("{}{} Error: stone name required for 'lift stone'", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color));
                        eprintln!("{}Example: garden-rake lift stone stone-02", " ".repeat(ui::constants::DEFAULT_INDENT));
                        return Ok(());
                    }
                    
                    let name = stone_name.as_ref().unwrap();
                    let url = format!("{}/api/v1/pond/stones/{}", endpoint.trim_end_matches('/'), name);
                    
                    match client.delete(&url).send().await {
                        Ok(response) if response.status() == reqwest::StatusCode::NOT_IMPLEMENTED => {
                            println!("{}{} Pond security not yet implemented (Phase 3b)", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("info", term.supports_color));
                            println!("{}This command will remove a stone from the pond trust network.", " ".repeat(ui::constants::DEFAULT_INDENT));
                        }
                        Ok(response) if response.status().is_success() => {
                            println!("{}{} Lifted {} from pond", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), name);
                        }
                        Ok(response) => {
                            eprintln!("{}{} Failed to lift stone: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), response.status());
                        }
                        Err(e) => {
                            eprintln!("{}{} Request failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), e);
                        }
                    }
                }
                _ => {
                    eprintln!("{}{} Invalid target: '{}'. Use 'keystone' or 'stone'", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), target_type);
                }
            }
        }

        Commands::Refresh { component, from, at } => {
            let endpoint = resolve_endpoint(&client, at).await?;
            println!("Refreshing {}...", component);
            refresh_component(&client, &endpoint, &component, &from).await?;
        }

        Commands::Reconcile { drop_invalid, at } => {
            let endpoint = resolve_endpoint(&client, at).await?;

            let body = reconcile_system(&client, &endpoint, drop_invalid).await?;

            let adopted = body
                .get("adopted")
                .and_then(|v| v.as_array())
                .map(|a| a.len())
                .unwrap_or(0);
            let dropped = body
                .get("dropped_invalid")
                .and_then(|v| v.as_array())
                .map(|a| a.len())
                .unwrap_or(0);
            let left = body
                .get("left_unregistered")
                .and_then(|v| v.as_array())
                .map(|a| a.len())
                .unwrap_or(0);

            println!("{}{} Reconcile complete", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color));
            println!("{}  Adopted: {}", " ".repeat(ui::constants::DEFAULT_INDENT), adopted);
            if drop_invalid {
                println!("  Dropped invalid: {}", dropped);
            }
            if left > 0 {
                println!("  Left unregistered: {}", left);
            }
        }
    }

    Ok(())
}

async fn watch_offering_logs(
    client: &reqwest::Client,
    endpoint: &str,
    offering: &str,
    timestamps: bool,
) -> anyhow::Result<()> {
    use futures_util::StreamExt;

    let url = format!(
        "{}/api/services/{}/logs{}",
        endpoint.trim_end_matches('/'),
        offering,
        if timestamps { "?timestamps=true" } else { "" }
    );
    
    println!("📡 Streaming logs from offering: {}\n", offering);
    let response = client
        .get(&url)
        .header("Accept", "text/event-stream")
        .send()
        .await?;

    if response.status() == reqwest::StatusCode::NOT_FOUND {
        println!("✗ Offering '{}' not found", offering);
        return Ok(());
    }

    if !response.status().is_success() {
        anyhow::bail!("Failed to connect: {}", response.status());
    }

    let mut stream = response.bytes_stream();

    let mut buffer = String::new();

    while let Some(chunk_result) = stream.next().await {
        let chunk = chunk_result?;
        let text = String::from_utf8_lossy(&chunk);
        buffer.push_str(&text);

        // Process complete SSE messages
        while let Some(pos) = buffer.find("\n\n") {
            let message = buffer[..pos].to_string();
            buffer.drain(..pos + 2);

            // Parse SSE message
            let mut event_type = "";
            let mut data = String::new();

            for line in message.lines() {
                if let Some(event) = line.strip_prefix("event: ") {
                    event_type = event;
                } else if let Some(d) = line.strip_prefix("data: ") {
                    data.push_str(d);
                }
            }

            if event_type == "log" {
                // Parse log line JSON
                if let Ok(log_line) = serde_json::from_str::<serde_json::Value>(&data) {
                    let log_text = log_line["log"].as_str().unwrap_or("");
                    let stream_type = log_line["stream"].as_str().unwrap_or("stdout");
                    
                    // Color code: red for stderr, white for stdout
                    if stream_type == "stderr" {
                        print!("\x1b[31m{}\x1b[0m", log_text);
                    } else {
                        print!("{}", log_text);
                    }
                }
            } else if event_type == "error" {
                println!("\n⚠ {}", data);
            }
        }
    }

    Ok(())
}

async fn watch_stone_logs(
    _endpoint: &str,
    _stone_name: &str,
    _timestamps: bool,
) -> anyhow::Result<()> {
    println!("✗ Stone-wide log streaming not yet implemented");
    println!("   Use: garden-rake watch offering <name> logs");
    Ok(())
}

async fn watch_events(
    client: &reqwest::Client,
    endpoint: &str,
    until_pattern: Option<String>,
) -> anyhow::Result<()> {
    use futures_util::StreamExt;

    let url = format!("{}/api/v1/events", endpoint.trim_end_matches('/'));
    println!("📡 Watching events from {}\n", endpoint);

    if let Some(ref pattern) = until_pattern {
        println!("⏳ Will exit when '{}' appears\n", pattern);
    }
    let response = client
        .get(&url)
        .header("Accept", "text/event-stream")
        .send()
        .await?;

    if !response.status().is_success() {
        anyhow::bail!("Failed to connect to event stream: {}", response.status());
    }

    let mut stream = response.bytes_stream();
    let mut buffer = String::new();

    while let Some(chunk) = stream.next().await {
        match chunk {
            Ok(bytes) => {
                buffer.push_str(&String::from_utf8_lossy(&bytes));
                
                // Process complete events (ended by \n\n)
                while let Some(pos) = buffer.find("\n\n") {
                    let event_text = buffer[..pos].to_string();
                    buffer.drain(..pos + 2);

                    // Parse SSE event
                    for line in event_text.lines() {
                        if let Some(data) = line.strip_prefix("data: ") {
                            // Skip "data: "
                            
                            // Try to parse as JSON
                            if let Ok(parsed) = serde_json::from_str::<serde_json::Value>(data) {
                                let timestamp = parsed.get("timestamp")
                                    .and_then(|t| t.as_str())
                                    .unwrap_or("");
                                let message = parsed.get("message")
                                    .and_then(|m| m.as_str())
                                    .unwrap_or(data);
                                let level = parsed.get("level")
                                    .and_then(|l| l.as_str())
                                    .unwrap_or("info");

                                // Colorize based on level
                                let symbol = match level {
                                    "error" => "✗",
                                    "warn" => "⚠",
                                    "info" => "ℹ",
                                    "debug" => "⚙",
                                    _ => "•",
                                };

                                let time_part = if timestamp.len() >= 19 {
                                    format!("[{}] ", &timestamp[11..19]) // HH:MM:SS from ISO timestamp
                                } else {
                                    // No event timestamp - use current wall-clock time
                                    format!("[{}] ", ui::format_wall_clock())
                                };

                                println!("{}{} {}", time_part, symbol, message);

                                // Check until pattern
                                if let Some(ref pattern) = until_pattern {
                                    if message.contains(pattern) {
                                        println!("\n✓ Pattern '{}' found, exiting\n", pattern);
                                        return Ok(());
                                    }
                                }
                            } else {
                                // Raw event data - add wall-clock timestamp
                                println!("[{}] • {}", ui::format_wall_clock(), data);

                                if let Some(ref pattern) = until_pattern {
                                    if data.contains(pattern) {
                                        println!("\n✓ Pattern '{}' found, exiting\n", pattern);
                                        return Ok(());
                                    }
                                }
                            }
                        }
                    }
                }
            }
            Err(e) => {
                tracing::warn!("Stream error: {}", e);
                println!("\n✗ Connection lost\n");
                break;
            }
        }
    }

    Ok(())
}
async fn refresh_component(
    client: &reqwest::Client,
    endpoint: &str,
    component: &str,
    binary_path: &std::path::Path,
) -> anyhow::Result<()> {
    use anyhow::{bail, Context};
    
    // Normalize component name
    let normalized_component = match component.to_lowercase().as_str() {
        "moss" => "moss",
        "rake" | "garden-rake" => garden_common::names::RAKE_BINARY,
        _ => bail!("Unknown component '{}'. Use 'moss' or 'rake'", component),
    };
    
    // Read binary file
    println!("📤 Reading binary file...");
    let binary_data = std::fs::read(binary_path)
        .context(format!("Failed to read binary file: {}", binary_path.display()))?;
    
    let size_mb = binary_data.len() as f64 / 1024.0 / 1024.0;
    println!("   Size: {:.2} MB", size_mb);
    
    // Basic validation: check for ELF header
    if binary_data.len() < 4 || &binary_data[0..4] != b"\x7fELF" {
        bail!("Not a valid ELF binary. Expected Linux executable.");
    }
    println!("   Format: ELF ✓");
    
    // Encode to base64
    println!("📦 Encoding binary...");
    let encoded = base64::engine::general_purpose::STANDARD.encode(&binary_data);
    
    // Send to moss
    println!("🚀 Uploading to stone...");
    let url = format!("{}/api/system/refresh", endpoint.trim_end_matches('/'));
    let response = client
        .post(&url)
        .json(&serde_json::json!({
            "component": normalized_component,
            "binary_data": encoded,
        }))
        .timeout(Duration::from_secs(30))
        .send()
        .await
        .context("Failed to send refresh request")?;
    
    let status = response.status();
    
    // Get response body as text first to see what we got
    let body_text = response.text().await
        .context("Failed to read response body")?;
    
    // Try to parse as JSON
    let body: serde_json::Value = match serde_json::from_str(&body_text) {
        Ok(json) => json,
        Err(e) => {
            println!("✗ Invalid JSON response");
            println!("   Status: {}", status);
            println!("   Response body: {}", body_text.chars().take(500).collect::<String>());
            bail!("Failed to parse JSON response: {}", e);
        }
    };
    
    if !status.is_success() {
        println!("✗ Refresh failed");
        println!("   Status: {}", status);
        if let Some(error) = body.get("error") {
            println!("   Error: {}", error);
        }
        if let Some(message) = body.get("message") {
            println!("   Message: {}", message);
        }
        bail!("Refresh request failed with status {}", status);
    }
    
    // Success
    println!("✅ {} refreshed successfully", normalized_component);
    
    if let Some(arch) = body.get("architecture").and_then(|v| v.as_str()) {
        println!("   Architecture: {}", arch);
    }
    
    if normalized_component == "moss" {
        println!("⏳ Moss is restarting...");
        println!("   (This may take a few seconds)");
        
        // Wait a moment for moss to restart
        tokio::time::sleep(Duration::from_secs(3)).await;
        
        // Try to ping moss
        let health_url = format!("{}/health", endpoint.trim_end_matches('/'));
        for attempt in 1..=5 {
            tokio::time::sleep(Duration::from_secs(1)).await;
            match client.get(&health_url).timeout(Duration::from_secs(2)).send().await {
                Ok(resp) if resp.status().is_success() => {
                    println!("✅ Moss is back online");
                    return Ok(());
                }
                _ => {
                    if attempt < 5 {
                        print!(".");
                        std::io::Write::flush(&mut std::io::stdout()).ok();
                    }
                }
            }
        }
        
        println!("\n⚠️  Moss did not respond after restart (this may be normal)");
        println!("   Check garden-moss status: systemctl status garden-moss.service");
    }
    
    Ok(())
}

async fn reconcile_system(
    client: &reqwest::Client,
    endpoint: &str,
    drop_invalid: bool,
) -> anyhow::Result<serde_json::Value> {
    use anyhow::Context;

    let url = format!("{}/api/system/reconcile", endpoint.trim_end_matches('/'));
    let payload = serde_json::json!({ "drop_invalid": drop_invalid });
    let response = client
        .post(&url)
        .json(&payload)
        .timeout(Duration::from_secs(30))
        .send()
        .await
        .context("Failed to send reconcile request")?;

    if response.status().is_success() {
        return Ok(response.json::<serde_json::Value>().await?);
    }

    let status = response.status();
    let text = response.text().await.unwrap_or_default();
    anyhow::bail!("Reconcile failed with status {}: {}", status, text);
}
