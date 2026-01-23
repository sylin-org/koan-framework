//! Find command - service discovery with connection strings
//!
//! Finds running services across the garden and returns connection URIs.
//! Supports search by name, category, or tags with cache-first architecture.
//!
//! Wishfully mode: Auto-provision if service not found and query matches a known offering.

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::suggestions;
use crate::ui;
use anyhow::Context;
use async_trait::async_trait;
use serde::{Deserialize, Serialize};
use std::time::Duration;

/// Output format for find command
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum FindOutputFormat {
    /// Human-readable output (default)
    Human,
    /// JSON output
    Json,
    /// URI only (hostname-based)
    Uri,
    /// URI only (IP-based fallback)
    UriIp,
}

impl Default for FindOutputFormat {
    fn default() -> Self {
        Self::Human
    }
}

impl FindOutputFormat {
    pub fn from_str(s: &str) -> Self {
        match s.to_lowercase().as_str() {
            "json" => Self::Json,
            "uri" | "connection-string" => Self::Uri,
            "uri-ip" | "ip" => Self::UriIp,
            _ => Self::Human,
        }
    }
}

/// Find services command
pub struct FindCommand {
    /// Search query (name, c:category, t:tag)
    pub query: String,
    /// Output format
    pub format: FindOutputFormat,
    /// Quiet mode (suppress hints)
    pub quiet_mode: bool,
    /// Fresh discovery (bypass cache)
    pub fresh: bool,
    /// Wishfully mode (auto-provision if not found)
    pub wishfully: bool,
}

impl FindCommand {
    pub fn new(
        query: String,
        format: FindOutputFormat,
        quiet_mode: bool,
        fresh: bool,
        wishfully: bool,
    ) -> Self {
        Self {
            query,
            format,
            quiet_mode,
            fresh,
            wishfully,
        }
    }
}

/// Stone reference in response
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StoneRef {
    pub id: String,
    pub name: String,
    pub endpoint: String,
}

/// Connection information
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ConnectionInfo {
    pub hostname: String,
    pub ip: String,
    pub port: u16,
    pub protocol: String,
    pub uris: Vec<String>,
}

/// Found service
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FoundService {
    pub name: String,
    pub offering: String,
    pub category: String,
    pub tags: Vec<String>,
    pub status: String,
    pub stone: StoneRef,
    pub connection: ConnectionInfo,
}

/// Service discovery response
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServiceDiscoveryResponse {
    pub found: bool,
    pub services: Vec<FoundService>,
    pub source: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub cache_age_seconds: Option<u64>,
    pub timestamp: String,
}

// Use shared ApiResponse from garden-common
use garden_common::api_utils::ApiResponse;

#[async_trait]
impl Command for FindCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        use garden_common::api_utils::{sanitize_query, is_suspicious};

        // Reject suspicious patterns client-side
        if is_suspicious(&self.query) {
            anyhow::bail!("Query contains invalid patterns");
        }

        // Sanitize query input
        let sanitized_query = sanitize_query(&self.query).into_value();

        // Build API URL with query parameters
        // Uses unified /api/v1/services endpoint with ?q= param
        let mut url = ctx.api_v1_url("services")?;
        url = format!("{}?q={}", url, urlencoding::encode(&sanitized_query));
        if self.fresh {
            url = format!("{}&fresh=true", url);
        }

        tracing::debug!(
            query = %sanitized_query,
            url = %url,
            endpoint = ?ctx.endpoint,
            "FindCommand: sending request to services?q="
        );

        // Make API request
        let response = ctx
            .client
            .get(&url)
            .send()
            .await
            .context("Failed to connect to moss")?;

        if !response.status().is_success() {
            let status = response.status();
            let body = response.text().await.unwrap_or_default();
            tracing::error!(
                status = %status,
                url = %url,
                body = %body,
                "FindCommand: API request failed"
            );
            anyhow::bail!("API error ({}): {}", status, body);
        }

        tracing::debug!(status = %response.status(), "FindCommand: API request succeeded");

        let api_response: ApiResponse<ServiceDiscoveryResponse> = response
            .json()
            .await
            .context("Failed to parse response")?;

        let discovery = api_response.data;

        // Handle not found case
        if !discovery.found {
            return self.handle_not_found(ctx).await;
        }

        // Render output based on format
        match self.format {
            FindOutputFormat::Human => {
                self.render_human(&discovery, ctx);
            }
            FindOutputFormat::Json => {
                self.render_json(&discovery)?;
            }
            FindOutputFormat::Uri => {
                self.render_uri(&discovery, false);
            }
            FindOutputFormat::UriIp => {
                self.render_uri(&discovery, true);
            }
        }

        // Self-teaching suggestions (unless quiet or non-human format)
        if self.format == FindOutputFormat::Human {
            suggestions::print_suggestions(cmd::FIND, self.quiet_mode);
        }

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::FIND
    }
}

/// Offering info for wishfully mode
#[derive(Debug, Clone, Serialize, Deserialize)]
struct OfferingInfo {
    pub name: String,
    pub category: String,
    #[serde(default)]
    pub description: String,
}

impl FindCommand {
    /// Check if query is a name search (not category or tag prefix)
    fn is_name_search(&self) -> bool {
        let q = self.query.trim().to_lowercase();
        !q.starts_with("c:")
            && !q.starts_with("cat:")
            && !q.starts_with("category:")
            && !q.starts_with("t:")
            && !q.starts_with("tag:")
            && !q.starts_with("tags:")
    }

    /// Check if the query matches a known offering
    async fn check_offering_exists(&self, ctx: &CommandContext) -> Option<OfferingInfo> {
        let endpoint = ctx.endpoint.as_ref()?;
        let url = format!(
            "{}/api/v1/offerings/{}",
            endpoint.trim_end_matches('/'),
            urlencoding::encode(&self.query)
        );

        let response = ctx.client.get(&url).send().await.ok()?;
        if !response.status().is_success() {
            return None;
        }

        let body: serde_json::Value = response.json().await.ok()?;
        let data = body.get("data")?;

        Some(OfferingInfo {
            name: data.get("name")?.as_str()?.to_string(),
            category: data
                .get("category")
                .and_then(|v| v.as_str())
                .unwrap_or("unknown")
                .to_string(),
            description: data
                .get("description")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string(),
        })
    }

    /// Install an offering and wait for completion
    async fn install_offering(&self, ctx: &CommandContext, offering: &str) -> anyhow::Result<()> {
        let endpoint = ctx
            .endpoint
            .as_ref()
            .ok_or_else(|| anyhow::anyhow!("No endpoint available"))?;

        let url = format!("{}/api/v1/services", endpoint.trim_end_matches('/'));
        let payload = serde_json::json!({
            "offering": offering,
            "ports": [],
            "environment": {}
        });

        println!(
            "{}{} Provisioning '{}' service...",
            " ".repeat(ui::constants::DEFAULT_INDENT),
            ui::status_indicator("pending", ctx.term.supports_color),
            offering
        );

        let response = ctx.client.post(&url).json(&payload).send().await?;
        let status = response.status();

        if !status.is_success() && status != reqwest::StatusCode::ACCEPTED {
            let body: serde_json::Value = response.json().await.unwrap_or_default();
            let error_msg = body
                .get("error")
                .and_then(|e| e.get("message"))
                .and_then(|v| v.as_str())
                .unwrap_or("Installation failed");
            anyhow::bail!("{}", error_msg);
        }

        let body: serde_json::Value = response.json().await?;

        // Extract job_id from response
        let job_id = body
            .get("message")
            .and_then(|v| v.as_str())
            .and_then(|msg| {
                // Parse "Job ID: <uuid>" from message
                if msg.contains("Job ID:") {
                    msg.split("Job ID:")
                        .nth(1)
                        .map(|s| s.trim().split_whitespace().next().unwrap_or(""))
                        .filter(|s| !s.is_empty())
                        .map(|s| s.to_string())
                } else {
                    None
                }
            });

        // If no job_id, check if it was immediately adopted
        if job_id.is_none() {
            let message = body
                .get("message")
                .and_then(|v| v.as_str())
                .unwrap_or("");
            if message.contains("Adopted") {
                println!(
                    "{}{} Service already exists (adopted)",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("ok", ctx.term.supports_color)
                );
                return Ok(());
            }
        }

        // Wait for job completion
        if let Some(job_id) = job_id {
            self.wait_for_job(ctx, endpoint, &job_id).await?;
        } else {
            // No job, assume immediate completion
            println!(
                "{}{} Service provisioned",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("ok", ctx.term.supports_color)
            );
        }

        Ok(())
    }

    /// Wait for a job to complete
    async fn wait_for_job(
        &self,
        ctx: &CommandContext,
        endpoint: &str,
        job_id: &str,
    ) -> anyhow::Result<()> {
        let url = format!("{}/api/v1/jobs/{}", endpoint.trim_end_matches('/'), job_id);
        let max_wait = Duration::from_secs(120);
        let poll_interval = Duration::from_millis(500);
        let start = std::time::Instant::now();

        loop {
            if start.elapsed() > max_wait {
                anyhow::bail!("Timeout waiting for service to start");
            }

            tokio::time::sleep(poll_interval).await;

            let response = ctx.client.get(&url).send().await;
            if let Ok(resp) = response {
                if resp.status().is_success() {
                    if let Ok(body) = resp.json::<serde_json::Value>().await {
                        let data = body.get("data").unwrap_or(&body);
                        let status = data
                            .get("status")
                            .and_then(|v| v.as_str())
                            .unwrap_or("unknown");

                        match status {
                            "completed" | "success" => {
                                println!(
                                    "{}{} Service ready",
                                    " ".repeat(ui::constants::DEFAULT_INDENT),
                                    ui::status_indicator("ok", ctx.term.supports_color)
                                );
                                return Ok(());
                            }
                            "failed" | "error" => {
                                let message = data
                                    .get("message")
                                    .and_then(|v| v.as_str())
                                    .unwrap_or("Installation failed");
                                anyhow::bail!("{}", message);
                            }
                            _ => {
                                // Still in progress
                                continue;
                            }
                        }
                    }
                }
            }
        }
    }

    /// Re-run find query after provisioning
    async fn retry_find(&self, ctx: &CommandContext) -> anyhow::Result<ServiceDiscoveryResponse> {
        use garden_common::api_utils::sanitize_query;

        // Wait a moment for service to fully register
        tokio::time::sleep(Duration::from_millis(500)).await;

        let sanitized_query = sanitize_query(&self.query).into_value();
        let mut url = ctx.api_v1_url("services")?;
        url = format!("{}?q={}&fresh=true", url, urlencoding::encode(&sanitized_query));

        let response = ctx
            .client
            .get(&url)
            .send()
            .await
            .context("Failed to connect to moss")?;

        if !response.status().is_success() {
            let status = response.status();
            let body = response.text().await.unwrap_or_default();
            anyhow::bail!("API error ({}): {}", status, body);
        }

        let api_response: ApiResponse<ServiceDiscoveryResponse> = response
            .json()
            .await
            .context("Failed to parse response")?;

        Ok(api_response.data)
    }

    /// Handle not found case
    async fn handle_not_found(&self, ctx: &CommandContext) -> CommandResult {
        if self.wishfully && self.is_name_search() {
            // Check if query matches a known offering
            if let Some(offering) = self.check_offering_exists(ctx).await {
                println!(
                    "{}No running '{}' service found",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    self.query
                );
                println!(
                    "{}Found matching offering: {} ({})",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    offering.name,
                    offering.category
                );
                println!();

                // Install the offering
                match self.install_offering(ctx, &offering.name).await {
                    Ok(()) => {
                        // Retry find after installation
                        println!();
                        match self.retry_find(ctx).await {
                            Ok(discovery) if discovery.found => {
                                // Success! Render the result
                                match self.format {
                                    FindOutputFormat::Human => {
                                        self.render_human(&discovery, ctx);
                                    }
                                    FindOutputFormat::Json => {
                                        self.render_json(&discovery)?;
                                    }
                                    FindOutputFormat::Uri => {
                                        self.render_uri(&discovery, false);
                                    }
                                    FindOutputFormat::UriIp => {
                                        self.render_uri(&discovery, true);
                                    }
                                }

                                if self.format == FindOutputFormat::Human {
                                    suggestions::print_suggestions(cmd::FIND, self.quiet_mode);
                                }

                                return Ok(());
                            }
                            Ok(_) => {
                                // Service installed but not found yet
                                println!(
                                    "{}{} Service installed but not yet ready",
                                    " ".repeat(ui::constants::DEFAULT_INDENT),
                                    ui::status_indicator("warn", ctx.term.supports_color)
                                );
                                println!(
                                    "{}Try again in a few seconds: garden-rake find {}",
                                    " ".repeat(ui::constants::DEFAULT_INDENT),
                                    self.query
                                );
                            }
                            Err(e) => {
                                println!(
                                    "{}{} Failed to verify service: {}",
                                    " ".repeat(ui::constants::DEFAULT_INDENT),
                                    ui::status_indicator("warn", ctx.term.supports_color),
                                    e
                                );
                            }
                        }
                    }
                    Err(e) => {
                        println!(
                            "{}{} Provisioning failed: {}",
                            " ".repeat(ui::constants::DEFAULT_INDENT),
                            ui::status_indicator("error", ctx.term.supports_color),
                            e
                        );
                        std::process::exit(3); // Exit code 3 for provisioning failed
                    }
                }

                std::process::exit(1);
            } else {
                // No matching offering found
                println!(
                    "{}No running '{}' service found",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    self.query
                );
                println!(
                    "{}{} No matching offering available to provision",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("warn", ctx.term.supports_color)
                );
                println!();
                println!(
                    "{}Suggestions:",
                    " ".repeat(ui::constants::DEFAULT_INDENT)
                );
                println!(
                    "{}  garden-rake offer              # View available offerings",
                    " ".repeat(ui::constants::DEFAULT_INDENT)
                );
                println!(
                    "{}  garden-rake find c:database    # Find any database",
                    " ".repeat(ui::constants::DEFAULT_INDENT)
                );
            }
        } else if self.wishfully {
            // Wishfully mode with category/tag search
            println!(
                "{}No running services found matching '{}'",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                self.query
            );
            println!(
                "{}{} Wishfully mode requires a specific offering name",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("info", ctx.term.supports_color)
            );
            println!();
            println!(
                "{}Try: garden-rake find mongodb wishfully",
                " ".repeat(ui::constants::DEFAULT_INDENT)
            );
        } else {
            println!(
                "{}No running '{}' service found",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                self.query
            );
            println!();
            println!(
                "{}Suggestions:",
                " ".repeat(ui::constants::DEFAULT_INDENT)
            );
            println!(
                "{}  garden-rake find {} wishfully  # Auto-provision {}",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                self.query,
                self.query
            );
            println!(
                "{}  garden-rake offer              # View available offerings",
                " ".repeat(ui::constants::DEFAULT_INDENT)
            );
            println!(
                "{}  garden-rake find c:database    # Find any database",
                " ".repeat(ui::constants::DEFAULT_INDENT)
            );
        }

        // Return exit code 1 for not found
        std::process::exit(1);
    }

    /// Render human-readable output
    fn render_human(&self, discovery: &ServiceDiscoveryResponse, _ctx: &CommandContext) {
        let services = &discovery.services;

        for svc in services {
            println!();
            println!(
                "{}  {} ({}) on {}",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                svc.name,
                svc.category,
                svc.stone.name
            );

            // Primary URI (hostname-based)
            if let Some(uri) = svc.connection.uris.first() {
                println!(
                    "{}  {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    uri
                );
            }
        }

        // Summary for multiple results
        if services.len() > 1 {
            let stone_count = services
                .iter()
                .map(|s| &s.stone.id)
                .collect::<std::collections::HashSet<_>>()
                .len();

            println!();
            println!(
                "{}Found {} services across {} stone{}",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                services.len(),
                stone_count,
                if stone_count != 1 { "s" } else { "" }
            );
        }

        // Hint for JSON output
        if !self.quiet_mode && self.format == FindOutputFormat::Human {
            println!();
            println!(
                "{}Hint: Use `garden-rake find {} --format json` for machine-readable output",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                self.query
            );
        }
    }

    /// Render JSON output
    fn render_json(&self, discovery: &ServiceDiscoveryResponse) -> CommandResult {
        let json = serde_json::to_string_pretty(discovery)
            .context("Failed to serialize response")?;
        println!("{}", json);
        Ok(())
    }

    /// Render URI-only output
    fn render_uri(&self, discovery: &ServiceDiscoveryResponse, use_ip: bool) {
        for svc in &discovery.services {
            let uri = if use_ip {
                // IP-based URI (second in list, fallback)
                svc.connection.uris.get(1).or(svc.connection.uris.first())
            } else {
                // Hostname-based URI (first in list)
                svc.connection.uris.first()
            };

            if let Some(u) = uri {
                println!("{}", u);
            }
        }
    }
}
