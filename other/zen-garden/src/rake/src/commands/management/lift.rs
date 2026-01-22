//! Lift command - remove pond elements
//!
//! Zen syntax for removing pond security elements:
//! - lift keystone: Remove pond security entirely
//! - lift stone <name>: Remove a stone from the pond

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::suggestions;
use crate::ui;
use async_trait::async_trait;

/// Lift target type
pub enum LiftTarget {
    /// Lift the keystone (remove pond entirely)
    Keystone,
    /// Lift a stone from the pond
    Stone { name: String },
}

/// Lift command for removing pond elements
pub struct LiftCommand {
    pub target: LiftTarget,
    pub quiet_mode: bool,
}

impl LiftCommand {
    pub fn new(target: LiftTarget, quiet_mode: bool) -> Self {
        Self { target, quiet_mode }
    }

    /// Create from CLI args
    pub fn from_args(
        target_type: String,
        stone_name: Option<String>,
        quiet_mode: bool,
    ) -> anyhow::Result<Self> {
        let target = match target_type.as_str() {
            "keystone" => LiftTarget::Keystone,
            "stone" => {
                let name = stone_name.ok_or_else(|| {
                    anyhow::anyhow!("Stone name required for 'lift stone'")
                })?;
                LiftTarget::Stone { name }
            }
            _ => anyhow::bail!("Invalid target: '{}'. Use 'keystone' or 'stone'", target_type),
        };
        Ok(Self::new(target, quiet_mode))
    }
}

#[async_trait]
impl Command for LiftCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        let endpoint = ctx.endpoint()?;

        match &self.target {
            LiftTarget::Keystone => {
                execute_lift_keystone(ctx, endpoint).await?;
            }
            LiftTarget::Stone { name } => {
                execute_lift_stone(ctx, endpoint, name).await?;
            }
        }

        // Self-teaching suggestions
        suggestions::print_suggestions(cmd::LIFT, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::LIFT
    }
}

async fn execute_lift_keystone(ctx: &CommandContext, endpoint: &str) -> anyhow::Result<()> {
    let url = format!("{}/api/v1/pond", endpoint.trim_end_matches('/'));

    match ctx.client.delete(&url).send().await {
        Ok(response) if response.status() == reqwest::StatusCode::NOT_IMPLEMENTED => {
            println!(
                "{}{} Pond security not yet implemented (Phase 3b)",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("info", ctx.term.supports_color)
            );
            println!(
                "{}This command will remove pond security (lift the keystone).",
                " ".repeat(ui::constants::DEFAULT_INDENT)
            );
        }
        Ok(response) if response.status().is_success() => {
            println!(
                "{}{} Keystone lifted (pond removed)",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("ok", ctx.term.supports_color)
            );
        }
        Ok(response) => {
            eprintln!(
                "{}{} Failed to lift keystone: {}",
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

async fn execute_lift_stone(
    ctx: &CommandContext,
    endpoint: &str,
    stone_name: &str,
) -> anyhow::Result<()> {
    let url = format!(
        "{}/api/v1/pond/stones/{}",
        endpoint.trim_end_matches('/'),
        stone_name
    );

    match ctx.client.delete(&url).send().await {
        Ok(response) if response.status() == reqwest::StatusCode::NOT_IMPLEMENTED => {
            println!(
                "{}{} Pond security not yet implemented (Phase 3b)",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("info", ctx.term.supports_color)
            );
            println!(
                "{}This command will remove a stone from the pond trust network.",
                " ".repeat(ui::constants::DEFAULT_INDENT)
            );
        }
        Ok(response) if response.status().is_success() => {
            println!(
                "{}{} Lifted {} from pond",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("ok", ctx.term.supports_color),
                stone_name
            );
        }
        Ok(response) => {
            eprintln!(
                "{}{} Failed to lift stone: {}",
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
