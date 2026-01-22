//! Startup initialization sequence
//!
//! Handles early daemon initialization:
//! - Docker daemon connection with retries
//! - Hardware capabilities loading/initialization
//! - Core channel creation
//!
//! Extracted from main.rs for cleaner separation of concerns.

use std::sync::Arc;
use tokio::sync::RwLock;
use garden_common::{
    CpuCapabilities, DetectionStatus, HardwareCapabilities,
    HardwareInventory, MemoryCapabilities, RuntimeInfo,
};
use crate::console::{ConsolePrinter, ConsoleEvent, EventCategory, EventStatus};
use crate::docker::DockerManager;
use crate::infra;

/// Docker connection configuration
pub struct DockerConfig {
    pub max_retries: u32,
    pub retry_delay_secs: u64,
}

impl Default for DockerConfig {
    fn default() -> Self {
        Self {
            max_retries: 30,      // 30 attempts = ~60 seconds
            retry_delay_secs: 2,
        }
    }
}

/// Connect to Docker daemon with retries
///
/// Attempts to connect to the Docker daemon, retrying on failure.
/// Emits console events for connection status.
pub async fn connect_docker(
    console: &ConsolePrinter,
    config: DockerConfig,
) -> anyhow::Result<Arc<DockerManager>> {
    let mut retries = 0;

    loop {
        match DockerManager::new() {
            Ok(dm) => {
                tracing::info!("Docker daemon connected successfully");

                console.emit(ConsoleEvent::new(
                    EventCategory::Docker,
                    EventStatus::Connected,
                    "Docker daemon".to_string()
                ));

                return Ok(Arc::new(dm));
            }
            Err(e) if retries < config.max_retries => {
                retries += 1;

                console.emit(ConsoleEvent::new(
                    EventCategory::Docker,
                    EventStatus::Retry,
                    format!("Attempt {}/{}", retries, config.max_retries)
                ));

                tracing::warn!(
                    error = ?e,
                    retry = retries,
                    max_retries = config.max_retries,
                    "Docker not ready, waiting {}s before retry...",
                    config.retry_delay_secs
                );
                tokio::time::sleep(tokio::time::Duration::from_secs(config.retry_delay_secs)).await;
            }
            Err(e) => {
                console.emit(ConsoleEvent::new(
                    EventCategory::Docker,
                    EventStatus::Failed,
                    format!("After {} retries", config.max_retries)
                ));

                tracing::error!(error = ?e, "Failed to connect to Docker daemon after {} retries", config.max_retries);
                return Err(e);
            }
        }
    }
}

/// Initialize hardware capabilities
///
/// Loads cached capabilities from disk, or creates a skeleton if no cache exists.
/// Returns the capabilities wrapped in Arc<RwLock> for shared access.
pub async fn init_capabilities(
    stone_id: &str,
    stone_name: &str,
    console: &ConsolePrinter,
) -> Arc<RwLock<Option<HardwareCapabilities>>> {
    let mut cached = infra::load_cached_capabilities().await;
    let mut needs_save = false;

    // Update stone_id and stone_name if they have changed
    // This fixes stale values from before first-boot initialization
    if let Some(ref mut caps) = cached {
        // Update stone_id if missing or different
        if caps.stone_id.as_deref() != Some(stone_id) {
            tracing::info!(
                old_id = ?caps.stone_id,
                new_id = %stone_id,
                "Stone ID updated in cached capabilities"
            );
            caps.stone_id = Some(stone_id.to_string());
            needs_save = true;
        }

        // Update stone_name if changed (e.g., after hostname was set during first boot)
        if caps.stone_name != stone_name {
            tracing::info!(
                old_name = %caps.stone_name,
                new_name = %stone_name,
                "Stone name changed - updating cached capabilities"
            );
            caps.stone_name = stone_name.to_string();
            needs_save = true;
        }

        if needs_save {
            let _ = infra::save_capabilities_cache(caps).await;
        }
    }

    let capabilities = Arc::new(RwLock::new(cached.clone()));

    if cached.is_none() {
        // Create skeleton for immediate API availability
        let skeleton = create_capabilities_skeleton(stone_id, stone_name);
        *capabilities.write().await = Some(skeleton.clone());
        let _ = infra::save_capabilities_cache(&skeleton).await;

        tracing::info!("Created capabilities skeleton (background detection will update)");
    } else {
        console.emit(ConsoleEvent::new(
            EventCategory::System,
            EventStatus::Loaded,
            "Hardware capabilities".to_string()
        ));
    }

    capabilities
}

/// Create a minimal capabilities skeleton for immediate API availability
fn create_capabilities_skeleton(stone_id: &str, stone_name: &str) -> HardwareCapabilities {
    HardwareCapabilities {
        stone_id: Some(stone_id.to_string()),
        stone_name: stone_name.to_string(),
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
    }
}
