//! Main daemon orchestration
//!
//! Coordinates all startup phases and background tasks.
//! Extracted from main.rs for cleaner separation of concerns.

use crate::{
    AppState, MossEvent, Job, JobStatus,
    // Task coordination
    start_lantern_registration,
    start_discovery_listener, start_hardware_detection,
    start_registry_loader, start_catalog_builder,
    start_manifest_loader, start_health_monitor, start_auto_adoption,
    install_batch_task,
    // Network monitoring
    NetworkMonitor, NetworkMonitorConfig,
    // Bootstrap functions
    load_preinstall_manifest,
    run_first_boot_initialization,
    router,
    bind_server, run_server, ServerConfig,
    connect_docker, init_capabilities, DockerConfig,
    version_string,
    // Console
    console,
    // Templates
    templates::TemplateLoader,
    // mDNS
    mdns,
    // Infrastructure
    infra,
};
use super::config::DaemonConfig;
use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::RwLock;

/// Run the Moss daemon with the given configuration
///
/// This is the main entry point after CLI parsing and config loading.
/// Handles all startup phases and background task coordination.
pub async fn run(config: DaemonConfig) -> anyhow::Result<()> {
    let stone_name = config.stone_name.clone();
    let port = config.port;

    // Phase 0: Load or generate stone_id (persistent GUID v7)
    // This must happen early as many components need it
    let stone_id = infra::load_or_generate_stone_id().await;
    tracing::info!(stone_id = %stone_id, stone_name = %stone_name, "Stone identity loaded");

    // Phase 1: First-boot initialization (Linux only)
    // Windows/dev environments don't need hostname/hosts/avahi setup
    if cfg!(target_os = "linux") && console::is_first_run() {
        start_first_boot_task(&stone_name, port, config.docker_retry_delay_secs());
    }

    // Phase 2: Network monitoring
    // Runs in background, polls every 5s when disconnected, 30s when connected
    let network_monitor = NetworkMonitor::start_with_config(
        NetworkMonitorConfig::default()
            .with_disconnect_retry(5)
            .with_connected_poll(30)
    ).await;

    // Phase 3: Resolve API endpoint
    // Prefer explicit STONE_HOST, otherwise use monitored network IP
    let use_static_host = std::env::var(garden_common::ENV_STONE_HOST)
        .ok()
        .filter(|h| !h.trim().is_empty());

    let api_endpoint = if let Some(host) = &use_static_host {
        format!("http://{}:{}", host.trim(), port)
    } else {
        format!("http://{}:{}", network_monitor.get_ip().await, port)
    };

    // Phase 4: mDNS announcement (Linux only) - includes stone_id in TXT records
    let _mdns = match mdns::announce_moss(Some(stone_id.as_str()), &stone_name, port) {
        Ok(daemon) => Some(daemon),
        Err(e) => {
            tracing::warn!(error = ?e, "mDNS announcement failed");
            None
        }
    };

    // Phase 4.5: mDNS lurk-listener (passive topology discovery)
    // Listens for mDNS announcements from neighbor stones to populate hot-cache
    if let Ok(mut mdns_rx) = mdns::start_mdns_lurk_listener(stone_name.clone()) {
        tokio::spawn(async move {
            loop {
                match mdns_rx.recv().await {
                    Ok(discovered) => {
                        // For now, just log - full cache integration comes in Phase 2
                        tracing::debug!(
                            stone_id = ?discovered.stone_id,
                            stone_name = %discovered.stone_name,
                            endpoint = %discovered.endpoint,
                            "mDNS: Neighbor stone cached for future lookup"
                        );
                        // TODO: Add to TopologyCache when implemented (Phase 2)
                    }
                    Err(tokio::sync::broadcast::error::RecvError::Lagged(n)) => {
                        tracing::warn!(missed = n, "mDNS lurk-listener: missed events");
                    }
                    Err(tokio::sync::broadcast::error::RecvError::Closed) => {
                        tracing::debug!("mDNS lurk-listener channel closed");
                        break;
                    }
                }
            }
        });
    }

    // Phase 5: Lantern registration
    // Console is None here since console_printer is created later
    start_lantern_registration(
        &stone_id,
        &stone_name,
        &api_endpoint,
        port,
        use_static_host.is_some(),
        &network_monitor,
        None,
    ).await;

    // Phase 6: Console printer
    let console_printer = Arc::new(console::ConsolePrinter::with_dedup_ttl(
        config.console_mode,
        config.event_dedup_ttl_secs,
    ));
    emit_startup_events(&console_printer, &config);

    // Phase 7: Docker connection
    let docker = connect_docker(&console_printer, DockerConfig::default()).await?;

    // Phase 8: Create channels
    let (event_tx, _) = tokio::sync::broadcast::channel::<MossEvent>(100);
    let shutdown_tx = Arc::new(tokio::sync::Notify::new());

    // Phase 9: Capabilities loading
    let capabilities_arc = init_capabilities(&stone_id, &stone_name, &console_printer).await;

    // Phase 10: Build AppState
    let state = AppState {
        stone_id: stone_id.clone(),
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

    // Phase 11: Start background tasks
    start_discovery_listener(stone_id.clone(), stone_name.clone(), api_endpoint.clone(), &console_printer).await;
    start_hardware_detection(stone_name.clone(), capabilities_arc.clone(), console_printer.clone(), state.clone());
    start_registry_loader(state.clone());
    start_catalog_builder(state.clone(), console_printer.clone());
    start_manifest_loader(state.clone(), console_printer.clone());

    // Phase 12: Pre-install manifest handling
    start_preinstall_handler(&state).await;

    // Phase 13: Health monitoring and auto-adoption
    start_health_monitor(state.clone());
    if let Some(cfg) = config.file_config.clone() {
        start_auto_adoption(state.clone(), cfg, &console_printer);
    }

    // Phase 14: HTTP server
    tracing::info!("Setting up HTTP router with 200 MB body limit");
    let app = router::configure(state.clone());
    let listener = bind_server(port, &console_printer).await?;

    run_server(
        listener,
        app,
        &api_endpoint,
        console_printer,
        shutdown_tx,
        ServerConfig::default(),
    ).await
}

/// Start first-boot initialization task (Linux only)
///
/// Waits for filesystem to become writable, then runs initialization.
/// Exits process after completion so systemd restarts with new config.
fn start_first_boot_task(stone_name: &str, port: u16, retry_delay_secs: u64) {
    tracing::info!("First run detected on Linux, spawning background initialization task");
    tracing::info!("First boot detected - will initialize console after Docker connection");

    let init_stone_name = stone_name.to_string();
    let init_port = port;

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

/// Emit startup console events
fn emit_startup_events(console_printer: &console::ConsolePrinter, config: &DaemonConfig) {
    console_printer.emit(console::ConsoleEvent::new(
        console::EventCategory::System,
        console::EventStatus::Starting,
        format!("Moss v{}", version_string())
    ));

    if config.file_config.is_some() {
        console_printer.emit(console::ConsoleEvent::new(
            console::EventCategory::Config,
            console::EventStatus::Loaded,
            "Configuration file".to_string()
        ));

        console_printer.emit(console::ConsoleEvent::new(
            console::EventCategory::Config,
            console::EventStatus::Merged,
            "Priority: CLI > Env > Config > Defaults".to_string()
        ));
    } else {
        console_printer.emit(console::ConsoleEvent::new(
            console::EventCategory::Config,
            console::EventStatus::NotFound,
            "Using defaults".to_string()
        ));
    }
}

/// Handle pre-install manifest on first boot
///
/// Validates offerings, creates installation job, and spawns background task.
async fn start_preinstall_handler(state: &AppState) {
    let manifest = match load_preinstall_manifest().await {
        Some(m) if m.auto_install => m,
        _ => return,
    };

    tracing::info!(
        "Starting auto-installation of {} services from manifest",
        manifest.offerings.len()
    );

    // Validate all offerings exist before creating job
    let invalid_offerings: Vec<_> = manifest.offerings.iter()
        .filter(|o| state.templates.load(o).is_err())
        .cloned()
        .collect();

    if !invalid_offerings.is_empty() {
        tracing::error!(
            offerings = ?invalid_offerings,
            "Pre-install manifest contains invalid offerings - skipping auto-install"
        );
        return;
    }

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
                    _ => continue,
                }
            } else {
                break;
            }
        }
    });

    tracing::info!("Pre-install job started: {} (check /api/jobs/{})", job_id, job_id);
}
