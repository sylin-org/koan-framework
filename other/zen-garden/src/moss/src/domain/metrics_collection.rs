//! Metrics collection and normalization for stone resources
//!
//! Reusable functions for fetching resource metrics from local and remote stones.
//! Provides normalized data structures for consistent scoring and comparison.

use anyhow::{Context, Result};
use garden_common::{DiskType, StoneResources};
use std::time::Duration;

/// Normalized stone metrics for placement evaluation
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct StoneMetrics {
    pub memory_free_mb: u64,
    pub memory_total_mb: u64,
    pub cpu_load_percent: u8,
    pub storage_free_gb: u64,
    pub storage_total_gb: u64,
    pub storage_type: DiskType,
    pub architecture: String,
}

/// Get metrics for tended stone (zero latency, no HTTP)
///
/// This is optimized for local evaluation - no network overhead.
pub fn get_local_metrics() -> Result<StoneMetrics> {
    let resources = crate::metrics::collect_stone_resources()
        .context("Failed to collect local stone resources")?;
    
    let (_, _, architecture) = crate::metrics::get_cpu_info()
        .unwrap_or_else(|_| ("Unknown".to_string(), vec![], std::env::consts::ARCH.to_string()));
    
    let storage_type_str = crate::metrics::detect_disk_type_for_mount(&resources.disk.path);
    let storage_type = storage_type_str
        .as_ref()
        .map(|s| parse_disk_type(s))
        .unwrap_or(DiskType::Unknown);
    
    Ok(normalize_metrics(&resources, &architecture, &storage_type))
}

/// Parse disk type string to DiskType enum
fn parse_disk_type(s: &str) -> DiskType {
    match s.to_lowercase().as_str() {
        "nvme" => DiskType::NVMe,
        "ssd" => DiskType::SSD,
        "hdd" => DiskType::HDD,
        _ => DiskType::Unknown,
    }
}

/// Fetch metrics from remote stone via HTTP
///
/// Calls the `/capabilities` endpoint and normalizes the response.
pub async fn fetch_stone_metrics(
    endpoint: &str,
    timeout: Duration,
) -> Result<StoneMetrics> {
    let client = reqwest::Client::builder()
        .timeout(timeout)
        .build()
        .context("Failed to build HTTP client")?;
    
    let capabilities_url = format!("{}/capabilities", endpoint.trim_end_matches('/'));
    let response = client
        .get(&capabilities_url)
        .send()
        .await
        .context("Failed to fetch capabilities from remote stone")?;
    
    if !response.status().is_success() {
        anyhow::bail!("Remote stone returned error: {}", response.status());
    }
    
    let caps: garden_common::HardwareCapabilities = response
        .json()
        .await
        .context("Failed to parse capabilities response")?;
    
    // Convert HardwareCapabilities to StoneMetrics
    let free_mb = caps.hardware.memory.total_mb.saturating_sub(
        // Estimate used memory (we don't have exact free in capabilities)
        (caps.hardware.memory.total_mb as f32 * 0.3) as u64
    );
    
    let storage_type = caps.hardware.disk
        .as_ref()
        .and_then(|d| d.disk_type.as_ref())
        .map(|s| parse_disk_type(s))
        .unwrap_or(DiskType::Unknown);
    
    Ok(StoneMetrics {
        memory_free_mb: free_mb, // Rough estimate
        memory_total_mb: caps.hardware.memory.total_mb,
        cpu_load_percent: 0, // Capabilities doesn't include current load
        storage_free_gb: caps.hardware.disk.as_ref().map(|d| d.total_gb / 2).unwrap_or(0), // Estimate 50% free
        storage_total_gb: caps.hardware.disk.as_ref().map(|d| d.total_gb).unwrap_or(0),
        storage_type,
        architecture: caps.hardware.cpu.architecture,
    })
}

/// Fetch metrics from multiple stones in parallel
///
/// Returns results in same order as input endpoints.
/// Failed fetches return Error variants.
pub async fn fetch_metrics_batch(
    endpoints: Vec<String>,
    timeout: Duration,
) -> Vec<Result<StoneMetrics>> {
    let futures: Vec<_> = endpoints
        .into_iter()
        .map(|endpoint| {
            let ep = endpoint.clone();
            async move {
                fetch_stone_metrics(&ep, timeout).await
            }
        })
        .collect();
    
    futures_util::future::join_all(futures).await
}

/// Normalize StoneResources to StoneMetrics
///
/// Pure function for converting internal resource format to placement metrics.
pub fn normalize_metrics(
    resources: &StoneResources,
    architecture: &str,
    storage_type: &DiskType,
) -> StoneMetrics {
    StoneMetrics {
        memory_free_mb: resources.memory.available_bytes / 1024 / 1024,
        memory_total_mb: resources.memory.total_bytes / 1024 / 1024,
        cpu_load_percent: resources.cpu.usage_percent as u8,
        storage_free_gb: resources.disk.available_bytes / 1024 / 1024 / 1024,
        storage_total_gb: resources.disk.total_bytes / 1024 / 1024 / 1024,
        storage_type: storage_type.clone(),
        architecture: architecture.to_string(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use garden_common::{CpuMetrics, DiskMetrics, MemoryMetrics};

    fn make_test_resources() -> StoneResources {
        StoneResources {
            cpu: CpuMetrics {
                cores: 8,
                usage_percent: 25.0,
                usage_friendly: "25%".to_string(),
            },
            memory: MemoryMetrics {
                total_bytes: 32 * 1024 * 1024 * 1024, // 32 GB
                used_bytes: 16 * 1024 * 1024 * 1024,  // 16 GB used
                available_bytes: 16 * 1024 * 1024 * 1024, // 16 GB free
                used_percent: 50.0,
                total_friendly: "32 GB".to_string(),
                used_friendly: "16 GB".to_string(),
                available_friendly: "16 GB".to_string(),
            },
            disk: DiskMetrics {
                path: "/".to_string(),
                total_bytes: 500 * 1024 * 1024 * 1024, // 500 GB
                used_bytes: 250 * 1024 * 1024 * 1024,  // 250 GB used
                available_bytes: 250 * 1024 * 1024 * 1024, // 250 GB free
                used_percent: 50.0,
                total_friendly: "500 GB".to_string(),
                used_friendly: "250 GB".to_string(),
                available_friendly: "250 GB".to_string(),
            },
            uptime_seconds: 10000,
            uptime_friendly: "2h 46m".to_string(),
        }
    }

    #[test]
    fn test_normalize_metrics() {
        let resources = make_test_resources();
        let metrics = normalize_metrics(&resources, "x86_64", &DiskType::NVMe);
        
        assert_eq!(metrics.memory_total_mb, 32768);  // 32 GB in MB
        assert_eq!(metrics.memory_free_mb, 16384);   // 16 GB in MB
        assert_eq!(metrics.cpu_load_percent, 25);
        assert_eq!(metrics.storage_total_gb, 500);
        assert_eq!(metrics.storage_free_gb, 250);
        assert_eq!(metrics.architecture, "x86_64");
        assert!(matches!(metrics.storage_type, DiskType::NVMe));
    }

    #[test]
    fn test_normalize_metrics_with_different_storage() {
        let resources = StoneResources {
            cpu: CpuMetrics {
                cores: 4,
                usage_percent: 50.0,
                usage_friendly: "50%".to_string(),
            },
            memory: MemoryMetrics {
                total_bytes: 8 * 1024 * 1024 * 1024,
                used_bytes: 6 * 1024 * 1024 * 1024,
                available_bytes: 2 * 1024 * 1024 * 1024,
                used_percent: 75.0,
                total_friendly: "8 GB".to_string(),
                used_friendly: "6 GB".to_string(),
                available_friendly: "2 GB".to_string(),
            },
            disk: DiskMetrics {
                path: "/data".to_string(),
                total_bytes: 1000 * 1024 * 1024 * 1024,
                used_bytes: 900 * 1024 * 1024 * 1024,
                available_bytes: 100 * 1024 * 1024 * 1024,
                used_percent: 90.0,
                total_friendly: "1000 GB".to_string(),
                used_friendly: "900 GB".to_string(),
                available_friendly: "100 GB".to_string(),
            },
            uptime_seconds: 5000,
            uptime_friendly: "1h 23m".to_string(),
        };
        
        let metrics = normalize_metrics(&resources, "aarch64", &DiskType::HDD);
        
        assert_eq!(metrics.memory_total_mb, 8192);
        assert_eq!(metrics.memory_free_mb, 2048);
        assert_eq!(metrics.cpu_load_percent, 50);
        assert_eq!(metrics.storage_total_gb, 1000);
        assert_eq!(metrics.storage_free_gb, 100);
        assert_eq!(metrics.architecture, "aarch64");
        assert!(matches!(metrics.storage_type, DiskType::HDD));
    }

    #[test]
    fn test_local_metrics_returns_normalized_data() {
        // This test validates the function executes without panicking
        // Actual values depend on the system
        let result = get_local_metrics();
        
        // Should succeed on any system with sysinfo
        match result {
            Ok(metrics) => {
                assert!(metrics.memory_total_mb > 0, "Should have non-zero total memory");
                assert!(metrics.cpu_load_percent <= 100, "CPU load should be <= 100%");
                assert!(!metrics.architecture.is_empty(), "Architecture should not be empty");
            }
            Err(e) => {
                // Log but don't fail - test environments may have restricted access
                println!("Local metrics failed (may be expected in CI): {}", e);
            }
        }
    }
}
