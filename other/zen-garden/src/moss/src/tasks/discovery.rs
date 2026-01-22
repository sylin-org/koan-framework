//! Service discovery background tasks
//!
//! Handles continuous registration with service discovery systems:
//! - Lantern service registry (centralized discovery)
//! - mDNS broadcasts (local network discovery) - future
//! - Pond synchronization (distributed discovery) - future

/// Lantern registration loop - registers this stone with Lantern every 45 seconds
///
/// Continuously registers this stone with the Lantern service discovery system.
/// Sends POST /api/register with stone ID, name, endpoint, and current service list.
///
/// Only runs if LANTERN_ENDPOINT environment variable is set.
///
/// # Arguments
/// * `stone_id` - This stone's unique identifier (GUID v7)
/// * `stone_name` - This stone's human-readable name (hostname)
/// * `endpoint` - This stone's HTTP endpoint (e.g., "http://192.168.1.100:7185")
/// * `lantern_endpoint` - Lantern service URL (e.g., "http://lantern:7190")
///
/// # Future Improvements
/// - TODO: Build actual service list from running containers
/// - TODO: Add health status to registration
/// - TODO: Handle Lantern unavailability gracefully
pub async fn lantern_registration_loop(
    stone_id: String,
    stone_name: String,
    endpoint: String,
    lantern_endpoint: String,
) -> anyhow::Result<()> {
    use reqwest::Client;
    use garden_common::RegisterRequest;

    tracing::info!(
        stone_id = %stone_id,
        stone_name = %stone_name,
        lantern_endpoint = %lantern_endpoint,
        "Starting Lantern registration loop"
    );

    let client = Client::new();
    let register_url = format!("{}/api/register", lantern_endpoint);

    loop {
        // TODO: Build service list from actual running containers
        // For now, just register as online with no services
        let request = RegisterRequest {
            stone_id: Some(stone_id.clone()),
            stone_name: stone_name.clone(),
            endpoint: endpoint.clone(),
            services: vec![],
        };

        match client
            .post(&register_url)
            .json(&request)
            .send()
            .await
        {
            Ok(response) if response.status().is_success() => {
                tracing::debug!("Registered with Lantern successfully");
            }
            Ok(response) => {
                tracing::warn!(
                    status = ?response.status(),
                    "Lantern registration returned non-success status"
                );
            }
            Err(e) => {
                tracing::warn!(error = ?e, "Failed to register with Lantern");
            }
        }

        // Sleep for 45 seconds before next heartbeat
        tokio::time::sleep(tokio::time::Duration::from_secs(45)).await;
    }
}
