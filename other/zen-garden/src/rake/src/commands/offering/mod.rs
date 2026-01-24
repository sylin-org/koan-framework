//! Offering commands
//!
//! Commands for managing service offerings:
//! - List available offerings
//! - Install offerings
//! - Query/search offerings
//! - View offering details

use std::collections::BTreeMap;
use std::time::Duration;
use anyhow::Result;
use async_trait::async_trait;
use garden_common::{CliFormatter, GardenApiResponse, GardenHttpClient, HardwareCapabilities, ServiceInfo};
use crate::commands::Command;
use crate::context::CommandContext;
use crate::discovery;
use crate::ui;

// ============================================================================
// Types
// ============================================================================

#[derive(Debug, serde::Deserialize)]
pub struct OfferingEntry {
    pub name: String,
    pub category: String,
    pub description: String,
    #[serde(default)]
    pub tags: Vec<String>,
    pub image: String,
    #[serde(default)]
    pub compatibility: OfferingCompatibility,
}

#[derive(Debug, Default, serde::Deserialize)]
pub struct OfferingCompatibility {
    #[serde(default)]
    pub decision: String,
    pub reason: Option<String>,
    pub original_image: Option<String>,
    pub fallback_image: Option<String>,
    pub suggestion: Option<String>,
}

#[derive(Debug, serde::Deserialize)]
pub struct TaxonomyDictionary {
    #[serde(default)]
    pub map: std::collections::HashMap<String, String>,
}

#[derive(Debug, serde::Deserialize)]
pub struct PlacementResponse {
    pub recommendations: Vec<PlacementRecommendation>,
    pub evaluated_stones: usize,
    pub timestamp: String,
}

#[derive(Debug, serde::Deserialize)]
pub struct PlacementRecommendation {
    pub stone_id: String,
    pub hostname: String,
    pub score: i32,
    pub is_local: bool,
    pub compatibility: String,
    pub metrics: PlacementMetrics,
    pub services_count: usize,
    pub breakdown: ScoreBreakdown,
}

#[derive(Debug, serde::Deserialize)]
pub struct PlacementMetrics {
    pub memory_free_mb: u64,
    pub memory_total_mb: u64,
    pub cpu_load_percent: u8,
    pub storage_free_gb: u64,
    pub storage_total_gb: u64,
    pub storage_type: String,
}

#[derive(Debug, serde::Deserialize)]
pub struct ScoreBreakdown {
    pub compatibility: i32,
    pub memory: i32,
    pub cpu: i32,
    pub storage: i32,
    pub hardware: i32,
    pub distribution: i32,
    pub tended_bonus: i32,
}

/// Action for offer command
#[derive(Debug, Clone)]
pub enum OfferAction {
    /// List all offerings
    List,
    /// Refresh offerings index
    Refresh,
    /// Show offering info
    Info { name: String },
    /// Install offering
    Install { name: String },
    /// Query/search offerings
    Query { query: String },
    /// Query across all stones
    QueryAnywhere { query: String },
    /// Get intelligent placement recommendation
    PlacementRecommend { name: String, quiet: bool },
}

pub struct OfferCommand {
    pub action: OfferAction,
    pub prefer: Vec<String>,
    pub anywhere_on_fail: bool,
    pub quiet_mode: bool,
}

// ============================================================================
// Taxonomy / Search Functions
// ============================================================================

fn load_taxonomy_dictionary() -> TaxonomyDictionary {
    // Repo-owned dictionary (compiled into rake) so query behavior is stable and portable.
    let raw = include_str!(concat!(env!("CARGO_MANIFEST_DIR"), "/../../manifests/taxonomy.dictionary.yaml"));
    serde_yaml::from_str::<TaxonomyDictionary>(raw).unwrap_or(TaxonomyDictionary {
        map: Default::default(),
    })
}

pub fn normalize_tokens(raw: &str, dict: &TaxonomyDictionary) -> Vec<String> {
    raw.split([',', ' ', '\t', '\n', '\r'])
        .map(|t| t.trim().to_lowercase())
        .filter(|t| !t.is_empty())
        .map(|t| dict.map.get(&t).cloned().unwrap_or(t))
        .collect()
}

pub fn token_matches_category(token: &str, category: &str) -> bool {
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

pub fn offering_relevance_score(tokens: &[String], offering: &OfferingEntry) -> i32 {
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

pub fn stone_prefer_score(prefer: &[String], caps: Option<&HardwareCapabilities>) -> i32 {
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

// ============================================================================
// API Functions
// ============================================================================

async fn fetch_offerings(
    client: &reqwest::Client,
    endpoint: &str,
) -> Result<Vec<OfferingEntry>> {
    let moss = GardenHttpClient::new(client, endpoint);
    let response = moss.get_raw("/api/v1/offerings").await?;

    if response.status() == reqwest::StatusCode::NOT_FOUND {
        anyhow::bail!("This stone's moss does not support validated offerings. Upgrade moss and retry.");
    }

    let api_response: GardenApiResponse<Vec<OfferingEntry>> = response.error_for_status()?.json().await?;
    Ok(api_response.data)
}

async fn fetch_capabilities(client: &reqwest::Client, endpoint: &str) -> Result<HardwareCapabilities> {
    let moss = GardenHttpClient::new(client, endpoint);
    let response: GardenApiResponse<HardwareCapabilities> = moss.get("/capabilities").await?;
    Ok(response.data)
}

async fn fetch_offering_info_json(
    client: &reqwest::Client,
    endpoint: &str,
    offering: &str,
) -> Result<serde_json::Value> {
    let moss = GardenHttpClient::new(client, endpoint);
    let path = format!("/api/v1/offerings/{}", offering);
    let response = moss.get_raw(&path).await?;

    if response.status() == reqwest::StatusCode::NOT_FOUND {
        anyhow::bail!("Unknown offering: {}", offering);
    }

    let api_response: GardenApiResponse<serde_json::Value> = response.error_for_status()?.json().await?;
    Ok(api_response.data)
}

async fn refresh_offerings_index(
    client: &reqwest::Client,
    endpoint: &str,
) -> Result<()> {
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
        println!("  Fingerprint: {}", fp);
    }

    Ok(())
}

// ============================================================================
// Display Functions
// ============================================================================

fn format_offering_flag(compat: &OfferingCompatibility) -> &'static str {
    match compat.decision.as_str() {
        garden_common::COMPAT_FALLBACK | garden_common::COMPAT_FAIL => "(!)",
        _ => "",
    }
}

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

async fn print_offerings_index(
    client: &reqwest::Client,
    endpoint: &str,
) -> Result<()> {
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

    println!("{}", ui::section_header("AVAILABLE OFFERINGS", &term));
    println!();

    let grid = ui::CategoryGrid::new(&term);

    for (category, mut items) in by_category {
        items.sort_by(|a, b| a.name.cmp(&b.name));

        let grid_items: Vec<String> = items.iter().map(|o| {
            if o.compatibility.decision == garden_common::COMPAT_FALLBACK {
                format!("{}{}", o.name, ui::constants::LEGEND_SYMBOL)
            } else {
                o.name.clone()
            }
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
) -> Result<()> {
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

// ============================================================================
// Recommendation Functions
// ============================================================================

async fn print_offer_query_recommendations(
    client: &reqwest::Client,
    endpoint: &str,
    query: &str,
    prefer: &[String],
) -> Result<()> {
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
) -> Result<()> {
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

async fn print_alternatives_for_failed_install(
    client: &reqwest::Client,
    endpoint: &str,
    offering: &str,
    prefer: &[String],
) -> Result<Option<String>> {
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

// ============================================================================
// Job Progress Streaming
// ============================================================================

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
) -> Result<()> {
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
        let mut interval = tokio::time::interval(Duration::from_millis(500));
        let timeout = Duration::from_secs(300);

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

    let mut interval = tokio::time::interval(Duration::from_millis(500));
    let timeout = Duration::from_secs(300); // 5 minutes
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

/// Handle intelligent placement recommendation
/// 
/// Interactive mode: Show recommendations, let user select
/// Quiet mode: Auto-select top stone and install
async fn handle_placement_recommendation(
    client: &reqwest::Client,
    offering: &str,
    quiet: bool,
) -> Result<()> {
    let term = ui::TerminalInfo::detect();
    let indent = " ".repeat(ui::constants::DEFAULT_INDENT);
    
    // Show waiting message (placement evaluation takes time)
    if !quiet {
        println!("{}⏳ Evaluating placement options for '{}'...", indent, offering);
        println!();
    }
    
    // Discover available stones
    let stones = discovery::discover_moss_auto(Duration::from_secs(3))?;
    
    if stones.is_empty() {
        println!("{}{} No stones discovered on network", indent, ui::status_indicator("error", term.supports_color));
        println!("{}Try: garden-rake status", indent);
        return Ok(());
    }
    
    // Try to get placement recommendation from first available stone
    // (In multi-stone setups, any stone can coordinate placement)
    let mut placement_response: Option<PlacementResponse> = None;
    
    for stone in &stones {
        let url = format!("{}/api/v1/garden/recommend", stone.stone_endpoint.trim_end_matches('/'));
        let payload = serde_json::json!({
            "offering": offering,
            "preferences": [],
            "top_n": 3
        });
        
        match client.post(&url).json(&payload).timeout(Duration::from_secs(5)).send().await {
            Ok(response) if response.status().is_success() => {
                if let Ok(json) = response.json::<serde_json::Value>().await {
                    if let Ok(data) = serde_json::from_value::<GardenApiResponse<PlacementResponse>>(json.clone()) {
                        placement_response = Some(data.data);
                        break;
                    } else if let Ok(data) = serde_json::from_value::<PlacementResponse>(json) {
                        placement_response = Some(data);
                        break;
                    }
                }
            }
            _ => continue,
        }
    }
    
    let placement = match placement_response {
        Some(p) => p,
        None => {
            println!("{}{} Could not get placement recommendations from any stone", indent, ui::status_indicator("error", term.supports_color));
            println!("{}Verify that Moss is running on at least one stone", indent);
            return Ok(());
        }
    };
    
    if placement.recommendations.is_empty() {
        println!("{}{} No compatible stones found for '{}'", indent, ui::status_indicator("error", term.supports_color), offering);
        println!("{}This offering may not be available or compatible with your network", indent);
        return Ok(());
    }
    
    // Quiet mode: Auto-select top recommendation
    if quiet {
        let top = &placement.recommendations[0];
        println!("{}Installing '{}' on {}...", indent, offering, top.hostname);
        
        // Find the stone's endpoint
        if let Some(stone) = stones.iter().find(|s| s.stone_name == top.hostname) {
            return install_on_stone(client, &stone.stone_endpoint, offering, quiet).await;
        } else {
            println!("{}{} Could not find endpoint for stone '{}'", indent, ui::status_indicator("error", term.supports_color), top.hostname);
            return Ok(());
        }
    }
    
    // Interactive mode: Show recommendations
    let fmt = CliFormatter::new();
    println!("{}{}", indent, fmt.title(&format!("PLACEMENT RECOMMENDATIONS FOR '{}'", offering.to_uppercase())));
    println!("{}{}", indent, fmt.divider(&"─".repeat(60)));
    println!();
    
    let top_n = placement.recommendations.len().min(3);
    for (idx, rec) in placement.recommendations.iter().take(top_n).enumerate() {
        let rank = idx + 1;
        let compat_icon = match rec.compatibility.as_str() {
            "compatible" => if term.supports_color { "✅" } else { "[OK]" },
            "fallback" => if term.supports_color { "⚠️" } else { "[WARN]" },
            _ => if term.supports_color { "❌" } else { "[FAIL]" },
        };
        
        println!("{}{}. {} {} (score: {})", indent, rank, compat_icon, rec.hostname, rec.score);
        println!("{}   Stone: {}", indent, rec.stone_id);
        
        // Show key metrics
        let mem_pct = (rec.metrics.memory_free_mb as f64 / rec.metrics.memory_total_mb as f64 * 100.0) as u8;
        let storage_pct = (rec.metrics.storage_free_gb as f64 / rec.metrics.storage_total_gb as f64 * 100.0) as u8;
        println!("{}   Resources: {}% mem free, {}% CPU load, {}% storage free",
            indent, mem_pct, rec.metrics.cpu_load_percent, storage_pct);
        println!("{}   Services: {} running", indent, rec.services_count);
        
        if rec.is_local {
            println!("{}   {} (tended stone)", indent, if term.supports_color { "🏠" } else { "[LOCAL]" });
        }
        
        println!();
    }
    
    println!("{}{}", indent, fmt.divider(&"─".repeat(60)));
    
    if placement.recommendations.len() == 1 {
        // Single option: ask for confirmation
        println!("{}Proceed with installation on '{}'? [Y/n]: ", indent, placement.recommendations[0].hostname);
        let mut input = String::new();
        std::io::stdin().read_line(&mut input)?;
        let input = input.trim().to_lowercase();
        
        if input.is_empty() || input == "y" || input == "yes" {
            let stone = &placement.recommendations[0];
            if let Some(stone_info) = stones.iter().find(|s| s.stone_name == stone.hostname) {
                return install_on_stone(client, &stone_info.stone_endpoint, offering, quiet).await;
            }
        } else {
            println!("{}Installation cancelled", indent);
        }
    } else {
        // Multiple options: let user select
        println!("{}Select stone (1-{}) or 'q' to quit: ", indent, top_n);
        let mut input = String::new();
        std::io::stdin().read_line(&mut input)?;
        let input = input.trim().to_lowercase();
        
        if input == "q" || input == "quit" || input == "exit" {
            println!("{}Installation cancelled", indent);
            return Ok(());
        }
        
        if let Ok(choice) = input.parse::<usize>() {
            if choice >= 1 && choice <= top_n {
                let stone = &placement.recommendations[choice - 1];
                if let Some(stone_info) = stones.iter().find(|s| s.stone_name == stone.hostname) {
                    return install_on_stone(client, &stone_info.stone_endpoint, offering, quiet).await;
                } else {
                    println!("{}{} Could not find endpoint for '{}'", indent, ui::status_indicator("error", term.supports_color), stone.hostname);
                }
            } else {
                println!("{}{} Invalid selection", indent, ui::status_indicator("error", term.supports_color));
            }
        } else {
            println!("{}{} Invalid input", indent, ui::status_indicator("error", term.supports_color));
        }
    }
    
    Ok(())
}

/// Install offering on a specific stone
async fn install_on_stone(
    client: &reqwest::Client,
    endpoint: &str,
    offering: &str,
    quiet: bool,
) -> Result<()> {
    // Delegate to existing install logic by creating a context
    let ctx = crate::context::CommandContext::with_endpoint(
        client.clone(),
        endpoint.to_string(),
        None,
        quiet,
        false,
        0, // verbose
    );
    
    let install_cmd = OfferCommand::install(
        offering.to_string(),
        vec![],
        false,
        quiet,
    );
    
    install_cmd.execute(&ctx).await
}

// ============================================================================
// Command Implementation
// ============================================================================

impl OfferCommand {
    pub fn list(quiet_mode: bool) -> Self {
        Self {
            action: OfferAction::List,
            prefer: vec![],
            anywhere_on_fail: false,
            quiet_mode,
        }
    }

    pub fn refresh(quiet_mode: bool) -> Self {
        Self {
            action: OfferAction::Refresh,
            prefer: vec![],
            anywhere_on_fail: false,
            quiet_mode,
        }
    }

    pub fn info(name: String, quiet_mode: bool) -> Self {
        Self {
            action: OfferAction::Info { name },
            prefer: vec![],
            anywhere_on_fail: false,
            quiet_mode,
        }
    }

    pub fn install(name: String, prefer: Vec<String>, anywhere_on_fail: bool, quiet_mode: bool) -> Self {
        Self {
            action: OfferAction::Install { name },
            prefer,
            anywhere_on_fail,
            quiet_mode,
        }
    }

    pub fn query(query: String, prefer: Vec<String>, quiet_mode: bool) -> Self {
        Self {
            action: OfferAction::Query { query },
            prefer,
            anywhere_on_fail: false,
            quiet_mode,
        }
    }

    pub fn query_anywhere(query: String, prefer: Vec<String>, quiet_mode: bool) -> Self {
        Self {
            action: OfferAction::QueryAnywhere { query },
            prefer,
            anywhere_on_fail: false,
            quiet_mode,
        }
    }

    pub fn placement_recommend(name: String, quiet: bool) -> Self {
        Self {
            action: OfferAction::PlacementRecommend { name, quiet },
            prefer: vec![],
            anywhere_on_fail: false,
            quiet_mode: quiet,
        }
    }

    /// Check if the given name is a known offering (for query detection)
    pub async fn is_known_offering(
        client: &reqwest::Client,
        endpoint: &str,
        name: &str,
    ) -> bool {
        if let Ok(offerings) = fetch_offerings(client, endpoint).await {
            offerings.iter().any(|o| o.name == name)
        } else {
            false
        }
    }
}

#[async_trait]
impl Command for OfferCommand {
    fn requires_endpoint(&self) -> bool {
        !matches!(self.action, OfferAction::QueryAnywhere { .. } | OfferAction::PlacementRecommend { .. })
    }

    fn show_stone_header(&self) -> bool {
        // Offer command manages its own display
        false
    }

    fn name(&self) -> &'static str {
        "offer"
    }

    async fn execute(&self, ctx: &CommandContext) -> Result<()> {
        let term = ui::TerminalInfo::detect();

        match &self.action {
            OfferAction::List => {
                let endpoint = ctx.endpoint.as_ref().expect("endpoint required for list");
                print_offerings_index(&ctx.client, endpoint).await?;
            }
            OfferAction::Refresh => {
                let endpoint = ctx.endpoint.as_ref().expect("endpoint required for refresh");
                refresh_offerings_index(&ctx.client, endpoint).await?;
            }
            OfferAction::Info { name } => {
                let endpoint = ctx.endpoint.as_ref().expect("endpoint required for info");
                print_offering_info(&ctx.client, endpoint, name).await?;
            }
            OfferAction::Query { query } => {
                let endpoint = ctx.endpoint.as_ref().expect("endpoint required for query");
                print_offer_query_recommendations(&ctx.client, endpoint, query, &self.prefer).await?;
            }
            OfferAction::QueryAnywhere { query } => {
                print_offer_anywhere_recommendations(&ctx.client, query, &self.prefer).await?;
            }
            OfferAction::PlacementRecommend { name, quiet } => {
                handle_placement_recommendation(&ctx.client, name, *quiet).await?;
            }
            OfferAction::Install { name } => {
                let endpoint = ctx.endpoint.as_ref().expect("endpoint required for install");
                // Check if service is already installed
                let services_url = format!("{}/api/v1/services", endpoint.trim_end_matches('/'));
                if let Ok(response) = ctx.client.get(&services_url).send().await {
                    if let Ok(json) = response.json::<serde_json::Value>().await {
                        let services: Vec<ServiceInfo> = serde_json::from_value(json.get("data").cloned().unwrap_or(json)).unwrap_or_default();
                        if let Some(existing) = services.iter().find(|s| s.offering == *name) {
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

                // POST /api/v1/services with JSON body
                let url = format!("{}/api/v1/services", endpoint.trim_end_matches('/'));
                let payload = serde_json::json!({
                    "offering": name,
                    "ports": [],
                    "environment": {}
                });

                let response = ctx.client.post(url).json(&payload).send().await?;
                let status = response.status();
                let body = response.json::<serde_json::Value>().await.ok();

                match status {
                    reqwest::StatusCode::ACCEPTED | reqwest::StatusCode::OK => {
                        if let Some(body) = body {
                            let fmt = CliFormatter::new();
                            let indent = " ".repeat(ui::constants::DEFAULT_INDENT);

                            let service_name = body.get("service").and_then(|v| v.as_str()).unwrap_or(name);
                            let action = body.get("action").and_then(|v| v.as_str()).unwrap_or("create");
                            let api_status = body.get("status").and_then(|v| v.as_str()).unwrap_or("pending");
                            let message = body.get("message").and_then(|v| v.as_str()).unwrap_or("");

                            // Display: lowercase name with status on same line
                            // mongodb      [pending create]
                            println!();
                            let status_text = format!("[{} {}]", api_status, action);
                            let padding = 16usize.saturating_sub(service_name.len());
                            println!("{}{}{}{}", indent, service_name, " ".repeat(padding), status_text);
                            println!("{}{}", indent, fmt.divider(&"─".repeat(47)));

                            // Extract job_id from message if present
                            let job_id = if message.contains("Job ID:") || message.contains("job:") {
                                message
                                    .split_whitespace()
                                    .skip_while(|s| !s.contains("ID") && !s.contains("job"))
                                    .nth(1)
                                    .map(|s| s.trim_end_matches(&['.', ',', '!'][..]).to_string())
                            } else {
                                None
                            };

                            if let Some(job_id) = job_id {
                                stream_job_progress(&ctx.client, endpoint, &job_id, service_name, self.quiet_mode).await?;
                            } else if message.contains("Adopted") {
                                println!("{}{} Service already exists (adopted)", indent, ui::status_indicator("ok", term.supports_color));
                                println!("{}{}", indent, message);
                            } else if message.contains("maintenance") {
                                println!("{}{} Under maintenance, retry later", indent, ui::status_indicator("pending", term.supports_color));
                            } else if !message.is_empty() {
                                println!("{}{}", indent, message);
                            }

                            // Display suggestions from v1 API (if not quiet)
                            if !self.quiet_mode {
                                if let Some(suggestions) = body.get("suggestions").and_then(|v| v.as_array()) {
                                    if !suggestions.is_empty() {
                                        println!();
                                        println!("{}{}", indent, fmt.divider(&"─".repeat(47)));
                                        println!("{}{}", indent, fmt.group("SUGGESTIONS"));
                                        for suggestion in suggestions {
                                            if let Some(s) = suggestion.as_str() {
                                                println!("{}    • {}", indent, s);
                                            }
                                        }
                                    }
                                }
                            }
                            println!();
                        }
                    }
                    reqwest::StatusCode::BAD_REQUEST => {
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
                                let derived_query = print_alternatives_for_failed_install(&ctx.client, endpoint, name, &self.prefer)
                                    .await
                                    .ok()
                                    .flatten();

                                if self.anywhere_on_fail {
                                    if let Some(q) = derived_query {
                                        println!("\n{}Searching across stones...", " ".repeat(ui::constants::DEFAULT_INDENT));
                                        let _ = print_offer_anywhere_recommendations(&ctx.client, &q, &self.prefer).await;
                                    }
                                }
                            }
                        } else {
                            println!("{}{} Failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), status);
                        }
                    }
                    reqwest::StatusCode::NOT_FOUND => {
                        println!("{}{} Unknown offering: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), name);
                        let _ = print_offer_query_recommendations(&ctx.client, endpoint, name, &self.prefer).await;
                    }
                    s if s.is_success() => {
                        println!("{}{} Offered {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("ok", term.supports_color), name);
                    }
                    reqwest::StatusCode::NOT_IMPLEMENTED => {
                        println!("{}ℹ️  Offer not implemented on server", " ".repeat(ui::constants::DEFAULT_INDENT));
                    }
                    _ => {
                        println!("{}{} Failed: {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), status);
                    }
                }
            }
        }

        Ok(())
    }
}
