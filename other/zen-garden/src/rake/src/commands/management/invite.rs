//! Invite command - pond zen syntax for invitation generation
//!
//! Zen syntax for generating pond invitations:
//! - invite: Generate a time-limited TOTP invitation code (equivalent to pond invite)

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::suggestions;
use crate::ui;
use async_trait::async_trait;

/// Invite command for pond zen syntax operations
pub struct InviteCommand {
    pub quiet_mode: bool,
}

impl InviteCommand {
    pub fn new(quiet_mode: bool) -> Self {
        Self { quiet_mode }
    }
}

#[async_trait]
impl Command for InviteCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        let endpoint = ctx.endpoint()?;
        let url = format!("{}/api/v1/pond/invite", endpoint.trim_end_matches('/'));

        match ctx.client.post(&url).send().await {
            Ok(response) if response.status() == reqwest::StatusCode::NOT_IMPLEMENTED => {
                println!(
                    "{}{} Pond security not yet implemented (Phase 3b)",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("info", ctx.term.supports_color)
                );
                println!(
                    "{}This command will generate a time-limited TOTP invitation code.",
                    " ".repeat(ui::constants::DEFAULT_INDENT)
                );
            }
            Ok(response) if response.status().is_success() => {
                if let Ok(body) = response.json::<serde_json::Value>().await {
                    if let Some(data) = body.get("data") {
                        if let Some(code) = data.get("code").and_then(|c| c.as_str()) {
                            println!(
                                "{}{} Invitation code: {}",
                                " ".repeat(ui::constants::DEFAULT_INDENT),
                                ui::status_indicator("ok", ctx.term.supports_color),
                                code
                            );
                            if let Some(ttl) = data.get("ttl_seconds").and_then(|t| t.as_u64()) {
                                println!(
                                    "{}Valid for {} seconds",
                                    " ".repeat(ui::constants::DEFAULT_INDENT + 3),
                                    ttl
                                );
                            }
                            if let Some(inviter) = data.get("inviter_stone").and_then(|i| i.as_str()) {
                                println!(
                                    "{}From: {}",
                                    " ".repeat(ui::constants::DEFAULT_INDENT + 3),
                                    inviter
                                );
                            }
                        }
                    }
                }
            }
            Ok(response) => {
                eprintln!(
                    "{}{} Failed to generate invitation: {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    response.status()
                );
            }
            Err(e) => {
                eprintln!(
                    "{}{} Request failed: {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    e
                );
            }
        }

        // Self-teaching suggestions
        suggestions::print_suggestions(cmd::INVITE, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::INVITE
    }
}
