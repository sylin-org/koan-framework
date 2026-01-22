//! Zen Garden Moss - Service orchestration daemon
//!
//! Entry point with CLI dispatch. All orchestration logic delegated to bootstrap module.

use garden_moss::{
    Cli, DaemonConfig, init_tracing, run_daemon,
};
use garden_moss::infra::kill_existing_moss_processes_graceful;
#[cfg(target_os = "windows")]
use garden_moss::Commands;
#[cfg(target_os = "windows")]
use garden_moss::infra::{install_windows_service, finalize_service_update, cleanup_after_service_update};

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    // Parse CLI arguments
    let cli = <Cli as clap::Parser>::parse();

    // Handle Windows service commands (early exit)
    #[cfg(target_os = "windows")]
    if let Some(command) = &cli.command {
        return match command {
            Commands::TakeRoot | Commands::InstallService => install_windows_service().await,
        };
    }

    #[cfg(target_os = "windows")]
    if cli.update_finalize {
        return finalize_service_update().await;
    }

    #[cfg(target_os = "windows")]
    if cli.cleanup_old {
        return cleanup_after_service_update().await;
    }

    // Load and merge configuration (CLI > Env > File > Defaults)
    let config = DaemonConfig::from_cli(&cli).await?;

    // Initialize tracing/logging
    init_tracing(&config);

    // Handle --force flag: kill existing processes
    if config.force {
        tracing::info!("--force flag set, attempting graceful shutdown of existing moss processes");
        if let Err(e) = kill_existing_moss_processes_graceful().await {
            tracing::warn!(error = ?e, "Failed to shutdown existing processes, continuing anyway");
        }
        tokio::time::sleep(tokio::time::Duration::from_millis(500)).await;
    }

    // Run daemon (all orchestration in bootstrap::run)
    run_daemon(config).await
}
