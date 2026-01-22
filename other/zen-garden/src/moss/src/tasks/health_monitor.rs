//! Health monitoring background task
//!
//! Continuous monitoring loop that:
//! - Polls Docker container health every 30 seconds
//! - Updates service registry with current status/health
//! - Adopts unregistered zen-offering containers (self-heal)
//! - Updates resource metrics (CPU, memory)
//!
//! This is a non-blocking background task that runs for the lifetime of the daemon.

use crate::AppState;
use crate::domain::adopt_offering_container;
use garden_common::{ServiceHealthStatus, ServiceStatus};

/// Background health monitoring loop
///
/// This task should be spawned with tokio::spawn() at daemon startup.
/// It runs indefinitely, polling Docker every 30 seconds.
///
/// # Non-Blocking
/// This function never returns - it's designed to run in the background
/// for the entire daemon lifetime. Spawn it and forget it.
///
/// # What It Does
/// 1. Polls all registered services for status/health
/// 2. Updates registry when status/health changes
/// 3. Fetches container resource metrics (CPU, memory)
/// 4. Discovers unregistered zen-offering containers
/// 5. Adopts discoveredcontainers if they match templates (self-heal)
/// 6. Persists registry changes to disk
///
/// # Example
/// ```rust,ignore
/// // At daemon startup
/// let state_clone = state.clone();
/// tokio::spawn(async move {
///     health_monitor_task(state_clone).await;
/// });
/// // Task runs forever in background
/// ```
pub async fn health_monitor_task(state: AppState) {
    let mut interval = tokio::time::interval(tokio::time::Duration::from_secs(30));

    loop {
        interval.tick().await;

        let registry_snapshot = { state.registry.read().await.clone() };

        for service in registry_snapshot {
            // Check container status
            let (status, health) = match state.docker.get_service_status(&service.name).await {
                Ok(status) => {
                    let health = state
                        .docker
                        .get_service_health(&service.name)
                        .await
                        .unwrap_or(ServiceHealthStatus::Offline);
                    (status, health)
                }
                Err(e) => {
                    tracing::warn!(
                        service = %service.name,
                        error = ?e,
                        "Failed to get service status, marking as offline"
                    );
                    (ServiceStatus::Stopped, ServiceHealthStatus::Offline)
                }
            };

            // Update registry if status or health changed
            if status != service.status || health != service.health {
                let mut reg = state.registry.write().await;
                if let Some(svc) = reg.iter_mut().find(|s| s.name == service.name) {
                    tracing::info!(
                        service = %service.name,
                        old_status = ?service.status,
                        new_status = ?status,
                        old_health = ?service.health,
                        new_health = ?health,
                        "Service state changed"
                    );
                    svc.status = status;
                    svc.health = health;
                }
            }

            // Update container resource metrics
            if let Ok(resources) = state.docker.get_container_stats(&service.name).await {
                let mut reg = state.registry.write().await;
                if let Some(svc) = reg.iter_mut().find(|s| s.name == service.name) {
                    svc.resources = Some(resources);
                }
            }
        }

        // Check for containers not in registry (external changes)
        // This provides self-heal: if someone manually starts a zen-offering container,
        // moss will adopt it into the registry
        match state.docker.list_zen_containers().await {
            Ok(container_names) => {
                let mut adopted_any = false;

                for container_name in &container_names {
                    // Check if already in registry (acquire read lock briefly)
                    let exists = {
                        let reg = state.registry.read().await;
                        reg.iter().any(|s| s.name == *container_name)
                    };

                    if !exists {
                        tracing::warn!(container = %container_name, "Found zen-offering container not in registry (adopting)");
                        match adopt_offering_container(&state.docker, &state.templates, container_name).await {
                            Ok(Some(info)) => {
                                // Double-check before adding (prevent race condition)
                                let mut reg = state.registry.write().await;
                                if !reg.iter().any(|s| s.name == info.name) {
                                    reg.push(info);
                                    adopted_any = true;
                                }
                            }
                            Ok(None) => {
                                tracing::warn!(container = %container_name, "No matching template for container; leaving unregistered");
                            }
                            Err(e) => {
                                tracing::warn!(container = %container_name, error = ?e, "Failed to adopt container; leaving it alone");
                            }
                        }
                    }
                }

                if adopted_any {
                    let _ = crate::persist_registry_state(&state).await;
                }
            }
            Err(e) => {
                tracing::error!(error = ?e, "Failed to list zen containers");
            }
        }
    }
}
