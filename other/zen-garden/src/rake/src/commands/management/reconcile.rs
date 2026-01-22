//! Reconcile command - sync offerings with actual state
//!
//! Reconciles the offerings state with actual container state.

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::suggestions;
use crate::ui;
use async_trait::async_trait;
use std::time::Duration;

/// Reconcile offerings with actual container state
pub struct ReconcileCommand {
    pub drop_invalid: bool,
    pub quiet_mode: bool,
}

impl ReconcileCommand {
    pub fn new(drop_invalid: bool, quiet_mode: bool) -> Self {
        Self {
            drop_invalid,
            quiet_mode,
        }
    }
}

#[async_trait]
impl Command for ReconcileCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        let body = reconcile_system(&ctx.client, ctx.endpoint()?, self.drop_invalid).await?;

        let adopted = body
            .get("adopted")
            .and_then(|v| v.as_array())
            .map(|a| a.len())
            .unwrap_or(0);
        let dropped = body
            .get("dropped_invalid")
            .and_then(|v| v.as_array())
            .map(|a| a.len())
            .unwrap_or(0);
        let left = body
            .get("left_unregistered")
            .and_then(|v| v.as_array())
            .map(|a| a.len())
            .unwrap_or(0);

        println!(
            "{}{} Reconcile complete",
            " ".repeat(ui::constants::DEFAULT_INDENT),
            ui::status_indicator("ok", ctx.term.supports_color)
        );
        println!(
            "{}  Adopted: {}",
            " ".repeat(ui::constants::DEFAULT_INDENT),
            adopted
        );
        if self.drop_invalid {
            println!("  Dropped invalid: {}", dropped);
        }
        if left > 0 {
            println!("  Left unregistered: {}", left);
        }

        // Self-teaching suggestions
        suggestions::print_suggestions(cmd::RECONCILE, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::RECONCILE
    }
}

/// Execute system reconcile via API
async fn reconcile_system(
    client: &reqwest::Client,
    endpoint: &str,
    drop_invalid: bool,
) -> anyhow::Result<serde_json::Value> {
    use anyhow::Context;

    let url = format!(
        "{}/api/v1/system/reconcile",
        endpoint.trim_end_matches('/')
    );
    let payload = serde_json::json!({ "drop_invalid": drop_invalid });
    let response = client
        .post(&url)
        .json(&payload)
        .timeout(Duration::from_secs(30))
        .send()
        .await
        .context("Failed to send reconcile request")?;

    if response.status().is_success() {
        return Ok(response.json::<serde_json::Value>().await?);
    }

    let status = response.status();
    let text = response.text().await.unwrap_or_default();
    anyhow::bail!("Reconcile failed with status {}: {}", status, text);
}
