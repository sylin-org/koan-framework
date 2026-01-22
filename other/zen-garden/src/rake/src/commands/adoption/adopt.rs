//! Adopt command - adopt an existing container
//!
//! Adopts an existing container into Zen Garden management.

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::suggestions;
use crate::ui;
use async_trait::async_trait;

/// Adopt an existing container
pub struct AdoptCommand {
    pub container: String,
    pub quiet_mode: bool,
}

impl AdoptCommand {
    pub fn new(container: String, quiet_mode: bool) -> Self {
        Self {
            container,
            quiet_mode,
        }
    }
}

#[async_trait]
impl Command for AdoptCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        let url = ctx.api_v1_url(&format!("offerings/{}/adopt", self.container))?;
        let response = ctx
            .client
            .post(&url)
            .json(&serde_json::json!({}))
            .send()
            .await?;
        let status = response.status();

        match status {
            s if s.is_success() => {
                if let Ok(body) = response.json::<serde_json::Value>().await {
                    let name = body
                        .get("data")
                        .and_then(|d| d.get("name"))
                        .and_then(|v| v.as_str())
                        .unwrap_or(&self.container);
                    println!(
                        "{}{} Adopted container '{}' as '{}'",
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("ok", ctx.term.supports_color),
                        self.container,
                        name
                    );
                }
            }
            reqwest::StatusCode::CONFLICT => {
                eprintln!(
                    "{}{} Container '{}' is already adopted",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("warn", ctx.term.supports_color),
                    self.container
                );
            }
            reqwest::StatusCode::NOT_FOUND => {
                eprintln!(
                    "{}{} Container '{}' not found or not adoptable",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    self.container
                );
            }
            _ => {
                eprintln!(
                    "{}{} Failed to adopt: {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    status
                );
            }
        }

        // Self-teaching suggestions
        suggestions::print_suggestions(cmd::ADOPT, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::ADOPT
    }
}
