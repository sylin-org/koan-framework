mod api;
mod console;
mod discovery;
mod docker;
mod mdns;
mod metrics;
mod network_singletons;
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
    error_codes, ApiError, CpuCapabilities, DaemonHealthStatus, DetectionStatus, DiskCapabilities, ErrorDetails, 
    HardwareCapabilities, HardwareInventory, HealthCheck, ServiceHealthStatus, 
    MemoryCapabilities, MetricsSnapshot, Ports, RuntimeInfo, ServiceInfo, 
    ServiceStatus,
};
use api::responses::ApiResponse;

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
#[derive(Debug, Clone, serde::Deserialize, serde::Serialize)]
struct MossConfig {
    /// Stone name identifier - default: "stone-01"
    stone_name: Option<String>,
    
    /// HTTP server port - default: 7185
    port: Option<u16>,
    
    /// Log level (trace/debug/info/warn/error) - default: "info"
    log_level: Option<String>,
    
    /// Fast sync timeout in seconds for rapid offering deployments - default: None (disabled)
    fast_sync_timeout: Option<u64>,
    
    /// Console output mode (silent/minimal/informative/verbose) - default: platform-specific
    console_mode: Option<String>,
    
    /// Event deduplication TTL in seconds - default: 10
    #[serde(default)]
    event_dedup_ttl_secs: Option<u64>,
    
    /// Docker connection retry delay in seconds - default: 3
    #[serde(default)]
    docker_retry_delay_secs: Option<u64>,
    
    /// Health check interval in seconds - default: 30
    #[serde(default)]
    health_check_interval_secs: Option<u64>,
    
    /// Docker reconnect interval in seconds - default: 5
    #[serde(default)]
    docker_reconnect_interval_secs: Option<u64>,
    
    /// HTTP capabilities fetch timeout in seconds - default: 5
    #[serde(default)]
    http_capabilities_timeout_secs: Option<u64>,
    
    /// HTTP health check timeout in seconds - default: 2
    #[serde(default)]
    http_health_timeout_secs: Option<u64>,
    
    /// HTTP quick health check timeout in milliseconds - default: 200
    #[serde(default)]
    http_quick_health_timeout_millis: Option<u64>,
    
    /// HTTP long operation timeout in seconds - default: 300 (5 minutes)
    #[serde(default)]
    http_long_operation_timeout_secs: Option<u64>,
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
                    // Console event emitted later in main() after console printer is available
                    Some(config)
                },
                Err(e) => {
                    tracing::warn!(path = ?config_path, error = ?e, "Failed to parse config file");
                    // Console event: Config | PARSE_ERROR emitted in main() as NotFound
                    None
                }
            },
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {
                tracing::debug!(path = ?config_path, "Config file not found, using defaults");
                // Console event: Config | NOT_FOUND emitted in main()
                None
            },
            Err(e) => {
                tracing::warn!(path = ?config_path, error = ?e, "Failed to read config file");
                // Console event: Config | READ_ERROR emitted in main() as NotFound
                None
            }
        }
    }
    
    /// Get event deduplication TTL in seconds (default: 10)
    #[allow(dead_code)]
    fn event_dedup_ttl_secs(&self) -> u64 {
        self.event_dedup_ttl_secs.unwrap_or(10)
    }
    
    /// Get Docker retry delay in seconds (default: 3)
    #[allow(dead_code)]
    fn docker_retry_delay_secs(&self) -> u64 {
        self.docker_retry_delay_secs.unwrap_or(3)
    }
    
    /// Get health check interval in seconds (default: 30)
    #[allow(dead_code)]
    fn health_check_interval_secs(&self) -> u64 {
        self.health_check_interval_secs.unwrap_or(30)
    }
    
    /// Get Docker reconnect interval in seconds (default: 5)
    #[allow(dead_code)]
    fn docker_reconnect_interval_secs(&self) -> u64 {
        self.docker_reconnect_interval_secs.unwrap_or(5)
    }
    
    /// Get HTTP capabilities timeout in seconds (default: 5)
    #[allow(dead_code)]
    fn http_capabilities_timeout_secs(&self) -> u64 {
        self.http_capabilities_timeout_secs.unwrap_or(5)
    }
    
    /// Get HTTP health timeout in seconds (default: 2)
    #[allow(dead_code)]
    fn http_health_timeout_secs(&self) -> u64 {
        self.http_health_timeout_secs.unwrap_or(2)
    }
    
    /// Get HTTP quick health timeout in milliseconds (default: 200)
    #[allow(dead_code)]
    fn http_quick_health_timeout_millis(&self) -> u64 {
        self.http_quick_health_timeout_millis.unwrap_or(200)
    }
    
    /// Get HTTP long operation timeout in seconds (default: 300)
    #[allow(dead_code)]
    fn http_long_operation_timeout_secs(&self) -> u64 {
        self.http_long_operation_timeout_secs.unwrap_or(300)
    }

    /// Save configuration to platform-specific path
    /// 
    /// Saves garden-moss.toml to:
    /// - Linux: /etc/zen-garden/garden-moss.toml
    /// - Windows: ./garden-moss.toml (current directory)
    /// 
    /// Returns Ok(()) on success, Err on write failure
    #[allow(dead_code)]
    fn save(&self) -> Result<(), std::io::Error> {
        let config_path = if cfg!(windows) {
            std::path::PathBuf::from(format!("./{}", garden_common::names::MOSS_CONFIG))
        } else {
            std::path::PathBuf::from(format!("{}/{}", garden_common::names::CONFIG_DIR, garden_common::names::MOSS_CONFIG))
        };
        
        let toml_content = toml::to_string_pretty(self)
            .map_err(|e| std::io::Error::new(std::io::ErrorKind::Other, e.to_string()))?;
        
        std::fs::write(&config_path, toml_content)?;
        
        tracing::info!(path = ?config_path, "Saved configuration to file");
        Ok(())
    }
}

/// Internal struct for compatibility checking only
#[derive(Debug, Clone)]
struct CompatCheckCapabilities {
    cpu_model: Option<String>,
    cpu_features: Option<Vec<String>>,
    architecture: Option<String>,
    total_memory_mb: Option<u64>,
    
    // GPU/AI capabilities
    has_cuda: bool,
    has_rocm: bool,
    has_directml: bool,
    has_openvino: bool,
    gpu_vram_total_mb: u64,
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
    
    // Detect GPU/AI capabilities
    let gpus = metrics::detect_gpus();
    let has_cuda = gpus.iter().any(|g| g.ai_runtime.as_ref().and_then(|r| r.cuda_version.as_ref()).is_some());
    let has_rocm = gpus.iter().any(|g| g.ai_runtime.as_ref().and_then(|r| r.rocm_version.as_ref()).is_some());
    let has_directml = gpus.iter().any(|g| g.ai_runtime.as_ref().map(|r| r.has_directml).unwrap_or(false));
    let has_openvino = gpus.iter().any(|g| g.ai_runtime.as_ref().map(|r| r.has_openvino).unwrap_or(false));
    let gpu_vram_total_mb: u64 = gpus.iter().filter_map(|g| g.vram_mb).sum();

    CompatCheckCapabilities {
        cpu_model: Some(cpu_model),
        cpu_features: Some(cpu_features),
        architecture: Some(architecture),
        total_memory_mb,
        has_cuda,
        has_rocm,
        has_directml,
        has_openvino,
        gpu_vram_total_mb,
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
    console: Arc<console::ConsolePrinter>,
    capabilities: Arc<RwLock<Option<HardwareCapabilities>>>,
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
    let gpus = metrics::detect_gpus();
    
    // Include GPU/AI capabilities in hash so offerings re-evaluate when AI hardware is detected
    let has_cuda = gpus.iter().any(|g| g.ai_runtime.as_ref().and_then(|r| r.cuda_version.as_ref()).is_some());
    let has_rocm = gpus.iter().any(|g| g.ai_runtime.as_ref().and_then(|r| r.rocm_version.as_ref()).is_some());
    let has_directml = gpus.iter().any(|g| g.ai_runtime.as_ref().map(|r| r.has_directml).unwrap_or(false));
    let has_openvino = gpus.iter().any(|g| g.ai_runtime.as_ref().map(|r| r.has_openvino).unwrap_or(false));
    let gpu_vram_total: u64 = gpus.iter().filter_map(|g| g.vram_mb).sum();
    
    let payload = serde_json::json!({
        "cpu_model": caps.cpu_model,
        "cpu_features": caps.cpu_features,
        "architecture": caps.architecture,
        "total_memory_mb": caps.total_memory_mb,
        "has_cuda": has_cuda,
        "has_rocm": has_rocm,
        "has_directml": has_directml,
        "has_openvino": has_openvino,
        "gpu_vram_total_mb": gpu_vram_total,
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
                decision: garden_common::COMPAT_PASS.to_string(),
                reason: None,
                original_image: None,
                fallback_image: None,
                suggestion: None,
            },
            CompatibilityDecision::Fallback { image, reason } => {
                let original_image = template.image.clone();
                template.image = image.clone();
                CompiledCompatibility {
                    decision: garden_common::COMPAT_FALLBACK.to_string(),
                    reason: Some(reason),
                    original_image: Some(original_image),
                    fallback_image: Some(image),
                    suggestion: None,
                }
            }
            CompatibilityDecision::Fail { reason, suggestion } => CompiledCompatibility {
                decision: garden_common::COMPAT_FAIL.to_string(),
                reason: Some(reason),
                original_image: Some(template.image.clone()),
                fallback_image: None,
                suggestion,
            },
        }
    } else {
        CompiledCompatibility {
            decision: garden_common::COMPAT_PASS.to_string(),
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
            status: garden_common::CHECK_PASS.to_string(),
            message: None,
        }
    } else {
        HealthCheck {
            status: garden_common::CHECK_FAIL.to_string(),
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
                    status: garden_common::CHECK_WARN.to_string(),
                    message: Some(format!(
                        "Low disk space: {:.1}% free ({} available)",
                        available_percent,
                        resources.disk.available_friendly
                    )),
                }
            } else {
                HealthCheck {
                    status: garden_common::CHECK_PASS.to_string(),
                    message: None,
                }
            }
        }
        Err(e) => HealthCheck {
            status: garden_common::CHECK_FAIL.to_string(),
            message: Some(format!("Failed to check disk: {}", e)),
        },
    }
}

fn check_memory_health() -> HealthCheck {
    match metrics::collect_stone_resources() {
        Ok(resources) => {
            if resources.memory.used_percent > 90.0 {
                HealthCheck {
                    status: garden_common::CHECK_WARN.to_string(),
                    message: Some(format!(
                        "High memory usage: {:.1}% ({} used of {})",
                        resources.memory.used_percent,
                        resources.memory.used_friendly,
                        resources.memory.total_friendly
                    )),
                }
            } else {
                HealthCheck {
                    status: garden_common::CHECK_PASS.to_string(),
                    message: None,
                }
            }
        }
        Err(e) => HealthCheck {
            status: garden_common::CHECK_FAIL.to_string(),
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
    
    // Initialization component (shows startup progress)
    let init_component = build_initialization_component(&state).await;
    components.insert("initialization".to_string(), init_component);

    // Determine overall status based on worst component status
    let overall_status = determine_overall_status(&components);
    
    // HTTP status code based on overall status
    let http_status = match overall_status.as_str() {
        garden_common::HEALTH_UNHEALTHY => StatusCode::SERVICE_UNAVAILABLE,
        _ => StatusCode::OK,
    };

    // Legacy boolean flags for backward compatibility
    let docker_ok = docker_check.status == garden_common::CHECK_PASS;
    let disk_ok = disk_check.status != garden_common::CHECK_FAIL;
    let memory_ok = memory_check.status != garden_common::CHECK_FAIL;
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

async fn build_initialization_component(state: &AppState) -> garden_common::ComponentHealth {
    let mut details = HashMap::new();
    
    // Check hardware detection status
    let caps_guard = state.capabilities.read().await;
    let detection_status = if let Some(caps) = caps_guard.as_ref() {
        match caps.detection_status {
            garden_common::DetectionStatus::Scanning => "scanning",
            garden_common::DetectionStatus::Partial => "partial",
            garden_common::DetectionStatus::Complete => "complete",
        }
    } else {
        "unknown"
    };
    details.insert("hardware_detection".to_string(), serde_json::json!(detection_status));
    
    // Check catalog build status
    let catalog_guard = state.offerings_index.read().await;
    let catalog_ready = catalog_guard.is_some();
    details.insert("catalog_ready".to_string(), serde_json::json!(catalog_ready));
    
    // Determine overall initialization health
    if detection_status == "complete" && catalog_ready {
        garden_common::ComponentHealth::healthy(details)
    } else {
        details.insert("message".to_string(), serde_json::json!("Initializing..."));
        garden_common::ComponentHealth::degraded(details)
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
            
            // Thresholds: >95% unhealthy, >90% degraded, else healthy
            if usage_percent > 95.0 {
                garden_common::ComponentHealth::unhealthy(details)
            } else if usage_percent > 90.0 {
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
            garden_common::HEALTH_UNHEALTHY => has_unhealthy = true,
            garden_common::HEALTH_DEGRADED => has_degraded = true,
            _ => {}
        }
    }
    
    if has_unhealthy {
        garden_common::HEALTH_UNHEALTHY.to_string()
    } else if has_degraded {
        garden_common::HEALTH_DEGRADED.to_string()
    } else {
        garden_common::HEALTH_HEALTHY.to_string()
    }
}

/// GET /capabilities - Static hardware inventory
async fn capabilities(State(state): State<AppState>) -> Json<ApiResponse<HardwareCapabilities>> {
    // Read from cache - capabilities are detected in background at startup
    let caps_guard = state.capabilities.read().await;
    
    if let Some(caps) = caps_guard.as_ref() {
        Json(ApiResponse {
            data: caps.clone(),
            suggestions: None,
        })
    } else {
        // Fallback: cache not ready yet, return minimal data
        Json(ApiResponse {
            data: HardwareCapabilities {
                stone_name: state.stone_name.clone(),
                hardware: HardwareInventory {
                    cpu: CpuCapabilities {
                        model: None,
                        cores: 1,
                        threads: None,
                        architecture: std::env::consts::ARCH.to_string(),
                        features: None,
                    },
                    memory: MemoryCapabilities { total_mb: 0 },
                    gpus: vec![],
                    disk: None,
                    storage: vec![],
                    os_version: None,
                    kernel_version: None,
                    swap_mb: None,
                },
                runtime: Some(RuntimeInfo {
                    docker_version: None,
                    os: std::env::consts::OS.to_string(),
                    kernel: None,
                }),
                detection_status: DetectionStatus::Scanning,
            },
            suggestions: Some(vec!["Hardware capabilities detection in progress".to_string()]),
        })
    }
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
        
        // AI/GPU capability checks
        if let Some(requires_ai_any) = &condition.requires_ai_any {
            // Match if ANY of the specified runtimes are present (OR logic)
            let has_match = requires_ai_any.iter().any(|runtime| {
                match runtime.to_lowercase().as_str() {
                    "cuda" => capabilities.has_cuda,
                    "rocm" => capabilities.has_rocm,
                    "directml" => capabilities.has_directml,
                    "openvino" => capabilities.has_openvino,
                    _ => false,
                }
            });
            matches &= has_match;
        }
        
        if let Some(requires_ai_all) = &condition.requires_ai_all {
            // Match if ALL of the specified runtimes are present (AND logic)
            let has_all = requires_ai_all.iter().all(|runtime| {
                match runtime.to_lowercase().as_str() {
                    "cuda" => capabilities.has_cuda,
                    "rocm" => capabilities.has_rocm,
                    "directml" => capabilities.has_directml,
                    "openvino" => capabilities.has_openvino,
                    _ => false,
                }
            });
            matches &= has_all;
        }
        
        if let Some(min_vram_mb) = condition.vram_mb_at_least {
            matches &= capabilities.gpu_vram_total_mb >= min_vram_mb;
        }
        
        if let Some(max_vram_mb) = condition.vram_mb_less_than {
            matches &= capabilities.gpu_vram_total_mb < max_vram_mb;
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
    tracing::info!(
        component = %payload.component,
        base64_size = payload.binary_data.len(),
        "Binary refresh requested - payload received successfully"
    );
    
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
    
    // If updating moss itself, trigger update mechanism
    if payload.component == garden_common::names::MOSS_BINARY {
        emit_event(
            &state,
            "info",
            format!("{} update staged. Initiating update process...", payload.component),
            None,
        );
        
        // Platform-specific update handling
        #[cfg(target_os = "windows")]
        {
            // Windows: Use garden-moss-new.exe dance to replace running binary
            let target_path_clone = target_path.clone();
            tokio::spawn(async move {
                tokio::time::sleep(tokio::time::Duration::from_secs(1)).await;
                tracing::info!("Initiating Windows self-update");
                
                let current_exe = match std::env::current_exe() {
                    Ok(p) => p,
                    Err(e) => {
                        tracing::error!(error = ?e, "Failed to get current exe path");
                        return;
                    }
                };
                
                let exe_dir = match current_exe.parent() {
                    Some(d) => d,
                    None => {
                        tracing::error!("No parent directory for current exe");
                        return;
                    }
                };
                
                let new_exe = exe_dir.join("garden-moss-new.exe");
                
                // Copy staged binary to garden-moss-new.exe
                if let Err(e) = std::fs::copy(&target_path_clone, &new_exe) {
                    tracing::error!(error = ?e, "Failed to copy staged binary to garden-moss-new.exe");
                    return;
                }
                
                tracing::info!("Launching garden-moss-new.exe for update finalization");
                
                // Launch garden-moss-new.exe with --update-finalize flag
                match std::process::Command::new(&new_exe)
                    .arg("--update-finalize")
                    .spawn()
                {
                    Ok(_) => {
                        tracing::info!("Update process launched, shutting down current instance");
                        // Exit current process to allow replacement
                        std::process::exit(0);
                    }
                    Err(e) => {
                        tracing::error!(error = ?e, "Failed to launch update process");
                    }
                }
            });
        }
        
        #[cfg(not(target_os = "windows"))]
        {
            // Linux: Traditional systemd restart
            tokio::spawn(async move {
                tokio::time::sleep(tokio::time::Duration::from_secs(1)).await;
                tracing::info!("Triggering service restart for binary update");
                
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
            });
        }
        
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
                    match state.docker.remove_service(&offering, Some(&state.console)).await {
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
    
    state.console.emit(console::ConsoleEvent::new(
        console::EventCategory::Jobs,
        console::EventStatus::Completed,
        format!("Install {} (job: {})", offering, &job_id[..8])
    ));

    tracing::info!(job_id, offering, "Service installation completed");
}

// Background task: install multiple services
async fn install_batch_task(state: &AppState, job_id: &str, offerings: Vec<String>) {
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
                let mut adopted_any = false;
                
                for container_name in &container_names {
                    // Check if already in registry (acquire read lock briefly)
                    let exists = {
                        let reg = state.registry.read().await;
                        reg.iter().any(|s| s.name == *container_name)
                    };
                    
                    if !exists {
                        tracing::warn!(container = %container_name, "Found zen-offering container not in registry (adopting)");
                        match adopt_offering_container(&state, container_name).await {
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
#[command(name = "garden-moss")]
#[command(about = "Zen Garden Moss - Service orchestration daemon")]
#[command(version = concat!(env!("CARGO_PKG_VERSION"), ".", env!("BUILD_NUMBER")))]
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
    
    /// Install Moss as a Windows service (Windows only)
    #[arg(long)]
    install_service: bool,
    
    /// Internal: Finalize update by replacing old binary (used during self-update)
    #[arg(long, hide = true)]
    update_finalize: bool,
    
    /// Internal: Cleanup old binary after update (used during self-update)
    #[arg(long, hide = true)]
    cleanup_old: bool,
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

/// Load capabilities from disk cache
async fn load_capabilities_cache() -> Option<HardwareCapabilities> {
    let path = if cfg!(windows) {
        "./stone-capabilities.json"
    } else {
        garden_common::names::STONE_CAPABILITIES_CACHE
    };
    
    match tokio::fs::read_to_string(path).await {
        Ok(content) => {
            match serde_json::from_str::<HardwareCapabilities>(&content) {
                Ok(caps) => {
                    tracing::info!("Loaded cached hardware capabilities");
                    Some(caps)
                }
                Err(e) => {
                    tracing::warn!(error = ?e, "Failed to parse capabilities cache");
                    None
                }
            }
        }
        Err(_) => {
            tracing::debug!("No cached capabilities found (first boot)");
            None
        }
    }
}

/// Save capabilities to disk cache
async fn save_capabilities_cache(caps: &HardwareCapabilities) -> anyhow::Result<()> {
    let path = if cfg!(windows) {
        "./stone-capabilities.json"
    } else {
        garden_common::names::STONE_CAPABILITIES_CACHE
    };
    
    let dir = if cfg!(windows) {
        "."
    } else {
        garden_common::names::CONFIG_DIR
    };
    
    tokio::fs::create_dir_all(dir).await?;
    
    let tmp_path = format!("{}.tmp", path);
    let content = serde_json::to_string_pretty(caps)?;
    tokio::fs::write(&tmp_path, content).await?;
    
    match tokio::fs::rename(&tmp_path, path).await {
        Ok(_) => {
            tracing::info!("Saved hardware capabilities cache to {}", path);
            Ok(())
        }
        Err(e) => {
            if cfg!(windows) {
                let _ = tokio::fs::remove_file(path).await;
                tokio::fs::rename(&tmp_path, path).await?;
                tracing::info!("Saved hardware capabilities cache to {}", path);
                Ok(())
            } else {
                Err(e.into())
            }
        }
    }
}

/// Detect hardware capabilities in background (progressive: CPU first, GPU later)
async fn detect_capabilities_background(
    stone_name: String,
    caps_arc: Arc<RwLock<Option<HardwareCapabilities>>>,
    console: Arc<console::ConsolePrinter>,
    state: AppState,
) {
    tracing::info!("Starting background hardware capability detection...");
    
    // === PHASE 1: CPU Detection (fast, <100ms) ===
    console.emit(console::ConsoleEvent::new(
        console::EventCategory::Ops,
        console::EventStatus::Active,
        "[CAPABILITY DETECTION] Detecting CPU features".to_string()
    ));
    
    let (cpu_model, cpu_features, architecture) = match metrics::get_cpu_info() {
        Ok(result) => result,
        Err(e) => {
            tracing::error!(error = ?e, "Failed to get CPU info");
            ("Unknown".to_string(), vec![], std::env::consts::ARCH.to_string())
        }
    };
    
    let resources = metrics::collect_stone_resources().ok();
    let cpu_cores = resources.as_ref().map(|r| r.cpu.cores).unwrap_or(1);
    let total_memory_mb = resources.as_ref()
        .map(|r| r.memory.total_bytes / 1024 / 1024)
        .unwrap_or(0);
    
    let disk = resources.as_ref().map(|r| DiskCapabilities {
        total_gb: r.disk.total_bytes / 1024 / 1024 / 1024,
        disk_type: metrics::detect_disk_type_for_mount(&r.disk.path),
    });
    
    tracing::info!("CPU detection complete: {} cores", cpu_cores);
    console.emit(console::ConsoleEvent::new(
        console::EventCategory::Ops,
        console::EventStatus::Active,
        format!("[CAPABILITY DETECTION] CPU: {} cores, {} features", cpu_cores, cpu_features.len())
    ));
    
    // Make CPU data available immediately (partial status)
    let partial_caps = HardwareCapabilities {
        stone_name: stone_name.clone(),
        hardware: HardwareInventory {
            cpu: CpuCapabilities {
                model: if cpu_model == "Unknown" { None } else { Some(cpu_model.clone()) },
                cores: cpu_cores,
                threads: None,
                architecture: architecture.clone(),
                features: if cpu_features.is_empty() { None } else { Some(cpu_features.clone()) },
            },
            memory: MemoryCapabilities { total_mb: total_memory_mb },
            gpus: vec![],  // Empty, GPU detection still running
            disk: disk.clone(),
            storage: vec![],  // Will be populated with complete caps
            os_version: None,
            kernel_version: None,
            swap_mb: None,
        },
        runtime: Some(RuntimeInfo {
            docker_version: None,
            os: std::env::consts::OS.to_string(),
            kernel: None,
        }),
        detection_status: DetectionStatus::Partial,
    };
    
    // Update in-memory cache immediately
    {
        let mut guard = caps_arc.write().await;
        *guard = Some(partial_caps.clone());
    }
    
    // Persist CPU data to disk (non-blocking for consumers)
    if let Err(e) = save_capabilities_cache(&partial_caps).await {
        tracing::warn!(error = ?e, "Failed to save partial capabilities");
    }
    console.emit(console::ConsoleEvent::new(
        console::EventCategory::System,
        console::EventStatus::Updated,
        "Hardware capabilities (CPU ready)".to_string()
    ));
    
    // === PHASE 2: GPU Detection (slow, 2-6 seconds on Windows) ===
    tracing::info!("Starting GPU detection (may take 2-6 seconds on Windows)...");
    console.emit(console::ConsoleEvent::new(
        console::EventCategory::Ops,
        console::EventStatus::Active,
        "[CAPABILITY DETECTION] Detecting GPUs (DXDiag, 2-6 sec)".to_string()
    ));
    
    let gpus = metrics::detect_gpus();
    let gpu_count = gpus.len();
    tracing::info!(gpu_count = gpus.len(), "GPU detection complete");
    console.emit(console::ConsoleEvent::new(
        console::EventCategory::Ops,
        console::EventStatus::Completed,
        format!("[CAPABILITY DETECTION] Found {} GPU(s)", gpu_count)
    ));
    
    // === PHASE 3: Storage, OS, Kernel, Swap Detection ===
    tracing::info!("Detecting storage and system information...");
    let storage = metrics::detect_storage();
    let os_version = metrics::detect_os_version();
    let kernel_version = metrics::detect_kernel_version();
    let swap_mb = metrics::detect_swap();
    tracing::info!("System information detection complete");
    
    // Build complete capabilities with GPU data
    let complete_caps = HardwareCapabilities {
        stone_name,
        hardware: HardwareInventory {
            cpu: CpuCapabilities {
                model: if cpu_model == "Unknown" { None } else { Some(cpu_model) },
                cores: cpu_cores,
                threads: None,
                architecture,
                features: if cpu_features.is_empty() { None } else { Some(cpu_features) },
            },
            memory: MemoryCapabilities { total_mb: total_memory_mb },
            gpus,
            disk,
            storage,
            os_version,
            kernel_version,
            swap_mb,
        },
        runtime: Some(RuntimeInfo {
            docker_version: None,
            os: std::env::consts::OS.to_string(),
            kernel: None,
        }),
        detection_status: DetectionStatus::Complete,
    };
    
    // Update in-memory cache with complete data
    {
        let mut guard = caps_arc.write().await;
        *guard = Some(complete_caps.clone());
    }
    
    // Persist complete data to disk
    match save_capabilities_cache(&complete_caps).await {
        Ok(_) => {
            tracing::info!("Complete capabilities saved to disk");
            console.emit(console::ConsoleEvent::new(
                console::EventCategory::Ops,
                console::EventStatus::Completed,
                "[CAPABILITY DETECTION] Cache persisted to disk".to_string()
            ));
        },
        Err(e) => tracing::warn!(error = ?e, "Failed to save complete capabilities"),
    }
    
    tracing::info!("Hardware capability detection complete");
    
    // Re-evaluate offerings index now that complete hardware is known
    // This ensures compatibility warnings update (e.g., no AI → no Ollama, no AVX → MongoDB warning)
    tracing::info!("Re-evaluating offerings compatibility with detected hardware...");
    if let Err(e) = ensure_offerings_index(&state, true).await {
        tracing::warn!(error = ?e, "Failed to rebuild offerings index after detection");
    } else {
        console.emit(console::ConsoleEvent::new(
            console::EventCategory::Ops,
            console::EventStatus::Completed,
            "[OFFERINGS] Compatibility re-evaluated".to_string()
        ));
    }
}

/// Windows service installation (Windows only)
#[cfg(target_os = "windows")]
async fn install_windows_service() -> anyhow::Result<()> {
    use std::process::Command;
    
    println!("Installing Zen Garden Moss as Windows service...");
    
    let exe_path = std::env::current_exe()?;
    let exe_path_str = exe_path.to_string_lossy();
    
    // Create service using sc.exe
    let output = Command::new("sc")
        .args([
            "create", "ZenGardenMoss",
            "binPath=", &exe_path_str,
            "start=", "auto",
            "DisplayName=", "Zen Garden Moss Daemon"
        ])
        .output()?;
    
    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        eprintln!("Failed to create service: {}", stderr);
        return Err(anyhow::anyhow!("Service creation failed"));
    }
    
    println!("✓ Service created successfully");
    
    // Start the service
    println!("Starting service...");
    let output = Command::new("sc")
        .args(["start", "ZenGardenMoss"])
        .output()?;
    
    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        eprintln!("Failed to start service: {}", stderr);
        println!("You can start it manually with: sc start ZenGardenMoss");
    } else {
        println!("✓ Service started successfully");
    }
    
    println!("\nMoss is now running as a Windows service.");
    println!("  View status: sc query ZenGardenMoss");
    println!("  Stop: sc stop ZenGardenMoss");
    println!("  Remove: sc delete ZenGardenMoss");
    
    Ok(())
}

/// Finalize Windows update by replacing old binary (runs as garden-moss-new.exe)
#[cfg(target_os = "windows")]
async fn finalize_windows_update() -> anyhow::Result<()> {
    use std::process::Command;
    
    println!("Finalizing Moss update...");
    
    let current_exe = std::env::current_exe()?;
    let exe_dir = current_exe.parent().ok_or_else(|| anyhow::anyhow!("No parent directory"))?;
    let target_exe = exe_dir.join("garden-moss.exe");
    
    // Wait for old process to exit (up to 30 seconds)
    println!("Waiting for old Moss process to exit...");
    for attempt in 1..=60 {
        let output = Command::new("tasklist")
            .args(["/FI", "IMAGENAME eq garden-moss.exe"])
            .output()?;
        
        let stdout = String::from_utf8_lossy(&output.stdout);
        if !stdout.contains("garden-moss.exe") {
            break;
        }
        
        if attempt == 60 {
            eprintln!("Timeout waiting for old process to exit");
            return Err(anyhow::anyhow!("Old process did not exit"));
        }
        
        tokio::time::sleep(tokio::time::Duration::from_millis(500)).await;
    }
    
    println!("Old process exited. Replacing binary...");
    std::fs::copy(&current_exe, &target_exe)?;
    println!("✓ Binary replaced successfully");
    
    // Check if running as service
    let is_service = std::env::var("RUNNING_AS_SERVICE").is_ok();
    
    if is_service {
        println!("Starting Moss service...");
        let _ = Command::new("sc")
            .args(["start", "ZenGardenMoss"])
            .output()?;
        println!("✓ Service start triggered");
    } else {
        println!("Launching new Moss...");
        Command::new(&target_exe)
            .arg("--cleanup-old")
            .spawn()?;
        println!("✓ New Moss launched");
    }
    
    println!("Update complete. This process will now exit.");
    Ok(())
}

/// Cleanup old binary after update
#[cfg(target_os = "windows")]
async fn cleanup_after_update() -> anyhow::Result<()> {
    use std::process::Command;
    
    let current_exe = std::env::current_exe()?;
    let exe_dir = current_exe.parent().ok_or_else(|| anyhow::anyhow!("No parent directory"))?;
    let old_exe = exe_dir.join("garden-moss-new.exe");
    
    if old_exe.exists() {
        // Wait for garden-moss-new.exe process to exit
        for _ in 1..=20 {
            let output = Command::new("tasklist")
                .args(["/FI", "IMAGENAME eq garden-moss-new.exe"])
                .output()?;
            
            let stdout = String::from_utf8_lossy(&output.stdout);
            if !stdout.contains("garden-moss-new.exe") {
                break;
            }
            
            tokio::time::sleep(tokio::time::Duration::from_millis(500)).await;
        }
        
        // Remove old binary
        std::fs::remove_file(&old_exe).ok();
    }
    
    // Continue with normal startup (fall through to main logic)
    Ok(())
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    // Parse CLI arguments first to check for special modes
    let cli = <Cli as clap::Parser>::parse();
    
    // Handle Windows service installation
    #[cfg(target_os = "windows")]
    if cli.install_service {
        return install_windows_service().await;
    }
    
    // Handle update finalization (runs as garden-moss-new.exe)
    #[cfg(target_os = "windows")]
    if cli.update_finalize {
        return finalize_windows_update().await;
    }
    
    // Handle cleanup of old binary after update
    #[cfg(target_os = "windows")]
    if cli.cleanup_old {
        return cleanup_after_update().await;
    }
    
    // Load configuration from file first (lowest priority)
    let config = MossConfig::load();
    
    // CLI already parsed above for special modes, reuse it
    
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
    let env_stone_name = std::env::var(garden_common::ENV_STONE_NAME).ok();
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
        .unwrap_or_else(|| garden_common::DEFAULT_STONE_NAME.to_string());
    
    let port = cli.port
        .or_else(|| config.as_ref().and_then(|c| c.port))
        .unwrap_or(garden_common::ports::MOSS_HTTP);
    
    let fast_sync_timeout = cli.fast_sync_timeout
        .or_else(|| config.as_ref().and_then(|c| c.fast_sync_timeout));
    
    // Determine console mode early for tracing level adjustment
    let console_mode = config.as_ref()
        .and_then(|c| c.console_mode.as_ref())
        .and_then(|mode_str| mode_str.parse::<console::ConsoleMode>().ok())
        .unwrap_or_else(|| console::detect_platform_console_mode());
    
    // Adjust tracing level based on console mode to avoid duplication with console events
    // verbose mode: keep INFO for debugging
    // all other modes: suppress to WARN to avoid spam (console events handle the rest)
    let default_tracing_level = match console_mode {
        console::ConsoleMode::Verbose => "info",
        _ => "warn",  // Suppress INFO logs when console events are active
    };
    
    // Initialize logging with merged log level
    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| EnvFilter::new(default_tracing_level))
        )
        .init();
    
    // Legacy structured log (keep for debugging until full migration)
    tracing::info!(
        stone_name = %stone_name,
        port = port,
        log_level = %log_level,
        fast_sync_timeout = ?fast_sync_timeout,
        config_loaded = config.is_some(),
        "Moss daemon starting with merged configuration (priority: CLI > Env > Config > Defaults)"
    );
    
    // Spawn first-boot initialization as background task if needed (Linux only)
    // Windows/dev environments don't need hostname/hosts/avahi setup
    if cfg!(target_os = "linux") && console::is_first_run() {
        tracing::info!("First run detected on Linux, spawning background initialization task");
        
        // Emit first-boot event (will create console later in initialization)
        tracing::info!("First boot detected - will initialize console after Docker connection");
        
        let init_stone_name = stone_name.clone();
        let init_port = port;
        let retry_delay_secs = config.as_ref().map(|c| c.docker_retry_delay_secs()).unwrap_or(3);
        tokio::spawn(async move {
            const MAX_ATTEMPTS: u32 = 20;
            
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
                                    tokio::time::sleep(tokio::time::Duration::from_secs(retry_delay_secs)).await;
                                }
                            }
                        }
                    }
                    Ok(false) | Err(_) => {
                        if attempt < MAX_ATTEMPTS {
                            tokio::time::sleep(tokio::time::Duration::from_secs(retry_delay_secs)).await;
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
        if let Ok(host) = std::env::var(garden_common::ENV_STONE_HOST) {
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

    // Spawn Lantern registration loop (if LANTERN_ENDPOINT is set)
    if let Ok(lantern_endpoint) = std::env::var(garden_common::ENV_LANTERN_ENDPOINT) {
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

    // Initialize console printer early for Docker connection events
    // Use console_mode from config if available, otherwise detect from platform
    let console_mode = config.as_ref()
        .and_then(|c| c.console_mode.as_ref())
        .and_then(|mode_str| mode_str.parse::<console::ConsoleMode>().ok())
        .unwrap_or_else(|| console::detect_platform_console_mode());
    let dedup_ttl = config.as_ref().map(|c| c.event_dedup_ttl_secs()).unwrap_or(10);
    let console_printer = Arc::new(console::ConsolePrinter::with_dedup_ttl(console_mode, dedup_ttl));
    
    // Emit startup event
    console_printer.emit(console::ConsoleEvent::new(
        console::EventCategory::System,
        console::EventStatus::Starting,
        format!("Moss v{}", moss_version_string())
    ));
    
    // Emit config loading event (config was loaded earlier before console was available)
    if config.is_some() {
        console_printer.emit(console::ConsoleEvent::new(
            console::EventCategory::Config,
            console::EventStatus::Loaded,
            "Configuration file".to_string()
        ));
        
        console_printer.emit(console::ConsoleEvent::new(
            console::EventCategory::Config,
            console::EventStatus::Merged,
            format!("Priority: CLI > Env > Config > Defaults")
        ));
    } else {
        // Config file not found or parse error - emit appropriate event
        // (We can't distinguish between not found vs parse error at this point,
        // but NotFound is more common so we use that)
        console_printer.emit(console::ConsoleEvent::new(
            console::EventCategory::Config,
            console::EventStatus::NotFound,
            "Using defaults".to_string()
        ));
    }

    // Wait for Docker to be ready (with retries for fresh installs)
    let docker = {
        let max_retries = 30; // 30 attempts = ~60 seconds
        let mut retries = 0;
        loop {
            match DockerManager::new() {
                Ok(dm) => {
                    tracing::info!("Docker daemon connected successfully");
                    
                    // Emit Docker connected event
                    console_printer.emit(console::ConsoleEvent::new(
                        console::EventCategory::Docker,
                        console::EventStatus::Connected,
                        "Docker daemon".to_string()
                    ));
                    
                    break Arc::new(dm);
                }
                Err(e) if retries < max_retries => {
                    retries += 1;
                    
                    // Emit retry event (deduplicator will handle spam - retries every 2s)
                    console_printer.emit(console::ConsoleEvent::new(
                        console::EventCategory::Docker,
                        console::EventStatus::Retry,
                        format!("Attempt {}/{}", retries, max_retries)
                    ));
                    
                    // Legacy tracing (keep during migration)
                    tracing::warn!(
                        error = ?e,
                        retry = retries,
                        max_retries = max_retries,
                        "Docker not ready, waiting 2s before retry..."
                    );
                    tokio::time::sleep(tokio::time::Duration::from_secs(2)).await;
                }
                Err(e) => {
                    // Emit connection failure event
                    console_printer.emit(console::ConsoleEvent::new(
                        console::EventCategory::Docker,
                        console::EventStatus::Failed,
                        format!("After {} retries", max_retries)
                    ));
                    
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

    // Load capabilities from disk cache (instant startup - background refresh will update)
    let cached_capabilities = load_capabilities_cache().await;
    let capabilities_arc = Arc::new(RwLock::new(cached_capabilities.clone()));
    
    // If no cache exists, write skeleton immediately so endpoints have valid data
    if cached_capabilities.is_none() {
        let skeleton = HardwareCapabilities {
            stone_name: stone_name.clone(),
            hardware: HardwareInventory {
                cpu: CpuCapabilities {
                    model: None,
                    cores: 0,
                    threads: None,
                    architecture: std::env::consts::ARCH.to_string(),
                    features: None,
                },
                memory: MemoryCapabilities { total_mb: 0 },
                gpus: vec![],  // CRITICAL: Must be present, even if empty
                disk: None,
                storage: vec![],
                os_version: None,
                kernel_version: None,
                swap_mb: None,
            },
            runtime: Some(RuntimeInfo {
                docker_version: None,
                os: std::env::consts::OS.to_string(),
                kernel: None,
            }),
            detection_status: DetectionStatus::Scanning,
        };
        *capabilities_arc.write().await = Some(skeleton.clone());
        let _ = save_capabilities_cache(&skeleton).await;
    } else {
        console_printer.emit(console::ConsoleEvent::new(
            console::EventCategory::System,
            console::EventStatus::Loaded,
            "Hardware capabilities".to_string()
        ));
    }

    let state = AppState {
        stone_name: stone_name.clone(),
        registry: Arc::new(RwLock::new(Vec::new())),
        docker: docker.clone(),
        templates: Arc::new(TemplateLoader::new()),
        jobs: Arc::new(RwLock::new(HashMap::new())),
        event_tx,
        shutdown_tx: shutdown_tx.clone(),
        start_time: std::time::Instant::now(),
        offerings_index: Arc::new(RwLock::new(None)),
        console: console_printer.clone(),
        capabilities: capabilities_arc.clone(),
    };
    
    // Start singleton UDP discovery listener IMMEDIATELY (before any blocking operations)
    // This ensures stones respond to discovery as soon as moss starts, even during initialization
    let discovery_stone_name = stone_name.clone();
    let discovery_endpoint = api_endpoint.clone();
    match discovery::ensure_udp_listener(
        discovery_stone_name,
        discovery_endpoint,
    )
    .await
    {
        Ok(receiver) => {
            // Spawn discovery event monitor (consumes from broadcast pipeline)
            let mut discovery_rx = receiver;
            tokio::spawn(async move {
                while let Ok(event) = discovery_rx.recv().await {
                    tracing::debug!(
                        request_id = %event.request.request_id,
                        from = %event.from_addr,
                        "Discovery request received via broadcast"
                    );
                    // Future: could emit metrics, update dashboards, log analytics, etc.
                }
                tracing::info!("Discovery event monitor stopped");
            });
            
            console_printer.emit(console::ConsoleEvent::new(
                console::EventCategory::Network,
                console::EventStatus::Started,
                format!("UDP discovery on port {}", garden_common::ports::DISCOVERY_UDP)
            ));
        }
        Err(e) => {
            tracing::error!(error = ?e, "Failed to start UDP discovery listener");
            console_printer.emit(console::ConsoleEvent::new(
                console::EventCategory::Network,
                console::EventStatus::Failed,
                format!("UDP discovery: {}", e)
            ));
        }
    }
    
    // Start background hardware detection (progressive: CPU fast, GPU slow)
    let bg_stone_name = stone_name.clone();
    let bg_caps = capabilities_arc.clone();
    let bg_console = console_printer.clone();
    let bg_state = state.clone();
    tokio::spawn(async move {
        bg_console.emit(console::ConsoleEvent::new(
            console::EventCategory::System,
            console::EventStatus::Scanning,
            "Hardware capabilities".to_string()
        ));
        
        // Progressive detection handles its own console updates
        detect_capabilities_background(bg_stone_name.clone(), bg_caps.clone(), bg_console.clone(), bg_state).await;
        
        bg_console.emit(console::ConsoleEvent::new(
            console::EventCategory::System,
            console::EventStatus::Updated,
            "Hardware capabilities (complete)".to_string()
        ));
    });

    // Load persisted registry state and adopt containers in background (non-blocking)
    let registry_state = state.clone();
    tokio::spawn(async move {
        // Load persisted registry state (best-effort)
        match load_registry_from_disk().await {
            Ok(mut loaded) => {
                // Reconcile: if the container no longer exists, mark it offline rather than dropping.
                for svc in loaded.iter_mut() {
                    if !registry_state.docker.zen_container_exists(&svc.name).await.unwrap_or(false) {
                        svc.status = ServiceStatus::Stopped;
                        svc.health = ServiceHealthStatus::Offline;
                    }
                }

                *registry_state.registry.write().await = loaded;
            }
            Err(e) => {
                tracing::warn!(error = ?e, "Failed to load persisted moss registry; starting empty");
            }
        }

        // Startup self-heal: adopt any existing zen-offering containers into the registry
        adopt_existing_containers(&registry_state).await;
    });

    // Build offerings index in background (non-blocking)
    let catalog_state = state.clone();
    let catalog_console = console_printer.clone();
    tokio::spawn(async move {
        tracing::info!("Building offerings catalog...");
        
        // Emit console event for manifest scanning
        catalog_console.emit(console::ConsoleEvent::new(
            console::EventCategory::Manifests,
            console::EventStatus::Scanning,
            "Runtime templates".to_string()
        ));
        
        match ensure_offerings_index(&catalog_state, false).await {
            Ok(_) => {
                let idx_guard = catalog_state.offerings_index.read().await;
                if let Some(idx) = idx_guard.as_ref() {
                    tracing::info!(
                        offerings_count = idx.offerings.len(),
                        "Offerings catalog loaded successfully"
                    );
                    
                    // Emit console event for successful manifest loading
                    catalog_console.emit(console::ConsoleEvent::new(
                        console::EventCategory::Manifests,
                        console::EventStatus::Loaded,
                        format!("{} manifests", idx.offerings.len())
                    ));
                }
            }
            Err(e) => {
                tracing::warn!(error = ?e, "Failed to build offerings catalog - API will return empty results");
                
                // Emit console event for manifest loading error
                catalog_console.emit(console::ConsoleEvent::new(
                    console::EventCategory::Manifests,
                    console::EventStatus::Invalid,
                    "Catalog build failed".to_string()
                ));
            }
        }
    });

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

    tracing::info!("Setting up HTTP router with 200 MB body limit");
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
        
        // V1 API - Console control
        .route("/api/v1/console/mode", get(api::v1::console::get_console_mode_v1))
        .route("/api/v1/console/mode", post(api::v1::console::set_console_mode_v1))
        
        // Apply 200 MB body limit to all routes
        .layer(axum::extract::DefaultBodyLimit::max(200 * 1024 * 1024))
        
        .with_state(state.clone());

    let addr: SocketAddr = format!("0.0.0.0:{}", port).parse()?;
    let listener = tokio::net::TcpListener::bind(addr).await?;
    tracing::info!(
        ?addr,
        api_endpoint = %api_endpoint,
        body_limit_mb = 200,
        "Moss HTTP server ready with 200 MB body limit configured"
    );
    
    // Emit HTTP server ready event
    state.console.emit(console::ConsoleEvent::new(
        console::EventCategory::System,
        console::EventStatus::Ready,
        format!("HTTP server → {}", api_endpoint)
    ));
    
    // Create server with graceful shutdown
    let server = axum::serve(listener, app)
        .with_graceful_shutdown(async move {
            shutdown_signal().await;
            tracing::info!("Shutdown signal received, initiating graceful shutdown");
            
            // Emit shutdown event (note: console_printer needs to be cloned earlier)
            // This will be added when we refactor shutdown signal handling
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
            
            // Emit shutdown event
            state.console.emit(console::ConsoleEvent::new(
                console::EventCategory::System,
                console::EventStatus::Shutting,
                "Admin requested".to_string()
            ));
        }
    }
    
    // Allow in-flight requests to complete (5s timeout)
    tracing::info!("Waiting up to 5s for in-flight requests to complete");
    
    // Emit draining event
    state.console.emit(console::ConsoleEvent::new(
        console::EventCategory::System,
        console::EventStatus::Draining,
        "In-flight requests".to_string()
    ));
    tokio::time::sleep(tokio::time::Duration::from_secs(5)).await;
    
    tracing::info!("Moss daemon shutdown complete");
    
    // Emit stopped event
    state.console.emit(console::ConsoleEvent::new(
        console::EventCategory::System,
        console::EventStatus::Stopped,
        "Shutdown complete".to_string()
    ));
    
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
