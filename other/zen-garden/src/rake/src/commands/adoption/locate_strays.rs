//! Locate strays command - list adoptable containers
//!
//! Lists containers that are not managed by Zen Garden (strays).

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::suggestions;
use crate::ui;
use async_trait::async_trait;

/// Locate stray (adoptable) containers
pub struct LocateStraysCommand {
    pub quiet_mode: bool,
}

impl LocateStraysCommand {
    pub fn new(quiet_mode: bool) -> Self {
        Self { quiet_mode }
    }
}

#[async_trait]
impl Command for LocateStraysCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        let url = ctx.api_v1_url("offerings/adoptable")?;
        let response = ctx.client.get(&url).send().await?;

        if response.status().is_success() {
            let body: serde_json::Value = response.json().await?;
            let strays = body.get("data").and_then(|d| d.as_array());

            if let Some(list) = strays {
                if list.is_empty() {
                    println!(
                        "{}No stray containers found",
                        " ".repeat(ui::constants::DEFAULT_INDENT)
                    );
                } else {
                    println!(
                        "{}Adoptable containers (strays):",
                        " ".repeat(ui::constants::DEFAULT_INDENT)
                    );
                    for stray in list {
                        let name = stray.get("name").and_then(|v| v.as_str()).unwrap_or("unknown");
                        let category = stray
                            .get("category")
                            .and_then(|v| v.as_str())
                            .unwrap_or("Unknown");
                        let version = stray.get("version").and_then(|v| v.as_str()).unwrap_or("");
                        if version.is_empty() {
                            println!(
                                "{}  {} ({})",
                                " ".repeat(ui::constants::DEFAULT_INDENT),
                                name,
                                category
                            );
                        } else {
                            println!(
                                "{}  {} ({}) - v{}",
                                " ".repeat(ui::constants::DEFAULT_INDENT),
                                name,
                                category,
                                version
                            );
                        }
                    }
                    println!(
                        "\n{}Use 'garden-rake adopt <name>' to adopt a container",
                        " ".repeat(ui::constants::DEFAULT_INDENT)
                    );
                }
            }
        } else {
            eprintln!(
                "{}{} Failed to list strays: {}",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("error", ctx.term.supports_color),
                response.status()
            );
        }

        // Self-teaching suggestions
        suggestions::print_suggestions(cmd::LOCATE, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::LOCATE
    }
}
