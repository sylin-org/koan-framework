//! Command implementations for garden-rake
//!
//! This module contains the command handlers extracted from main.rs
//! for better separation of concerns and testability.
//!
//! ## Architecture
//!
//! Commands implement the `Command` trait which provides:
//! - `execute()` - The command's business logic
//! - `requires_endpoint()` - Whether stone resolution is needed
//! - `show_stone_header()` - Whether to show stone banner
//!
//! The dispatcher handles common pre/post logic:
//! - Endpoint resolution (if required)
//! - Stone header display
//! - Suggestion printing
//!
//! ## Module Structure
//! - `help` - Command catalog and help display
//! - `discovery/` - observe, watch, list, status
//! - `lifecycle/` - offer, rest, wake, remove, upgrade
//! - `adoption/` - adopt, release, borrow, return, find
//! - `management/` - tend, reconcile, refresh
//! - `pond/` - place, invite

pub mod help;

// Command categories (to be extracted incrementally)
pub mod adoption;
pub mod discovery;
pub mod lifecycle;

use crate::context::CommandContext;
use async_trait::async_trait;

/// Command execution result
pub type CommandResult = anyhow::Result<()>;

/// Trait for command handlers
///
/// Commands implement this trait to be dispatched by the CLI.
/// The trait provides hooks for common behavior like endpoint resolution.
#[async_trait]
pub trait Command: Send + Sync {
    /// Execute the command with the given context
    async fn execute(&self, ctx: &CommandContext) -> CommandResult;

    /// Whether this command requires a resolved stone endpoint
    ///
    /// Default: true (most commands need a stone)
    fn requires_endpoint(&self) -> bool {
        true
    }

    /// Whether to display the stone header banner
    ///
    /// Default: same as requires_endpoint()
    fn show_stone_header(&self) -> bool {
        self.requires_endpoint()
    }

    /// Command name for suggestions lookup
    fn name(&self) -> &'static str;
}

/// Marker trait for commands that don't need endpoint
pub trait LocalCommand: Command {}

// Re-export commonly used items
pub use help::{display_all_commands, display_command_detail};
