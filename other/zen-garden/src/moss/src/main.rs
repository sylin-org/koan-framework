// Import types and functions from the library (lib.rs)
use garden_moss::{
    AppState, MossEvent, Job, JobStatus,
    // Task functions
    auto_adoption_task,
    install_batch_task,
    health_monitor_task,
    detect_capabilities_background,
    lantern_registration_loop,
    // Bootstrap functions
    load_preinstall_manifest,
    run_first_boot_initialization,
    // Domain functions
    adopt_existing_containers,
    ensure_offerings_index,
    // Infra functions
    infra,
};
use garden_moss::infra::{
    MossConfig,
    get_local_ip,
    kill_existing_moss_processes_graceful,
};
#[cfg(target_os = "windows")]
use garden_moss::infra::{install_windows_service, finalize_service_update, cleanup_after_service_update};

// All modules are now part of the library (lib.rs)
use garden_moss::{console, docker, templates, api, discovery, mdns};
use docker::DockerManager;
use templates::TemplateLoader;

use axum::{
    routing::{get, post},
    Router,
};
use std::collections::HashMap;
use std::net::SocketAddr;
use std::sync::Arc;
use tokio::sync::RwLock;
use tracing_subscriber::EnvFilter;
use garden_common::{
    CpuCapabilities, DetectionStatus,
    HardwareCapabilities, HardwareInventory, ServiceHealthStatus,
    MemoryCapabilities, RuntimeInfo,
    ServiceStatus,
};

// error_response() and error_response_value() removed - use garden_moss::error_response
// MossConfig moved to infra/config.rs

// All compatibility types and functions extracted to domain/compatibility.rs
// - CompatCheckCapabilities, CompatibilityDecision, CompiledCompatibility
// - get_current_compat_capabilities(), compile_compatibility(), evaluate_compatibility()
// - validate_binary_architecture()
// CompiledOffering, OfferingsFingerprint, OfferingsIndexCache now imported from library

// Removed duplicate persistence functions - use centralized versions:
// - load_registry_from_disk() → garden_moss::infra::load_registry()
// - persist_registry_to_disk() → garden_moss::persist_registry_to_disk()
// - persist_registry_state() → garden_moss::persist_registry_state()
// - PreInstallManifest → garden_moss::PreInstallManifest

// adopt_offering_container and adopt_existing_containers extracted to domain/adoption.rs
// JobStatus, Job, MossEvent, AppState now imported from library

fn moss_version_string() -> String {
    // build.rs injects BUILD_NUMBER (see src/moss/src/discovery.rs)
    format!("{}.{}", env!("CARGO_PKG_VERSION"), env!("BUILD_NUMBER"))
}

// All compatibility and offerings utility functions removed - now in domain modules:
// - blake3_hex(), current_capabilities_hash(), templates_hash() → Would be in domain/offerings if needed
// - compile_compatibility(), evaluate_compatibility() → domain/compatibility.rs
// - validate_binary_architecture() → domain/compatibility.rs
// - rebuild_offerings_index() → Would be in domain/offerings if needed
// All HTTP handlers removed - now in api/v1/:
// - health check functions → domain/health.rs + api/v1/health.rs
// - capabilities() → api/v1/capabilities.rs
// - get_metrics() → api/v1/metrics.rs
// RefreshPayload and refresh_component() extracted to legacy_helpers - use garden_moss::{RefreshPayload, refresh_component}

// reconcile_now and ReconcileRequest extracted to domain/reconciliation.rs (reconcile_services)
// Used by api/v1/services.rs::reconcile_inventory_v1

// get_job_status and list_jobs extracted to api/v1/jobs.rs

// install_service_task and install_batch_task extracted to tasks/job_executors.rs
// health_monitor_task extracted to tasks/health_monitor.rs
// load_preinstall_manifest extracted to bootstrap/preinstall.rs

#[derive(clap::Parser)]
#[command(name = "garden-moss")]
#[command(about = "Zen Garden Moss - Service orchestration daemon")]
#[command(version = concat!(env!("CARGO_PKG_VERSION"), ".", env!("BUILD_NUMBER")))]
struct Cli {
    #[command(subcommand)]
    command: Option<Commands>,
    
    /// Stone name identifier
    /// Priority: CLI arg > STONE_NAME env var > config file > default
    #[arg(long, env = "STONE_NAME")]
    stone_name: Option<String>,
    
    /// HTTP server port
    /// Priority: CLI arg > PORT env var > config file > default (7185)
    #[arg(long, env = "PORT")]
    port: Option<u16>,
    
    /// Log level (trace, debug, info, warn, error)
    /// Priority: CLI arg > RUST_LOG env var > config file > default (info)
    #[arg(long, env = "RUST_LOG")]
    log_level: Option<String>,
    
    /// Fast sync timeout in seconds for rapid offering deployments
    /// Priority: CLI arg > FAST_SYNC_TIMEOUT env var > config file > default (disabled)
    #[arg(long, env = "FAST_SYNC_TIMEOUT")]
    fast_sync_timeout: Option<u64>,
    
    /// Force start by killing existing moss processes
    #[arg(long)]
    force: bool,
    
    /// Internal: Finalize update by replacing old binary (used during self-update)
    #[arg(long, hide = true)]
    update_finalize: bool,
    
    /// Internal: Cleanup old binary after update (used during self-update)
    #[arg(long, hide = true)]
    cleanup_old: bool,
}

#[derive(clap::Subcommand)]
enum Commands {
    /// Install moss as a system service and start it (Zen: take-root)
    #[cfg(target_os = "windows")]
    TakeRoot,
    
    /// Install moss as a system service and start it (Normative: install-service)
    #[cfg(target_os = "windows")]
    #[command(name = "install-service")]
    InstallService,
}

// run_first_boot_initialization() extracted to bootstrap/first_boot.rs

// Process management functions extracted to infra/process.rs:
// - kill_existing_moss_processes_graceful()
// - check_moss_processes_exist()
// - kill_existing_moss_processes()

// load_capabilities_cache() and save_capabilities_cache() extracted to infra/hardware - use garden_moss::infra::{load_cached_capabilities, save_capabilities_cache}

// Windows service functions extracted to infra/service.rs:
// - install_windows_service() (was take_root_windows)
// - finalize_service_update() (was finalize_windows_update)
// - cleanup_after_service_update() (was cleanup_after_update)

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
    
    // Load configuration from file first (lowest priority)
    let config = MossConfig::load();
    
    // CLI already parsed above for special modes, reuse it
    
    // Merge configuration with priority: CLI > Env > Config File > Defaults
    // Note: clap already merges CLI args with env vars, so we only need to fill in from config file
    let log_level = cli.log_level
        .or_else(|| config.as_ref().and_then(|c| c.log_level.clone()))
        .unwrap_or_else(|| "info".to_string());

    // Stone identity:
    // - The network-visible name is the system hostname (mDNS: <hostname>.local).
    // - Historically the systemd unit set STONE_NAME, which can drift after first-boot rename.
    //
    // Priority: explicit CLI flag (--stone-name) > config file > system hostname > STONE_NAME env > default
    let env_stone_name = std::env::var(garden_common::ENV_STONE_NAME).ok();
    let explicit_cli_stone_name = if cli.stone_name.is_some() && env_stone_name.is_none() {
        cli.stone_name.clone()
    } else {
        None
    };

    let system_hostname = console::get_hostname().await.ok();
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
        .or_else(|| system_hostname.clone())
        .or_else(|| env_stone_name.clone())
        .unwrap_or_else(|| garden_common::DEFAULT_STONE_NAME.to_string());
    
    let port = cli.port
        .or_else(|| config.as_ref().and_then(|c| c.port))
        .unwrap_or(garden_common::ports::MOSS_HTTP);
    
    let fast_sync_timeout = cli.fast_sync_timeout
        .or_else(|| config.as_ref().and_then(|c| c.fast_sync_timeout));
    
    // Determine console mode early for tracing level adjustment
    let console_mode = config.as_ref()
        .and_then(|c| c.console_mode.as_ref())
        .and_then(|mode_str| mode_str.parse::<console::ConsoleMode>().ok())
        .unwrap_or_else(|| console::detect_platform_console_mode());
    
    // Adjust tracing level based on console mode to avoid duplication with console events
    // verbose mode: keep INFO for debugging
    // all other modes: suppress to WARN to avoid spam (console events handle the rest)
    let default_tracing_level = match console_mode {
        console::ConsoleMode::Verbose => "info",
        _ => "warn",  // Suppress INFO logs when console events are active
    };
    
    // Initialize logging with merged log level
    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| EnvFilter::new(default_tracing_level))
        )
        .init();
    
    // Legacy structured log (keep for debugging until full migration)
    tracing::info!(
        stone_name = %stone_name,
        port = port,
        log_level = %log_level,
        fast_sync_timeout = ?fast_sync_timeout,
        config_loaded = config.is_some(),
        "Moss daemon starting with merged configuration (priority: CLI > Env > Config > Defaults)"
    );
    
    // Spawn first-boot initialization as background task if needed (Linux only)
    // Windows/dev environments don't need hostname/hosts/avahi setup
    if cfg!(target_os = "linux") && console::is_first_run() {
        tracing::info!("First run detected on Linux, spawning background initialization task");
        
        // Emit first-boot event (will create console later in initialization)
        tracing::info!("First boot detected - will initialize console after Docker connection");
        
        let init_stone_name = stone_name.clone();
        let init_port = port;
        let retry_delay_secs = config.as_ref().map(|c| c.docker_retry_delay_secs()).unwrap_or(3);
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

    // Prefer explicit STONE_HOST, otherwise auto-detect network IP
    let api_endpoint = {
        if let Ok(host) = std::env::var(garden_common::ENV_STONE_HOST) {
            let trimmed = host.trim();
            if !trimmed.is_empty() {
                format!("http://{}:{}", trimmed, port)
            } else {
                format!("http://{}:{}", get_local_ip(), port)
            }
        } else {
            // Auto-detect local network IP for UDP discovery responses
            format!("http://{}:{}", get_local_ip(), port)
        }
    };

    // Start mDNS announcer (Linux only)
    let _mdns = match mdns::announce_moss(&stone_name, port) {
        Ok(daemon) => Some(daemon),
        Err(e) => {
            tracing::warn!(error = ?e, "mDNS announcement failed");
            None
        }
    };

    // Spawn Lantern registration loop (if LANTERN_ENDPOINT is set)
    if let Ok(lantern_endpoint) = std::env::var(garden_common::ENV_LANTERN_ENDPOINT) {
        let trimmed = lantern_endpoint.trim().to_string();
        if !trimmed.is_empty() {
            let reg_stone_name = stone_name.clone();
            let reg_endpoint = api_endpoint.clone();
            tokio::spawn(async move {
                if let Err(e) = lantern_registration_loop(reg_stone_name, reg_endpoint, trimmed).await {
                    tracing::error!(error = ?e, "Lantern registration loop failed");
                }
            });
        }
    }

    // Initialize console printer early for Docker connection events
    // Use console_mode from config if available, otherwise detect from platform
    let console_mode = config.as_ref()
        .and_then(|c| c.console_mode.as_ref())
        .and_then(|mode_str| mode_str.parse::<console::ConsoleMode>().ok())
        .unwrap_or_else(|| console::detect_platform_console_mode());
    let dedup_ttl = config.as_ref().map(|c| c.event_dedup_ttl_secs()).unwrap_or(10);
    let console_printer = Arc::new(console::ConsolePrinter::with_dedup_ttl(console_mode, dedup_ttl));
    
    // Emit startup event
    console_printer.emit(console::ConsoleEvent::new(
        console::EventCategory::System,
        console::EventStatus::Starting,
        format!("Moss v{}", moss_version_string())
    ));
    
    // Emit config loading event (config was loaded earlier before console was available)
    if config.is_some() {
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
        // Config file not found or parse error - emit appropriate event
        // (We can't distinguish between not found vs parse error at this point,
        // but NotFound is more common so we use that)
        console_printer.emit(console::ConsoleEvent::new(
            console::EventCategory::Config,
            console::EventStatus::NotFound,
            "Using defaults".to_string()
        ));
    }

    // Wait for Docker to be ready (with retries for fresh installs)
    let docker = {
        let max_retries = 30; // 30 attempts = ~60 seconds
        let mut retries = 0;
        loop {
            match DockerManager::new() {
                Ok(dm) => {
                    tracing::info!("Docker daemon connected successfully");
                    
                    // Emit Docker connected event
                    console_printer.emit(console::ConsoleEvent::new(
                        console::EventCategory::Docker,
                        console::EventStatus::Connected,
                        "Docker daemon".to_string()
                    ));
                    
                    break Arc::new(dm);
                }
                Err(e) if retries < max_retries => {
                    retries += 1;
                    
                    // Emit retry event (deduplicator will handle spam - retries every 2s)
                    console_printer.emit(console::ConsoleEvent::new(
                        console::EventCategory::Docker,
                        console::EventStatus::Retry,
                        format!("Attempt {}/{}", retries, max_retries)
                    ));
                    
                    // Legacy tracing (keep during migration)
                    tracing::warn!(
                        error = ?e,
                        retry = retries,
                        max_retries = max_retries,
                        "Docker not ready, waiting 2s before retry..."
                    );
                    tokio::time::sleep(tokio::time::Duration::from_secs(2)).await;
                }
                Err(e) => {
                    // Emit connection failure event
                    console_printer.emit(console::ConsoleEvent::new(
                        console::EventCategory::Docker,
                        console::EventStatus::Failed,
                        format!("After {} retries", max_retries)
                    ));
                    
                    tracing::error!(error = ?e, "Failed to connect to Docker daemon after {} retries", max_retries);
                    return Err(e);
                }
            }
        }
    };

    // Create event broadcast channel (capacity 100 events)
    let (event_tx, _) = tokio::sync::broadcast::channel::<MossEvent>(100);
    
    // Create shutdown notification channel
    let shutdown_tx = Arc::new(tokio::sync::Notify::new());

    // Load capabilities from disk cache (instant startup - background refresh will update)
    let cached_capabilities = infra::load_cached_capabilities().await;
    let capabilities_arc = Arc::new(RwLock::new(cached_capabilities.clone()));
    
    // If no cache exists, write skeleton immediately so endpoints have valid data
    if cached_capabilities.is_none() {
        let skeleton = HardwareCapabilities {
            stone_name: stone_name.clone(),
            hardware: HardwareInventory {
                cpu: CpuCapabilities {
                    model: None,
                    cores: 0,
                    threads: None,
                    architecture: std::env::consts::ARCH.to_string(),
                    features: None,
                },
                memory: MemoryCapabilities { total_mb: 0 },
                gpus: vec![],  // CRITICAL: Must be present, even if empty
                disk: None,
                storage: vec![],
                os_version: None,
                kernel_version: None,
                swap_mb: None,
                ai_capabilities: None,
            },
            runtime: Some(RuntimeInfo {
                docker_version: None,
                os: std::env::consts::OS.to_string(),
                kernel: None,
            }),
            detection_status: DetectionStatus::Scanning,
        };
        *capabilities_arc.write().await = Some(skeleton.clone());
        let _ = infra::save_capabilities_cache(&skeleton).await;
    } else {
        console_printer.emit(console::ConsoleEvent::new(
            console::EventCategory::System,
            console::EventStatus::Loaded,
            "Hardware capabilities".to_string()
        ));
    }

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
    };
    
    // Start singleton UDP discovery listener IMMEDIATELY (before any blocking operations)
    // This ensures stones respond to discovery as soon as moss starts, even during initialization
    let discovery_stone_name = stone_name.clone();
    let discovery_endpoint = api_endpoint.clone();
    match discovery::ensure_udp_listener(
        discovery_stone_name,
        discovery_endpoint,
    )
    .await
    {
        Ok(receiver) => {
            // Spawn discovery event monitor (consumes from broadcast pipeline)
            let mut discovery_rx = receiver;
            tokio::spawn(async move {
                while let Ok(event) = discovery_rx.recv().await {
                    tracing::debug!(
                        request_id = %event.request.request_id,
                        from = %event.from_addr,
                        "Discovery request received via broadcast"
                    );
                    // Future: could emit metrics, update dashboards, log analytics, etc.
                }
                tracing::info!("Discovery event monitor stopped");
            });
            
            console_printer.emit(console::ConsoleEvent::new(
                console::EventCategory::Network,
                console::EventStatus::Started,
                format!("UDP discovery on port {}", garden_common::ports::DISCOVERY_UDP)
            ));
        }
        Err(e) => {
            tracing::error!(error = ?e, "Failed to start UDP discovery listener");
            console_printer.emit(console::ConsoleEvent::new(
                console::EventCategory::Network,
                console::EventStatus::Failed,
                format!("UDP discovery: {}", e)
            ));
        }
    }
    
    // Start background hardware detection (progressive: CPU fast, GPU slow)
    let bg_stone_name = stone_name.clone();
    let bg_caps = capabilities_arc.clone();
    let bg_console = console_printer.clone();
    let bg_state = state.clone();
    tokio::spawn(async move {
        bg_console.emit(console::ConsoleEvent::new(
            console::EventCategory::System,
            console::EventStatus::Scanning,
            "Hardware capabilities".to_string()
        ));
        
        // Progressive detection handles its own console updates
        detect_capabilities_background(bg_stone_name.clone(), bg_caps.clone(), bg_console.clone(), bg_state).await;
        
        bg_console.emit(console::ConsoleEvent::new(
            console::EventCategory::System,
            console::EventStatus::Updated,
            "Hardware capabilities (complete)".to_string()
        ));
    });

    // Load persisted registry state and adopt containers in background (non-blocking)
    let registry_state = state.clone();
    tokio::spawn(async move {
        // Load persisted registry state (best-effort)
        match infra::load_registry().await {
            Ok(mut loaded) => {
                // Reconcile: if the container no longer exists, mark it offline rather than dropping.
                for svc in loaded.iter_mut() {
                    if !registry_state.docker.zen_container_exists(&svc.name).await.unwrap_or(false) {
                        svc.status = ServiceStatus::Stopped;
                        svc.health = ServiceHealthStatus::Offline;
                    }
                }

                *registry_state.registry.write().await = loaded;
            }
            Err(e) => {
                tracing::warn!(error = ?e, "Failed to load persisted moss registry; starting empty");
            }
        }

        // Startup self-heal: adopt any existing zen-offering containers into the registry
        adopt_existing_containers(&registry_state).await;
    });

    // Build offerings index in background (non-blocking)
    let catalog_state = state.clone();
    let catalog_console = console_printer.clone();
    tokio::spawn(async move {
        tracing::info!("Building offerings catalog...");
        
        // Emit console event for manifest scanning
        catalog_console.emit(console::ConsoleEvent::new(
            console::EventCategory::Manifests,
            console::EventStatus::Scanning,
            "Runtime templates".to_string()
        ));
        
        match ensure_offerings_index(&catalog_state, false).await {
            Ok(_) => {
                let idx_guard = catalog_state.offerings_index.read().await;
                if let Some(idx) = idx_guard.as_ref() {
                    tracing::info!(
                        offerings_count = idx.offerings.len(),
                        "Offerings catalog loaded successfully"
                    );
                    
                    // Emit console event for successful manifest loading
                    catalog_console.emit(console::ConsoleEvent::new(
                        console::EventCategory::Manifests,
                        console::EventStatus::Loaded,
                        format!("{} manifests", idx.offerings.len())
                    ));
                }
            }
            Err(e) => {
                tracing::warn!(error = ?e, "Failed to build offerings catalog - API will return empty results");
                
                // Emit console event for manifest loading error
                catalog_console.emit(console::ConsoleEvent::new(
                    console::EventCategory::Manifests,
                    console::EventStatus::Invalid,
                    "Catalog build failed".to_string()
                ));
            }
        }
    });

    // Load offering manifests (for multi-mode offerings)
    let manifest_state = state.clone();
    let manifest_console = console_printer.clone();
    tokio::spawn(async move {
        tracing::info!("Loading offering manifests...");

        manifest_console.emit(console::ConsoleEvent::new(
            console::EventCategory::Manifests,
            console::EventStatus::Scanning,
            "Offering manifests",
        ));

        match crate::infra::load_offerings(crate::infra::default_offerings_dir()).await {
            Ok(manifests) => {
                let count = manifests.len();
                {
                    let mut guard = manifest_state.manifests.write().await;
                    *guard = manifests;
                }

                tracing::info!(count, "Offering manifests loaded successfully");

                manifest_console.emit(console::ConsoleEvent::new(
                    console::EventCategory::Manifests,
                    console::EventStatus::Loaded,
                    format!("{} offerings", count),
                ));
            }
            Err(e) => {
                tracing::warn!(error = ?e, "Failed to load offering manifests");

                manifest_console.emit(console::ConsoleEvent::new(
                    console::EventCategory::Manifests,
                    console::EventStatus::Invalid,
                    "Manifest load failed",
                ));
            }
        }
    });

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

    // Spawn health monitoring background task
    let health_state = state.clone();
    tokio::spawn(async move {
        health_monitor_task(health_state).await;
    });

    // Spawn auto-adoption background task (if enabled)
    let adoption_state = state.clone();
    let adoption_config = config.as_ref().map(|c| c.adoption()).unwrap_or_else(|| infra::MossConfig {
        stone_name: None,
        port: None,
        log_level: None,
        fast_sync_timeout: None,
        console_mode: None,
        event_dedup_ttl_secs: None,
        docker_retry_delay_secs: None,
        health_check_interval_secs: None,
        docker_reconnect_interval_secs: None,
        http_capabilities_timeout_secs: None,
        http_health_timeout_secs: None,
        http_quick_health_timeout_millis: None,
        http_long_operation_timeout_secs: None,
        adoption: None,
    }.adoption());

    if adoption_config.is_enabled() {
        tracing::info!("Auto-adoption enabled, starting adoption background task");
        console_printer.emit(console::ConsoleEvent::new(
            console::EventCategory::Config,
            console::EventStatus::Loaded,
            "Auto-adoption enabled",
        ));

        tokio::spawn(async move {
            auto_adoption_task(adoption_state, adoption_config).await;
        });
    } else {
        tracing::info!("Auto-adoption disabled (deployment profile or configuration)");
        console_printer.emit(console::ConsoleEvent::new(
            console::EventCategory::Config,
            console::EventStatus::Loaded,
            "Auto-adoption disabled",
        ));
    }

    tracing::info!("Setting up HTTP router with 200 MB body limit");
    let app = Router::new()
        // Standard health/monitoring endpoints (root level)
        .route("/health", get(api::v1::health::get_health))
        .route("/capabilities", get(api::v1::capabilities::get_capabilities))
        .route("/metrics", get(api::v1::metrics::get_metrics))
        
        // V1 API - Offerings (Human Layer)
        .route("/api/v1/offerings", get(api::v1::offerings::list_offerings_v1))
        .route("/api/v1/offerings", post(api::v1::offerings::plant_offering_v1))
        .route("/api/v1/offerings/:name", get(api::v1::offerings::get_offering_v1))
        .route("/api/v1/offerings/:name", axum::routing::delete(api::v1::offerings::take_away_offering_v1))
        .route("/api/v1/offerings/:name/manifest", get(api::v1::offerings::get_offering_manifest_v1))
        .route("/api/v1/offerings/heal", post(api::v1::offerings::heal_garden_v1))
        .route("/api/v1/offerings/refresh", post(api::v1::offerings::refresh_catalog_v1))

        // V1 API - Adoption (Multi-mode offerings)
        .route("/api/v1/offerings/adoptable", get(api::v1::adoption::list_adoptable_v1))
        .route("/api/v1/offerings/adopted", get(api::v1::adoption::list_adopted_v1))
        .route("/api/v1/offerings/borrowed", get(api::v1::adoption::list_borrowed_v1))
        .route("/api/v1/offerings/:offering/adopt", post(api::v1::adoption::adopt_offering_v1))
        .route("/api/v1/offerings/:offering/adopt", axum::routing::delete(api::v1::adoption::unadopt_offering_v1))
        .route("/api/v1/adoption/borrow", post(api::v1::adoption::borrow_service_v1))
        .route("/api/v1/adoption/borrow/:name", axum::routing::delete(api::v1::adoption::unborrow_service_v1))

        // V1 API - Services (Technical Layer)
        .route("/api/v1/services/manifests", get(api::v1::services::list_manifests_v1))
        .route("/api/v1/services/:name/manifest", get(api::v1::services::get_manifest_v1))
        .route("/api/v1/services", get(api::v1::services::list_services_v1))
        .route("/api/v1/services", post(api::v1::services::create_service_v1))
        .route("/api/v1/services/:service", get(api::v1::services::get_service_v1))
        .route("/api/v1/services/:service", axum::routing::delete(api::v1::services::delete_service_v1))
        .route("/api/v1/services/:service/logs", get(api::v1::services::stream_service_logs_v1))
        .route("/api/v1/services/:service/restart", post(api::v1::services::restart_service_v1))
        .route("/api/v1/services/:service/rest", post(api::v1::services::rest_service_v1))
        .route("/api/v1/services/:service/wake", post(api::v1::services::wake_service_v1))
        .route("/api/v1/services/:service/nourish", post(api::v1::services::nourish_service_v1))
        .route("/api/v1/services/:service/destroy", post(api::v1::services::destroy_service_v1))
        .route("/api/v1/services/:service/cordon", post(api::v1::services::cordon_service_v1))
        .route("/api/v1/services/reconcile", post(api::v1::services::reconcile_inventory_v1))
        .route("/api/v1/services/refresh", post(api::v1::services::refresh_manifests_v1))
        
        // V1 API - Stone operations
        .route("/api/v1/stone/upgrade", post(api::v1::stone::upgrade_stone_v1))
        .route("/api/v1/stone/shutdown", post(api::v1::stone::shutdown_stone_v1))
        
        // V1 API - Events & Jobs
        .route("/api/v1/events", get(api::v1::events::stream_events))
        .route("/api/v1/jobs", get(api::v1::jobs::list_jobs))
        .route("/api/v1/jobs/:job_id", get(api::v1::jobs::get_job_status))
        
        // V1 API - Garden topology
        .route("/api/v1/garden", get(api::v1::garden::get_garden_v1))
        .route("/api/v1/garden/stones/:stone_name", get(api::v1::garden::get_stone_v1))
        .route("/api/v1/stone", get(api::v1::garden::get_local_stone_v1))
        
        // V1 API - Pond security
        .route("/api/v1/pond/init", post(api::v1::pond::pond_init_v1))
        .route("/api/v1/pond", axum::routing::delete(api::v1::pond::pond_remove_v1))
        .route("/api/v1/pond/invite", post(api::v1::pond::pond_invite_v1))
        .route("/api/v1/pond/join", post(api::v1::pond::pond_join_v1))
        .route("/api/v1/pond/stones/:stone_name", axum::routing::delete(api::v1::pond::pond_untrust_v1))
        .route("/api/v1/pond/status", get(api::v1::pond::pond_status_v1))
        
        // V1 API - Console control
        .route("/api/v1/console/mode", get(api::v1::console::get_console_mode_v1))
        .route("/api/v1/console/mode", post(api::v1::console::set_console_mode_v1))
        
        // Admin endpoints
        .route("/admin/take-root", post(api::v1::admin::admin_take_root))
        
        // Apply 200 MB body limit to all routes
        .layer(axum::extract::DefaultBodyLimit::max(200 * 1024 * 1024))
        
        .with_state(state.clone());

    let addr: SocketAddr = format!("0.0.0.0:{}", port).parse()?;
    let listener = match tokio::net::TcpListener::bind(addr).await {
        Ok(listener) => listener,
        Err(e) => {
            let error_msg = if e.kind() == std::io::ErrorKind::AddrInUse {
                format!(
                    "Port {} is already in use. Another garden-moss instance may be running.\n\
                    Try: Stop-Process -Name garden-moss -Force\n\
                    Or use a different port: garden-moss --port <port>",
                    port
                )
            } else {
                format!(
                    "Failed to bind HTTP server to {}:{}: {}\n\
                    Check firewall settings and ensure the port is available.",
                    addr.ip(), addr.port(), e
                )
            };
            
            state.console.emit(console::ConsoleEvent::new(
                console::EventCategory::System,
                console::EventStatus::Failed,
                error_msg.clone()
            ));
            
            anyhow::bail!(error_msg);
        }
    };
    
    tracing::info!(
        ?addr,
        api_endpoint = %api_endpoint,
        body_limit_mb = 200,
        "Moss HTTP server ready with 200 MB body limit configured"
    );
    
    // Emit HTTP server ready event
    state.console.emit(console::ConsoleEvent::new(
        console::EventCategory::System,
        console::EventStatus::Ready,
        format!("HTTP server → {}", api_endpoint)
    ));
    
    // Create server with graceful shutdown
    let server = axum::serve(listener, app)
        .with_graceful_shutdown(async move {
            shutdown_signal().await;
            tracing::info!("Shutdown signal received, initiating graceful shutdown");
            
            // Emit shutdown event (note: console_printer needs to be cloned earlier)
            // This will be added when we refactor shutdown signal handling
        });
    
    // Run server with shutdown coordination
    tokio::select! {
        result = server => {
            if let Err(e) = result {
                tracing::error!(error = ?e, "Server error");
                return Err(e.into());
            }
        }
        _ = shutdown_tx.notified() => {
            tracing::info!("Admin shutdown requested");
            
            // Emit shutdown event
            state.console.emit(console::ConsoleEvent::new(
                console::EventCategory::System,
                console::EventStatus::Shutting,
                "Admin requested".to_string()
            ));
        }
    }
    
    // Allow in-flight requests to complete (5s timeout)
    tracing::info!("Waiting up to 5s for in-flight requests to complete");
    
    // Emit draining event
    state.console.emit(console::ConsoleEvent::new(
        console::EventCategory::System,
        console::EventStatus::Draining,
        "In-flight requests".to_string()
    ));
    tokio::time::sleep(tokio::time::Duration::from_secs(5)).await;
    
    tracing::info!("Moss daemon shutdown complete");
    
    // Emit stopped event
    state.console.emit(console::ConsoleEvent::new(
        console::EventCategory::System,
        console::EventStatus::Stopped,
        "Shutdown complete".to_string()
    ));
    
    Ok(())
}

/// POST /admin/shutdown - Trigger graceful shutdown
// admin_shutdown and admin_take_root extracted to api/v1/admin.rs

/// Cross-platform shutdown signal handler
async fn shutdown_signal() {
    #[cfg(unix)]
    {
        use tokio::signal::unix::{signal, SignalKind};
        
        let mut sigterm = signal(SignalKind::terminate())
            .expect("Failed to install SIGTERM handler");
        let mut sigint = signal(SignalKind::interrupt())
            .expect("Failed to install SIGINT handler");
        
        tokio::select! {
            _ = sigterm.recv() => {
                tracing::info!("SIGTERM received");
            }
            _ = sigint.recv() => {
                tracing::info!("SIGINT received");
            }
        }
    }
    
    #[cfg(windows)]
    {
        tokio::signal::ctrl_c()
            .await
            .expect("Failed to install Ctrl+C handler");
        tracing::info!("Ctrl+C received");
    }
}

// get_local_ip() extracted to infra/network.rs
// lantern_registration_loop() extracted to tasks/discovery.rs
