//! List command - display services on a stone
//!
//! Shows all services installed on the target stone with their status.

use crate::api::extract_services;
use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::suggestions;
use crate::ui::{self, TerminalInfo};
use async_trait::async_trait;
use garden_common::ServiceInfo;

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
        let response: serde_json::Value = ctx.client.get(&url).send().await?.json::<serde_json::Value>().await?;
        let services = extract_services(&response);

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
fn render_services_table(services: &[ServiceInfo], term: &TerminalInfo) {
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
