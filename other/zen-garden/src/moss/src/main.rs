mod api;
mod console;
mod discovery;
mod docker;
mod mdns;
mod metrics;
mod templates;

use axum::{
    extract::{Path, State},
    http::StatusCode,
    response::sse::{Event, KeepAlive, Sse},
    routing::{get, post},
    Json, Router,
};
use base64::Engine;
use docker::DockerManager;
use futures_util::stream::Stream;
use blake3;
use serde_json::json;
use std::collections::HashMap;
use std::convert::Infallible;
use std::net::SocketAddr;
use std::sync::Arc;
use templates::TemplateLoader;
use tokio::sync::RwLock;
use tokio_stream::wrappers::BroadcastStream;
use tokio_stream::StreamExt;
use tracing_subscriber::EnvFilter;
use garden_common::{
    error_codes, ApiError, ApiResponse, CpuCapabilities, DaemonHealthStatus, DiskCapabilities, ErrorDetails, 
    HardwareCapabilities, HardwareInventory, HealthCheck, ServiceHealthStatus, 
    MemoryCapabilities, MetricsSnapshot, Ports, RuntimeInfo, ServiceInfo, 
    ServiceStatus,
};

/// Create standardized error response with code, message, and optional details
/// 
/// Returns a tuple of (StatusCode, Json<ApiError>) for consistent error handling
fn error_response(
    status: StatusCode,
    code: &str,
    message: String,
    details: Option<HashMap<String, serde_json::Value>>,
) -> (StatusCode, Json<ApiError>) {
    (
        status,
        Json(ApiError {
            error: ErrorDetails {
                code: code.to_string(),
                message,
                details,
            },
        }),
    )
}

/// Create standardized error response as Json<Value> for endpoints that return Json<Value>
fn error_response_value(
    status: StatusCode,
    code: &str,
    message: String,
    details: Option<HashMap<String, serde_json::Value>>,
) -> (StatusCode, Json<serde_json::Value>) {
    let api_error = ApiError {
        error: ErrorDetails {
            code: code.to_string(),
            message,
            details,
        },
    };
    (
        status,
        Json(serde_json::to_value(api_error).unwrap()),
    )
}

/// Configuration loaded from moss.toml file
/// 
/// Priority order: CLI arguments > Environment variables > Config file > Defaults
/// 
/// Expected file format:
/// ```toml
/// stone_name = "stone-01"
/// port = 7185
/// log_level = "info"  # Options: trace, debug, info, warn, error
/// ```
/// 
/// File locations (first found wins):
/// - Linux: /etc/zen-garden/moss.toml
/// - Windows: ./moss.toml (current directory)
#[derive(Debug, Clone, serde::Deserialize)]
struct MossConfig {
    /// Stone name identifier - default: "stone-01"
    stone_name: Option<String>,
    
    /// HTTP server port - default: 7185
    port: Option<u16>,
    
    /// Log level (trace/debug/info/warn/error) - default: "info"
    log_level: Option<String>,
    
    /// Fast sync timeout in seconds for rapid offering deployments - default: None (disabled)
    fast_sync_timeout: Option<u64>,
}

impl MossConfig {
    /// Load configuration from platform-specific path
    /// 
    /// Searches for garden-moss.toml at:
    /// - Linux: /etc/zen-garden/garden-moss.toml
    /// - Windows: ./garden-moss.toml (current directory)
    /// 
    /// Returns None if file not found or contains errors (falls back to defaults)
    fn load() -> Option<Self> {
        let config_path = if cfg!(windows) {
            std::path::PathBuf::from(format!("./{}", garden_common::names::MOSS_CONFIG))
        } else {
            std::path::PathBuf::from(format!("{}/{}", garden_common::names::CONFIG_DIR, garden_common::names::MOSS_CONFIG))
        };

        match std::fs::read_to_string(&config_path) {
            Ok(content) => match toml::from_str::<MossConfig>(&content) {
                Ok(config) => {
                    tracing::info!(
                        path = ?config_path,
                        stone_name = ?config.stone_name,
                        port = ?config.port,
                        log_level = ?config.log_level,
                        fast_sync_timeout = ?config.fast_sync_timeout,
                        "Loaded configuration from file"
                    );
                    Some(config)
                },
                Err(e) => {
                    tracing::warn!(path = ?config_path, error = ?e, "Failed to parse config file");
                    None
                }
            },
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {
                tracing::debug!(path = ?config_path, "Config file not found, using defaults");
                None
            },
            Err(e) => {
                tracing::warn!(path = ?config_path, error = ?e, "Failed to read config file");
                None
            }
        }
    }
}

/// Internal struct for compatibility checking only
#[derive(Debug, Clone)]
struct CompatCheckCapabilities {
    cpu_model: Option<String>,
    cpu_features: Option<Vec<String>>,
    architecture: Option<String>,
    total_memory_mb: Option<u64>,
}

#[derive(Debug, Clone)]
enum CompatibilityDecision {
    Pass,
    Fallback { image: String, reason: String },
    Fail { reason: String, suggestion: Option<String> },
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
struct CompiledCompatibility {
    decision: String, // "pass" | "fallback" | "fail"
    #[serde(skip_serializing_if = "Option::is_none")]
    reason: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    original_image: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    fallback_image: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    suggestion: Option<String>,
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
struct CompiledOffering {
    name: String,
    category: String,
    description: String,
    #[serde(default)]
    tags: Vec<String>,
    image: String, // effective image after compatibility evaluation
    ports: Vec<(u16, u16)>,
    environment: Vec<String>,
    volumes: Vec<(String, String)>,
    compatibility: CompiledCompatibility,
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize, PartialEq, Eq)]
struct OfferingsFingerprint {
    moss_version: String,
    capabilities_hash: String,
    templates_hash: String,
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
struct OfferingsIndexCache {
    fingerprint: OfferingsFingerprint,
    generated_at: String,
    offerings: Vec<CompiledOffering>,
}

fn get_current_compat_capabilities() -> CompatCheckCapabilities {
    let (cpu_model, cpu_features, architecture) = metrics::get_cpu_info()
        .unwrap_or_else(|_| ("Unknown".to_string(), vec![], std::env::consts::ARCH.to_string()));
    let resources = metrics::collect_stone_resources().ok();
    let total_memory_mb = resources.as_ref().map(|r| r.memory.total_bytes / 1024 / 1024);

    CompatCheckCapabilities {
        cpu_model: Some(cpu_model),
        cpu_features: Some(cpu_features),
        architecture: Some(architecture),
        total_memory_mb,
    }
}

async fn load_registry_from_disk() -> anyhow::Result<Vec<ServiceInfo>> {
    let path = garden_common::names::MOSS_REGISTRY;
    match tokio::fs::read_to_string(path).await {
        Ok(content) => Ok(serde_json::from_str::<Vec<ServiceInfo>>(&content)?),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(Vec::new()),
        Err(e) => Err(e.into()),
    }
}

async fn persist_registry_to_disk(services: &[ServiceInfo]) -> anyhow::Result<()> {
    let dir = garden_common::names::CONFIG_DIR;
    let path = garden_common::names::MOSS_REGISTRY;
    tokio::fs::create_dir_all(dir).await?;

    let tmp_path = format!("{}.tmp", path);
    let content = serde_json::to_string_pretty(services)?;
    tokio::fs::write(&tmp_path, content).await?;

    match tokio::fs::rename(&tmp_path, path).await {
        Ok(_) => Ok(()),
        Err(e) => {
            // Windows won't rename over an existing file.
            if cfg!(windows) {
                let _ = tokio::fs::remove_file(path).await;
                tokio::fs::rename(&tmp_path, path).await?;
                Ok(())
            } else {
                Err(e.into())
            }
        }
    }
}

async fn persist_registry_state(state: &AppState) {
    let snapshot = {
        let reg = state.registry.read().await;
        reg.clone()
    };

    if let Err(e) = persist_registry_to_disk(&snapshot).await {
        tracing::warn!(error = ?e, "Failed to persist moss registry");
    }
}

async fn adopt_offering_container(state: &AppState, offering: &str) -> anyhow::Result<Option<ServiceInfo>> {
    // Only adopt if the offering maps to a known template (valid manifest/template).
    let mut template = match state.templates.load(offering) {
        Ok(t) => t,
        Err(_) => return Ok(None),
    };

    // Compute expected image based on compatibility rules.
    if let Some(rules) = &template.compatibility {
        let capabilities = get_current_compat_capabilities();
        match evaluate_compatibility(rules, &capabilities) {
            CompatibilityDecision::Pass => {}
            CompatibilityDecision::Fallback { image, .. } => template.image = image,
            CompatibilityDecision::Fail { .. } => {
                // Leave container alone, but adopt it as degraded/incompatible.
            }
        }
    }

    let status = state.docker.get_service_status(offering).await.unwrap_or(ServiceStatus::Unknown);
    let mut health = state
        .docker
        .get_service_health(offering)
        .await
        .unwrap_or(ServiceHealthStatus::Offline);

    let actual_image = state.docker.get_service_image(offering).await.unwrap_or_else(|_| "<unknown>".to_string());
    let expected_image = template.image.clone();

    // If the running image doesn't match what we'd expect (including compatibility fallback), mark degraded.
    if actual_image != "<unknown>" && actual_image != expected_image {
        health = ServiceHealthStatus::Degraded;
    }

    let native_port = template.ports.first().map(|(host, _)| *host).unwrap_or(30000);
    let version = actual_image.split(':').next_back().unwrap_or("latest").to_string();

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
    };

    Ok(Some(adopted))
}

async fn adopt_existing_containers(state: &AppState) {
    let existing = match state.docker.list_zen_containers().await {
        Ok(list) => list,
        Err(e) => {
            tracing::warn!(error = ?e, "Failed to list zen containers for adoption");
            return;
        }
    };

    let mut changed = false;

    for offering in existing {
        let already = {
            let reg = state.registry.read().await;
            reg.iter().any(|s| s.name == offering)
        };
        if already {
            continue;
        }

        match adopt_offering_container(state, &offering).await {
            Ok(Some(info)) => {
                tracing::info!(offering = %offering, "Adopting existing zen-offering container into registry");
                let mut reg = state.registry.write().await;
                reg.push(info);
                changed = true;
            }
            Ok(None) => {
                tracing::warn!(offering = %offering, "Found zen-offering container but no matching template; leaving unregistered");
            }
            Err(e) => {
                tracing::warn!(offering = %offering, error = ?e, "Failed to adopt existing container; leaving it alone");
            }
        }
    }

    if changed {
        persist_registry_state(state).await;
    }
}

#[derive(Clone, Debug, serde::Serialize, serde::Deserialize)]
enum JobStatus {
    Pending,
    Running,
    Completed,
    Failed,
}

#[derive(Clone, Debug, serde::Serialize)]
struct Job {
    id: String,
    offerings: Vec<String>,
    status: JobStatus,
    completed: Vec<String>,
    failed: HashMap<String, String>, // service -> error message
    started_at: std::time::SystemTime,
    completed_at: Option<std::time::SystemTime>,
}

#[derive(Debug, serde::Deserialize)]
struct PreInstallManifest {
    offerings: Vec<String>,
    auto_install: bool,
}

#[derive(Clone, Debug, serde::Serialize)]
struct MossEvent {
    timestamp: String,
    level: String,
    message: String,
    job_id: Option<String>,
}

#[derive(Clone)]
struct AppState {
    stone_name: String,
    registry: Arc<RwLock<Vec<ServiceInfo>>>,
    docker: Arc<DockerManager>,
    templates: Arc<TemplateLoader>,
    jobs: Arc<RwLock<HashMap<String, Job>>>,
    event_tx: tokio::sync::broadcast::Sender<MossEvent>,
    shutdown_tx: Arc<tokio::sync::Notify>,
    start_time: std::time::Instant,
    offerings_index: Arc<RwLock<Option<OfferingsIndexCache>>>,
}

fn moss_version_string() -> String {
    // build.rs injects BUILD_NUMBER (see src/moss/src/discovery.rs)
    format!("{}.{}", env!("CARGO_PKG_VERSION"), env!("BUILD_NUMBER"))
}

fn blake3_hex(bytes: &[u8]) -> String {
    blake3::hash(bytes).to_hex().to_string()
}

fn current_capabilities_hash() -> String {
    let caps = get_current_compat_capabilities();
    let payload = serde_json::json!({
        "cpu_model": caps.cpu_model,
        "cpu_features": caps.cpu_features,
        "architecture": caps.architecture,
        "total_memory_mb": caps.total_memory_mb,
    });
    blake3_hex(serde_json::to_vec(&payload).unwrap_or_default().as_slice())
}

async fn templates_hash(state: &AppState) -> anyhow::Result<String> {
    let templates = state.templates.list_templates()?;
    let mut hasher = blake3::Hasher::new();

    // Include moss version in the template hash input so schema/template parsing changes
    // can't accidentally reuse an old cache.
    hasher.update(moss_version_string().as_bytes());

    // Hash each offering's effective config in stable order.
    let mut templates = templates;
    templates.sort_by(|a, b| a.name.cmp(&b.name));
    for t in templates {
        let template = state.templates.load(&t.name)?;
        let payload = serde_json::json!({
            "name": t.name,
            "category": t.category,
            "description": t.description,
            "tags": t.tags,
            "image": template.image,
            "ports": template.ports,
            "environment": template.environment,
            "volumes": template.volumes,
            "compatibility": template.compatibility,
        });
        hasher.update(serde_json::to_vec(&payload).unwrap_or_default().as_slice());
    }

    Ok(hasher.finalize().to_hex().to_string())
}

fn compile_compatibility(
    template: &mut templates::ServiceTemplate,
) -> CompiledCompatibility {
    if let Some(rules) = &template.compatibility {
        let capabilities = get_current_compat_capabilities();
        match evaluate_compatibility(rules, &capabilities) {
            CompatibilityDecision::Pass => CompiledCompatibility {
                decision: "pass".to_string(),
                reason: None,
                original_image: None,
                fallback_image: None,
                suggestion: None,
            },
            CompatibilityDecision::Fallback { image, reason } => {
                let original_image = template.image.clone();
                template.image = image.clone();
                CompiledCompatibility {
                    decision: "fallback".to_string(),
                    reason: Some(reason),
                    original_image: Some(original_image),
                    fallback_image: Some(image),
                    suggestion: None,
                }
            }
            CompatibilityDecision::Fail { reason, suggestion } => CompiledCompatibility {
                decision: "fail".to_string(),
                reason: Some(reason),
                original_image: Some(template.image.clone()),
                fallback_image: None,
                suggestion,
            },
        }
    } else {
        CompiledCompatibility {
            decision: "pass".to_string(),
            reason: None,
            original_image: None,
            fallback_image: None,
            suggestion: None,
        }
    }
}

async fn load_offerings_index_from_disk() -> anyhow::Result<Option<OfferingsIndexCache>> {
    let path = garden_common::names::MOSS_OFFERINGS_INDEX;
    match tokio::fs::read_to_string(path).await {
        Ok(content) => Ok(Some(serde_json::from_str::<OfferingsIndexCache>(&content)?)),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(None),
        Err(e) => Err(e.into()),
    }
}

async fn persist_offerings_index_to_disk(cache: &OfferingsIndexCache) -> anyhow::Result<()> {
    let dir = garden_common::names::CONFIG_DIR;
    let path = garden_common::names::MOSS_OFFERINGS_INDEX;
    tokio::fs::create_dir_all(dir).await?;

    let tmp_path = format!("{}.tmp", path);
    let content = serde_json::to_string_pretty(cache)?;
    tokio::fs::write(&tmp_path, content).await?;

    match tokio::fs::rename(&tmp_path, path).await {
        Ok(_) => Ok(()),
        Err(e) => {
            // Windows won't rename over an existing file.
            if cfg!(windows) {
                let _ = tokio::fs::remove_file(path).await;
                tokio::fs::rename(&tmp_path, path).await?;
                Ok(())
            } else {
                Err(e.into())
            }
        }
    }
}

async fn rebuild_offerings_index(state: &AppState) -> anyhow::Result<OfferingsIndexCache> {
    let mut templates = state.templates.list_templates()?;
    templates.sort_by(|a, b| a.name.cmp(&b.name));

    let fingerprint = OfferingsFingerprint {
        moss_version: moss_version_string(),
        capabilities_hash: current_capabilities_hash(),
        templates_hash: templates_hash(state).await?,
    };

    let mut offerings = Vec::with_capacity(templates.len());
    for t in templates {
        let mut template = state.templates.load(&t.name)?;
        let compatibility = compile_compatibility(&mut template);

        offerings.push(CompiledOffering {
            name: t.name,
            category: t.category,
            description: t.description,
            tags: t.tags,
            image: template.image,
            ports: template.ports,
            environment: template.environment,
            volumes: template.volumes,
            compatibility,
        });
    }

    Ok(OfferingsIndexCache {
        fingerprint,
        generated_at: chrono::Utc::now().to_rfc3339(),
        offerings,
    })
}

async fn ensure_offerings_index(state: &AppState, force_rebuild: bool) -> anyhow::Result<()> {
    if !force_rebuild {
        let existing = state.offerings_index.read().await;
        if existing.is_some() {
            return Ok(());
        }
    }

    // Try disk cache first (best-effort)
    if !force_rebuild {
        if let Some(on_disk) = load_offerings_index_from_disk().await? {
            let current = OfferingsFingerprint {
                moss_version: moss_version_string(),
                capabilities_hash: current_capabilities_hash(),
                templates_hash: templates_hash(state).await?,
            };

            if on_disk.fingerprint == current {
                *state.offerings_index.write().await = Some(on_disk);
                return Ok(());
            }
        }
    }

    let rebuilt = rebuild_offerings_index(state).await?;
    persist_offerings_index_to_disk(&rebuilt).await?;
    *state.offerings_index.write().await = Some(rebuilt);
    Ok(())
}

async fn get_compiled_offering(state: &AppState, offering: &str) -> anyhow::Result<Option<CompiledOffering>> {
    ensure_offerings_index(state, false).await?;
    let guard = state.offerings_index.read().await;
    Ok(guard
        .as_ref()
        .and_then(|idx| idx.offerings.iter().find(|o| o.name == offering).cloned()))
}

fn emit_event(state: &AppState, level: &str, message: String, job_id: Option<String>) {
    let event = MossEvent {
        timestamp: chrono::Utc::now().to_rfc3339(),
        level: level.to_string(),
        message,
        job_id,
    };
    
    // Send to broadcast channel (ignore if no receivers)
    let _ = state.event_tx.send(event.clone());
    
    // Also log to tracing
    match level {
        "error" => tracing::error!("{}", event.message),
        "warn" => tracing::warn!("{}", event.message),
        "debug" => tracing::debug!("{}", event.message),
        _ => tracing::info!("{}", event.message),
    }
}

async fn stream_events(
    State(state): State<AppState>,
) -> Sse<impl Stream<Item = Result<Event, Infallible>>> {
    let rx = state.event_tx.subscribe();
    let stream = BroadcastStream::new(rx)
        .filter_map(|result| match result {
            Ok(event) => Some(Ok::<MossEvent, tokio_stream::wrappers::errors::BroadcastStreamRecvError>(event)),
            Err(tokio_stream::wrappers::errors::BroadcastStreamRecvError::Lagged(n)) => {
                tracing::warn!("SSE client lagged {} messages", n);
                None
            }
        })
        .map(|event_result| {
            let event = event_result.unwrap();
            let data = serde_json::to_string(&event).unwrap_or_default();
            Event::default()
                .event("moss-event")
                .data(data)
        })
        .map(Ok);

    Sse::new(stream).keep_alive(KeepAlive::default())
}

async fn stream_logs(
    Path(service): Path<String>,
    State(state): State<AppState>,
) -> Result<Sse<impl Stream<Item = Result<Event, Infallible>>>, (StatusCode, Json<ApiError>)> {
    // Check if service exists
    let registry = state.registry.read().await;
    let service_exists = registry.iter().any(|svc| svc.name == service);
    let service_status = registry.iter().find(|svc| svc.name == service).map(|s| s.status.clone());
    drop(registry);

    if !service_exists {
        let mut details = HashMap::new();
        details.insert("service_name".to_string(), serde_json::json!(service));
        return Err(error_response(
            StatusCode::NOT_FOUND,
            error_codes::SERVICE_NOT_FOUND,
            format!("Service '{}' not found", service),
            Some(details),
        ));
    }

    // Check if container is running
    if let Some(status) = service_status {
        if status != ServiceStatus::Running {
            let mut details = HashMap::new();
            details.insert("service_name".to_string(), serde_json::json!(service));
            details.insert("current_status".to_string(), serde_json::json!(format!("{:?}", status)));
            return Err(error_response(
                StatusCode::BAD_REQUEST,
                error_codes::CONTAINER_NOT_RUNNING,
                format!("Service '{}' is not running", service),
                Some(details),
            ));
        }
    }

    // Get logs stream from Docker
    let logs_stream = state.docker.get_logs_stream(&service, false);
    let service_clone = service.clone();

    // Convert to SSE events
    let stream = logs_stream
        .map(move |log_result| {
            match log_result {
                Ok(log_line) => {
                    let data = serde_json::to_string(&log_line).unwrap_or_default();
                    Event::default().event("log").data(data)
                }
                Err(e) => {
                    tracing::warn!(service = %service_clone, error = ?e, "Log stream error");
                    Event::default()
                        .event("error")
                        .data(format!("Log stream error: {}", e))
                }
            }
        })
        .map(Ok);

    Ok(Sse::new(stream).keep_alive(KeepAlive::default()))
}

async fn check_docker_health(state: &AppState) -> HealthCheck {
    if state.docker.is_healthy().await {
        HealthCheck {
            status: "pass".to_string(),
            message: None,
        }
    } else {
        HealthCheck {
            status: "fail".to_string(),
            message: Some("Docker daemon unavailable".to_string()),
        }
    }
}

fn check_disk_health() -> HealthCheck {
    match metrics::collect_stone_resources() {
        Ok(resources) => {
            let available_percent = (resources.disk.available_bytes as f32 / resources.disk.total_bytes as f32) * 100.0;
            if available_percent < 10.0 {
                HealthCheck {
                    status: "warn".to_string(),
                    message: Some(format!(
                        "Low disk space: {:.1}% free ({} available)",
                        available_percent,
                        resources.disk.available_friendly
                    )),
                }
            } else {
                HealthCheck {
                    status: "pass".to_string(),
                    message: None,
                }
            }
        }
        Err(e) => HealthCheck {
            status: "fail".to_string(),
            message: Some(format!("Failed to check disk: {}", e)),
        },
    }
}

fn check_memory_health() -> HealthCheck {
    match metrics::collect_stone_resources() {
        Ok(resources) => {
            if resources.memory.used_percent > 90.0 {
                HealthCheck {
                    status: "warn".to_string(),
                    message: Some(format!(
                        "High memory usage: {:.1}% ({} used of {})",
                        resources.memory.used_percent,
                        resources.memory.used_friendly,
                        resources.memory.total_friendly
                    )),
                }
            } else {
                HealthCheck {
                    status: "pass".to_string(),
                    message: None,
                }
            }
        }
        Err(e) => HealthCheck {
            status: "fail".to_string(),
            message: Some(format!("Failed to check memory: {}", e)),
        },
    }
}

async fn health(State(state): State<AppState>) -> (StatusCode, Json<DaemonHealthStatus>) {
    let docker_check = check_docker_health(&state).await;
    let disk_check = check_disk_health();
    let memory_check = check_memory_health();

    // Build legacy checks HashMap for backward compatibility
    let mut checks = HashMap::new();
    checks.insert("docker".to_string(), docker_check.clone());
    checks.insert("disk".to_string(), disk_check.clone());
    checks.insert("memory".to_string(), memory_check.clone());

    // Build new components with detailed information
    let mut components = HashMap::new();
    
    // Docker component
    let docker_component = build_docker_component(&state).await;
    components.insert("docker".to_string(), docker_component);
    
    // Disk component
    let disk_component = build_disk_component();
    components.insert("disk".to_string(), disk_component);
    
    // Memory component
    let memory_component = build_memory_component();
    components.insert("memory".to_string(), memory_component);

    // Determine overall status based on worst component status
    let overall_status = determine_overall_status(&components);
    
    // HTTP status code based on overall status
    let http_status = match overall_status.as_str() {
        "unhealthy" => StatusCode::SERVICE_UNAVAILABLE,
        _ => StatusCode::OK,
    };

    // Legacy boolean flags for backward compatibility
    let docker_ok = docker_check.status == "pass";
    let disk_ok = disk_check.status != "fail";
    let memory_ok = memory_check.status != "fail";
    let uptime_seconds = state.start_time.elapsed().as_secs();

    (
        http_status,
        Json(DaemonHealthStatus {
            status: overall_status,
            timestamp: chrono::Utc::now().to_rfc3339(),
            components,
            docker_available: docker_ok,
            disk_space_ok: disk_ok,
            memory_ok,
            uptime_seconds,
            checks,
        })
    )
}

async fn build_docker_component(state: &AppState) -> garden_common::ComponentHealth {
    let mut details = HashMap::new();
    
    if state.docker.is_healthy().await {
        details.insert("available".to_string(), serde_json::json!(true));
        garden_common::ComponentHealth::healthy(details)
    } else {
        details.insert("available".to_string(), serde_json::json!(false));
        garden_common::ComponentHealth::unhealthy(details)
    }
}

fn build_disk_component() -> garden_common::ComponentHealth {
    let mut details = HashMap::new();
    
    match metrics::collect_stone_resources() {
        Ok(resources) => {
            let total_gb = resources.disk.total_bytes as f64 / 1_073_741_824.0;
            let free_gb = resources.disk.available_bytes as f64 / 1_073_741_824.0;
            let usage_percent = resources.disk.used_percent;
            
            details.insert("free_gb".to_string(), serde_json::json!(format!("{:.1}", free_gb)));
            details.insert("total_gb".to_string(), serde_json::json!(format!("{:.1}", total_gb)));
            details.insert("usage_percent".to_string(), serde_json::json!(format!("{:.2}", usage_percent)));
            
            // Thresholds: >95% unhealthy, >80% degraded, else healthy
            if usage_percent > 95.0 {
                garden_common::ComponentHealth::unhealthy(details)
            } else if usage_percent > 80.0 {
                garden_common::ComponentHealth::degraded(details)
            } else {
                garden_common::ComponentHealth::healthy(details)
            }
        }
        Err(_) => {
            details.insert("error".to_string(), serde_json::json!("Unable to collect disk metrics"));
            garden_common::ComponentHealth::unhealthy(details)
        }
    }
}

fn build_memory_component() -> garden_common::ComponentHealth {
    let mut details = HashMap::new();
    
    match metrics::collect_stone_resources() {
        Ok(resources) => {
            let total_gb = resources.memory.total_bytes as f64 / 1_073_741_824.0;
            let available_gb = resources.memory.available_bytes as f64 / 1_073_741_824.0;
            let usage_percent = resources.memory.used_percent;
            
            details.insert("available_gb".to_string(), serde_json::json!(format!("{:.1}", available_gb)));
            details.insert("total_gb".to_string(), serde_json::json!(format!("{:.1}", total_gb)));
            details.insert("usage_percent".to_string(), serde_json::json!(format!("{:.2}", usage_percent)));
            
            // Thresholds: >95% unhealthy, >85% degraded, else healthy
            if usage_percent > 95.0 {
                garden_common::ComponentHealth::unhealthy(details)
            } else if usage_percent > 85.0 {
                garden_common::ComponentHealth::degraded(details)
            } else {
                garden_common::ComponentHealth::healthy(details)
            }
        }
        Err(_) => {
            details.insert("error".to_string(), serde_json::json!("Unable to collect memory metrics"));
            garden_common::ComponentHealth::unhealthy(details)
        }
    }
}

fn determine_overall_status(components: &HashMap<String, garden_common::ComponentHealth>) -> String {
    // Overall status is worst component status: unhealthy > degraded > healthy
    let mut has_unhealthy = false;
    let mut has_degraded = false;
    
    for component in components.values() {
        match component.status.as_str() {
            "unhealthy" => has_unhealthy = true,
            "degraded" => has_degraded = true,
            _ => {}
        }
    }
    
    if has_unhealthy {
        "unhealthy".to_string()
    } else if has_degraded {
        "degraded".to_string()
    } else {
        "healthy".to_string()
    }
}

/// GET /capabilities - Static hardware inventory
async fn capabilities(State(state): State<AppState>) -> Json<ApiResponse<HardwareCapabilities>> {
    let (cpu_model, cpu_features, architecture) = metrics::get_cpu_info()
        .unwrap_or_else(|_| ("Unknown".to_string(), vec![], std::env::consts::ARCH.to_string()));
    
    let resources = metrics::collect_stone_resources().ok();
    let total_memory_mb = resources.as_ref()
        .map(|r| r.memory.total_bytes / 1024 / 1024)
        .unwrap_or(0);
    
    let gpus = metrics::detect_gpus();
    
    let disk = resources.as_ref().map(|r| DiskCapabilities {
        total_gb: r.disk.total_bytes / 1024 / 1024 / 1024,
        disk_type: metrics::detect_disk_type_for_mount(&r.disk.path),
    });
    
    let cores = resources.as_ref().map(|r| r.cpu.cores).unwrap_or(1);
    
    let caps = HardwareCapabilities {
        stone_name: state.stone_name.clone(),
        hardware: HardwareInventory {
            cpu: CpuCapabilities {
                model: if cpu_model == "Unknown" { None } else { Some(cpu_model) },
                cores,
                threads: None, // TODO: Detect thread count
                architecture,
                features: if cpu_features.is_empty() { None } else { Some(cpu_features) },
            },
            memory: MemoryCapabilities {
                total_mb: total_memory_mb,
            },
            gpus,
            disk,
        },
        runtime: Some(RuntimeInfo {
            docker_version: None, // TODO: Query Docker version
            os: std::env::consts::OS.to_string(),
            kernel: None, // TODO: Get kernel version
        }),
    };
    
    Json(ApiResponse {
        data: caps,
        suggestions: None,
    })
}

/// GET /metrics - Dynamic performance snapshot
async fn get_metrics() -> Json<ApiResponse<MetricsSnapshot>> {
    let resources = metrics::collect_stone_resources()
        .unwrap_or_else(|_| {
            // Fallback with minimal data
            garden_common::StoneResources {
                cpu: garden_common::CpuMetrics {
                    cores: 1,
                    usage_percent: 0.0,
                    usage_friendly: "0%".to_string(),
                },
                memory: garden_common::MemoryMetrics {
                    total_bytes: 0,
                    used_bytes: 0,
                    available_bytes: 0,
                    used_percent: 0.0,
                    total_friendly: "0 B".to_string(),
                    used_friendly: "0 B".to_string(),
                    available_friendly: "0 B".to_string(),
                },
                disk: garden_common::DiskMetrics {
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
        });
    
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

/// Evaluate compatibility rules and determine if fallback is needed
fn evaluate_compatibility(
    rules: &garden_common::CompatibilityRules,
    capabilities: &CompatCheckCapabilities,
) -> CompatibilityDecision {
    // Evaluate each rule in order (first match wins). A rule matches only if
    // all specified condition fields match (AND semantics).
    for rule in &rules.compatibility_rules {
        let condition = &rule.condition;
        let mut matches = true;

        if let Some(models) = &condition.processor_models {
            // Exact match against CPU model string (case-sensitive, since most
            // model strings are already normalized by the source).
            let ok = capabilities
                .cpu_model
                .as_ref()
                .map(|cpu_model| models.iter().any(|model| cpu_model == model))
                .unwrap_or(false);
            matches &= ok;
        }

        if let Some(patterns) = &condition.processor_patterns {
            let ok = capabilities
                .cpu_model
                .as_ref()
                .map(|cpu_model| patterns.iter().any(|pattern| cpu_model.contains(pattern)))
                .unwrap_or(false);
            matches &= ok;
        }

        if let Some(required_missing) = &condition.cpu_features_missing {
            // Match when any of the listed features are missing.
            let ok = capabilities
                .cpu_features
                .as_ref()
                .map(|cpu_features| required_missing.iter().any(|f| !cpu_features.contains(f)))
                // If we couldn't detect CPU features, don't assume they're missing.
                .unwrap_or(false);
            matches &= ok;
        }

        if let Some(architectures) = &condition.architectures {
            let ok = capabilities
                .architecture
                .as_ref()
                .map(|arch| architectures.contains(arch))
                .unwrap_or(false);
            matches &= ok;
        }

        if let Some(max_memory_mb) = condition.memory_mb_less_than {
            let ok = capabilities
                .total_memory_mb
                .map(|total_memory| total_memory < max_memory_mb)
                .unwrap_or(false);
            matches &= ok;
        }

        if matches {
            if let Some(fallback) = &rule.fallback {
                return CompatibilityDecision::Fallback {
                    image: fallback.image.clone(),
                    reason: rule.reason.clone(),
                };
            }

            return CompatibilityDecision::Fail {
                reason: rule.reason.clone(),
                suggestion: rule.suggestion.clone(),
            };
        }
    }

    CompatibilityDecision::Pass
}

/// Validate ELF binary architecture matches system
fn validate_binary_architecture(binary_data: &[u8]) -> anyhow::Result<String> {
    use anyhow::bail;
    
    // ELF header structure:
    // 0x00-03: Magic (\x7fELF)
    // 0x04: Class (1=32-bit, 2=64-bit)
    // 0x12-13: Machine type (little-endian u16)
    
    if binary_data.len() < 20 {
        bail!("Binary too small (expected at least 20 bytes for ELF header)");
    }
    
    if &binary_data[0..4] != b"\x7fELF" {
        bail!("Not a valid ELF binary (invalid magic bytes)");
    }
    
    let machine_type = u16::from_le_bytes([binary_data[0x12], binary_data[0x13]]);
    let arch = match machine_type {
        0x3E => "x86_64",
        0xB7 => "aarch64",
        0x28 => "arm",
        _ => bail!("Unsupported architecture: machine type {:#x}", machine_type),
    };
    
    // Compare with system architecture
    let system_arch = std::env::consts::ARCH;
    if arch != system_arch {
        bail!(
            "Architecture mismatch: binary is {}, but system is {}",
            arch,
            system_arch
        );
    }
    
    Ok(arch.to_string())
}

#[derive(serde::Deserialize)]
struct RefreshPayload {
    component: String,
    binary_data: String, // base64-encoded
}

async fn refresh_component(
    State(state): State<AppState>,
    Json(payload): Json<RefreshPayload>,
) -> (StatusCode, Json<serde_json::Value>) {
    tracing::info!(component = %payload.component, "Binary refresh requested");
    
    // Decode base64 binary data
    let binary_data = match base64::engine::general_purpose::STANDARD.decode(&payload.binary_data) {
        Ok(data) => data,
        Err(e) => {
            tracing::error!(error = ?e, "Failed to decode base64 binary data");
            return (
                StatusCode::BAD_REQUEST,
                Json(serde_json::json!({
                    "status": "error",
                    "message": "Invalid base64 encoding",
                    "error": format!("{}", e),
                })),
            );
        }
    };
    
    // Validate architecture
    let arch = match validate_binary_architecture(&binary_data) {
        Ok(a) => a,
        Err(e) => {
            tracing::error!(error = ?e, "Binary validation failed");
            return (
                StatusCode::BAD_REQUEST,
                Json(serde_json::json!({
                    "status": "error",
                    "message": "Binary validation failed",
                    "error": format!("{}", e),
                })),
            );
        }
    };
    
    tracing::info!(
        component = %payload.component,
        architecture = %arch,
        size = binary_data.len(),
        "Binary validated successfully"
    );
    
    // Determine target path based on component
    // Write to staging directory, systemd helper will copy to final location
    let staging_dir = format!("{}/bin", garden_common::names::STONE_HOME);
    let target_path = match payload.component.as_str() {
        garden_common::names::MOSS_BINARY => format!("{}/{}.staged", staging_dir, garden_common::names::MOSS_BINARY),
        "garden-rake" => format!("{}/{}.staged", staging_dir, garden_common::names::RAKE_BINARY),
        _ => {
            tracing::warn!(component = %payload.component, "Unknown component");
            let mut details = HashMap::new();
            details.insert("component".to_string(), serde_json::json!(payload.component));
            details.insert("valid_components".to_string(), serde_json::json!([garden_common::names::MOSS_BINARY, garden_common::names::RAKE_BINARY]));
            return (
                StatusCode::BAD_REQUEST,
                Json(serde_json::to_value(ApiError {
                    error: ErrorDetails {
                        code: error_codes::INVALID_COMPONENT.to_string(),
                        message: format!("Unknown component: {}", payload.component),
                        details: Some(details),
                    },
                }).unwrap()),
            );
        }
    };
    
    // Ensure staging directory exists
    if let Err(e) = std::fs::create_dir_all(&staging_dir) {
        tracing::error!(error = ?e, dir = %staging_dir, "Failed to create staging directory");
        let mut details = HashMap::new();
        details.insert("directory".to_string(), serde_json::json!(staging_dir));
        details.insert("io_error".to_string(), serde_json::json!(format!("{}", e)));
        return (
            StatusCode::INTERNAL_SERVER_ERROR,
            Json(serde_json::to_value(ApiError {
                error: ErrorDetails {
                    code: error_codes::INSUFFICIENT_RESOURCES.to_string(),
                    message: "Failed to create staging directory".to_string(),
                    details: Some(details),
                },
            }).unwrap()),
        );
    }
    
    // Write to temporary location
    let temp_path = format!("{}.tmp", target_path);
    if let Err(e) = std::fs::write(&temp_path, &binary_data) {
        tracing::error!(error = ?e, temp_path = %temp_path, "Failed to write binary");
        let mut details = HashMap::new();
        details.insert("temp_path".to_string(), serde_json::json!(temp_path));
        details.insert("io_error".to_string(), serde_json::json!(format!("{}", e)));
        return (
            StatusCode::INTERNAL_SERVER_ERROR,
            Json(serde_json::to_value(ApiError {
                error: ErrorDetails {
                    code: error_codes::INSUFFICIENT_RESOURCES.to_string(),
                    message: "Failed to write binary file".to_string(),
                    details: Some(details),
                },
            }).unwrap()),
        );
    }
    
    // Make executable (Unix only)
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        if let Err(e) = std::fs::set_permissions(&temp_path, std::fs::Permissions::from_mode(0o755)) {
            tracing::error!(error = ?e, temp_path = %temp_path, "Failed to set permissions");
            let _ = std::fs::remove_file(&temp_path);
            let mut details = HashMap::new();
            details.insert("temp_path".to_string(), serde_json::json!(temp_path));
            details.insert("io_error".to_string(), serde_json::json!(format!("{}", e)));
            return (
                StatusCode::INTERNAL_SERVER_ERROR,
                Json(serde_json::to_value(ApiError {
                    error: ErrorDetails {
                        code: error_codes::INSUFFICIENT_RESOURCES.to_string(),
                        message: "Failed to set executable permissions".to_string(),
                        details: Some(details),
                    },
                }).unwrap()),
            );
        }
    }
    
    // Atomic move to staged location
    if let Err(e) = std::fs::rename(&temp_path, &target_path) {
        tracing::error!(error = ?e, target = %target_path, "Failed to move binary");
        let _ = std::fs::remove_file(&temp_path);
        let mut details = HashMap::new();
        details.insert("target_path".to_string(), serde_json::json!(target_path));
        details.insert("io_error".to_string(), serde_json::json!(format!("{}", e)));
        return (
            StatusCode::INTERNAL_SERVER_ERROR,
            Json(serde_json::to_value(ApiError {
                error: ErrorDetails {
                    code: error_codes::INSUFFICIENT_RESOURCES.to_string(),
                    message: "Failed to stage binary".to_string(),
                    details: Some(details),
                },
            }).unwrap()),
        );
    }
    
    tracing::info!(component = %payload.component, path = %target_path, "Binary staged successfully");
    
    // If updating moss itself, trigger service restart
    if payload.component == garden_common::names::MOSS_BINARY {
        emit_event(
            &state,
            "info",
            format!("{} binary staged. Restarting service to apply update...", payload.component),
            None,
        );
        
        // Trigger service restart (platform-specific)
        tokio::spawn(async move {
            tokio::time::sleep(tokio::time::Duration::from_secs(1)).await;
            tracing::info!("Triggering service restart for binary update");
            
            #[cfg(target_os = "windows")]
            {
                // Try to restart via Windows Services
                let output = std::process::Command::new("sc")
                    .args(["stop", "ZenGardenMoss"])
                    .output();
                
                match output {
                    Ok(result) if result.status.success() => {
                        // Wait briefly then start
                        tokio::time::sleep(tokio::time::Duration::from_secs(2)).await;
                        let _ = std::process::Command::new("sc")
                            .args(["start", "ZenGardenMoss"])
                            .output();
                        tracing::info!("Windows service restart triggered");
                    }
                    Ok(result) => {
                        tracing::warn!(
                            exit_code = ?result.status.code(),
                            stderr = %String::from_utf8_lossy(&result.stderr),
                            "Failed to trigger Windows service restart"
                        );
                    }
                    Err(e) => {
                        tracing::error!(error = ?e, "Failed to execute sc command");
                    }
                }
            }
            
            #[cfg(not(target_os = "windows"))]
            {
                // Try to restart via systemd
                let output = std::process::Command::new("sudo")
                    .args(&["systemctl", "restart", garden_common::names::MOSS_SERVICE.trim_end_matches(".service")])
                    .output();
                
                match output {
                    Ok(result) if result.status.success() => {
                        tracing::info!("Service restart triggered successfully");
                    }
                    Ok(result) => {
                        tracing::warn!(
                            exit_code = ?result.status.code(),
                            stderr = %String::from_utf8_lossy(&result.stderr),
                            "Failed to trigger service restart"
                        );
                    }
                    Err(e) => {
                        tracing::error!(error = ?e, "Failed to execute systemctl");
                    }
                }
            }
        });
        
        (
            StatusCode::ACCEPTED,
            Json(serde_json::json!({
                "status": "accepted",
                "message": format!("{} binary staged successfully. Service restart initiated.", payload.component),
                "component": payload.component,
                "architecture": arch,
                "staged_path": target_path,
            })),
        )
    } else {
        emit_event(
            &state,
            "info",
            format!("{} binary staged successfully", payload.component),
            None,
        );
        
        (
            StatusCode::OK,
            Json(serde_json::json!({
                "status": "success",
                "message": format!("{} binary staged successfully", payload.component),
                "component": payload.component,
                "architecture": arch,
                "staged_path": target_path,
            })),
        )
    }
}

#[derive(Debug, serde::Deserialize)]
struct ReconcileRequest {
    /// If true, remove any zen-offering-* containers that do not map to a known template.
    #[serde(default)]
    drop_invalid: bool,
}

/// POST /api/system/reconcile - Trigger immediate registry reconcile + adoption.
///
/// Behavior:
/// - Always attempts non-destructive adoption of existing zen-offering containers.
/// - If `drop_invalid=true`, containers with no matching offering template are removed.
async fn reconcile_now(
    State(state): State<AppState>,
    Json(payload): Json<ReconcileRequest>,
) -> (StatusCode, Json<serde_json::Value>) {
    let existing = match state.docker.list_zen_containers().await {
        Ok(list) => list,
        Err(e) => {
            let mut details = HashMap::new();
            details.insert("docker_error".to_string(), serde_json::json!(format!("{}", e)));
            return error_response_value(
                StatusCode::INTERNAL_SERVER_ERROR,
                error_codes::DOCKER_ERROR,
                "Failed to list zen containers".to_string(),
                Some(details),
            );
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

        match adopt_offering_container(&state, &offering).await {
            Ok(Some(info)) => {
                let mut reg = state.registry.write().await;
                reg.push(info);
                adopted.push(offering);
            }
            Ok(None) => {
                // "Invalid" in this context means: zen-offering-* container exists, but we have
                // no known template/manifest mapping for that offering.
                if payload.drop_invalid {
                    match state.docker.remove_service(&offering).await {
                        Ok(_) => dropped_invalid.push(offering),
                        Err(e) => {
                            tracing::warn!(offering = %offering, error = ?e, "Failed to drop invalid container; leaving it alone");
                            left_unregistered.push(offering);
                        }
                    }
                } else {
                    left_unregistered.push(offering);
                }
            }
            Err(e) => {
                tracing::warn!(offering = %offering, error = ?e, "Adoption failed; leaving it alone");
                left_unregistered.push(offering);
            }
        }
    }

    if !adopted.is_empty() || !dropped_invalid.is_empty() {
        persist_registry_state(&state).await;
    }

    (
        StatusCode::OK,
        Json(serde_json::json!({
            "status": "ok",
            "adopted": adopted,
            "dropped_invalid": dropped_invalid,
            "skipped_existing": skipped_existing,
            "left_unregistered": left_unregistered,
        })),
    )
}

async fn get_job_status(
    Path(job_id): Path<String>,
    State(state): State<AppState>,
) -> (StatusCode, Json<ApiResponse<Job>>) {
    let jobs = state.jobs.read().await;
    match jobs.get(&job_id) {
        Some(job) => (StatusCode::OK, Json(ApiResponse {
            data: job.clone(),
            suggestions: None,
        })),
        None => {
            let mut details = HashMap::new();
            details.insert("job_id".to_string(), serde_json::json!(job_id));
            return (StatusCode::NOT_FOUND, Json(ApiResponse {
                data: Job {
                    id: job_id.clone(),
                    status: JobStatus::Failed,
                    offerings: vec![],
                    completed: vec![],
                    failed: HashMap::new(),
                    started_at: std::time::SystemTime::now(),
                    completed_at: Some(std::time::SystemTime::now()),
                },
                suggestions: Some(vec!["Check job ID is correct".to_string()]),
            }));
        }
    }
}

async fn list_jobs(State(state): State<AppState>) -> (StatusCode, Json<ApiResponse<Vec<Job>>>) {
    let jobs = state.jobs.read().await;
    let job_list: Vec<Job> = jobs.values().cloned().collect();
    (StatusCode::OK, Json(ApiResponse {
        data: job_list,
        suggestions: None,
    }))
}

// Background task: install single service
async fn install_service_task(state: &AppState, job_id: &str, offering: &str) {
    // Update job status to Running
    {
        let mut jobs = state.jobs.write().await;
        if let Some(job) = jobs.get_mut(job_id) {
            job.status = JobStatus::Running;
        }
    }

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
            emit_event(
                state,
                "error",
                format!("Offering not found: {}", offering),
                Some(job_id.to_string()),
            );
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
        emit_event(
            state,
            "error",
            format!("Compatibility validation failed: {}", reason),
            Some(job_id.to_string()),
        );

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
        )
        .await
    {
        emit_event(state, "error", format!("Installation failed for {}: {}", offering, e), Some(job_id.to_string()));
        tracing::error!(job_id, offering, error = ?e, "Docker install failed");
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

    // Add to registry
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
    };

    {
        let mut registry = state.registry.write().await;
        if let Some(existing) = registry.iter_mut().find(|svc| svc.name == offering) {
            *existing = info;
        } else {
            registry.push(info);
        }
    }

    persist_registry_state(state).await;

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

    tracing::info!(job_id, offering, "Service installation completed");
}

// Background task: install multiple services
async fn install_batch_task(state: &AppState, job_id: &str, offerings: Vec<String>) {
    // Update job status to Running
    {
        let mut jobs = state.jobs.write().await;
        if let Some(job) = jobs.get_mut(job_id) {
            job.status = JobStatus::Running;
        }
    }

    tracing::info!(job_id, count = offerings.len(), "Starting batch installation");

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
        };

        {
            let mut registry = state.registry.write().await;
            if let Some(existing) = registry.iter_mut().find(|svc| svc.name == offering) {
                *existing = info;
            } else {
                registry.push(info);
            }
        }

        persist_registry_state(state).await;

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
            job.status = if job.failed.is_empty() {
                JobStatus::Completed
            } else {
                JobStatus::Failed
            };
            job.completed_at = Some(std::time::SystemTime::now());
        }
    }

    tracing::info!(job_id, "Batch installation completed");
}

/// Background task that monitors Docker container health and updates the service registry
///
/// Also performs non-destructive self-heal by adopting existing zen-offering containers
/// into the registry when they map to a known offering template.
async fn health_monitor_task(state: AppState) {
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
        match state.docker.list_zen_containers().await {
            Ok(container_names) => {
                let registry_names: Vec<String> = {
                    let reg = state.registry.read().await;
                    reg.iter().map(|s| s.name.clone()).collect()
                };

                let mut adopted_any = false;
                
                for container_name in &container_names {
                    if !registry_names.iter().any(|n| n == container_name) {
                        tracing::warn!(container = %container_name, "Found zen-offering container not in registry (adopting)");
                        match adopt_offering_container(&state, container_name).await {
                            Ok(Some(info)) => {
                                let mut reg = state.registry.write().await;
                                reg.push(info);
                                adopted_any = true;
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
                    persist_registry_state(&state).await;
                }
            }
            Err(e) => {
                tracing::error!(error = ?e, "Failed to list zen containers");
            }
        }
    }
}

async fn load_preinstall_manifest() -> Option<PreInstallManifest> {
    let path = "/home/stone/garden-moss-preinstall.json";
    if std::path::Path::new(path).exists() {
        tracing::info!("Found pre-install manifest at {}", path);
        match tokio::fs::read_to_string(path).await {
            Ok(content) => match serde_json::from_str(&content) {
                Ok(manifest) => {
                    tracing::info!("Loaded pre-install manifest with {} offerings", 
                        serde_json::from_str::<serde_json::Value>(&content)
                            .ok()?
                            .get("offerings")?
                            .as_array()?
                            .len());
                    Some(manifest)
                },
                Err(e) => {
                    tracing::error!(error = ?e, "Failed to parse pre-install manifest");
                    None
                }
            },
            Err(e) => {
                tracing::error!(error = ?e, "Failed to read pre-install manifest");
                None
            }
        }
    } else {
        tracing::debug!("No pre-install manifest found at {}", path);
        None
    }
}

#[derive(clap::Parser)]
#[command(name = "moss")]
#[command(about = "Zen Garden Moss - Service orchestration daemon")]
struct Cli {
    /// Stone name identifier
    /// Priority: CLI arg > STONE_NAME env var > config file > default
    #[arg(long, env = "STONE_NAME")]
    stone_name: Option<String>,
    
    /// HTTP server port
    /// Priority: CLI arg > PORT env var > config file > default (7185)
    #[arg(long, env = "PORT")]
    port: Option<u16>,
    
    /// Log level (trace, debug, info, warn, error)
    /// Priority: CLI arg > RUST_LOG env var > config file > default (info)
    #[arg(long, env = "RUST_LOG")]
    log_level: Option<String>,
    
    /// Fast sync timeout in seconds for rapid offering deployments
    /// Priority: CLI arg > FAST_SYNC_TIMEOUT env var > config file > default (disabled)
    #[arg(long, env = "FAST_SYNC_TIMEOUT")]
    fast_sync_timeout: Option<u64>,
    
    /// Force start by killing existing moss processes
    #[arg(long)]
    force: bool,
}

/// Run first-boot initialization sequence
/// 
/// Displays progress on console, generates unique name, configures hostname, and creates MOTD
async fn run_first_boot_initialization(old_name: &str, port: u16) -> anyhow::Result<String> {
    console::display_header("Zen Garden - First Boot")?;
    console::tty_write("")?;
    console::display_item("Temporary Name", old_name)?;
    console::display_wait("Starting first-time setup")?;
    console::tty_write("")?;
    
    // Generate unique name with collision detection
    console::display_header("Name Generation")?;
    let new_name = console::generate_unique_name().await?;
    console::tty_write("")?;
    
    // Configure system hostname
    console::display_header("System Configuration")?;
    console::set_hostname(&new_name).await?;
    console::update_hosts_file(old_name, &new_name).await?;
    console::restart_avahi().await?;
    console::test_mdns_resolution(&new_name).await?;
    console::tty_write("")?;
    
    // Update Moss configuration
    console::display_header("Moss Configuration")?;
    console::update_moss_config(&new_name).await?;
    console::tty_write("")?;
    
    // Create MOTD
    let url = format!("http://{}:{}", console::get_local_ip_sync(), port);
    console::write_motd(&new_name, &url)?;
    console::tty_write("")?;
    
    // Final summary
    console::display_header("Setup Complete")?;
    console::display_item("Stone Name", &new_name)?;
    console::display_item("Management URL", &url)?;
    console::display_item("Username", "stone")?;
    console::display_item("Password", "garden")?;
    console::tty_write("")?;
    console::display_success("Stone is ready for use")?;
    console::tty_write("")?;
    
    Ok(new_name)
}

async fn kill_existing_moss_processes_graceful() -> anyhow::Result<()> {
    // Try graceful shutdown via HTTP first
    let client = reqwest::Client::builder()
        .timeout(std::time::Duration::from_secs(3))
        .build()?;
    
    match client.post(format!("http://127.0.0.1:{}/admin/shutdown", garden_common::ports::MOSS_HTTP))
        .send()
        .await
    {
        Ok(response) if response.status().is_success() => {
            tracing::info!("Sent graceful shutdown request to existing moss instance");
            
            // Wait up to 3 seconds for graceful shutdown
            for _ in 0..30 {
                tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;
                
                // Check if process is still running
                let still_running = check_moss_processes_exist();
                if !still_running {
                    tracing::info!("Existing moss instance shut down gracefully");
                    return Ok(());
                }
            }
            
            tracing::warn!("Graceful shutdown timed out after 3s, forcing kill");
        }
        Ok(response) => {
            tracing::warn!(status = ?response.status(), "Graceful shutdown request returned non-success status");
        }
        Err(e) => {
            tracing::debug!(error = ?e, "Could not connect to existing moss instance for graceful shutdown");
        }
    }
    
    // Graceful shutdown failed or timed out, force kill
    kill_existing_moss_processes()
}

fn check_moss_processes_exist() -> bool {
    #[cfg(target_os = "windows")]
    {
        use std::process::Command;
        let current_pid = std::process::id();
        
        if let Ok(output) = Command::new("tasklist")
            .args(["/FI", "IMAGENAME eq garden-moss.exe", "/FO", "CSV", "/NH"])
            .output()
        {
            if output.status.success() {
                let stdout = String::from_utf8_lossy(&output.stdout);
                for line in stdout.lines() {
                    if let Some(pid_str) = line.split(',').nth(1) {
                        let pid_str = pid_str.trim_matches('"').trim();
                        if let Ok(pid) = pid_str.parse::<u32>() {
                            if pid != current_pid {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        false
    }
    
    #[cfg(not(target_os = "windows"))]
    {
        use std::process::Command;
        let current_pid = std::process::id();
        
        if let Ok(output) = Command::new("pgrep").arg(garden_common::names::MOSS_BINARY).output() {
            if output.status.success() {
                let stdout = String::from_utf8_lossy(&output.stdout);
                for line in stdout.lines() {
                    if let Ok(pid) = line.trim().parse::<u32>() {
                        if pid != current_pid {
                            return true;
                        }
                    }
                }
            }
        }
        false
    }
}

fn kill_existing_moss_processes() -> anyhow::Result<()> {
    #[cfg(target_os = "windows")]
    {
        use std::process::Command;
        
        // Get current process ID to avoid killing ourselves
        let current_pid = std::process::id();
        
        // Use tasklist to find garden-moss.exe processes
        let output = Command::new("tasklist")
            .args(["/FI", "IMAGENAME eq garden-moss.exe", "/FO", "CSV", "/NH"])
            .output()?;
        
        if output.status.success() {
            let stdout = String::from_utf8_lossy(&output.stdout);
            for line in stdout.lines() {
                // Parse CSV: "garden-moss.exe","PID","..."  
                if let Some(pid_str) = line.split(',').nth(1) {
                    let pid_str = pid_str.trim_matches('"').trim();
                    if let Ok(pid) = pid_str.parse::<u32>() {
                        if pid != current_pid {
                            tracing::info!("Killing existing moss process: PID {}", pid);
                            let _ = Command::new("taskkill")
                                .args(["/PID", &pid.to_string(), "/F"])
                                .output();
                        }
                    }
                }
            }
        }
    }
    
    #[cfg(not(target_os = "windows"))]
    {
        use std::process::Command;
        
        // Get current process ID
        let current_pid = std::process::id();
        
        // Use pgrep to find moss processes
        let output = Command::new("pgrep")
            .arg("moss")
            .output()?;
        
        if output.status.success() {
            let stdout = String::from_utf8_lossy(&output.stdout);
            for line in stdout.lines() {
                if let Ok(pid) = line.trim().parse::<u32>() {
                    if pid != current_pid {
                        tracing::info!("Killing existing moss process: PID {}", pid);
                        let _ = Command::new("kill")
                            .args(&["-9", &pid.to_string()])
                            .output();
                    }
                }
            }
        }
    }
    
    Ok(())
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    // Load configuration from file first (lowest priority)
    let config = MossConfig::load();
    
    // Parse CLI arguments (CLI and env vars handled by clap with #[arg(env)])
    let cli = <Cli as clap::Parser>::parse();
    
    // Merge configuration with priority: CLI > Env > Config File > Defaults
    // Note: clap already merges CLI args with env vars, so we only need to fill in from config file
    let log_level = cli.log_level
        .or_else(|| config.as_ref().and_then(|c| c.log_level.clone()))
        .unwrap_or_else(|| "info".to_string());

    // Stone identity:
    // - The network-visible name is the system hostname (mDNS: <hostname>.local).
    // - Historically the systemd unit set STONE_NAME, which can drift after first-boot rename.
    //
    // Priority: explicit CLI flag (--stone-name) > config file > system hostname > STONE_NAME env > default
    let env_stone_name = std::env::var("STONE_NAME").ok();
    let explicit_cli_stone_name = if cli.stone_name.is_some() && env_stone_name.is_none() {
        cli.stone_name.clone()
    } else {
        None
    };

    let system_hostname = console::get_hostname().await.ok();
    if let (Some(env_name), Some(sys_name)) = (&env_stone_name, &system_hostname) {
        if env_name != sys_name {
            tracing::warn!(
                env_stone_name = %env_name,
                system_hostname = %sys_name,
                "STONE_NAME env does not match system hostname; preferring hostname (fix systemd unit to remove Environment=STONE_NAME)"
            );
        }
    }

    let stone_name = explicit_cli_stone_name
        .or_else(|| config.as_ref().and_then(|c| c.stone_name.clone()))
        .or_else(|| system_hostname.clone())
        .or_else(|| env_stone_name.clone())
        .unwrap_or_else(|| "stone-01".to_string());
    
    let port = cli.port
        .or_else(|| config.as_ref().and_then(|c| c.port))
        .unwrap_or(garden_common::ports::MOSS_HTTP);
    
    let fast_sync_timeout = cli.fast_sync_timeout
        .or_else(|| config.as_ref().and_then(|c| c.fast_sync_timeout));
    
    // Initialize logging with merged log level
    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| EnvFilter::new(&log_level))
        )
        .init();
    
    tracing::info!(
        stone_name = %stone_name,
        port = port,
        log_level = %log_level,
        fast_sync_timeout = ?fast_sync_timeout,
        config_loaded = config.is_some(),
        "Moss daemon starting with merged configuration (priority: CLI > Env > Config > Defaults)"
    );
    
    // Spawn first-boot initialization as background task if needed
    if console::is_first_run() {
        tracing::info!("First run detected, spawning background initialization task");
        
        let init_stone_name = stone_name.clone();
        let init_port = port;
        tokio::spawn(async move {
            const MAX_ATTEMPTS: u32 = 20;
            const RETRY_DELAY_SECS: u64 = 3;
            
            let _ = console::tty_write("");
            let _ = console::display_wait("First-boot setup: Waiting for filesystem to become writable");
            
            for attempt in 1..=MAX_ATTEMPTS {
                match console::ensure_etc_writable().await {
                    Ok(true) => {
                        tracing::info!(attempt, "Filesystem is writable, proceeding with first boot initialization");
                        let _ = console::display_success("Filesystem ready, starting configuration");
                        
                        match run_first_boot_initialization(&init_stone_name, init_port).await {
                            Ok(new_name) => {
                                if let Err(e) = console::mark_first_run_complete().await {
                                    tracing::error!(error = ?e, "Failed to mark first-run complete");
                                }
                                
                                tracing::info!(new_name = %new_name, "First boot initialization completed successfully");
                                let _ = console::tty_write("");
                                let _ = console::display_success(&format!("✓ Stone configured as: {}", new_name));
                                let _ = console::display_wait("Restarting to apply new configuration...");
                                let _ = console::tty_write("");
                                
                                // Exit so systemd restarts us with the new configuration
                                std::process::exit(0);
                            }
                            Err(e) => {
                                tracing::error!(error = ?e, "First boot initialization failed");
                                let _ = console::display_error(&format!("Setup failed: {}", e));
                                if attempt < MAX_ATTEMPTS {
                                    tokio::time::sleep(tokio::time::Duration::from_secs(RETRY_DELAY_SECS)).await;
                                }
                            }
                        }
                    }
                    Ok(false) | Err(_) => {
                        if attempt < MAX_ATTEMPTS {
                            tokio::time::sleep(tokio::time::Duration::from_secs(RETRY_DELAY_SECS)).await;
                        } else {
                            tracing::error!("First boot initialization abandoned - filesystem never became writable");
                            let _ = console::display_error("Setup abandoned - filesystem remained read-only");
                        }
                    }
                }
            }
        });
    }
    
    // Handle --force flag: try graceful shutdown, then force kill if needed
    if cli.force {
        tracing::info!("--force flag set, attempting graceful shutdown of existing moss processes");
        if let Err(e) = kill_existing_moss_processes_graceful().await {
            tracing::warn!(error = ?e, "Failed to shutdown existing processes, continuing anyway");
        }
        // Give the OS time to free the port
        tokio::time::sleep(tokio::time::Duration::from_millis(500)).await;
    }

    // Prefer explicit STONE_HOST, otherwise auto-detect network IP
    let api_endpoint = {
        if let Ok(host) = std::env::var("STONE_HOST") {
            let trimmed = host.trim();
            if !trimmed.is_empty() {
                format!("http://{}:{}", trimmed, port)
            } else {
                format!("http://{}:{}", get_local_ip(), port)
            }
        } else {
            // Auto-detect local network IP for UDP discovery responses
            format!("http://{}:{}", get_local_ip(), port)
        }
    };

    // Start mDNS announcer (Linux only)
    let _mdns = match mdns::announce_moss(&stone_name, port) {
        Ok(daemon) => Some(daemon),
        Err(e) => {
            tracing::warn!(error = ?e, "mDNS announcement failed");
            None
        }
    };

    // Spawn UDP discovery listener
    let discovery_stone_name = stone_name.clone();
    let discovery_endpoint = api_endpoint.clone();
    tokio::spawn(async move {
        if let Err(e) = discovery::udp_listener(discovery_stone_name, discovery_endpoint).await {
            tracing::error!(error = ?e, "UDP discovery listener failed");
        }
    });

    // Spawn Lantern registration loop (if LANTERN_ENDPOINT is set)
    if let Ok(lantern_endpoint) = std::env::var("LANTERN_ENDPOINT") {
        let trimmed = lantern_endpoint.trim().to_string();
        if !trimmed.is_empty() {
            let reg_stone_name = stone_name.clone();
            let reg_endpoint = api_endpoint.clone();
            tokio::spawn(async move {
                if let Err(e) = lantern_registration_loop(reg_stone_name, reg_endpoint, trimmed).await {
                    tracing::error!(error = ?e, "Lantern registration loop failed");
                }
            });
        }
    }

    // Wait for Docker to be ready (with retries for fresh installs)
    let docker = {
        let max_retries = 30; // 30 attempts = ~60 seconds
        let mut retries = 0;
        loop {
            match DockerManager::new() {
                Ok(dm) => {
                    tracing::info!("Docker daemon connected successfully");
                    break Arc::new(dm);
                }
                Err(e) if retries < max_retries => {
                    retries += 1;
                    tracing::warn!(
                        error = ?e,
                        retry = retries,
                        max_retries = max_retries,
                        "Docker not ready, waiting 2s before retry..."
                    );
                    tokio::time::sleep(tokio::time::Duration::from_secs(2)).await;
                }
                Err(e) => {
                    tracing::error!(error = ?e, "Failed to connect to Docker daemon after {} retries", max_retries);
                    return Err(e);
                }
            }
        }
    };

    // Create event broadcast channel (capacity 100 events)
    let (event_tx, _) = tokio::sync::broadcast::channel::<MossEvent>(100);
    
    // Create shutdown notification channel
    let shutdown_tx = Arc::new(tokio::sync::Notify::new());

    let state = AppState {
        stone_name,
        registry: Arc::new(RwLock::new(Vec::new())),
        docker: docker.clone(),
        templates: Arc::new(TemplateLoader::new()),
        jobs: Arc::new(RwLock::new(HashMap::new())),
        event_tx,
        shutdown_tx: shutdown_tx.clone(),
        start_time: std::time::Instant::now(),
        offerings_index: Arc::new(RwLock::new(None)),
    };

    // Load persisted registry state (best-effort)
    match load_registry_from_disk().await {
        Ok(mut loaded) => {
            // Reconcile: if the container no longer exists, mark it offline rather than dropping.
            for svc in loaded.iter_mut() {
                if !docker.zen_container_exists(&svc.name).await.unwrap_or(false) {
                    svc.status = ServiceStatus::Stopped;
                    svc.health = ServiceHealthStatus::Offline;
                }
            }

            *state.registry.write().await = loaded;
        }
        Err(e) => {
            tracing::warn!(error = ?e, "Failed to load persisted moss registry; starting empty");
        }
    }

    // Startup self-heal: adopt any existing zen-offering containers into the registry.
    adopt_existing_containers(&state).await;

    // Build offerings index at startup
    tracing::info!("Building offerings catalog...");
    match ensure_offerings_index(&state, false).await {
        Ok(_) => {
            let idx_guard = state.offerings_index.read().await;
            if let Some(idx) = idx_guard.as_ref() {
                tracing::info!(
                    offerings_count = idx.offerings.len(),
                    "Offerings catalog loaded successfully"
                );
            }
        }
        Err(e) => {
            tracing::warn!(error = ?e, "Failed to build offerings catalog - API will return empty results");
        }
    }

    // Check for pre-install manifest on first boot
    if let Some(manifest) = load_preinstall_manifest().await {
        if manifest.auto_install {
            tracing::info!(
                "Starting auto-installation of {} services from manifest", 
                manifest.offerings.len()
            );
            
            // Validate all offerings exist before creating job
            let mut invalid_offerings = Vec::new();
            for offering in &manifest.offerings {
                if let Err(_) = state.templates.load(offering) {
                    invalid_offerings.push(offering.clone());
                }
            }

            if !invalid_offerings.is_empty() {
                tracing::error!(
                    offerings = ?invalid_offerings,
                    "Pre-install manifest contains invalid offerings - skipping auto-install"
                );
            } else {
                let job_id = uuid::Uuid::now_v7().to_string();
            let job = Job {
                id: job_id.clone(),
                offerings: manifest.offerings.clone(),
                status: JobStatus::Pending,
                completed: vec![],
                failed: HashMap::new(),
                started_at: std::time::SystemTime::now(),
                completed_at: None,
            };
            
            state.jobs.write().await.insert(job_id.clone(), job);
            
            // Spawn background installation + cleanup task
            let install_state = state.clone();
            let install_job_id = job_id.clone();
            let install_offerings = manifest.offerings.clone();
            tokio::spawn(async move {
                install_batch_task(&install_state, &install_job_id, install_offerings).await;
                
                // Wait for job completion, then remove manifest
                loop {
                    tokio::time::sleep(tokio::time::Duration::from_secs(5)).await;
                    let jobs = install_state.jobs.read().await;
                    if let Some(job) = jobs.get(&install_job_id) {
                        match job.status {
                            JobStatus::Completed | JobStatus::Failed => {
                                drop(jobs); // Release lock
                                tracing::info!("Pre-install job finished, removing manifest");
                                if let Err(e) = tokio::fs::remove_file("/home/stone/garden-moss-preinstall.json").await {
                                    tracing::warn!(error = ?e, "Failed to remove pre-install manifest");
                                } else {
                                    tracing::info!("Pre-install manifest removed - system ready");
                                }
                                break;
                            }
                            _ => continue, // Still running
                        }
                    } else {
                        break; // Job not found
                    }
                }
            });
            
            tracing::info!("Pre-install job started: {} (check /api/jobs/{})", job_id, job_id);
            }
        }
    }

    // Spawn health monitoring background task
    let health_state = state.clone();
    tokio::spawn(async move {
        health_monitor_task(health_state).await;
    });

    let app = Router::new()
        // Standard health/monitoring endpoints (root level)
        .route("/health", get(health))
        .route("/capabilities", get(capabilities))
        .route("/metrics", get(get_metrics))
        
        // V1 API - Offerings (Human Layer)
        .route("/api/v1/offerings", get(api::v1::offerings::list_offerings_v1))
        .route("/api/v1/offerings", post(api::v1::offerings::plant_offering_v1))
        .route("/api/v1/offerings/:name", get(api::v1::offerings::get_offering_v1))
        .route("/api/v1/offerings/:name", axum::routing::delete(api::v1::offerings::take_away_offering_v1))
        .route("/api/v1/offerings/:name/manifest", get(api::v1::offerings::get_offering_manifest_v1))
        .route("/api/v1/offerings/heal", post(api::v1::offerings::heal_garden_v1))
        .route("/api/v1/offerings/refresh", post(api::v1::offerings::refresh_catalog_v1))
        
        // V1 API - Services (Technical Layer)
        .route("/api/v1/services/manifests", get(api::v1::services::list_manifests_v1))
        .route("/api/v1/services/:name/manifest", get(api::v1::services::get_manifest_v1))
        .route("/api/v1/services", get(api::v1::services::list_services_v1))
        .route("/api/v1/services", post(api::v1::services::create_service_v1))
        .route("/api/v1/services/:service", get(api::v1::services::get_service_v1))
        .route("/api/v1/services/:service", axum::routing::delete(api::v1::services::delete_service_v1))
        .route("/api/v1/services/:service/logs", get(api::v1::services::stream_service_logs_v1))
        .route("/api/v1/services/:service/restart", post(api::v1::services::restart_service_v1))
        .route("/api/v1/services/:service/cordon", post(api::v1::services::cordon_service_v1))
        .route("/api/v1/services/reconcile", post(api::v1::services::reconcile_inventory_v1))
        .route("/api/v1/services/refresh", post(api::v1::services::refresh_manifests_v1))
        
        // V1 API - Stone operations
        .route("/api/v1/stone/upgrade", post(api::v1::stone::upgrade_stone_v1))
        .route("/api/v1/stone/shutdown", post(api::v1::stone::shutdown_stone_v1))
        
        // V1 API - Events & Jobs
        .route("/api/v1/events", get(stream_events))
        .route("/api/v1/jobs", get(list_jobs))
        .route("/api/v1/jobs/:job_id", get(get_job_status))
        
        // V1 API - Garden topology
        .route("/api/v1/garden", get(api::v1::garden::get_garden_v1))
        .route("/api/v1/garden/stones/:stone_name", get(api::v1::garden::get_stone_v1))
        .route("/api/v1/stone", get(api::v1::garden::get_local_stone_v1))
        
        // V1 API - Pond security
        .route("/api/v1/pond/init", post(api::v1::pond::pond_init_v1))
        .route("/api/v1/pond", axum::routing::delete(api::v1::pond::pond_remove_v1))
        .route("/api/v1/pond/invite", post(api::v1::pond::pond_invite_v1))
        .route("/api/v1/pond/join", post(api::v1::pond::pond_join_v1))
        .route("/api/v1/pond/stones/:stone_name", axum::routing::delete(api::v1::pond::pond_untrust_v1))
        .route("/api/v1/pond/status", get(api::v1::pond::pond_status_v1))
        .layer(axum::extract::DefaultBodyLimit::max(200 * 1024 * 1024)) // 200 MB for binary uploads
        .with_state(state.clone());

    let addr: SocketAddr = format!("0.0.0.0:{}", port).parse()?;
    let listener = tokio::net::TcpListener::bind(addr).await?;
    tracing::info!(?addr, api_endpoint = %api_endpoint, "Moss HTTP server ready");
    
    // Create server with graceful shutdown
    let server = axum::serve(listener, app)
        .with_graceful_shutdown(async move {
            shutdown_signal().await;
            tracing::info!("Shutdown signal received, initiating graceful shutdown");
        });
    
    // Run server with shutdown coordination
    tokio::select! {
        result = server => {
            if let Err(e) = result {
                tracing::error!(error = ?e, "Server error");
                return Err(e.into());
            }
        }
        _ = shutdown_tx.notified() => {
            tracing::info!("Admin shutdown requested");
        }
    }
    
    // Allow in-flight requests to complete (5s timeout)
    tracing::info!("Waiting up to 5s for in-flight requests to complete");
    tokio::time::sleep(tokio::time::Duration::from_secs(5)).await;
    
    tracing::info!("Moss daemon shutdown complete");
    Ok(())
}

/// POST /admin/shutdown - Trigger graceful shutdown
async fn admin_shutdown(State(state): State<AppState>) -> (StatusCode, Json<serde_json::Value>) {
    tracing::info!("Admin shutdown endpoint called");
    state.shutdown_tx.notify_one();
    (StatusCode::OK, Json(json!({
        "success": true,
        "message": "Shutdown initiated"
    })))
}

/// Cross-platform shutdown signal handler
async fn shutdown_signal() {
    #[cfg(unix)]
    {
        use tokio::signal::unix::{signal, SignalKind};
        
        let mut sigterm = signal(SignalKind::terminate())
            .expect("Failed to install SIGTERM handler");
        let mut sigint = signal(SignalKind::interrupt())
            .expect("Failed to install SIGINT handler");
        
        tokio::select! {
            _ = sigterm.recv() => {
                tracing::info!("SIGTERM received");
            }
            _ = sigint.recv() => {
                tracing::info!("SIGINT received");
            }
        }
    }
    
    #[cfg(windows)]
    {
        tokio::signal::ctrl_c()
            .await
            .expect("Failed to install Ctrl+C handler");
        tracing::info!("Ctrl+C received");
    }
}

/// Get the local network IP address (first non-loopback IPv4)
fn get_local_ip() -> String {
    use std::net::IpAddr;
    
    if let Ok(addrs) = local_ip_address::list_afinet_netifas() {
        for (_, ip) in addrs {
            if let IpAddr::V4(ipv4) = ip {
                // Skip loopback and link-local addresses
                if !ipv4.is_loopback() && !ipv4.is_link_local() {
                    return ipv4.to_string();
                }
            }
        }
    }
    
    // Fallback to loopback if no network interface found
    "127.0.0.1".to_string()
}

/// Lantern registration loop - registers this stone with Lantern every 45 seconds
/// 
/// Sends POST /api/register with stone name, endpoint, and current service list
/// Only runs if LANTERN_ENDPOINT environment variable is set
async fn lantern_registration_loop(
    stone_name: String,
    endpoint: String,
    lantern_endpoint: String,
) -> anyhow::Result<()> {
    use reqwest::Client;
    use garden_common::RegisterRequest;
    
    tracing::info!(
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
