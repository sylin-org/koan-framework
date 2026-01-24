//! Background job executors for service installation
//!
//! Non-blocking async tasks for:
//! - Single service installation
//! - Batch service installation
//!
//! These tasks:
//! - Run in background via tokio::spawn()
//! - Update job status in shared state
//! - Emit events for progress tracking
//! - Don't block the HTTP response

use crate::{AppState, JobStatus, emit_event};
use crate::domain::get_compiled_offering;
use crate::console;
use garden_common::{Ports, ServiceHealthStatus, ServiceInfo, ServiceStatus};

/// Execute single service installation in background
///
/// This is a long-running task that should be spawned with tokio::spawn().
/// It:
/// 1. Updates job status to Running
/// 2. Resolves offering from offerings index
/// 3. Validates compatibility
/// 4. Pulls Docker image and creates container
/// 5. Adds service to registry
/// 6. Updates job status to Completed/Failed
///
/// # Non-Blocking
/// This function is designed to run in the background. The HTTP endpoint
/// should spawn this task and immediately return the job ID to the client.
///
/// # Parameters
/// - `state`: Application state (cloned, cheap due to Arc)
/// - `job_id`: Job ID for tracking
/// - `offering`: Offering name to install
///
/// # Example
/// ```rust,ignore
/// let state_clone = state.clone();
/// let job_id = job_id.to_string();
/// let offering = offering.to_string();
/// tokio::spawn(async move {
///     install_service_task(&state_clone, &job_id, &offering).await;
/// });
/// ```
pub async fn install_service_task(state: &AppState, job_id: &str, offering: &str) {
    // Update job status to Running
    {
        let mut jobs = state.jobs.write().await;
        if let Some(job) = jobs.get_mut(job_id) {
            job.status = JobStatus::Running;
        }
    }

    // Emit job started event
    state.console.emit(console::ConsoleEvent::new(
        console::EventCategory::Jobs,
        console::EventStatus::Started,
        format!("Install {} (job: {})", offering, &job_id[..8])
    ));

    emit_event(state, "info", format!("Starting installation: {}", offering), Some(job_id.to_string()));
    tracing::info!(job_id, offering, "Starting service installation");

    emit_event(
        state,
        "debug",
        format!("Resolving compiled offering config for {}", offering),
        Some(job_id.to_string()),
    );

    let compiled = match get_compiled_offering(state, offering).await {
        Ok(Some(o)) => o,
        Ok(None) => {
            state.console.emit(console::ConsoleEvent::new(
                console::EventCategory::Jobs,
                console::EventStatus::Failed,
                format!("Offering not found: {}", offering)
            ));
            emit_event(
                state,
                "error",
                format!("Offering not found: {}", offering),
                Some(job_id.to_string()),
            );
            // Remove Installing entry from registry
            remove_installing_entry(state, offering).await;
            let mut jobs = state.jobs.write().await;
            if let Some(job) = jobs.get_mut(job_id) {
                job.status = JobStatus::Failed;
                job.failed
                    .insert(offering.to_string(), "Offering not found".to_string());
                job.completed_at = Some(std::time::SystemTime::now());
            }
            return;
        }
        Err(e) => {
            emit_event(
                state,
                "error",
                format!("Failed to read offerings index for {}: {}", offering, e),
                Some(job_id.to_string()),
            );
            // Remove Installing entry from registry
            remove_installing_entry(state, offering).await;
            let mut jobs = state.jobs.write().await;
            if let Some(job) = jobs.get_mut(job_id) {
                job.status = JobStatus::Failed;
                job.failed
                    .insert(offering.to_string(), format!("Offerings index error: {}", e));
                job.completed_at = Some(std::time::SystemTime::now());
            }
            return;
        }
    };

    if compiled.compatibility.decision == "fail" {
        let reason = compiled
            .compatibility
            .reason
            .clone()
            .unwrap_or_else(|| "Incompatible".to_string());
        state.console.emit(console::ConsoleEvent::new(
            console::EventCategory::Jobs,
            console::EventStatus::Failed,
            format!("Compatibility: {}", offering)
        ));
        emit_event(
            state,
            "error",
            format!("Compatibility validation failed: {}", reason),
            Some(job_id.to_string()),
        );

        // Remove Installing entry from registry
        remove_installing_entry(state, offering).await;
        let mut jobs = state.jobs.write().await;
        if let Some(job) = jobs.get_mut(job_id) {
            job.status = JobStatus::Failed;
            job.failed
                .insert(offering.to_string(), format!("Compatibility failed: {}", reason));
            job.completed_at = Some(std::time::SystemTime::now());
        }
        return;
    }

    match compiled.compatibility.decision.as_str() {
        "fallback" => {
            emit_event(
                state,
                "warning",
                format!(
                    "Compatibility fallback: {}",
                    compiled.compatibility.reason.clone().unwrap_or_default()
                ),
                Some(job_id.to_string()),
            );
        }
        _ => {}
    }

    // Install via Docker
    emit_event(
        state,
        "info",
        format!("Pulling image: {}", compiled.image),
        Some(job_id.to_string()),
    );
    let ports_for_docker = compiled.ports.clone();
    if let Err(e) = state
        .docker
        .install_service(
            offering,
            &compiled.image,
            ports_for_docker,
            compiled.environment,
            compiled.volumes,
            Some(&state.console),
        )
        .await
    {
        state.console.emit(console::ConsoleEvent::new(
            console::EventCategory::Jobs,
            console::EventStatus::Failed,
            format!("Install failed: {}", offering)
        ));
        emit_event(state, "error", format!("Installation failed for {}: {}", offering, e), Some(job_id.to_string()));
        tracing::error!(job_id, offering, error = ?e, "Docker install failed");
        // Remove Installing entry from registry
        remove_installing_entry(state, offering).await;
        let mut jobs = state.jobs.write().await;
        if let Some(job) = jobs.get_mut(job_id) {
            job.status = JobStatus::Failed;
            job.failed.insert(offering.to_string(), format!("Install failed: {}", e));
            job.completed_at = Some(std::time::SystemTime::now());
        }
        return;
    }

    emit_event(state, "info", format!("Creating container for {}", offering), Some(job_id.to_string()));

    // Extract port info
    let native_port = compiled.ports.first().map(|(host, _)| *host).unwrap_or(30000);

    // Update existing registry entry (created with Installing status before job started)
    // Change status from Installing to Running and clear job_id
    {
        let mut registry = state.registry.write().await;
        if let Some(existing) = registry.iter_mut().find(|svc| svc.name == offering) {
            existing.status = ServiceStatus::Running;
            existing.health = ServiceHealthStatus::Healthy;
            existing.job_id = None;
            existing.version = compiled.image.split(':').next_back().unwrap_or("latest").into();
            existing.ports = Ports {
                native: native_port,
                agnostic: None,
            };
        } else {
            // Fallback: entry was somehow removed, recreate it
            let info = ServiceInfo {
                name: offering.to_string(),
                offering: offering.to_string(),
                version: compiled.image.split(':').next_back().unwrap_or("latest").into(),
                status: ServiceStatus::Running,
                health: ServiceHealthStatus::Healthy,
                ports: Ports {
                    native: native_port,
                    agnostic: None,
                },
                resources: None,
                job_id: None,
            };
            registry.push(info);
        }
    }

    let _ = state.persist_registry().await;
    
    // Sync services to self_entry and broadcast chirp so topology reflects the change immediately
    state.sync_self_services(true).await;

    emit_event(state, "info", format!("✓ Service {} started successfully", offering), Some(job_id.to_string()));

    // Mark job as completed
    {
        let mut jobs = state.jobs.write().await;
        if let Some(job) = jobs.get_mut(job_id) {
            job.status = JobStatus::Completed;
            job.completed.push(offering.to_string());
            job.completed_at = Some(std::time::SystemTime::now());
        }
    }

    state.console.emit(console::ConsoleEvent::new(
        console::EventCategory::Jobs,
        console::EventStatus::Completed,
        format!("Install {} (job: {})", offering, &job_id[..8])
    ));

    tracing::info!(job_id, offering, "Service installation completed");
}

/// Execute batch service installation in background
///
/// This is a long-running task that should be spawned with tokio::spawn().
/// It installs multiple services sequentially, tracking success/failure for each.
///
/// # Non-Blocking
/// This function is designed to run in the background. The HTTP endpoint
/// should spawn this task and immediately return the job ID to the client.
///
/// # Parameters
/// - `state`: Application state (cloned, cheap due to Arc)
/// - `job_id`: Job ID for tracking
/// - `offerings`: List of offering names to install
///
/// # Example
/// ```rust,ignore
/// let state_clone = state.clone();
/// let job_id = job_id.to_string();
/// let offerings = vec!["nginx".to_string(), "postgres".to_string()];
/// tokio::spawn(async move {
///     install_batch_task(&state_clone, &job_id, offerings).await;
/// });
/// ```
pub async fn install_batch_task(state: &AppState, job_id: &str, offerings: Vec<String>) {
    let offerings_count = offerings.len();

    // Update job status to Running
    {
        let mut jobs = state.jobs.write().await;
        if let Some(job) = jobs.get_mut(job_id) {
            job.status = JobStatus::Running;
        }
    }

    state.console.emit(console::ConsoleEvent::new(
        console::EventCategory::Jobs,
        console::EventStatus::Started,
        format!("Batch install {} services (job: {})", offerings_count, &job_id[..8])
    ));

    tracing::info!(job_id, count = offerings_count, "Starting batch installation");

    for offering in offerings {
        tracing::info!(job_id, offering, "Installing service");

        let compiled = match get_compiled_offering(state, &offering).await {
            Ok(Some(o)) => o,
            Ok(None) => {
                let mut jobs = state.jobs.write().await;
                if let Some(job) = jobs.get_mut(job_id) {
                    job.failed
                        .insert(offering.clone(), "Offering not found".to_string());
                }
                continue;
            }
            Err(e) => {
                let mut jobs = state.jobs.write().await;
                if let Some(job) = jobs.get_mut(job_id) {
                    job.failed
                        .insert(offering.clone(), format!("Offerings index error: {}", e));
                }
                continue;
            }
        };

        if compiled.compatibility.decision == "fail" {
            let reason = compiled
                .compatibility
                .reason
                .clone()
                .unwrap_or_else(|| "Incompatible".to_string());
            tracing::error!(job_id, offering, reason = %reason, "Compatibility validation failed");
            let mut jobs = state.jobs.write().await;
            if let Some(job) = jobs.get_mut(job_id) {
                job.failed
                    .insert(offering.clone(), format!("Compatibility failed: {}", reason));
            }
            continue;
        }

        // Install via Docker
        let ports_for_docker = compiled.ports.clone();
        if let Err(e) = state
            .docker
            .install_service(
                &offering,
                &compiled.image,
                ports_for_docker,
                compiled.environment,
                compiled.volumes,
                Some(&state.console),
            )
            .await
        {
            tracing::error!(job_id, offering, error = ?e, "Docker install failed");
            let mut jobs = state.jobs.write().await;
            if let Some(job) = jobs.get_mut(job_id) {
                job.failed.insert(offering.clone(), format!("Install failed: {}", e));
            }
            continue;
        }

        // Extract port info
        let native_port = compiled.ports.first().map(|(host, _)| *host).unwrap_or(30000);

        // Add to registry
        let info = ServiceInfo {
            name: offering.clone(),
            offering: offering.clone(),
            version: compiled.image.split(':').next_back().unwrap_or("latest").into(),
            status: ServiceStatus::Running,
            health: ServiceHealthStatus::Healthy,
            ports: Ports {
                native: native_port,
                agnostic: None,
            },
            resources: None,
            job_id: None,
        };

        {
            let mut registry = state.registry.write().await;
            if let Some(existing) = registry.iter_mut().find(|svc| svc.name == offering) {
                *existing = info;
            } else {
                registry.push(info);
            }
        }

        let _ = state.persist_registry().await;
        
        // Sync services to self_entry and broadcast chirp so topology reflects the change immediately
        state.sync_self_services(true).await;

        // Mark offering as completed
        {
            let mut jobs = state.jobs.write().await;
            if let Some(job) = jobs.get_mut(job_id) {
                job.completed.push(offering.clone());
            }
        }

        tracing::info!(job_id, offering, "Service installed");
    }

    // Mark job as completed (or failed if some services failed)
    {
        let mut jobs = state.jobs.write().await;
        if let Some(job) = jobs.get_mut(job_id) {
            let failed = !job.failed.is_empty();
            job.status = if failed {
                JobStatus::Failed
            } else {
                JobStatus::Completed
            };
            job.completed_at = Some(std::time::SystemTime::now());

            // Emit completion event
            if failed {
                state.console.emit(console::ConsoleEvent::new(
                    console::EventCategory::Jobs,
                    console::EventStatus::Failed,
                    format!("Batch install {} failed, {} succeeded (job: {})", job.failed.len(), job.completed.len(), &job_id[..8])
                ));
            } else {
                state.console.emit(console::ConsoleEvent::new(
                    console::EventCategory::Jobs,
                    console::EventStatus::Completed,
                    format!("Batch install {} services (job: {})", offerings_count, &job_id[..8])
                ));
            }
        }
    }

    tracing::info!(job_id, "Batch installation completed");
}

/// Remove an Installing entry from the registry on failure
///
/// Called when a service installation fails to clean up the placeholder entry
/// that was created before the installation job started.
async fn remove_installing_entry(state: &AppState, offering: &str) {
    let mut registry = state.registry.write().await;
    if let Some(pos) = registry.iter().position(|svc| svc.name == offering && svc.status == ServiceStatus::Installing) {
        registry.remove(pos);
        tracing::debug!(offering, "Removed Installing entry from registry after failure");
    }
    drop(registry);
    let _ = state.persist_registry().await;
    
    // Sync services to self_entry to reflect the removal
    state.sync_self_services(true).await;
}
