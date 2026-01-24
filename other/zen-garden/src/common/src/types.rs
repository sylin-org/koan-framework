//! Zen Common Types
//! Core data structures for service discovery, health, resources, and registry

use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use crate::constants::*;

// ============================================================================
// Service Types
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub enum ServiceStatus {
    /// Service is being installed (image pull, container creation)
    Installing,
    Running,
    Stopped,
    Maintenance,
    Degraded,
    Unknown,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServiceInfo {
    pub name: String,
    pub offering: String,
    pub version: String,
    pub status: ServiceStatus,
    pub health: ServiceHealthStatus,
    pub ports: Ports,
    pub resources: Option<ContainerResources>,
    /// Job ID for tracking installation progress (only set when status is Installing)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub job_id: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Ports {
    pub native: u16,
    pub agnostic: Option<u16>,
}

// ============================================================================
// Health Types
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub enum ServiceHealthStatus {
    Healthy,
    Degraded,
    Offline,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HealthCheck {
    pub status: String,  // "pass", "warn", or "fail"
    #[serde(skip_serializing_if = "Option::is_none")]
    pub message: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ComponentHealth {
    pub status: String,  // "healthy", "degraded", or "unhealthy"
    #[serde(flatten)]
    pub details: HashMap<String, serde_json::Value>,
}

impl ComponentHealth {
    pub fn healthy(details: HashMap<String, serde_json::Value>) -> Self {
        Self {
            status: HEALTH_HEALTHY.to_string(),
            details,
        }
    }

    pub fn degraded(details: HashMap<String, serde_json::Value>) -> Self {
        Self {
            status: HEALTH_DEGRADED.to_string(),
            details,
        }
    }

    pub fn unhealthy(details: HashMap<String, serde_json::Value>) -> Self {
        Self {
            status: HEALTH_UNHEALTHY.to_string(),
            details,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DaemonHealthStatus {
    pub status: String,  // "healthy", "degraded", or "unhealthy"
    pub version: String,  // Software version (e.g., "0.1.202601231053")
    pub timestamp: String,  // ISO 8601 timestamp
    pub components: HashMap<String, ComponentHealth>,
    // Legacy fields for backward compatibility
    #[serde(skip_serializing)]
    pub docker_available: bool,
    #[serde(skip_serializing)]
    pub disk_space_ok: bool,
    #[serde(skip_serializing)]
    pub memory_ok: bool,
    #[serde(skip_serializing)]
    pub uptime_seconds: u64,
    #[serde(skip_serializing_if = "HashMap::is_empty")]
    pub checks: HashMap<String, HealthCheck>,
}

// ============================================================================
// Resource Types
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum DiskType {
    NVMe,
    SSD,
    HDD,
    Unknown,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StorageDevice {
    pub identifier: String,  // e.g., "sda", "nvme0n1", "C:"
    pub size_gb: u64,
    pub disk_type: DiskType,
    pub partition_count: usize,
    pub used_percent: f32,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AiRuntime {
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub cuda_version: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub rocm_version: Option<String>,
    #[serde(default)]
    pub has_directml: bool,
    #[serde(default)]
    pub has_openvino: bool,
}

impl Default for AiRuntime {
    fn default() -> Self {
        Self {
            cuda_version: None,
            rocm_version: None,
            has_directml: false,
            has_openvino: false,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct GpuInfo {
    pub vendor: String,
    pub model: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub vram_mb: Option<u64>,
    #[serde(skip_serializing_if = "Vec::is_empty", default)]
    pub capabilities: Vec<String>,  // "cuda", "rocm", "vulkan", "directml", "opencl"

    /// Detected AI runtimes in dual format
    /// Supports both simple ("cuda") and versioned ("cuda:12.2") formats
    /// Example: ["cuda", "cuda:12.2", "directml"]
    #[serde(skip_serializing_if = "Vec::is_empty", default)]
    pub ai_runtimes: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StoneResources {
    pub cpu: CpuMetrics,
    pub memory: MemoryMetrics,
    pub disk: DiskMetrics,
    pub uptime_seconds: u64,
    pub uptime_friendly: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CpuMetrics {
    pub cores: usize,
    pub usage_percent: f32,
    pub usage_friendly: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MemoryMetrics {
    pub total_bytes: u64,
    pub used_bytes: u64,
    pub available_bytes: u64,
    pub used_percent: f32,
    pub total_friendly: String,
    pub used_friendly: String,
    pub available_friendly: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DiskMetrics {
    pub total_bytes: u64,
    pub used_bytes: u64,
    pub available_bytes: u64,
    pub used_percent: f32,
    pub path: String,
    pub total_friendly: String,
    pub used_friendly: String,
    pub available_friendly: String,
}

/// Hardware capability detection status
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum DetectionStatus {
    /// Detection not yet started or in early stages
    Scanning,
    /// CPU and memory detected, GPU detection in progress
    Partial,
    /// All hardware detection complete
    Complete,
}

/// AI capabilities summary aggregated across all GPUs
///
/// This provides a quick overview of available AI acceleration without
/// needing to iterate through individual GPUs. Useful for:
/// - Fast capability checks ("has any AI runtime?")
/// - Service placement decisions
/// - Lantern service discovery
/// - Health monitoring
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct AiCapabilitiesSummary {
    /// All available runtimes (deduplicated across GPUs)
    /// Supports both simple format ("cuda") and versioned format ("cuda:12.2")
    pub runtimes: Vec<String>,

    /// All GPU vendors present (lowercase)
    pub vendors: Vec<String>,

    /// Total VRAM across all GPUs (MB)
    pub total_vram_mb: u64,

    /// Number of AI-capable GPUs
    pub gpu_count: usize,

    /// Whether hardware detection is complete
    pub detection_complete: bool,
}

impl AiCapabilitiesSummary {
    /// Check if any AI acceleration is available
    pub fn has_any_acceleration(&self) -> bool {
        !self.runtimes.is_empty()
    }

    /// Check if a specific runtime is available (case-insensitive)
    /// Supports both "cuda" and "cuda:12.2" format checks
    pub fn supports_runtime(&self, runtime: &str) -> bool {
        let runtime_lower = runtime.to_lowercase();
        self.runtimes.iter().any(|r| {
            let r_lower = r.to_lowercase();
            // Match either exact or base runtime (e.g., "cuda" matches "cuda:12.2")
            r_lower == runtime_lower || r_lower.starts_with(&format!("{}:", runtime_lower))
        })
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HardwareCapabilities {
    /// Unique stone identifier (GUID v7, generated once on first boot)
    /// Immutable even if hostname changes. Used for cache keying and distributed tracking.
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub stone_id: Option<String>,
    pub stone_name: String,
    pub hardware: HardwareInventory,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub runtime: Option<RuntimeInfo>,
    pub detection_status: DetectionStatus,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HardwareInventory {
    pub cpu: CpuCapabilities,
    pub memory: MemoryCapabilities,
    #[serde(skip_serializing_if = "Vec::is_empty", default)]
    pub gpus: Vec<GpuInfo>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub disk: Option<DiskCapabilities>,
    #[serde(skip_serializing_if = "Vec::is_empty", default)]
    pub storage: Vec<StorageDevice>,
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub os_version: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub kernel_version: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub swap_mb: Option<u64>,

    /// AI capabilities summary (NEW - backwards compatible)
    /// Aggregated view of AI acceleration across all GPUs
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub ai_capabilities: Option<AiCapabilitiesSummary>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CpuCapabilities {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub model: Option<String>,
    pub cores: usize,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub threads: Option<usize>,
    pub architecture: String,
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub features: Option<Vec<String>>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MemoryCapabilities {
    pub total_mb: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DiskCapabilities {
    pub total_gb: u64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub disk_type: Option<String>,  // "SSD", "HDD", "NVMe"
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RuntimeInfo {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub docker_version: Option<String>,
    pub os: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub kernel: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MetricsSnapshot {
    pub timestamp: String,
    pub cpu: CpuMetrics,
    pub memory: MemoryMetrics,
    pub disk: DiskMetrics,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub network: Option<NetworkMetrics>,
    pub uptime_seconds: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct NetworkMetrics {
    pub rx_bytes_per_sec: u64,
    pub tx_bytes_per_sec: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ContainerResources {
    pub cpu_percent: f32,
    pub cpu_friendly: String,
    pub memory_bytes: u64,
    pub memory_limit_bytes: u64,
    pub memory_percent: f32,
    pub memory_friendly: String,
    pub memory_limit_friendly: String,
    pub network_rx_bytes: u64,
    pub network_tx_bytes: u64,
    pub network_rx_friendly: String,
    pub network_tx_friendly: String,
    pub block_read_bytes: u64,
    pub block_write_bytes: u64,
    pub block_read_friendly: String,
    pub block_write_friendly: String,
    pub uptime_seconds: u64,
    pub uptime_friendly: String,
}

// ============================================================================
// Discovery Protocol Types
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DiscoveryRequest {
    pub discover: String,
    pub request_id: String,
    pub requester: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DiscoveryResponse {
    /// Unique stone identifier (GUID v7)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub stone_id: Option<String>,
    pub stone_name: String,
    pub stone_endpoint: String,
    pub moss_version: String,
    pub lantern_endpoint: Option<String>,
}

// ============================================================================
// UDP Announcement Envelope (unified message format)
// ============================================================================

/// UDP Announcement envelope for type-safe message routing
///
/// All UDP broadcasts use this envelope format. Consumers filter by `announcement_type`
/// and deserialize `data` into the appropriate typed payload.
///
/// # Example
/// ```ignore
/// let announcement = UdpAnnouncement {
///     announcement_type: announcement_types::STONE_CHIRP.to_string(),
///     data: serde_json::to_value(&chirp_payload)?,
/// };
/// ```
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UdpAnnouncement {
    /// Announcement type discriminator
    #[serde(rename = "type")]
    pub announcement_type: String,
    /// Typed payload (deserialize based on announcement_type)
    pub data: serde_json::Value,
}

/// Known announcement type constants
pub mod announcement_types {
    /// Discovery request from a stone looking for peers
    pub const DISCOVERY_REQUEST: &str = "discovery_request";
    /// Discovery response to a request
    pub const DISCOVERY_RESPONSE: &str = "discovery_response";
    /// Periodic stone chirp with full state (services, capabilities)
    pub const STONE_CHIRP: &str = "stone_chirp";
    /// Stone going offline announcement (graceful shutdown)
    pub const STONE_GOODBYE: &str = "stone_goodbye";
}

/// Service information for topology entries and chirp payloads
///
/// Lightweight representation of service state for UDP topology broadcasts.
/// Full ServiceInfo (with health, ports, resources) is used in local registry.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TopologyServiceEntry {
    pub name: String,
    pub offering: String,
    pub category: String,
    pub status: String,
}

impl TopologyServiceEntry {
    /// Convert full ServiceInfo to lightweight TopologyServiceEntry
    /// Used when syncing registry to self_entry for chirp broadcasts
    pub fn from_service_info(service: &ServiceInfo, category: Option<&str>) -> Self {
        Self {
            name: service.name.clone(),
            offering: service.offering.clone(),
            category: category.unwrap_or(&service.offering).to_string(),
            status: match service.status {
                ServiceStatus::Running => SERVICE_RUNNING,
                ServiceStatus::Stopped => SERVICE_STOPPED,
                ServiceStatus::Installing => SERVICE_INSTALLING,
                ServiceStatus::Maintenance => SERVICE_MAINTENANCE,
                ServiceStatus::Degraded => SERVICE_DEGRADED,
                ServiceStatus::Unknown => SERVICE_UNKNOWN,
            }.to_string(),
        }
    }
    
    /// Batch convert ServiceInfo vec to TopologyServiceEntry vec
    pub fn from_service_infos(services: &[ServiceInfo]) -> Vec<Self> {
        services.iter()
            .map(|svc| Self::from_service_info(svc, None))
            .collect()
    }
}

/// Stone connectivity status
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum StoneStatus {
    /// Stone is actively announcing (seen within threshold)
    Online,
    /// Stone has stopped announcing but is remembered for WoL
    Offline,
}

impl std::fmt::Display for StoneStatus {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            StoneStatus::Online => write!(f, "online"),
            StoneStatus::Offline => write!(f, "offline"),
        }
    }
}

/// Topology entry representing a stone in the garden
///
/// Used for:
/// - Self topology entry (this stone's current state)
/// - Peer topology cache (discovered stones)
/// - Chirp wire format (UDP broadcast payload)
///
/// Health progresses: starting → initializing → thriving/degraded
/// 
/// **Services**: Full ServiceInfo (richer than ChirpServiceInfo)
/// - Enables detailed service state across all use cases
/// - UDP chirps will be larger (~3-4x) but provide complete info
/// - Optional fields skipped during serialization to reduce payload
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TopologyEntry {
    pub stone_id: String,
    pub stone_name: String,
    pub endpoint: String,
    pub moss_version: String,
    /// Services running on this stone (lightweight topology representation)
    pub services: Vec<TopologyServiceEntry>,
    /// MAC address for Wake-on-LAN support
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub mac: Option<String>,
    /// Health status: "starting", "initializing", "thriving", "degraded"
    pub health: String,
    /// Hardware capabilities (available after detection)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub capabilities: Option<HardwareCapabilities>,
    /// Current connectivity status
    pub status: StoneStatus,
    /// When this stone was first discovered
    pub discovered_at: chrono::DateTime<chrono::Utc>,
    /// When this stone was last seen (chirp received)
    pub last_seen: chrono::DateTime<chrono::Utc>,
}

/// Stone goodbye payload - sent when stone is shutting down gracefully
///
/// Enables immediate offline marking instead of waiting for chirp timeout.
/// Minimal payload - just identification fields needed to find the stone.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StoneGoodbyePayload {
    pub stone_id: String,
    pub stone_name: String,
}

// ============================================================================
// Lantern Service Registry Types
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RegisterRequest {
    /// Unique stone identifier (GUID v7)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub stone_id: Option<String>,
    pub stone_name: String,
    pub endpoint: String,
    pub services: Vec<RegisterServiceInfo>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RegisterServiceInfo {
    pub name: String,
    pub service_type: String,
    pub status: String,
    pub connection_string: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RegisterResponse {
    pub ttl_seconds: u32,
    pub next_heartbeat_seconds: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ResolveRequest {
    pub service_type: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ResolveResponse {
    pub stone_name: String,
    pub endpoint: String,
    pub service: ResolveServiceInfo,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ResolveServiceInfo {
    pub name: String,
    pub service_type: String,
    pub connection_string: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct LanternTopology {
    pub stones: Vec<LanternStoneState>,
    pub last_updated: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct LanternStoneState {
    /// Unique stone identifier (GUID v7)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub stone_id: Option<String>,
    pub name: String,
    pub endpoint: String,
    pub status: String,
    pub services: Vec<LanternServiceState>,
    pub last_seen: String,
    pub first_seen: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub offline_since: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct LanternServiceState {
    pub name: String,
    pub service_type: String,
    pub status: String,
    pub connection_string: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct GardenEvent {
    pub event_type: String,
    pub timestamp: String,
    pub stone_name: String,
    pub details: serde_json::Value,
}

// ============================================================================
// Pond Security Types (Phase 1: surface defined, no implementation)
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PondConfig {
    pub enabled: bool,
    pub keystone_path: Option<String>,
    pub require_mtls: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct KeystoneRequest {
    pub pond_name: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StoneInviteRequest {
    pub stone_name: String,
    pub expiry_hours: Option<u32>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StoneInviteResponse {
    pub invitation_code: String,
    pub expires_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PlaceStoneRequest {
    pub invitation_code: String,
}

// ============================================================================
// Compatibility System Types
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CompatibilityRules {
    pub version: String,
    pub compatibility_rules: Vec<CompatibilityRule>,
    pub post_install_healthcheck: Option<PostInstallHealthcheck>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CompatibilityRule {
    pub name: String,
    pub condition: RuleCondition,
    pub reason: String,
    pub suggestion: Option<String>,
    pub fallback: Option<FallbackConfig>,
    /// If true, this rule produces a warning instead of failing installation.
    /// Use for "proceed with caution" scenarios where the offering may work
    /// but has known issues on certain hardware.
    #[serde(default, skip_serializing_if = "std::ops::Not::not")]
    pub warn_only: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RuleCondition {
    pub processor_models: Option<Vec<String>>,
    pub processor_patterns: Option<Vec<String>>,
    pub cpu_features_missing: Option<Vec<String>>,
    pub architectures: Option<Vec<String>>,
    pub memory_mb_less_than: Option<u64>,
    
    // AI/GPU requirements
    /// Match if ANY of the listed AI runtimes are present (OR logic: ['cuda', 'rocm'])
    #[serde(skip_serializing_if = "Option::is_none")]
    pub requires_ai_any: Option<Vec<String>>,
    /// Match if ALL of the listed AI runtimes are present (AND logic: ['cuda', 'directml'])
    #[serde(skip_serializing_if = "Option::is_none")]
    pub requires_ai_all: Option<Vec<String>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub vram_mb_less_than: Option<u64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub vram_mb_at_least: Option<u64>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FallbackConfig {
    pub image: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PostInstallHealthcheck {
    pub enabled: bool,
    pub scan_log_lines: usize,
    pub timeout_seconds: u64,
    pub patterns: Vec<HealthcheckPattern>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HealthcheckPattern {
    pub pattern: String,
    pub reason: String,
    pub suggestion: Option<String>,
    pub fallback: Option<FallbackConfig>,
}

// ============================================================================
// Offering Modes Types (Multi-deployment patterns)
// ============================================================================

/// Deployment mode for an offering
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum OfferingMode {
    /// Container-based offering managed by Moss (default, current system)
    Managed,
    /// Existing service (native or containerized) adopted by Moss
    Adopted,
    /// External network service announced by Moss
    Borrowed,
}

/// Control level for adopted offerings
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum AdoptedControlLevel {
    /// Moss manages lifecycle (start/stop/restart)
    Full,
    /// Moss monitors health only (default - safe)
    Monitor,
    /// Moss announces existence only (discovery)
    Announce,
}

impl Default for AdoptedControlLevel {
    fn default() -> Self {
        Self::Monitor
    }
}

/// Service network location
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServiceLocation {
    pub host: String,
    pub port: u16,
    pub protocol: String,  // "http", "tcp", "mongodb", "postgres", etc.
}

/// Adopted offering information
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AdoptedOfferingInfo {
    pub name: String,
    pub offering: String,
    pub mode: OfferingMode,
    pub location: ServiceLocation,
    pub control_level: AdoptedControlLevel,
    pub health: ServiceHealthStatus,
    pub detected_at: String,  // ISO 8601

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub version: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub start_command: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub stop_command: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub restart_command: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub health_check_url: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub container_name: Option<String>,
}

/// Borrowed offering information (external service)
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BorrowedOfferingInfo {
    pub name: String,
    pub offering: String,
    pub mode: OfferingMode,
    pub location: ServiceLocation,
    pub announced_at: String,  // ISO 8601

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub health_method: Option<HealthMethod>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub credentials_key: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub connection_template: Option<String>,
}

/// Health check method for borrowed offerings
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum HealthMethod {
    /// HTTP endpoint probe
    Http,
    /// TCP socket connectivity
    Tcp,
    /// No health check (always assume healthy)
    None,
}

// ============================================================================
// API Error Types
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ApiError {
    pub error: ErrorDetails,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ErrorDetails {
    pub code: String,
    pub message: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub details: Option<HashMap<String, serde_json::Value>>,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_service_status_serde() {
        let status = ServiceStatus::Running;
        let json = serde_json::to_string(&status).unwrap();
        let deserialized: ServiceStatus = serde_json::from_str(&json).unwrap();
        assert_eq!(status, deserialized);
    }

    #[test]
    fn test_service_health_status_serde() {
        let health = ServiceHealthStatus::Healthy;
        let json = serde_json::to_string(&health).unwrap();
        let deserialized: ServiceHealthStatus = serde_json::from_str(&json).unwrap();
        assert_eq!(health, deserialized);
    }

    #[test]
    fn test_service_info_serde() {
        let info = ServiceInfo {
            name: "mongodb".into(),
            offering: "mongodb".into(),
            version: "7.0".into(),
            status: ServiceStatus::Running,
            health: ServiceHealthStatus::Healthy,
            ports: Ports {
                native: 27017,
                agnostic: Some(8080),
            },
            resources: None,
        };
        let json = serde_json::to_string(&info).unwrap();
        let deserialized: ServiceInfo = serde_json::from_str(&json).unwrap();
        assert_eq!(info.name, deserialized.name);
        assert_eq!(info.status, deserialized.status);
    }

    #[test]
    fn test_discovery_request_serde() {
        let req = DiscoveryRequest {
            discover: "moss".into(),
            request_id: "test-123".into(),
            requester: "rake".into(),
        };
        let json = serde_json::to_string(&req).unwrap();
        let deserialized: DiscoveryRequest = serde_json::from_str(&json).unwrap();
        assert_eq!(req.discover, deserialized.discover);
        assert_eq!(req.request_id, deserialized.request_id);
    }

    #[test]
    fn test_discovery_response_serde() {
        let resp = DiscoveryResponse {
            stone_id: Some("01234567-89ab-cdef-0123-456789abcdef".into()),
            stone_name: "stone-01".into(),
            stone_endpoint: "http://localhost:3001".into(),
            moss_version: "0.1.0".into(),
            lantern_endpoint: None,
        };
        let json = serde_json::to_string(&resp).unwrap();
        let deserialized: DiscoveryResponse = serde_json::from_str(&json).unwrap();
        assert_eq!(resp.stone_name, deserialized.stone_name);
    }

    #[test]
    fn test_pond_config_defaults() {
        let config = PondConfig {
            enabled: false,
            keystone_path: None,
            require_mtls: false,
        };
        assert!(!config.enabled);
        assert!(!config.require_mtls);
    }

    #[test]
    fn test_stone_invite_request() {
        let req = StoneInviteRequest {
            stone_name: "stone-02".into(),
            expiry_hours: Some(24),
        };
        let json = serde_json::to_string(&req).unwrap();
        assert!(json.contains("stone-02"));
    }

    #[test]
    fn test_offering_mode_serde() {
        let mode = OfferingMode::Adopted;
        let json = serde_json::to_string(&mode).unwrap();
        assert_eq!(json, "\"adopted\"");
        let deserialized: OfferingMode = serde_json::from_str(&json).unwrap();
        assert_eq!(mode, deserialized);
    }

    #[test]
    fn test_adopted_control_level_default() {
        let default = AdoptedControlLevel::default();
        assert_eq!(default, AdoptedControlLevel::Monitor);
    }

    #[test]
    fn test_adopted_control_level_serde() {
        let level = AdoptedControlLevel::Full;
        let json = serde_json::to_string(&level).unwrap();
        assert_eq!(json, "\"full\"");
        let deserialized: AdoptedControlLevel = serde_json::from_str(&json).unwrap();
        assert_eq!(level, deserialized);
    }

    #[test]
    fn test_service_location_serde() {
        let location = ServiceLocation {
            host: "localhost".into(),
            port: 27017,
            protocol: "mongodb".into(),
        };
        let json = serde_json::to_string(&location).unwrap();
        let deserialized: ServiceLocation = serde_json::from_str(&json).unwrap();
        assert_eq!(location.host, deserialized.host);
        assert_eq!(location.port, deserialized.port);
        assert_eq!(location.protocol, deserialized.protocol);
    }

    #[test]
    fn test_adopted_offering_minimal() {
        // Test minimal adopted offering (all optional fields omitted)
        let info = AdoptedOfferingInfo {
            name: "my-mongodb".into(),
            offering: "mongodb".into(),
            mode: OfferingMode::Adopted,
            location: ServiceLocation {
                host: "localhost".into(),
                port: 27017,
                protocol: "mongodb".into(),
            },
            control_level: AdoptedControlLevel::Monitor,
            health: ServiceHealthStatus::Healthy,
            detected_at: "2024-01-01T00:00:00Z".into(),
            version: None,
            start_command: None,
            stop_command: None,
            restart_command: None,
            health_check_url: None,
            container_name: None,
        };
        let json = serde_json::to_string(&info).unwrap();
        // Ensure optional fields are not present in JSON
        assert!(!json.contains("version"));
        assert!(!json.contains("start_command"));
        assert!(!json.contains("stop_command"));
        let deserialized: AdoptedOfferingInfo = serde_json::from_str(&json).unwrap();
        assert_eq!(info.name, deserialized.name);
        assert_eq!(info.offering, deserialized.offering);
    }

    #[test]
    fn test_borrowed_offering_minimal() {
        // Test minimal borrowed offering
        let info = BorrowedOfferingInfo {
            name: "nas-storage".into(),
            offering: "storage".into(),
            mode: OfferingMode::Borrowed,
            location: ServiceLocation {
                host: "nas.local".into(),
                port: 445,
                protocol: "smb".into(),
            },
            announced_at: "2024-01-01T00:00:00Z".into(),
            health_method: None,
            credentials_key: None,
            connection_template: None,
        };
        let json = serde_json::to_string(&info).unwrap();
        // Ensure optional fields are not present in JSON
        assert!(!json.contains("health_method"));
        assert!(!json.contains("credentials_key"));
        assert!(!json.contains("connection_template"));
        let deserialized: BorrowedOfferingInfo = serde_json::from_str(&json).unwrap();
        assert_eq!(info.name, deserialized.name);
    }

    #[test]
    fn test_health_method_serde() {
        let method = HealthMethod::Http;
        let json = serde_json::to_string(&method).unwrap();
        assert_eq!(json, "\"http\"");
        let deserialized: HealthMethod = serde_json::from_str(&json).unwrap();
        assert_eq!(method, deserialized);
    }
}
