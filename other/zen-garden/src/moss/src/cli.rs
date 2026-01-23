//! Command-line interface definitions for garden-moss
//!
//! This module contains all CLI argument parsing and subcommand definitions.
//! Extracted from main.rs to keep the entry point minimal.

use clap::Parser;

/// Zen Garden Moss - Service orchestration daemon
#[derive(Parser)]
#[command(name = "garden-moss")]
#[command(about = "Zen Garden Moss - Service orchestration daemon")]
#[command(version = concat!(env!("CARGO_PKG_VERSION"), ".", env!("BUILD_NUMBER")))]
pub struct Cli {
    #[command(subcommand)]
    pub command: Option<Commands>,

    /// Stone name identifier
    /// Priority: CLI arg > STONE_NAME env var > config file > default
    #[arg(long, env = "STONE_NAME")]
    pub stone_name: Option<String>,

    /// HTTP server port
    /// Priority: CLI arg > PORT env var > config file > default (7185)
    #[arg(long, env = "PORT")]
    pub port: Option<u16>,

    /// Log level (trace, debug, info, warn, error)
    /// Priority: CLI arg > RUST_LOG env var > config file > default (info)
    #[arg(long, env = "RUST_LOG")]
    pub log_level: Option<String>,

    /// Fast sync timeout in seconds for rapid offering deployments
    /// Priority: CLI arg > FAST_SYNC_TIMEOUT env var > config file > default (disabled)
    #[arg(long, env = "FAST_SYNC_TIMEOUT")]
    pub fast_sync_timeout: Option<u64>,

    /// Force start by killing existing moss processes
    #[arg(long)]
    pub force: bool,

    /// Internal: Finalize update by replacing old binary (used during self-update)
    #[arg(long, hide = true)]
    pub update_finalize: bool,

    /// Internal: Cleanup old binary after update (used during self-update)
    #[arg(long, hide = true)]
    pub cleanup_old: bool,
}

#[derive(clap::Subcommand)]
pub enum Commands {
    /// Install moss as a system service and start it (Zen: take-root)
    #[cfg(target_os = "windows")]
    TakeRoot,

    /// Install moss as a system service and start it (Normative: install-service)
    #[cfg(target_os = "windows")]
    #[command(name = "install-service")]
    InstallService,
}

/// Parse CLI arguments
pub fn parse() -> Cli {
    Cli::parse()
}

/// Moss version string (compile-time constant)
/// Format: {major}.{minor}.{moment} e.g., "0.1.202601231053"
pub const VERSION: &str = concat!(env!("CARGO_PKG_VERSION"), ".", env!("BUILD_NUMBER"));

/// Get the moss version string (version.build)
/// Prefer using VERSION const directly when possible to avoid allocation.
pub fn version_string() -> String {
    VERSION.to_string()
}
