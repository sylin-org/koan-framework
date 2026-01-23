//! List command - display services on a stone
//!
//! Shows all services installed on the target stone with their status.

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::suggestions;
use crate::ui::{self, TerminalInfo};
use anyhow::Context;
use async_trait::async_trait;
use serde::Deserialize;

/// Service discovery response (matches moss ServiceDiscoveryResponse)
#[derive(Debug, Deserialize)]
#[allow(dead_code)]
struct ServiceDiscoveryResponse {
    found: bool,
    services: Vec<FoundService>,
    source: String,
}

/// Found service (matches moss FoundService)
#[derive(Debug, Deserialize)]
#[allow(dead_code)]
struct FoundService {
    name: String,
    offering: String,
    category: String,
    status: String,
}

// Use shared ApiResponse from garden-common
use garden_common::api_utils::ApiResponse;

/// List services on a stone
pub struct ListCommand {
    pub quiet_mode: bool,
}

impl ListCommand {
    pub fn new(quiet_mode: bool) -> Self {
        Self { quiet_mode }
    }
}

#[async_trait]
impl Command for ListCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        let url = ctx.api_v1_url("services")?;
        let response = ctx.client.get(&url).send().await?;

        let api_response: ApiResponse<ServiceDiscoveryResponse> = response
            .json()
            .await
            .context("Failed to parse services response")?;

        let services = api_response.data.services;

        if services.is_empty() {
            println!(
                "{}",
                ui::empty_state("No services installed", Some("Use: garden-rake offer <service>"))
            );
        } else {
            println!("{}", ui::section_header("SERVICES", &ctx.term));
            println!();
            render_services_table(&services, &ctx.term);
        }

        // Self-teaching suggestions
        suggestions::print_suggestions(cmd::LIST, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::LIST
    }
}

/// Render services in a formatted table
fn render_services_table(services: &[FoundService], term: &TerminalInfo) {
    let mut table = ui::TableBuilder::new()
        .add_column(ui::constants::MAX_SERVICE_NAME_LEN, ui::Align::Left)
        .add_column(20, ui::Align::Left)
        .add_column(16, ui::Align::Left);

    let mut running_count = 0;
    let mut stopped_count = 0;

    for svc in services {
        let status_lower = svc.status.to_lowercase();
        if status_lower.contains(garden_common::SERVICE_RUNNING) {
            running_count += 1;
        } else {
            stopped_count += 1;
        }

        let status_display = ui::status_indicator(&status_lower, term.supports_color);
        table.add_row(vec![
            ui::truncate_name(&svc.name, ui::constants::MAX_SERVICE_NAME_LEN),
            status_display,
            if svc.offering.is_empty() {
                garden_common::VALUE_UNKNOWN.to_string()
            } else {
                svc.offering.clone()
            },
        ]);
    }

    println!("{}", table.render());
    println!();
    println!(
        "{}  {} services ({} running, {} stopped)",
        " ".repeat(ui::constants::DEFAULT_INDENT),
        services.len(),
        running_count,
        stopped_count
    );
}
