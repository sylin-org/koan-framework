//! Stone admin commands - power management
//!
//! Commands for controlling stone power state:
//! - Rouse: Wake a stone via Wake-on-LAN
//! - Slumber: Shut down a stone (power off)
//! - Stir: Reboot a stone

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::suggestions;
use crate::ui;
use async_trait::async_trait;
use std::time::Duration;

// ============================================================================
// Rouse Command - Wake-on-LAN
// ============================================================================

/// Wake a stone via Wake-on-LAN magic packet
pub struct RouseCommand {
    /// Stone name to wake
    pub stone_name: String,
    pub quiet_mode: bool,
}

impl RouseCommand {
    pub fn new(stone_name: String, quiet_mode: bool) -> Self {
        Self { stone_name, quiet_mode }
    }
}

#[async_trait]
impl Command for RouseCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        let url = ctx.api_v1_url(&format!("admin/stone/{}/wake", self.stone_name))?;

        println!(
            "{}{} Rousing {}...",
            " ".repeat(ui::constants::DEFAULT_INDENT),
            ui::status_indicator("info", ctx.term.supports_color),
            self.stone_name
        );

        let response = ctx
            .client
            .post(&url)
            .timeout(Duration::from_secs(10))
            .send()
            .await?;

        let status = response.status();

        match status {
            s if s.is_success() => {
                if let Ok(body) = response.json::<serde_json::Value>().await {
                    let message = body.get("message").and_then(|v| v.as_str()).unwrap_or("Wake-on-LAN packet sent");
                    let mac = body.get("mac").and_then(|v| v.as_str());
                    let stone_status = body.get("status").and_then(|v| v.as_str());

                    println!(
                        "{}{} {}",
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("ok", ctx.term.supports_color),
                        message
                    );

                    if let Some(mac_addr) = mac {
                        println!(
                            "{}   MAC: {}",
                            " ".repeat(ui::constants::DEFAULT_INDENT),
                            mac_addr
                        );
                    }

                    if let Some(status) = stone_status {
                        println!(
                            "{}   Status was: {}",
                            " ".repeat(ui::constants::DEFAULT_INDENT),
                            status
                        );
                    }
                } else {
                    println!(
                        "{}{} Wake-on-LAN packet sent to {}",
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("ok", ctx.term.supports_color),
                        self.stone_name
                    );
                }
            }
            reqwest::StatusCode::NOT_FOUND => {
                if let Ok(body) = response.json::<serde_json::Value>().await {
                    let error = body.get("error").and_then(|v| v.as_str()).unwrap_or("Stone not found");
                    let hint = body.get("hint").and_then(|v| v.as_str());

                    eprintln!(
                        "{}{} {}",
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("error", ctx.term.supports_color),
                        error
                    );

                    if let Some(h) = hint {
                        eprintln!(
                            "{}   Hint: {}",
                            " ".repeat(ui::constants::DEFAULT_INDENT),
                            h
                        );
                    }
                } else {
                    eprintln!(
                        "{}{} Stone '{}' not found in topology cache",
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("error", ctx.term.supports_color),
                        self.stone_name
                    );
                }
            }
            reqwest::StatusCode::BAD_REQUEST => {
                if let Ok(body) = response.json::<serde_json::Value>().await {
                    let error = body.get("error").and_then(|v| v.as_str()).unwrap_or("No MAC address");
                    let hint = body.get("hint").and_then(|v| v.as_str());

                    eprintln!(
                        "{}{} {}",
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("error", ctx.term.supports_color),
                        error
                    );

                    if let Some(h) = hint {
                        eprintln!(
                            "{}   Hint: {}",
                            " ".repeat(ui::constants::DEFAULT_INDENT),
                            h
                        );
                    }
                } else {
                    eprintln!(
                        "{}{} Stone '{}' has no MAC address cached",
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("error", ctx.term.supports_color),
                        self.stone_name
                    );
                }
            }
            _ => {
                eprintln!(
                    "{}{} Failed to send WoL packet: HTTP {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    status
                );
            }
        }

        suggestions::print_suggestions(cmd::ROUSE, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::ROUSE
    }
}

// ============================================================================
// Slumber Command - Stone Shutdown
// ============================================================================

/// Shut down a stone (power off)
pub struct SlumberCommand {
    pub quiet_mode: bool,
}

impl SlumberCommand {
    pub fn new(quiet_mode: bool) -> Self {
        Self { quiet_mode }
    }
}

#[async_trait]
impl Command for SlumberCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        let url = ctx.api_v1_url("admin/stone/shutdown")?;

        println!(
            "{}{} Requesting stone to enter slumber...",
            " ".repeat(ui::constants::DEFAULT_INDENT),
            ui::status_indicator("warn", ctx.term.supports_color)
        );

        let response = ctx
            .client
            .post(&url)
            .timeout(Duration::from_secs(10))
            .send()
            .await?;

        let status = response.status();

        match status {
            s if s.is_success() => {
                if let Ok(body) = response.json::<serde_json::Value>().await {
                    let message = body.get("message").and_then(|v| v.as_str()).unwrap_or("Shutdown initiated");

                    println!(
                        "{}{} {}",
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("ok", ctx.term.supports_color),
                        message
                    );
                } else {
                    println!(
                        "{}{} Stone entering slumber...",
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("ok", ctx.term.supports_color)
                    );
                }
            }
            _ => {
                eprintln!(
                    "{}{} Failed to initiate shutdown: HTTP {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    status
                );
            }
        }

        suggestions::print_suggestions(cmd::SLUMBER, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::SLUMBER
    }
}

// ============================================================================
// Stir Command - Stone Reboot
// ============================================================================

/// Reboot a stone
pub struct StirCommand {
    pub quiet_mode: bool,
}

impl StirCommand {
    pub fn new(quiet_mode: bool) -> Self {
        Self { quiet_mode }
    }
}

#[async_trait]
impl Command for StirCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        let url = ctx.api_v1_url("admin/stone/reboot")?;

        println!(
            "{}{} Requesting stone to stir (reboot)...",
            " ".repeat(ui::constants::DEFAULT_INDENT),
            ui::status_indicator("warn", ctx.term.supports_color)
        );

        let response = ctx
            .client
            .post(&url)
            .timeout(Duration::from_secs(10))
            .send()
            .await?;

        let status = response.status();

        match status {
            s if s.is_success() => {
                if let Ok(body) = response.json::<serde_json::Value>().await {
                    let message = body.get("message").and_then(|v| v.as_str()).unwrap_or("Reboot initiated");

                    println!(
                        "{}{} {}",
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("ok", ctx.term.supports_color),
                        message
                    );
                } else {
                    println!(
                        "{}{} Stone stirring...",
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("ok", ctx.term.supports_color)
                    );
                }
            }
            _ => {
                eprintln!(
                    "{}{} Failed to initiate reboot: HTTP {}",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    status
                );
            }
        }

        suggestions::print_suggestions(cmd::STIR, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::STIR
    }
}
