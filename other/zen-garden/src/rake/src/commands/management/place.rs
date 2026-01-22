//! Place command - pond zen syntax for placement
//!
//! Zen syntax for pond security placement operations:
//! - place keystone: Initialize pond security (equivalent to pond init)
//! - place stone --code <code>: Join pond with invitation code (equivalent to pond join)

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::suggestions;
use crate::ui;
use async_trait::async_trait;

/// Place target type
pub enum PlaceTarget {
    /// Place the keystone (initialize pond)
    Keystone { passphrase: Option<String> },
    /// Place a stone in the pond (join with code)
    Stone { code: String },
}

/// Place command for pond zen syntax operations
pub struct PlaceCommand {
    pub target: PlaceTarget,
    pub quiet_mode: bool,
}

impl PlaceCommand {
    pub fn new(target: PlaceTarget, quiet_mode: bool) -> Self {
        Self { target, quiet_mode }
    }

    /// Create from CLI args
    pub fn from_args(
        target_type: String,
        code: Option<String>,
        passphrase: Option<String>,
        quiet_mode: bool,
    ) -> anyhow::Result<Self> {
        let target = match target_type.as_str() {
            "keystone" => PlaceTarget::Keystone { passphrase },
            "stone" => {
                let code = code.ok_or_else(|| {
                    anyhow::anyhow!("--code required for placing a stone\nExample: garden-rake place stone --code ABC123")
                })?;
                PlaceTarget::Stone { code }
            }
            _ => anyhow::bail!("Invalid target: '{}'. Use 'keystone' or 'stone'", target_type),
        };
        Ok(Self::new(target, quiet_mode))
    }
}

#[async_trait]
impl Command for PlaceCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        let endpoint = ctx.endpoint()?;

        match &self.target {
            PlaceTarget::Keystone { passphrase } => {
                execute_place_keystone(ctx, endpoint, passphrase.clone()).await?;
            }
            PlaceTarget::Stone { code } => {
                execute_place_stone(ctx, endpoint, code).await?;
            }
        }

        // Self-teaching suggestions
        suggestions::print_suggestions(cmd::PLACE, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::PLACE
    }
}

async fn execute_place_keystone(
    ctx: &CommandContext,
    endpoint: &str,
    passphrase: Option<String>,
) -> anyhow::Result<()> {
    let pass = passphrase.unwrap_or_else(|| {
        // In a real implementation, prompt for passphrase securely
        println!(
            "{}{} Using default passphrase. Use --passphrase for custom encryption.",
            " ".repeat(ui::constants::DEFAULT_INDENT),
            ui::status_indicator("info", ctx.term.supports_color)
        );
        "changeme".to_string()
    });

    let url = format!("{}/api/v1/pond/init", endpoint.trim_end_matches('/'));
    let payload = serde_json::json!({ "passphrase": pass });

    match ctx.client.post(&url).json(&payload).send().await {
        Ok(response) if response.status() == reqwest::StatusCode::NOT_IMPLEMENTED => {
            println!(
                "{}{} Pond security not yet implemented (Phase 3b)",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("info", ctx.term.supports_color)
            );
            println!(
                "{}This command will initialize pond security with encrypted certificates.",
                " ".repeat(ui::constants::DEFAULT_INDENT)
            );
        }
        Ok(response) if response.status().is_success() => {
            println!(
                "{}{} Pond initialized (keystone placed)",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("ok", ctx.term.supports_color)
            );
        }
        Ok(response) => {
            eprintln!(
                "{}{} Failed to initialize pond: {}",
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

    Ok(())
}

async fn execute_place_stone(
    ctx: &CommandContext,
    endpoint: &str,
    code: &str,
) -> anyhow::Result<()> {
    let url = format!("{}/api/v1/pond/join", endpoint.trim_end_matches('/'));
    let payload = serde_json::json!({ "code": code });

    match ctx.client.post(&url).json(&payload).send().await {
        Ok(response) if response.status() == reqwest::StatusCode::NOT_IMPLEMENTED => {
            println!(
                "{}{} Pond security not yet implemented (Phase 3b)",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("info", ctx.term.supports_color)
            );
            println!(
                "{}This command will join an existing pond using an invitation code.",
                " ".repeat(ui::constants::DEFAULT_INDENT)
            );
        }
        Ok(response) if response.status().is_success() => {
            println!(
                "{}{} Joined pond successfully (stone placed)",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("ok", ctx.term.supports_color)
            );
            if let Ok(body) = response.json::<serde_json::Value>().await {
                if let Some(data) = body.get("data") {
                    if let Some(stone_name) = data.get("stone_name").and_then(|s| s.as_str()) {
                        println!(
                            "{}Stone: {}",
                            " ".repeat(ui::constants::DEFAULT_INDENT + 3),
                            stone_name
                        );
                    }
                    if let Some(cornerstone) = data.get("cornerstone").and_then(|c| c.as_str()) {
                        println!(
                            "{}Cornerstone: {}",
                            " ".repeat(ui::constants::DEFAULT_INDENT + 3),
                            cornerstone
                        );
                    }
                }
            }
        }
        Ok(response) => {
            eprintln!(
                "{}{} Failed to join pond: {}",
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

    Ok(())
}
