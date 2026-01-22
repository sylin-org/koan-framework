//! Remove command - soft delete a service
//!
//! Removes a service from management (container preserved as stray).

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::suggestions;
use crate::ui;
use async_trait::async_trait;
use std::io::Write;

/// Remove a service (soft delete)
pub struct RemoveCommand {
    pub service: String,
    pub force: bool,
    pub quiet_mode: bool,
}

impl RemoveCommand {
    pub fn new(service: String, force: bool, quiet_mode: bool) -> Self {
        Self {
            service,
            force,
            quiet_mode,
        }
    }
}

#[async_trait]
impl Command for RemoveCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        // Confirmation prompt (unless --force or quiet mode)
        if !self.force && !self.quiet_mode {
            println!(
                "{}⚠️  This will permanently remove service '{}'",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                self.service
            );
            println!(
                "{}Container and any associated volumes will be deleted.",
                " ".repeat(ui::constants::DEFAULT_INDENT)
            );
            print!(
                "{}Continue? [y/N]: ",
                " ".repeat(ui::constants::DEFAULT_INDENT)
            );
            std::io::stdout().flush()?;

            let mut input = String::new();
            std::io::stdin().read_line(&mut input)?;

            if !input.trim().eq_ignore_ascii_case("y") {
                println!("{}Cancelled", " ".repeat(ui::constants::DEFAULT_INDENT));
                return Ok(());
            }
            println!();
        }

        let url = ctx.api_v1_url(&format!("services/{}", self.service))?;
        let response = ctx.client.delete(&url).send().await?;
        let status = response.status();

        match status {
            s if s.is_success() => {
                if let Ok(body) = response.json::<serde_json::Value>().await {
                    let message = body.get("message").and_then(|v| v.as_str()).unwrap_or("");

                    println!(
                        "{}{} Removed {}",
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("ok", ctx.term.supports_color),
                        self.service
                    );
                    if !message.is_empty() {
                        println!(
                            "{}   {}",
                            " ".repeat(ui::constants::DEFAULT_INDENT),
                            message
                        );
                    }

                    // Display suggestions if present and not in quiet mode
                    if !self.quiet_mode {
                        if let Some(suggestions) = body.get("suggestions").and_then(|v| v.as_array())
                        {
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
                    println!(
                        "{}{} Removed {}",
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("ok", ctx.term.supports_color),
                        self.service
                    );
                }
            }
            reqwest::StatusCode::NOT_FOUND => {
                eprintln!(
                    "{}{} Service '{}' not found",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    self.service
                );
            }
            _ => {
                eprintln!(
                    "{}{} Failed: {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    status
                );
            }
        }

        // Self-teaching suggestions
        suggestions::print_suggestions(cmd::REMOVE, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::REMOVE
    }
}
