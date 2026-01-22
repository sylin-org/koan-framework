//! Auto-adoption background task
//!
//! Continuous adoption loop that:
//! - Scans for adoptable offerings every 5 minutes
//! - Detects services configured for adopted mode
//! - Adopts stable detected services automatically
//! - Respects stability thresholds and exclusion rules
//!
//! This is a non-blocking background task that runs for the lifetime of the daemon.

use crate::AppState;
use crate::infra::config::AdoptionConfig;
use crate::domain::DetectionOrchestrator;
use garden_common::{AdoptedOfferingInfo, ServiceLocation, ServiceHealthStatus, OfferingMode};

/// Background auto-adoption loop
///
/// This task should be spawned with tokio::spawn() at daemon startup.
/// It runs indefinitely, scanning for adoptable offerings every 5 minutes.
///
/// # Non-Blocking
/// This function never returns - it's designed to run in the background
/// for the entire daemon lifetime. Spawn it and forget it.
///
/// # What It Does
/// 1. Scans all manifests with adopted mode support
/// 2. Runs detection for offerings not yet adopted
/// 3. Adopts offerings that pass stability threshold
/// 4. Respects exclusion patterns from configuration
/// 5. Persists adopted offerings to registry
///
/// # Example
/// ```rust,ignore
/// // At daemon startup
/// let state_clone = state.clone();
/// let config_clone = adoption_config.clone();
/// tokio::spawn(async move {
///     auto_adoption_task(state_clone, config_clone).await;
/// });
/// // Task runs forever in background
/// ```
pub async fn auto_adoption_task(state: AppState, config: AdoptionConfig) {
    // Run immediately on startup, then every 5 minutes
    let mut interval = tokio::time::interval(tokio::time::Duration::from_secs(300));

    loop {
        interval.tick().await;

        tracing::debug!("Running auto-adoption scan");

        // Load manifests that support adopted mode
        let manifests = state.manifests.read().await.clone();

        let orchestrator = DetectionOrchestrator::new(state.docker.clone());

        for manifest in manifests {
            // Only check manifests with adopted mode
            if !manifest.modes.iter().any(|m| matches!(m, OfferingMode::Adopted)) {
                continue;
            }

            // Check exclusion list
            if config.is_excluded(&manifest.name) {
                tracing::debug!(offering = %manifest.name, "Skipping excluded offering");
                continue;
            }

            // Check if already adopted
            {
                let adopted = state.adopted_offerings.read().await;
                if adopted.iter().any(|a| a.offering == manifest.name) {
                    continue; // Already adopted
                }
            }

            // Try detection
            match orchestrator.detect(&manifest).await {
                Ok(result) if result.detected && result.stable => {
                    tracing::info!(
                        offering = %manifest.name,
                        version = ?result.version,
                        "Auto-adopting detected offering"
                    );

                    // Parse default control level
                    let control_level = match config.default_control_level() {
                        "full" => garden_common::AdoptedControlLevel::Full,
                        "announce" => garden_common::AdoptedControlLevel::Announce,
                        _ => garden_common::AdoptedControlLevel::Monitor, // Default safe
                    };

                    // Create adopted offering info
                    // TODO: Extract actual location from detection result
                    let location = ServiceLocation {
                        host: "localhost".to_string(),
                        port: manifest.ports.first().map(|(host, _)| *host).unwrap_or(0),
                        protocol: manifest.category.clone(),
                    };

                    let adopted_info = AdoptedOfferingInfo {
                        name: format!("{}@adopted", manifest.name),
                        offering: manifest.name.clone(),
                        mode: OfferingMode::Adopted,
                        location,
                        control_level,
                        health: ServiceHealthStatus::Healthy,
                        detected_at: chrono::Utc::now().to_rfc3339(),
                        version: result.version,
                        start_command: manifest.control.as_ref().and_then(|c| c.start_command.clone()),
                        stop_command: manifest.control.as_ref().and_then(|c| c.stop_command.clone()),
                        restart_command: manifest.control.as_ref().and_then(|c| c.restart_command.clone()),
                        health_check_url: manifest.control.as_ref().and_then(|c| c.health_check_url.clone()),
                        container_name: None,
                    };

                    // Add to adopted registry
                    {
                        let mut adopted = state.adopted_offerings.write().await;
                        adopted.push(adopted_info.clone());
                    }

                    // Emit console event
                    state.console.emit(crate::console::ConsoleEvent::new(
                        crate::console::EventCategory::Services,
                        crate::console::EventStatus::Healthy,
                        format!("Auto-adopted {}", manifest.name),
                    ));

                    // TODO: Persist adopted offerings registry to disk
                }
                Ok(result) if result.detected && !result.stable => {
                    tracing::debug!(
                        offering = %manifest.name,
                        "Detected but not yet stable (waiting for stability threshold)"
                    );
                }
                Ok(_) => {
                    // Not detected, skip
                }
                Err(e) => {
                    tracing::warn!(
                        offering = %manifest.name,
                        error = ?e,
                        "Detection failed for offering"
                    );
                }
            }
        }

        tracing::debug!("Auto-adoption scan complete");
    }
}
