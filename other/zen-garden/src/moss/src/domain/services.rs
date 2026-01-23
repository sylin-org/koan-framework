//! Service counting utilities for placement evaluation
//!
//! Reusable functions for counting services on local and remote stones.

use anyhow::{Context, Result};
use std::time::Duration;

/// Get count of running services on local stone
///
/// Fast, zero-latency check of local service registry.
pub async fn get_local_service_count(
    state: &crate::AppState,
) -> Result<usize> {
    let registry = state.registry.read().await;
    
    // Count services that are in running state
    let count = registry.iter()
        .filter(|svc| svc.status == garden_common::ServiceStatus::Running)
        .count();
    
    Ok(count)
}

/// Fetch service count from remote stone via HTTP
///
/// Calls the `/api/v1/services` endpoint and counts running services.
pub async fn fetch_remote_service_count(
    endpoint: &str,
    timeout: Duration,
) -> Result<usize> {
    let client = reqwest::Client::builder()
        .timeout(timeout)
        .build()
        .context("Failed to build HTTP client")?;
    
    let services_url = format!("{}/api/v1/services", endpoint.trim_end_matches('/'));
    let response = client
        .get(&services_url)
        .send()
        .await
        .context("Failed to fetch services from remote stone")?;
    
    if !response.status().is_success() {
        anyhow::bail!("Remote stone returned error: {}", response.status());
    }
    
    let services: Vec<garden_common::ServiceInfo> = response
        .json()
        .await
        .context("Failed to parse services response")?;
    
    // Count running services
    let count = services.iter()
        .filter(|svc| svc.status == garden_common::ServiceStatus::Running)
        .count();
    
    Ok(count)
}

#[cfg(test)]
mod tests {
    use super::*;
    
    // Note: get_local_service_count requires AppState, so we test it via integration tests
    // fetch_remote_service_count requires a live HTTP server, so we test it manually
    
    #[test]
    fn test_service_counting_compiles() {
        // This test just ensures the module compiles correctly
        assert!(true);
    }
}
