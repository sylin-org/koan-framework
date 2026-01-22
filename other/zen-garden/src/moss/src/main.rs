// Import types and functions from the library (lib.rs)
use garden_moss::{
    AppState, MossEvent, Job, JobStatus,
    // Task coordination (replaces inline spawning)
    start_lantern_registration,
    start_discovery_listener, start_hardware_detection,
    start_registry_loader, start_catalog_builder,
    start_manifest_loader, start_health_monitor, start_auto_adoption,
    // Task functions (for pre-install only - will be extracted later)
    install_batch_task,
    // Network monitoring
    NetworkMonitor, NetworkMonitorConfig,
    // Bootstrap functions
    load_preinstall_manifest,
    run_first_boot_initialization,
    router,
    bind_server, run_server, ServerConfig,
    connect_docker, init_capabilities, DockerConfig,
    // Configuration
    DaemonConfig, init_tracing,
    // CLI
    Cli, Commands, version_string,
};
use garden_moss::infra::kill_existing_moss_processes_graceful;
#[cfg(target_os = "windows")]
use garden_moss::infra::{install_windows_service, finalize_service_update, cleanup_after_service_update};

use garden_moss::{console, templates, mdns};
use templates::TemplateLoader;
use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::RwLock;

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    // Parse CLI arguments first to check for special modes
    let cli = <Cli as clap::Parser>::parse();
    
    // Handle subcommands (take-root/install-service)
    #[cfg(target_os = "windows")]
    if let Some(command) = &cli.command {
        return match command {
            Commands::TakeRoot | Commands::InstallService => install_windows_service().await,
        };
    }

    // Handle update finalization (runs as garden-moss-new.exe)
    #[cfg(target_os = "windows")]
    if cli.update_finalize {
        return finalize_service_update().await;
    }

    // Handle cleanup of old binary after update
    #[cfg(target_os = "windows")]
    if cli.cleanup_old {
        return cleanup_after_service_update().await;
    }
    
    // Load and merge configuration (CLI > Env > File > Defaults)
    // Uses extracted DaemonConfig from bootstrap/config.rs
    let config = DaemonConfig::from_cli(&cli).await?;

    // Initialize tracing/logging based on configuration
    init_tracing(&config);

    // Extract frequently used values for convenience
    let stone_name = config.stone_name.clone();
    let port = config.port;

    // Spawn first-boot initialization as background task if needed (Linux only)
    // Windows/dev environments don't need hostname/hosts/avahi setup
    if cfg!(target_os = "linux") && console::is_first_run() {
        tracing::info!("First run detected on Linux, spawning background initialization task");

        // Emit first-boot event (will create console later in initialization)
        tracing::info!("First boot detected - will initialize console after Docker connection");

        let init_stone_name = stone_name.clone();
        let init_port = port;
        let retry_delay_secs = config.docker_retry_delay_secs();
        tokio::spawn(async move {
            const MAX_ATTEMPTS: u32 = 20;
            
            let _ = console::tty_write("");
            let _ = console::display_wait("First-boot setup: Waiting for filesystem to become writable");
            
            for attempt in 1..=MAX_ATTEMPTS {
                match console::ensure_etc_writable().await {
                    Ok(true) => {
                        tracing::info!(attempt, "Filesystem is writable, proceeding with first boot initialization");
                        let _ = console::display_success("Filesystem ready, starting configuration");
                        
                        match run_first_boot_initialization(&init_stone_name, init_port).await {
                            Ok(new_name) => {
                                if let Err(e) = console::mark_first_run_complete().await {
                                    tracing::error!(error = ?e, "Failed to mark first-run complete");
                                }
                                
                                tracing::info!(new_name = %new_name, "First boot initialization completed successfully");
                                let _ = console::tty_write("");
                                let _ = console::display_success(&format!("✓ Stone configured as: {}", new_name));
                                let _ = console::display_wait("Restarting to apply new configuration...");
                                let _ = console::tty_write("");
                                
                                // Exit so systemd restarts us with the new configuration
                                std::process::exit(0);
                            }
                            Err(e) => {
                                tracing::error!(error = ?e, "First boot initialization failed");
                                let _ = console::display_error(&format!("Setup failed: {}", e));
                                if attempt < MAX_ATTEMPTS {
                                    tokio::time::sleep(tokio::time::Duration::from_secs(retry_delay_secs)).await;
                                }
                            }
                        }
                    }
                    Ok(false) | Err(_) => {
                        if attempt < MAX_ATTEMPTS {
                            tokio::time::sleep(tokio::time::Duration::from_secs(retry_delay_secs)).await;
                        } else {
                            tracing::error!("First boot initialization abandoned - filesystem never became writable");
                            let _ = console::display_error("Setup abandoned - filesystem remained read-only");
                        }
                    }
                }
            }
        });
    }
    
    // Handle --force flag: try graceful shutdown, then force kill if needed
    if cli.force {
        tracing::info!("--force flag set, attempting graceful shutdown of existing moss processes");
        if let Err(e) = kill_existing_moss_processes_graceful().await {
            tracing::warn!(error = ?e, "Failed to shutdown existing processes, continuing anyway");
        }
        // Give the OS time to free the port
        tokio::time::sleep(tokio::time::Duration::from_millis(500)).await;
    }

    // Start network monitor for IP change detection
    // This runs in background and polls every 5s when disconnected, 30s when connected
    let network_monitor = NetworkMonitor::start_with_config(
        NetworkMonitorConfig::default()
            .with_disconnect_retry(5)  // Retry every 5s when no valid LAN IP
            .with_connected_poll(30)   // Poll every 30s when connected
    ).await;

    // Prefer explicit STONE_HOST, otherwise use monitored network IP
    let use_static_host = std::env::var(garden_common::ENV_STONE_HOST)
        .ok()
        .filter(|h| !h.trim().is_empty());

    let api_endpoint = if let Some(host) = &use_static_host {
        format!("http://{}:{}", host.trim(), port)
    } else {
        // Use network monitor's current IP (auto-detected)
        format!("http://{}:{}", network_monitor.get_ip().await, port)
    };

    // Start mDNS announcer (Linux only)
    let _mdns = match mdns::announce_moss(&stone_name, port) {
        Ok(daemon) => Some(daemon),
        Err(e) => {
            tracing::warn!(error = ?e, "mDNS announcement failed");
            None
        }
    };

    // Start Lantern registration if LANTERN_ENDPOINT is configured
    // Uses extracted coordinator function for cleaner main.rs
    // Console is None here since console_printer is created later
    start_lantern_registration(
        &stone_name,
        &api_endpoint,
        port,
        use_static_host.is_some(),
        &network_monitor,
        None,
    ).await;

    // Initialize console printer early for Docker connection events
    // Uses console_mode and dedup_ttl from merged DaemonConfig
    let console_printer = Arc::new(console::ConsolePrinter::with_dedup_ttl(
        config.console_mode,
        config.event_dedup_ttl_secs,
    ));

    // Emit startup event
    console_printer.emit(console::ConsoleEvent::new(
        console::EventCategory::System,
        console::EventStatus::Starting,
        format!("Moss v{}", version_string())
    ));

    // Emit config loading event (config was loaded earlier before console was available)
    if config.file_config.is_some() {
        console_printer.emit(console::ConsoleEvent::new(
            console::EventCategory::Config,
            console::EventStatus::Loaded,
            "Configuration file".to_string()
        ));

        console_printer.emit(console::ConsoleEvent::new(
            console::EventCategory::Config,
            console::EventStatus::Merged,
            format!("Priority: CLI > Env > Config > Defaults")
        ));
    } else {
        // Config file not found or parse error
        console_printer.emit(console::ConsoleEvent::new(
            console::EventCategory::Config,
            console::EventStatus::NotFound,
            "Using defaults".to_string()
        ));
    }

    // Wait for Docker to be ready (with retries for fresh installs)
    // Uses extracted connect_docker() from bootstrap/startup.rs
    let docker = connect_docker(&console_printer, DockerConfig::default()).await?;

    // Create event broadcast channel (capacity 100 events)
    let (event_tx, _) = tokio::sync::broadcast::channel::<MossEvent>(100);
    
    // Create shutdown notification channel
    let shutdown_tx = Arc::new(tokio::sync::Notify::new());

    // Load capabilities from disk cache (instant startup - background refresh will update)
    // Uses extracted init_capabilities() from bootstrap/startup.rs
    let capabilities_arc = init_capabilities(&stone_name, &console_printer).await;

    let state = AppState {
        stone_name: stone_name.clone(),
        registry: Arc::new(RwLock::new(Vec::new())),
        adopted_offerings: Arc::new(RwLock::new(Vec::new())),
        borrowed_offerings: Arc::new(RwLock::new(Vec::new())),
        manifests: Arc::new(RwLock::new(Vec::new())),
        docker: docker.clone(),
        templates: Arc::new(TemplateLoader::new()),
        jobs: Arc::new(RwLock::new(HashMap::new())),
        event_tx,
        shutdown_tx: shutdown_tx.clone(),
        start_time: std::time::Instant::now(),
        offerings_index: Arc::new(RwLock::new(None)),
        console: console_printer.clone(),
        capabilities: capabilities_arc.clone(),
        network_monitor: Arc::new(network_monitor),
        api_port: port,
    };
    
    // Start background tasks using extracted coordinator functions
    // UDP discovery - critical for stone visibility, starts immediately
    start_discovery_listener(stone_name.clone(), api_endpoint.clone(), &console_printer).await;

    // Hardware detection - progressive (CPU fast, GPU slow)
    start_hardware_detection(stone_name.clone(), capabilities_arc.clone(), console_printer.clone(), state.clone());

    // Registry loading and container adoption
    start_registry_loader(state.clone());

    // Offerings catalog building
    start_catalog_builder(state.clone(), console_printer.clone());

    // Offering manifests loading
    start_manifest_loader(state.clone(), console_printer.clone());

    // Check for pre-install manifest on first boot
    if let Some(manifest) = load_preinstall_manifest().await {
        if manifest.auto_install {
            tracing::info!(
                "Starting auto-installation of {} services from manifest", 
                manifest.offerings.len()
            );
            
            // Validate all offerings exist before creating job
            let mut invalid_offerings = Vec::new();
            for offering in &manifest.offerings {
                if let Err(_) = state.templates.load(offering) {
                    invalid_offerings.push(offering.clone());
                }
            }

            if !invalid_offerings.is_empty() {
                tracing::error!(
                    offerings = ?invalid_offerings,
                    "Pre-install manifest contains invalid offerings - skipping auto-install"
                );
            } else {
                let job_id = uuid::Uuid::now_v7().to_string();
            let job = Job {
                id: job_id.clone(),
                offerings: manifest.offerings.clone(),
                status: JobStatus::Pending,
                completed: vec![],
                failed: HashMap::new(),
                started_at: std::time::SystemTime::now(),
                completed_at: None,
            };
            
            state.jobs.write().await.insert(job_id.clone(), job);
            
            // Spawn background installation + cleanup task
            let install_state = state.clone();
            let install_job_id = job_id.clone();
            let install_offerings = manifest.offerings.clone();
            tokio::spawn(async move {
                install_batch_task(&install_state, &install_job_id, install_offerings).await;
                
                // Wait for job completion, then remove manifest
                loop {
                    tokio::time::sleep(tokio::time::Duration::from_secs(5)).await;
                    let jobs = install_state.jobs.read().await;
                    if let Some(job) = jobs.get(&install_job_id) {
                        match job.status {
                            JobStatus::Completed | JobStatus::Failed => {
                                drop(jobs); // Release lock
                                tracing::info!("Pre-install job finished, removing manifest");
                                if let Err(e) = tokio::fs::remove_file("/home/stone/garden-moss-preinstall.json").await {
                                    tracing::warn!(error = ?e, "Failed to remove pre-install manifest");
                                } else {
                                    tracing::info!("Pre-install manifest removed - system ready");
                                }
                                break;
                            }
                            _ => continue, // Still running
                        }
                    } else {
                        break; // Job not found
                    }
                }
            });
            
            tracing::info!("Pre-install job started: {} (check /api/jobs/{})", job_id, job_id);
            }
        }
    }

    // Health monitoring - periodic container health checks
    start_health_monitor(state.clone());

    // Auto-adoption - watches for new containers matching naming patterns
    if let Some(cfg) = config.file_config.clone() {
        start_auto_adoption(state.clone(), cfg, &console_printer);
    }

    // Configure HTTP router (routes defined in bootstrap/router.rs)
    tracing::info!("Setting up HTTP router with 200 MB body limit");
    let app = router::configure(state.clone());

    // Bind HTTP server (with user-friendly error messages)
    let listener = bind_server(port, &console_printer).await?;

    // Run server with graceful shutdown support
    run_server(
        listener,
        app,
        &api_endpoint,
        console_printer,
        shutdown_tx,
        ServerConfig::default(),
    ).await
}
