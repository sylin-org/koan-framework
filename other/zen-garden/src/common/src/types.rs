//! Zen Common Types
//! Core data structures for service discovery, health, resources, and registry

use serde::{Deserialize, Serialize};
use std::collections::HashMap;

// ============================================================================
// Service Types
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub enum ServiceStatus {
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
            status: "healthy".to_string(),
            details,
        }
    }

    pub fn degraded(details: HashMap<String, serde_json::Value>) -> Self {
        Self {
            status: "degraded".to_string(),
            details,
        }
    }

    pub fn unhealthy(details: HashMap<String, serde_json::Value>) -> Self {
        Self {
            status: "unhealthy".to_string(),
            details,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DaemonHealthStatus {
    pub status: String,  // "healthy", "degraded", or "unhealthy"
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

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct GpuInfo {
    pub vendor: String,
    pub model: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub vram_mb: Option<u64>,
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub capabilities: Vec<String>,  // "cuda", "rocm", "vulkan", "directml", "opencl"
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

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HardwareCapabilities {
    pub stone_name: String,
    pub hardware: HardwareInventory,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub runtime: Option<RuntimeInfo>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HardwareInventory {
    pub cpu: CpuCapabilities,
    pub memory: MemoryCapabilities,
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub gpus: Vec<GpuInfo>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub disk: Option<DiskCapabilities>,
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
    pub stone_name: String,
    pub stone_endpoint: String,
    pub moss_version: String,
    pub lantern_endpoint: Option<String>,
}

// ============================================================================
// Lantern Service Registry Types
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RegisterRequest {
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
    pub pebble_path: Option<String>,
    pub require_mtls: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PebbleRequest {
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
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RuleCondition {
    pub processor_models: Option<Vec<String>>,
    pub processor_patterns: Option<Vec<String>>,
    pub cpu_features_missing: Option<Vec<String>>,
    pub architectures: Option<Vec<String>>,
    pub memory_mb_less_than: Option<u64>,
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
            pebble_path: None,
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
}
