//! Browse command - explore command manifest
//!
//! Lists and describes available commands from the manifest.

use crate::command_manifest::{self, MANIFEST};
use crate::commands::help::{display_all_commands, display_command_category, display_command_detail};
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::ui;
use async_trait::async_trait;

/// Browse commands in the manifest
pub struct BrowseCommand {
    pub name: Option<String>,
    pub category: Option<String>,
    pub zen: bool,
    pub normative: bool,
}

impl BrowseCommand {
    pub fn new(
        name: Option<String>,
        category: Option<String>,
        zen: bool,
        normative: bool,
    ) -> Self {
        Self {
            name,
            category,
            zen,
            normative,
        }
    }
}

#[async_trait]
impl Command for BrowseCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        if let Some(cmd_name) = &self.name {
            // Show detailed info for specific command
            if let Some(cmd) = MANIFEST.get(cmd_name) {
                display_command_detail(cmd, self.zen, self.normative);
            } else {
                eprintln!(
                    "{}{} Command '{}' not found",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", ctx.term.supports_color),
                    cmd_name
                );
                println!();
                println!(
                    "{}Available commands:",
                    " ".repeat(ui::constants::DEFAULT_INDENT)
                );
                for c in MANIFEST.all() {
                    println!("{}  {}", " ".repeat(ui::constants::DEFAULT_INDENT), c.name);
                }
            }
        } else if let Some(cat_name) = &self.category {
            // Filter by category
            let category = match cat_name.to_lowercase().as_str() {
                "discovery" => command_manifest::CommandCategory::Discovery,
                "lifecycle" => command_manifest::CommandCategory::Lifecycle,
                "management" => command_manifest::CommandCategory::Management,
                "system" => command_manifest::CommandCategory::System,
                "pond" => command_manifest::CommandCategory::Pond,
                _ => {
                    eprintln!(
                        "{}{} Unknown category: {}",
                        " ".repeat(ui::constants::DEFAULT_INDENT),
                        ui::status_indicator("error", ctx.term.supports_color),
                        cat_name
                    );
                    println!();
                    println!(
                        "{}Available categories: discovery, lifecycle, management, system, pond",
                        " ".repeat(ui::constants::DEFAULT_INDENT)
                    );
                    return Ok(());
                }
            };

            let cmds = MANIFEST.by_category(&category);
            display_command_category(&category, &cmds, self.zen, self.normative);
        } else {
            // Show all commands grouped by category
            display_all_commands(self.zen, self.normative);
        }

        // BrowseCommands is a meta-command, no suggestions needed
        Ok(())
    }

    fn requires_endpoint(&self) -> bool {
        false
    }

    fn show_stone_header(&self) -> bool {
        false
    }

    fn name(&self) -> &'static str {
        "browse"
    }
}
