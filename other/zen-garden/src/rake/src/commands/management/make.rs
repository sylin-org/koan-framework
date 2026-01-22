//! Make command - configure stone console mode
//!
//! Zen syntax for setting console verbosity:
//! - make stone sing: Set verbose mode
//! - make stone quiet: Set informative mode (default)
//! - make stone silent: Set silent mode
//! - make stone minimal: Set minimal mode (critical only)

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::suggestions;
use crate::ui;
use async_trait::async_trait;

/// Console mode action
pub enum MakeActionType {
    /// Set verbose mode (stone sings)
    Sing { forever: bool },
    /// Set informative mode (default)
    Quiet,
    /// Set silent mode
    Silent,
    /// Set minimal mode (critical only)
    Minimal,
}

/// Make command for configuring console mode
pub struct MakeCommand {
    pub action: MakeActionType,
    pub quiet_mode: bool,
}

impl MakeCommand {
    pub fn new(action: MakeActionType, quiet_mode: bool) -> Self {
        Self { action, quiet_mode }
    }
}

#[async_trait]
impl Command for MakeCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        let endpoint = ctx.endpoint()?;
        let url = format!("{}/api/v1/console/mode", endpoint.trim_end_matches('/'));

        match &self.action {
            MakeActionType::Sing { forever } => {
                execute_make_sing(ctx, &url, *forever).await?;
            }
            MakeActionType::Quiet => {
                execute_make_quiet(ctx, &url).await?;
            }
            MakeActionType::Silent => {
                execute_make_silent(ctx, &url).await?;
            }
            MakeActionType::Minimal => {
                execute_make_minimal(ctx, &url).await?;
            }
        }

        // Self-teaching suggestions
        suggestions::print_suggestions(cmd::MAKE, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::MAKE
    }
}

async fn execute_make_sing(ctx: &CommandContext, url: &str, forever: bool) -> anyhow::Result<()> {
    let timeout_minutes = if forever { 0 } else { 30 };
    let persist = forever;

    let payload = serde_json::json!({
        "mode": "verbose",
        "persist": persist,
        "timeout_minutes": timeout_minutes
    });

    match ctx
        .client
        .post(url)
        .header("Content-Type", "application/json")
        .json(&payload)
        .send()
        .await
    {
        Ok(response) if response.status().is_success() => {
            if forever {
                println!(
                    "{}{} Stone singing (verbose mode, permanent)",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("ok", ctx.term.supports_color)
                );
            } else {
                println!(
                    "{}{} Stone singing (verbose mode, 30min timeout)",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("ok", ctx.term.supports_color)
                );
            }
        }
        Ok(response) => {
            let status = response.status();
            if let Ok(body) = response.text().await {
                eprintln!(
                    "{}{} Failed to set mode: {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    body
                );
            } else {
                eprintln!(
                    "{}{} Failed to set mode: {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    status
                );
            }
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

async fn execute_make_quiet(ctx: &CommandContext, url: &str) -> anyhow::Result<()> {
    let payload = serde_json::json!({
        "mode": "informative",
        "persist": true,
        "timeout_minutes": 0
    });

    match ctx
        .client
        .post(url)
        .header("Content-Type", "application/json")
        .json(&payload)
        .send()
        .await
    {
        Ok(response) if response.status().is_success() => {
            println!(
                "{}{} Stone quieted (informative mode, permanent)",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("ok", ctx.term.supports_color)
            );
        }
        Ok(response) => {
            let status = response.status();
            if let Ok(body) = response.text().await {
                eprintln!(
                    "{}{} Failed to set mode: {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    body
                );
            } else {
                eprintln!(
                    "{}{} Failed to set mode: {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    status
                );
            }
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

async fn execute_make_silent(ctx: &CommandContext, url: &str) -> anyhow::Result<()> {
    let payload = serde_json::json!({
        "mode": "silent",
        "persist": true,
        "timeout_minutes": 0
    });

    match ctx
        .client
        .post(url)
        .header("Content-Type", "application/json")
        .json(&payload)
        .send()
        .await
    {
        Ok(response) if response.status().is_success() => {
            println!(
                "{}{} Stone silenced (silent mode, permanent)",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("ok", ctx.term.supports_color)
            );
        }
        Ok(response) => {
            let status = response.status();
            if let Ok(body) = response.text().await {
                eprintln!(
                    "{}{} Failed to set mode: {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    body
                );
            } else {
                eprintln!(
                    "{}{} Failed to set mode: {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    status
                );
            }
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

async fn execute_make_minimal(ctx: &CommandContext, url: &str) -> anyhow::Result<()> {
    let payload = serde_json::json!({
        "mode": "minimal",
        "persist": true,
        "timeout_minutes": 0
    });

    match ctx
        .client
        .post(url)
        .header("Content-Type", "application/json")
        .json(&payload)
        .send()
        .await
    {
        Ok(response) if response.status().is_success() => {
            println!(
                "{}{} Stone set to minimal mode (critical only, permanent)",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("ok", ctx.term.supports_color)
            );
        }
        Ok(response) => {
            let status = response.status();
            if let Ok(body) = response.text().await {
                eprintln!(
                    "{}{} Failed to set mode: {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    body
                );
            } else {
                eprintln!(
                    "{}{} Failed to set mode: {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    status
                );
            }
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
