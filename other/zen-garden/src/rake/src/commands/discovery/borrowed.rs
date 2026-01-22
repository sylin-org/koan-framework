//! Borrowed command - list borrowed services
//!
//! Shows external services that have been borrowed (registered but not managed).

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::suggestions;
use crate::ui;
use async_trait::async_trait;

/// List borrowed services
pub struct BorrowedCommand {
    pub quiet_mode: bool,
}

impl BorrowedCommand {
    pub fn new(quiet_mode: bool) -> Self {
        Self { quiet_mode }
    }
}

#[async_trait]
impl Command for BorrowedCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        let url = ctx.api_v1_url("offerings/borrowed")?;
        let response = ctx.client.get(&url).send().await?;

        if response.status().is_success() {
            let body: serde_json::Value = response.json().await?;
            let borrowed = body.get("data").and_then(|d| d.as_array());

            if let Some(list) = borrowed {
                if list.is_empty() {
                    println!(
                        "{}No borrowed services",
                        " ".repeat(ui::constants::DEFAULT_INDENT)
                    );
                } else {
                    println!(
                        "{}Borrowed services:",
                        " ".repeat(ui::constants::DEFAULT_INDENT)
                    );
                    for svc in list {
                        let name = svc.get("name").and_then(|v| v.as_str()).unwrap_or("unknown");
                        let url_str = svc
                            .get("connection_template")
                            .and_then(|v| v.as_str())
                            .unwrap_or("");
                        if !url_str.is_empty() {
                            println!(
                                "{}  {} ({})",
                                " ".repeat(ui::constants::DEFAULT_INDENT),
                                name,
                                url_str
                            );
                        } else {
                            println!(
                                "{}  {}",
                                " ".repeat(ui::constants::DEFAULT_INDENT),
                                name
                            );
                        }
                    }
                }
            }
        } else {
            eprintln!(
                "{}{} Failed to list borrowed services: {}",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("error", ctx.term.supports_color),
                response.status()
            );
        }

        // Self-teaching suggestions
        suggestions::print_suggestions(cmd::BORROWED, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::BORROWED
    }
}
