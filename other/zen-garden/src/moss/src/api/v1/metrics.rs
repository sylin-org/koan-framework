//! System metrics API endpoint
//!
//! Provides real-time system resource metrics including:
//! - CPU usage and core count
//! - Memory usage (total, used, available)
//! - Disk usage (total, used, available)
//! - System uptime
//!
//! Metrics are collected on-demand from the system.
//! If collection fails, a fallback with zero values is returned.

use axum::Json;
use garden_common::{MetricsSnapshot, StoneResources, CpuMetrics, MemoryMetrics, DiskMetrics};
use crate::api::responses::ApiResponse;
use crate::metrics;

/// GET /metrics - Real-time system resource metrics
///
/// Returns current CPU, memory, disk usage and system uptime.
/// Metrics are collected on-demand from the operating system.
///
/// # Fallback Behavior
/// If metrics collection fails, returns a fallback response with zero values
/// rather than failing the request.
pub async fn get_metrics() -> Json<ApiResponse<MetricsSnapshot>> {
    let resources = metrics::collect_stone_resources()
        .unwrap_or_else(|_| create_fallback_resources());

    let snapshot = MetricsSnapshot {
        timestamp: chrono::Utc::now().to_rfc3339(),
        cpu: resources.cpu,
        memory: resources.memory,
        disk: resources.disk,
        network: None, // TODO: Add network metrics
        uptime_seconds: resources.uptime_seconds,
    };

    Json(ApiResponse {
        data: snapshot,
        suggestions: None,
    })
}

/// Create fallback resource metrics with zero values
///
/// Used when metrics collection fails to ensure endpoint always returns valid data.
fn create_fallback_resources() -> StoneResources {
    StoneResources {
        cpu: CpuMetrics {
            cores: 1,
            usage_percent: 0.0,
            usage_friendly: "0%".to_string(),
        },
        memory: MemoryMetrics {
            total_bytes: 0,
            used_bytes: 0,
            available_bytes: 0,
            used_percent: 0.0,
            total_friendly: "0 B".to_string(),
            used_friendly: "0 B".to_string(),
            available_friendly: "0 B".to_string(),
        },
        disk: DiskMetrics {
            total_bytes: 0,
            used_bytes: 0,
            available_bytes: 0,
            used_percent: 0.0,
            path: "/".to_string(),
            total_friendly: "0 B".to_string(),
            used_friendly: "0 B".to_string(),
            available_friendly: "0 B".to_string(),
        },
        uptime_seconds: 0,
        uptime_friendly: "0s".to_string(),
    }
}
