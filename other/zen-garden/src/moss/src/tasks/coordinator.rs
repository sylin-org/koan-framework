//! Background task coordination
//!
//! Orchestrates all background tasks that run during daemon operation:
//! - UDP discovery listener
//! - Hardware capability detection
//! - Registry loading and container adoption
//! - Offerings catalog building
//! - Manifest loading
//! - Health monitoring
//! - Auto-adoption
//! - Lantern registration and network event handling
//!
//! Extracted from main.rs for cleaner separation of concerns.

use std::sync::Arc;
use tokio::sync::RwLock;
use garden_common::{HardwareCapabilities, ServiceHealthStatus, ServiceStatus};
use crate::console::{ConsolePrinter, ConsoleEvent, EventCategory, EventStatus};
use crate::discovery::UdpEvent;
use crate::domain::topology::{TopologyCache, upsert_from_chirp, mark_stone_offline};
use crate::{
    AppState,
    adopt_existing_containers, ensure_offerings_index,
    detect_capabilities_background, health_monitor_task, auto_adoption_task,
    lantern_registration_loop,
    discovery, infra,
};
use crate::tasks::network_monitor::{NetworkMonitor, NetworkEvent};

/// Start UDP discovery listener with topology cache integration
///
/// Enables stone discovery via UDP broadcast.
/// Handles discovery requests (chirp response), stone chirps (topology updates), and goodbyes.
/// Returns immediately after spawning the listener.
pub async fn start_discovery_listener(
    stone_id: String,
    stone_name: String,
    api_endpoint: String,
    topology_cache: TopologyCache,
    self_entry: Arc<RwLock<crate::domain::TopologyEntry>>,
    console: &ConsolePrinter,
) {
    match discovery::ensure_udp_listener(stone_id, stone_name, api_endpoint).await {
        Ok(receiver) => {
            // Spawn UDP event monitor that handles both requests and chirps
            let mut udp_rx = receiver;
            tokio::spawn(async move {
                while let Ok(event) = udp_rx.recv().await {
                    match event {
                        UdpEvent::Request { request, from_addr } => {
                            tracing::debug!(
                                request_id = %request.request_id,
                                from = %from_addr,
                                "Discovery request received, responding with self entry chirp"
                            );
                            
                            // Respond to discovery request by chirping our current self entry
                            let entry = self_entry.read().await.clone();
                            if let Err(e) = crate::announcement::announce(&entry).await {
                                tracing::warn!(
                                    error = ?e,
                                    request_id = %request.request_id,
                                    "Failed to respond to discovery request"
                                );
                            }
                        }
                        UdpEvent::Chirp { chirp, from_addr } => {
                            tracing::debug!(
                                stone = %chirp.stone_name,
                                services = chirp.services.len(),
                                mac = ?chirp.mac,
                                health = %chirp.health,
                                from = %from_addr,
                                "Stone chirp received, updating topology cache"
                            );
                            // Update topology cache with chirp data
                            upsert_from_chirp(&topology_cache, chirp).await;
                        }
                        UdpEvent::Goodbye { goodbye, from_addr } => {
                            tracing::info!(
                                stone = %goodbye.stone_name,
                                from = %from_addr,
                                "Stone goodbye received, marking offline"
                            );
                            // Mark stone as offline immediately (don't wait for timeout)
                            mark_stone_offline(&topology_cache, &goodbye.stone_id).await;
                        }
                    }
                }
                tracing::info!("UDP event monitor stopped");
            });

            console.emit(ConsoleEvent::new(
                EventCategory::Network,
                EventStatus::Started,
                format!("UDP listener on port {}", garden_common::ports::DISCOVERY_UDP),
            ));
        }
        Err(e) => {
            tracing::error!(error = ?e, "Failed to start UDP listener");
            console.emit(ConsoleEvent::new(
                EventCategory::Network,
                EventStatus::Failed,
                format!("UDP listener: {}", e),
            ));
        }
    }
}

/// Start background hardware detection
///
/// Progressively detects hardware capabilities (CPU fast, GPU slow).
pub fn start_hardware_detection(
    stone_name: String,
    capabilities: Arc<RwLock<Option<HardwareCapabilities>>>,
    console: Arc<ConsolePrinter>,
    state: AppState,
) {
    tokio::spawn(async move {
        console.emit(ConsoleEvent::new(
            EventCategory::System,
            EventStatus::Scanning,
            "Hardware capabilities".to_string()
        ));

        detect_capabilities_background(stone_name, capabilities, console.clone(), state).await;

        console.emit(ConsoleEvent::new(
            EventCategory::System,
            EventStatus::Updated,
            "Hardware capabilities (complete)".to_string()
        ));
    });
}

/// Start registry loading and container adoption
///
/// Loads persisted registry state and adopts any existing zen-offering containers.
pub fn start_registry_loader(state: AppState) {
    tokio::spawn(async move {
        // Load persisted registry state (best-effort)
        match infra::load_registry().await {
            Ok(mut loaded) => {
                // Reconcile: if the container no longer exists, mark it offline
                for svc in loaded.iter_mut() {
                    if !state.docker.zen_container_exists(&svc.name).await.unwrap_or(false) {
                        svc.status = ServiceStatus::Stopped;
                        svc.health = ServiceHealthStatus::Offline;
                    }
                }
                *state.registry.write().await = loaded;
            }
            Err(e) => {
                tracing::warn!(error = ?e, "Failed to load persisted moss registry; starting empty");
            }
        }

        // Startup self-heal: adopt any existing zen-offering containers
        adopt_existing_containers(&state).await;
    });
}

/// Start offerings catalog builder
///
/// Builds the offerings index from runtime templates.
pub fn start_catalog_builder(state: AppState, console: Arc<ConsolePrinter>) {
    tokio::spawn(async move {
        tracing::info!("Building offerings catalog...");

        console.emit(ConsoleEvent::new(
            EventCategory::Manifests,
            EventStatus::Scanning,
            "Runtime templates".to_string()
        ));

        match ensure_offerings_index(&state, false).await {
            Ok(_) => {
                let idx_guard = state.offerings_index.read().await;
                if let Some(idx) = idx_guard.as_ref() {
                    tracing::info!(
                        offerings_count = idx.offerings.len(),
                        "Offerings catalog loaded successfully"
                    );
                    console.emit(ConsoleEvent::new(
                        EventCategory::Manifests,
                        EventStatus::Loaded,
                        format!("{} manifests", idx.offerings.len())
                    ));
                }
            }
            Err(e) => {
                tracing::warn!(error = ?e, "Failed to build offerings catalog");
                console.emit(ConsoleEvent::new(
                    EventCategory::Manifests,
                    EventStatus::Invalid,
                    "Catalog build failed".to_string()
                ));
            }
        }
    });
}

/// Start offering manifest loader
///
/// Loads offering manifests for multi-mode offerings.
pub fn start_manifest_loader(state: AppState, console: Arc<ConsolePrinter>) {
    tokio::spawn(async move {
        tracing::info!("Loading offering manifests...");

        console.emit(ConsoleEvent::new(
            EventCategory::Manifests,
            EventStatus::Scanning,
            "Offering manifests",
        ));

        match infra::load_manifests(infra::default_manifests_dir()).await {
            Ok(manifests) => {
                let count = manifests.len();
                *state.manifests.write().await = manifests;

                tracing::info!(count, "Offering manifests loaded successfully");
                console.emit(ConsoleEvent::new(
                    EventCategory::Manifests,
                    EventStatus::Loaded,
                    format!("{} offerings", count),
                ));
            }
            Err(e) => {
                tracing::warn!(error = ?e, "Failed to load offering manifests");
                console.emit(ConsoleEvent::new(
                    EventCategory::Manifests,
                    EventStatus::Invalid,
                    "Manifest load failed",
                ));
            }
        }
    });
}

/// Start health monitoring task
pub fn start_health_monitor(state: AppState) {
    tokio::spawn(async move {
        health_monitor_task(state).await;
    });
}

/// Start auto-adoption task if enabled
pub fn start_auto_adoption(
    state: AppState,
    config: infra::MossConfig,
    console: &ConsolePrinter,
) {
    let adoption_config = config.adoption();

    if adoption_config.is_enabled() {
        tracing::info!("Auto-adoption enabled, starting adoption background task");
        console.emit(ConsoleEvent::new(
            EventCategory::Config,
            EventStatus::Loaded,
            "Auto-adoption enabled",
        ));

        tokio::spawn(async move {
            auto_adoption_task(state, adoption_config).await;
        });
    } else {
        tracing::info!("Auto-adoption disabled (deployment profile or configuration)");
        console.emit(ConsoleEvent::new(
            EventCategory::Config,
            EventStatus::Loaded,
            "Auto-adoption disabled",
        ));
    }
}

/// Start Lantern registration if LANTERN_ENDPOINT is configured
///
/// Spawns the main registration loop and (if using dynamic IP) an IP change handler
/// that triggers immediate re-registration when the network IP changes.
///
/// Console parameter is optional - pass None if console isn't available yet.
pub async fn start_lantern_registration(
    stone_id: &str,
    stone_name: &str,
    api_endpoint: &str,
    port: u16,
    use_static_host: bool,
    network_monitor: &NetworkMonitor,
    console: Option<&ConsolePrinter>,
) {
    let lantern_endpoint = match std::env::var(garden_common::ENV_LANTERN_ENDPOINT) {
        Ok(ep) => {
            let trimmed = ep.trim().to_string();
            if trimmed.is_empty() {
                return;
            }
            trimmed
        }
        Err(_) => return,
    };

    if let Some(c) = console {
        c.emit(ConsoleEvent::new(
            EventCategory::Network,
            EventStatus::Starting,
            "Lantern registration",
        ));
    }

    // Main registration loop
    let reg_stone_id = stone_id.to_string();
    let reg_stone_name = stone_name.to_string();
    let reg_endpoint = api_endpoint.to_string();
    let lantern_url = lantern_endpoint.clone();

    tokio::spawn(async move {
        if let Err(e) = lantern_registration_loop(reg_stone_id, reg_stone_name, reg_endpoint, lantern_url).await {
            tracing::error!(error = ?e, "Lantern registration loop failed");
        }
    });

    // If using dynamic IP (not STONE_HOST), spawn IP change handler
    if !use_static_host {
        let change_stone_id = stone_id.to_string();
        let change_stone_name = stone_name.to_string();
        let change_lantern = lantern_endpoint.clone();
        let change_port = port;
        let mut network_rx = network_monitor.subscribe();

        tokio::spawn(async move {
            while let Ok(event) = network_rx.recv().await {
                match event {
                    NetworkEvent::IpChanged { ref old, ref new } => {
                        let new_endpoint = format!("http://{}:{}", new, change_port);
                        tracing::info!(
                            old = %old,
                            new = %new,
                            endpoint = %new_endpoint,
                            "Network IP changed, triggering immediate Lantern re-registration"
                        );

                        // Immediate re-registration (don't wait for next heartbeat)
                        let client = reqwest::Client::new();
                        let register_url = format!("{}/api/register", change_lantern);
                        let request = garden_common::RegisterRequest {
                            stone_id: Some(change_stone_id.clone()),
                            stone_name: change_stone_name.clone(),
                            endpoint: new_endpoint,
                            services: vec![],
                        };

                        match client.post(&register_url).json(&request).send().await {
                            Ok(resp) if resp.status().is_success() => {
                                tracing::info!("Re-registered with Lantern after IP change");
                            }
                            Ok(resp) => {
                                tracing::warn!(status = ?resp.status(), "Lantern re-registration returned non-success");
                            }
                            Err(e) => {
                                tracing::warn!(error = ?e, "Failed to re-register with Lantern after IP change");
                            }
                        }
                    }
                    NetworkEvent::Reconnected { ref new } => {
                        let new_endpoint = format!("http://{}:{}", new, change_port);
                        tracing::info!(
                            new = %new,
                            endpoint = %new_endpoint,
                            "Network reconnected, triggering immediate Lantern re-registration"
                        );

                        // Immediate re-registration (don't wait for next heartbeat)
                        let client = reqwest::Client::new();
                        let register_url = format!("{}/api/register", change_lantern);
                        let request = garden_common::RegisterRequest {
                            stone_id: Some(change_stone_id.clone()),
                            stone_name: change_stone_name.clone(),
                            endpoint: new_endpoint,
                            services: vec![],
                        };

                        match client.post(&register_url).json(&request).send().await {
                            Ok(resp) if resp.status().is_success() => {
                                tracing::info!("Re-registered with Lantern after reconnect");
                            }
                            Ok(resp) => {
                                tracing::warn!(status = ?resp.status(), "Lantern re-registration returned non-success");
                            }
                            Err(e) => {
                                tracing::warn!(error = ?e, "Failed to re-register with Lantern after reconnect");
                            }
                        }
                    }
                    NetworkEvent::Disconnected { current, reason } => {
                        tracing::warn!(
                            ip = %current,
                            reason = %reason,
                            "Network disconnected, Lantern registration suspended until reconnect"
                        );
                    }
                }
            }
        });
    }

    if let Some(c) = console {
        c.emit(ConsoleEvent::new(
            EventCategory::Network,
            EventStatus::Started,
            "Lantern registration loop",
        ));
    }
}

/// Start all background tasks
///
/// Convenience function to start all standard background tasks.
/// Call this after AppState is constructed.
pub async fn start_all_background_tasks(
    state: &AppState,
    stone_name: &str,
    api_endpoint: &str,
    capabilities: Arc<RwLock<Option<HardwareCapabilities>>>,
    config: Option<infra::MossConfig>,
) {
    let console = state.console.clone();

    // Start UDP discovery (immediate - critical for stone visibility)
    start_discovery_listener(
        state.stone_id.clone(),
        stone_name.to_string(),
        api_endpoint.to_string(),
        state.topology_cache.clone(),
        state.self_entry.clone(),
        &console,
    )
    .await;

    // Start hardware detection (progressive)
    start_hardware_detection(
        stone_name.to_string(),
        capabilities,
        console.clone(),
        state.clone(),
    );

    // Start registry loading and adoption
    start_registry_loader(state.clone());

    // Start catalog building
    start_catalog_builder(state.clone(), console.clone());

    // Start manifest loading
    start_manifest_loader(state.clone(), console.clone());

    // Start health monitoring
    start_health_monitor(state.clone());

    // Start auto-adoption if configured
    if let Some(cfg) = config {
        start_auto_adoption(state.clone(), cfg, &console);
    } else {
        // No config - log that auto-adoption is disabled
        tracing::info!("No config provided, auto-adoption uses internal defaults");
        console.emit(ConsoleEvent::new(
            EventCategory::Config,
            EventStatus::Loaded,
            "Auto-adoption (no config)",
        ));
    }
}
