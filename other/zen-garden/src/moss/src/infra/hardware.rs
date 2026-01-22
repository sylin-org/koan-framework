//! Hardware detection and capabilities management
//!
//! Provides composable functions for detecting system hardware,
//! managing capabilities cache, and progressive detection.

use anyhow::Result;
use garden_common::HardwareCapabilities;
use std::path::PathBuf;

/// Load cached hardware capabilities from disk
///
/// Returns None if cache doesn't exist or is invalid.
/// This allows instant startup while background detection runs.
pub async fn load_cached_capabilities() -> Option<HardwareCapabilities> {
    let path = PathBuf::from(garden_common::names::CONFIG_DIR).join("capabilities.json");

    match tokio::fs::read_to_string(&path).await {
        Ok(content) => {
            match serde_json::from_str::<HardwareCapabilities>(&content) {
                Ok(caps) => {
                    tracing::debug!("Loaded capabilities from cache");
                    Some(caps)
                }
                Err(e) => {
                    tracing::warn!(error = ?e, "Failed to parse capabilities cache");
                    None
                }
            }
        }
        Err(_) => {
            tracing::debug!("No capabilities cache found");
            None
        }
    }
}

/// Save hardware capabilities to disk cache
///
/// Uses atomic write (temp file + rename) for consistency.
pub async fn save_capabilities_cache(capabilities: &HardwareCapabilities) -> Result<()> {
    let dir = PathBuf::from(garden_common::names::CONFIG_DIR);
    tokio::fs::create_dir_all(&dir).await?;

    let path = dir.join("capabilities.json");
    let tmp_path = path.with_extension("json.tmp");

    let content = serde_json::to_string_pretty(capabilities)?;
    tokio::fs::write(&tmp_path, content).await?;

    // Atomic rename
    match tokio::fs::rename(&tmp_path, &path).await {
        Ok(_) => Ok(()),
        Err(e) => {
            // Windows doesn't allow rename over existing file
            if cfg!(windows) {
                let _ = tokio::fs::remove_file(&path).await;
                tokio::fs::rename(&tmp_path, &path).await?;
                Ok(())
            } else {
                Err(e.into())
            }
        }
    }
}

/// Detect hardware capabilities (CPU, memory, GPU, disk)
///
/// This is a progressive detection:
/// 1. Fast: CPU, memory, disk (< 100ms)
/// 2. Slow: GPU detection (may take seconds)
///
/// Call this in a background task to avoid blocking startup.
pub async fn detect_hardware(stone_name: String) -> Result<HardwareCapabilities> {
    use garden_common::{HardwareInventory, DetectionStatus, CpuCapabilities, MemoryCapabilities, DiskCapabilities, RuntimeInfo};

    tracing::info!("Starting hardware detection");

    // Fast detection: CPU and memory using metrics module
    let (cpu_model, cpu_features, architecture) = crate::metrics::get_cpu_info()
        .unwrap_or_else(|_| ("Unknown".to_string(), vec![], std::env::consts::ARCH.to_string()));

    let resources = crate::metrics::collect_stone_resources().ok();
    let cpu_cores = resources.as_ref().map(|r| r.cpu.cores).unwrap_or(1);
    let total_memory_mb = resources.as_ref()
        .map(|r| r.memory.total_bytes / 1024 / 1024)
        .unwrap_or(0);

    let disk = resources.as_ref().map(|r| DiskCapabilities {
        total_gb: r.disk.total_bytes / 1024 / 1024 / 1024,
        disk_type: crate::metrics::detect_disk_type_for_mount(&r.disk.path),
    });

    // Slow detection: GPUs
    tracing::debug!("Detecting GPUs (may take a few seconds)...");
    let gpus = crate::metrics::detect_gpus();

    // Additional system info
    let os_version = crate::metrics::detect_os_version();
    let kernel_version = crate::metrics::detect_kernel_version();
    let swap_mb = crate::metrics::detect_swap();

    let hardware = HardwareInventory {
        cpu: CpuCapabilities {
            model: if cpu_model == "Unknown" { None } else { Some(cpu_model.clone()) },
            cores: cpu_cores,
            threads: None,
            architecture,
            features: if cpu_features.is_empty() { None } else { Some(cpu_features) },
        },
        memory: MemoryCapabilities {
            total_mb: total_memory_mb,
        },
        gpus,
        disk,
        storage: vec![],
        os_version,
        kernel_version: kernel_version.clone(),
        swap_mb,
        ai_capabilities: None,
    };

    let capabilities = HardwareCapabilities {
        stone_name,
        hardware,
        runtime: Some(RuntimeInfo {
            docker_version: None,
            os: std::env::consts::OS.to_string(),
            kernel: kernel_version,
        }),
        detection_status: DetectionStatus::Complete,
    };

    tracing::info!(
        cpu = ?capabilities.hardware.cpu.model,
        memory_gb = capabilities.hardware.memory.total_mb / 1024,
        gpus = capabilities.hardware.gpus.len(),
        "Hardware detection complete"
    );

    Ok(capabilities)
}

/// Create a skeleton capabilities object for instant startup
///
/// Use this when cache doesn't exist. Background detection will update it.
pub fn create_skeleton(stone_name: String) -> HardwareCapabilities {
    use garden_common::{DetectionStatus, HardwareInventory, CpuCapabilities, MemoryCapabilities};

    let hardware = HardwareInventory {
        cpu: CpuCapabilities {
            model: None,
            cores: 0,
            threads: None,
            architecture: std::env::consts::ARCH.to_string(),
            features: None,
        },
        memory: MemoryCapabilities {
            total_mb: 0,
        },
        gpus: vec![],
        disk: None,
        storage: vec![],
        os_version: None,
        kernel_version: None,
        swap_mb: None,
        ai_capabilities: None,
    };

    HardwareCapabilities {
        stone_name,
        hardware,
        runtime: None,
        detection_status: DetectionStatus::Scanning,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use garden_common::DetectionStatus;

    #[tokio::test]
    async fn test_cache_round_trip() {
        let caps = create_skeleton("test-stone".into());

        // Save
        save_capabilities_cache(&caps).await.expect("save failed");

        // Load
        let loaded = load_cached_capabilities().await.expect("should load");

        assert_eq!(loaded.stone_name, "test-stone");
        assert_eq!(loaded.detection_status, DetectionStatus::Scanning);
    }
}
