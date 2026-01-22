//! Watch command - real-time event and log streaming
//!
//! Provides live streaming of events and logs:
//! - watch: Stream stone events
//! - watch offering <name> logs: Stream offering logs
//! - watch stone <name> logs: Stream stone-wide logs (planned)

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::discovery;
use crate::suggestions;
use crate::ui;
use async_trait::async_trait;
use futures_util::StreamExt;
use garden_common::{GardenApiResponse, HardwareCapabilities};
use std::time::Duration;

/// Watch target type
pub enum WatchTargetType {
    /// Watch events from stone
    Events { until: Option<String> },
    /// Watch logs from offering
    OfferingLogs { name: String, timestamps: bool },
    /// Watch logs from stone (planned)
    StoneLogs { name: String, timestamps: bool },
}

/// Watch command for event/log streaming
pub struct WatchCommand {
    pub target: WatchTargetType,
    pub quiet_mode: bool,
}

impl WatchCommand {
    pub fn new(target: WatchTargetType, quiet_mode: bool) -> Self {
        Self { target, quiet_mode }
    }

    /// Create for event watching
    pub fn events(until: Option<String>, quiet_mode: bool) -> Self {
        Self::new(WatchTargetType::Events { until }, quiet_mode)
    }

    /// Create for offering log watching
    pub fn offering_logs(name: String, timestamps: bool, quiet_mode: bool) -> Self {
        Self::new(WatchTargetType::OfferingLogs { name, timestamps }, quiet_mode)
    }

    /// Create for stone log watching
    pub fn stone_logs(name: String, timestamps: bool, quiet_mode: bool) -> Self {
        Self::new(WatchTargetType::StoneLogs { name, timestamps }, quiet_mode)
    }
}

#[async_trait]
impl Command for WatchCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        match &self.target {
            WatchTargetType::Events { until } => {
                let endpoint = ctx.endpoint()?;
                watch_events(&ctx.client, endpoint, until.clone()).await?;
            }
            WatchTargetType::OfferingLogs { name, timestamps } => {
                let endpoint = ctx.endpoint()?;
                watch_offering_logs(&ctx.client, endpoint, name, *timestamps).await?;
            }
            WatchTargetType::StoneLogs { name, timestamps } => {
                // Stone logs need special resolution by stone name
                let endpoint = resolve_stone_endpoint(&ctx.client, name, &ctx.term).await?;
                watch_stone_logs(&endpoint, name, *timestamps).await?;
            }
        }

        // Self-teaching suggestions
        suggestions::print_suggestions(cmd::WATCH, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::WATCH
    }
}

/// Resolve endpoint for a specific stone by name
async fn resolve_stone_endpoint(
    client: &reqwest::Client,
    stone_name: &str,
    term: &ui::TerminalInfo,
) -> anyhow::Result<String> {
    // Try to discover stones
    let mut endpoints = Vec::new();
    let _ = discovery::discover_all_moss_stream(
        Duration::from_secs(2),
        |response, _instant| {
            endpoints.push(response);
        },
    );

    if endpoints.is_empty() {
        eprintln!(
            "{}{} No stones discovered",
            " ".repeat(ui::constants::DEFAULT_INDENT),
            ui::status_indicator("error", term.supports_color)
        );
        anyhow::bail!("No stones discovered");
    }

    for response in endpoints {
        let ep = &response.stone_endpoint;
        let caps_url = format!("{}/capabilities", ep.trim_end_matches('/'));
        if let Ok(resp) = client.get(&caps_url).send().await {
            if let Ok(caps_response) = resp.json::<GardenApiResponse<HardwareCapabilities>>().await {
                if caps_response.data.stone_name.to_lowercase() == stone_name.to_lowercase() {
                    return Ok(ep.clone());
                }
            }
        }
    }

    eprintln!(
        "{}{} Stone '{}' not found",
        " ".repeat(ui::constants::DEFAULT_INDENT),
        ui::status_indicator("error", term.supports_color),
        stone_name
    );
    anyhow::bail!("Stone '{}' not found", stone_name)
}

/// Stream logs from an offering
async fn watch_offering_logs(
    client: &reqwest::Client,
    endpoint: &str,
    offering: &str,
    timestamps: bool,
) -> anyhow::Result<()> {
    let url = format!(
        "{}/api/v1/services/{}/logs{}",
        endpoint.trim_end_matches('/'),
        offering,
        if timestamps { "?timestamps=true" } else { "" }
    );

    println!("Streaming logs from offering: {}\n", offering);
    let response = client
        .get(&url)
        .header("Accept", "text/event-stream")
        .send()
        .await?;

    if response.status() == reqwest::StatusCode::NOT_FOUND {
        println!("x Offering '{}' not found", offering);
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
                println!("\n! {}", data);
            }
        }
    }

    Ok(())
}

/// Stream stone-wide logs (placeholder)
async fn watch_stone_logs(
    _endpoint: &str,
    _stone_name: &str,
    _timestamps: bool,
) -> anyhow::Result<()> {
    println!("x Stone-wide log streaming not yet implemented");
    println!("   Use: garden-rake watch offering <name> logs");
    Ok(())
}

/// Stream events from stone
async fn watch_events(
    client: &reqwest::Client,
    endpoint: &str,
    until_pattern: Option<String>,
) -> anyhow::Result<()> {
    let url = format!("{}/api/v1/events", endpoint.trim_end_matches('/'));
    println!("Watching events from {}\n", endpoint);

    if let Some(ref pattern) = until_pattern {
        println!("Will exit when '{}' appears\n", pattern);
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

                                // Symbol based on level
                                let symbol = match level {
                                    "error" => "x",
                                    "warn" => "!",
                                    "info" => "i",
                                    "debug" => "*",
                                    _ => "o",
                                };

                                let time_part = if timestamp.len() >= 19 {
                                    format!("[{}] ", &timestamp[11..19]) // HH:MM:SS from ISO timestamp
                                } else {
                                    format!("[{}] ", ui::format_wall_clock())
                                };

                                println!("{}{} {}", time_part, symbol, message);

                                // Check until pattern
                                if let Some(ref pattern) = until_pattern {
                                    if message.contains(pattern) {
                                        println!("\nv Pattern '{}' found, exiting\n", pattern);
                                        return Ok(());
                                    }
                                }
                            } else {
                                // Raw event data - add wall-clock timestamp
                                println!("[{}] o {}", ui::format_wall_clock(), data);

                                if let Some(ref pattern) = until_pattern {
                                    if data.contains(pattern) {
                                        println!("\nv Pattern '{}' found, exiting\n", pattern);
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
                println!("\nx Connection lost\n");
                break;
            }
        }
    }

    Ok(())
}
