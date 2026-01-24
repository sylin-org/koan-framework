//! Container adoption domain logic
//!
//! Handles discovery and adoption of existing Zen Garden containers:
//! - Validates containers against templates
//! - Evaluates compatibility and detects image mismatches
//! - Registers adopted containers in the service registry
//!
//! This is pure domain logic - delegates I/O to infra layer.

use crate::{AppState, ServiceInfo};
use crate::domain::{
    CompatibilityDecision, evaluate_compatibility, get_current_compat_capabilities,
};
use crate::docker::DockerManager;
use crate::templates::TemplateLoader;
use garden_common::{Ports, ServiceHealthStatus, ServiceStatus};

/// Adopt a container for a specific offering into the registry
///
/// Validates that:
/// 1. The offering has a known template/manifest
/// 2. Compatibility rules are evaluated (may trigger fallback image)
/// 3. Running image matches expected image (or marks as degraded)
///
/// # Returns
/// - `Ok(Some(ServiceInfo))`: Container successfully adopted
/// - `Ok(None)`: No template found for offering (container left alone)
/// - `Err(_)`: Adoption failed (Docker API error)
///
/// # Composability
/// This function is pure domain logic - it doesn't modify state directly.
/// Callers are responsible for:
/// - Adding returned ServiceInfo to registry
/// - Persisting registry changes
/// - Emitting events
pub async fn adopt_offering_container(
    docker: &DockerManager,
    templates: &TemplateLoader,
    offering: &str,
) -> anyhow::Result<Option<ServiceInfo>> {
    // Only adopt if the offering maps to a known template (valid manifest/template).
    let mut template = match templates.load(offering) {
        Ok(t) => t,
        Err(_) => return Ok(None),
    };

    // Compute expected image based on compatibility rules.
    if let Some(rules) = &template.compatibility {
        let capabilities = get_current_compat_capabilities();
        match evaluate_compatibility(rules, &capabilities) {
            CompatibilityDecision::Pass => {}
            CompatibilityDecision::Warning { .. } => {
                // Warning: proceed with caution, but don't change image
            }
            CompatibilityDecision::Fallback { image, .. } => template.image = image,
            CompatibilityDecision::Fail { .. } => {
                // Leave container alone, but adopt it as degraded/incompatible.
            }
        }
    }

    let status = docker
        .get_service_status(offering)
        .await
        .unwrap_or(ServiceStatus::Unknown);
    let mut health = docker
        .get_service_health(offering)
        .await
        .unwrap_or(ServiceHealthStatus::Offline);

    let actual_image = docker
        .get_service_image(offering)
        .await
        .unwrap_or_else(|_| "<unknown>".to_string());
    let expected_image = template.image.clone();

    // If the running image doesn't match what we'd expect (including compatibility fallback), mark degraded.
    if actual_image != "<unknown>" && actual_image != expected_image {
        health = ServiceHealthStatus::Degraded;
    }

    let native_port = template.ports.first().map(|(host, _)| *host).unwrap_or(30000);
    let version = actual_image
        .split(':')
        .next_back()
        .unwrap_or("latest")
        .to_string();

    let adopted = ServiceInfo {
        name: offering.to_string(),
        offering: offering.to_string(),
        version,
        status: if health == ServiceHealthStatus::Degraded && status == ServiceStatus::Running {
            ServiceStatus::Degraded
        } else {
            status
        },
        health,
        ports: Ports {
            native: native_port,
            agnostic: None,
        },
        resources: None,
        job_id: None,
    };

    Ok(Some(adopted))
}

/// Adopt all existing Zen Garden containers that aren't already in the registry
///
/// This function:
/// 1. Lists all zen-offering-* containers
/// 2. Filters out containers already in the registry
/// 3. Attempts to adopt each container
/// 4. Returns adoption results for caller to handle
///
/// # Returns
/// `AdoptionResult` containing:
/// - `adopted`: Successfully adopted ServiceInfo entries
/// - `no_template`: Containers with no matching template
/// - `failed`: Containers that failed adoption with error messages
///
/// # Composability
/// This function is pure domain logic - it doesn't modify state.
/// Callers are responsible for:
/// - Adding adopted services to registry
/// - Persisting registry changes
/// - Emitting events
/// - Logging warnings for failed adoptions
pub async fn adopt_existing_containers(
    state: &AppState,
) -> AdoptionResult {
    let existing = match state.docker.list_zen_containers().await {
        Ok(list) => list,
        Err(e) => {
            tracing::warn!(error = ?e, "Failed to list zen containers for adoption");
            return AdoptionResult::default();
        }
    };

    let mut adopted = Vec::new();
    let mut no_template = Vec::new();
    let mut failed = Vec::new();

    for offering in existing {
        let already = {
            let reg = state.registry.read().await;
            reg.iter().any(|s| s.name == offering)
        };
        if already {
            continue;
        }

        match adopt_offering_container(&state.docker, &state.templates, &offering).await {
            Ok(Some(info)) => {
                tracing::info!(offering = %offering, "Adopting existing zen-offering container into registry");
                adopted.push(info);
            }
            Ok(None) => {
                tracing::warn!(offering = %offering, "Found zen-offering container but no matching template; leaving unregistered");
                no_template.push(offering);
            }
            Err(e) => {
                tracing::warn!(offering = %offering, error = ?e, "Failed to adopt existing container; leaving it alone");
                failed.push((offering, format!("{}", e)));
            }
        }
    }

    AdoptionResult {
        adopted,
        no_template,
        failed,
    }
}

/// Result of container adoption operation
#[derive(Debug, Default)]
pub struct AdoptionResult {
    /// Successfully adopted containers
    pub adopted: Vec<ServiceInfo>,
    /// Containers with no matching template (left unregistered)
    pub no_template: Vec<String>,
    /// Containers that failed adoption (offering, error message)
    pub failed: Vec<(String, String)>,
}
