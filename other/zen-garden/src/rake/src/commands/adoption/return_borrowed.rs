//! Return command - unregister a borrowed service
//!
//! Returns (unregisters) a borrowed external service.

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::suggestions;
use crate::ui;
use async_trait::async_trait;

/// Return (unregister) a borrowed service
pub struct ReturnCommand {
    pub name: String,
    pub quiet_mode: bool,
}

impl ReturnCommand {
    pub fn new(name: String, quiet_mode: bool) -> Self {
        Self { name, quiet_mode }
    }
}

#[async_trait]
impl Command for ReturnCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        let url = ctx.api_v1_url(&format!("adoption/borrow/{}", self.name))?;
        let response = ctx.client.delete(&url).send().await?;
        let status = response.status();

        match status {
            s if s.is_success() => {
                println!(
                    "{}{} Returned borrowed service '{}'",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("ok", ctx.term.supports_color),
                    self.name
                );
            }
            reqwest::StatusCode::NOT_FOUND => {
                eprintln!(
                    "{}{} Service '{}' is not currently borrowed",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    self.name
                );
            }
            _ => {
                eprintln!(
                    "{}{} Failed to return: {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    status
                );
            }
        }

        // Self-teaching suggestions
        suggestions::print_suggestions(cmd::RETURN, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::RETURN
    }
}
