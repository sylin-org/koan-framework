//! Zen Rake Library
//!
//! CLI tool for service discovery and management.
//!
//! Architecture:
//! - commands/  - Command handlers by category
//! - api/       - HTTP client and response parsing
//! - ui/        - Terminal output and formatting
//! - discovery/ - Stone discovery (UDP, mDNS)

pub mod api;
pub mod client;
pub mod command_manifest;
pub mod commands;
pub mod context;
pub mod discovery;
pub mod layout;
pub mod stone_cache;
pub mod suggestions;
pub mod tending;
pub mod ui;

// Re-exports for convenience
pub use client::{resolve_target_endpoint, CachedStoneOps, CachedStoneInfo};
pub use context::CommandContext;
pub use layout::{Layout, IndentLevel};
pub use ui::{OutputWriter, TerminalInfo};
