//! Configuration loading and merging
//!
//! Handles the configuration priority chain:
//! - CLI arguments (highest priority)
//! - Environment variables
//! - Configuration file
//! - Defaults (lowest priority)
//!
//! Extracted from main.rs for cleaner separation of concerns.

use crate::{cli::Cli, console, infra::MossConfig};
use tracing_subscriber::EnvFilter;

/// Merged daemon configuration from all sources
#[derive(Clone)]
pub struct DaemonConfig {
    /// Stone identity name
    pub stone_name: String,
    /// HTTP API port
    pub port: u16,
    /// Log level string
    pub log_level: String,
    /// Console output mode
    pub console_mode: console::ConsoleMode,
    /// Fast sync timeout (optional)
    pub fast_sync_timeout: Option<u64>,
    /// Event deduplication TTL in seconds
    pub event_dedup_ttl_secs: u64,
    /// Original file config (for access to all settings)
    pub file_config: Option<MossConfig>,
    /// Whether --force flag was set
    pub force: bool,
}

impl DaemonConfig {
    /// Load and merge configuration from CLI, environment, and file
    ///
    /// Priority: CLI > Env > Config File > Defaults
    pub async fn from_cli(cli: &Cli) -> anyhow::Result<Self> {
        // Load configuration from file first (lowest priority)
        let file_config = MossConfig::load();

        // Merge log level
        let log_level = cli.log_level
            .clone()
            .or_else(|| file_config.as_ref().and_then(|c| c.log_level.clone()))
            .unwrap_or_else(|| "info".to_string());

        // Resolve stone name with complex priority chain
        let stone_name = resolve_stone_name(cli, &file_config).await?;

        // Merge port
        let port = cli.port
            .or_else(|| file_config.as_ref().and_then(|c| c.port))
            .unwrap_or(garden_common::ports::MOSS_HTTP);

        // Merge fast sync timeout
        let fast_sync_timeout = cli.fast_sync_timeout
            .or_else(|| file_config.as_ref().and_then(|c| c.fast_sync_timeout));

        // Determine console mode
        let console_mode = file_config.as_ref()
            .and_then(|c| c.console_mode.as_ref())
            .and_then(|mode_str| mode_str.parse::<console::ConsoleMode>().ok())
            .unwrap_or_else(console::detect_platform_console_mode);

        // Event deduplication TTL
        let event_dedup_ttl_secs = file_config.as_ref()
            .map(|c| c.event_dedup_ttl_secs())
            .unwrap_or(10);

        Ok(Self {
            stone_name,
            port,
            log_level,
            console_mode,
            fast_sync_timeout,
            event_dedup_ttl_secs,
            file_config,
            force: cli.force,
        })
    }

    /// Get retry delay for Docker connection
    pub fn docker_retry_delay_secs(&self) -> u64 {
        self.file_config.as_ref()
            .map(|c| c.docker_retry_delay_secs())
            .unwrap_or(3)
    }
}

/// Resolve stone name with priority chain
///
/// Priority: explicit CLI flag (--stone-name) > config file > system hostname > STONE_NAME env > default
async fn resolve_stone_name(cli: &Cli, config: &Option<MossConfig>) -> anyhow::Result<String> {
    let env_stone_name = std::env::var(garden_common::ENV_STONE_NAME).ok();

    // CLI flag only counts if it wasn't set via env var
    let explicit_cli_stone_name = if cli.stone_name.is_some() && env_stone_name.is_none() {
        cli.stone_name.clone()
    } else {
        None
    };

    let system_hostname = console::get_hostname().await.ok();

    // Warn if env and hostname mismatch
    if let (Some(env_name), Some(sys_name)) = (&env_stone_name, &system_hostname) {
        if env_name != sys_name {
            tracing::warn!(
                env_stone_name = %env_name,
                system_hostname = %sys_name,
                "STONE_NAME env does not match system hostname; preferring hostname (fix systemd unit to remove Environment=STONE_NAME)"
            );
        }
    }

    let stone_name = explicit_cli_stone_name
        .or_else(|| config.as_ref().and_then(|c| c.stone_name.clone()))
        .or_else(|| system_hostname)
        .or_else(|| env_stone_name)
        .unwrap_or_else(|| garden_common::DEFAULT_STONE_NAME.to_string());

    Ok(stone_name)
}

/// Initialize tracing/logging based on configuration
///
/// Adjusts tracing level based on console mode to avoid duplication.
pub fn init_tracing(config: &DaemonConfig) {
    // Adjust tracing level based on console mode to avoid duplication with console events
    // verbose mode: keep INFO for debugging
    // all other modes: suppress to WARN to avoid spam (console events handle the rest)
    let default_tracing_level = match config.console_mode {
        console::ConsoleMode::Verbose => "info",
        _ => "warn",  // Suppress INFO logs when console events are active
    };

    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| EnvFilter::new(default_tracing_level))
        )
        .init();

    // Legacy structured log (keep for debugging until full migration)
    tracing::info!(
        stone_name = %config.stone_name,
        port = config.port,
        log_level = %config.log_level,
        fast_sync_timeout = ?config.fast_sync_timeout,
        config_loaded = config.file_config.is_some(),
        "Moss daemon starting with merged configuration (priority: CLI > Env > Config > Defaults)"
    );
}
