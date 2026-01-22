//! Service reconciliation domain logic
//!
//! Handles reconciliation of container state with registry:
//! - Discovers unregistered containers
//! - Adopts valid containers into registry
//! - Optionally removes invalid containers (no matching template)
//!
//! This is pure domain logic - delegates I/O to infra layer.

use crate::AppState;
use crate::domain::adopt_offering_container;
use crate::console;

/// Reconcile container state with the registry
///
/// This function:
/// 1. Lists all zen-offering-* containers
/// 2. Identifies containers not in registry
/// 3. Attempts to adopt each unregistered container
/// 4. Optionally drops containers without matching templates
///
/// # Parameters
/// - `state`: Application state with registry and Docker access
/// - `drop_invalid`: If true, removes containers with no matching template
///
/// # Returns
/// `ReconciliationResult` containing:
/// - `adopted`: Successfully adopted offerings
/// - `dropped_invalid`: Containers removed (only if drop_invalid=true)
/// - `skipped_existing`: Containers already in registry
/// - `left_unregistered`: Containers left alone (no template, drop_invalid=false)
///
/// # Composability
/// This function modifies state (adds to registry, removes containers).
/// Callers are responsible for:
/// - Persisting registry changes (if adopted or dropped any)
/// - Emitting events
/// - HTTP response formatting
pub async fn reconcile_services(
    state: &AppState,
    drop_invalid: bool,
) -> ReconciliationResult {
    let existing = match state.docker.list_zen_containers().await {
        Ok(list) => list,
        Err(e) => {
            tracing::error!(error = ?e, "Failed to list zen containers during reconciliation");
            return ReconciliationResult {
                error: Some(format!("Failed to list zen containers: {}", e)),
                ..Default::default()
            };
        }
    };

    let mut adopted = Vec::new();
    let mut dropped_invalid = Vec::new();
    let mut skipped_existing = Vec::new();
    let mut left_unregistered = Vec::new();

    for offering in existing {
        let in_registry = {
            let reg = state.registry.read().await;
            reg.iter().any(|s| s.name == offering)
        };

        if in_registry {
            skipped_existing.push(offering);
            continue;
        }

        match adopt_offering_container(&state.docker, &state.templates, &offering).await {
            Ok(Some(info)) => {
                tracing::info!(offering = %offering, "Reconciliation: adopting unregistered container");
                let mut reg = state.registry.write().await;
                reg.push(info);
                adopted.push(offering);
            }
            Ok(None) => {
                // "Invalid" in this context means: zen-offering-* container exists, but we have
                // no known template/manifest mapping for that offering.
                if drop_invalid {
                    tracing::warn!(offering = %offering, "Reconciliation: dropping invalid container (no matching template)");
                    match state.docker.remove_service(&offering, Some(&state.console)).await {
                        Ok(_) => {
                            dropped_invalid.push(offering.clone());
                            // Emit console event for dropped container
                            state.console.emit(console::ConsoleEvent::new(
                                console::EventCategory::Services,
                                console::EventStatus::Stopped,
                                format!("Dropped invalid: {}", offering),
                            ));
                        }
                        Err(e) => {
                            tracing::warn!(offering = %offering, error = ?e, "Failed to drop invalid container; leaving it alone");
                            left_unregistered.push(offering);
                        }
                    }
                } else {
                    tracing::debug!(offering = %offering, "Reconciliation: leaving unregistered container (no template, drop_invalid=false)");
                    left_unregistered.push(offering);
                }
            }
            Err(e) => {
                tracing::warn!(offering = %offering, error = ?e, "Reconciliation: adoption failed; leaving container alone");
                left_unregistered.push(offering);
            }
        }
    }

    ReconciliationResult {
        adopted,
        dropped_invalid,
        skipped_existing,
        left_unregistered,
        error: None,
    }
}

/// Result of service reconciliation operation
#[derive(Debug, Default)]
pub struct ReconciliationResult {
    /// Successfully adopted offerings
    pub adopted: Vec<String>,
    /// Containers removed (no matching template)
    pub dropped_invalid: Vec<String>,
    /// Containers already in registry (skipped)
    pub skipped_existing: Vec<String>,
    /// Containers left unregistered (no template, not dropped)
    pub left_unregistered: Vec<String>,
    /// Error message if reconciliation failed
    pub error: Option<String>,
}

impl ReconciliationResult {
    /// Check if any changes were made (adopted or dropped)
    pub fn has_changes(&self) -> bool {
        !self.adopted.is_empty() || !self.dropped_invalid.is_empty()
    }

    /// Check if reconciliation encountered an error
    pub fn is_error(&self) -> bool {
        self.error.is_some()
    }
}
