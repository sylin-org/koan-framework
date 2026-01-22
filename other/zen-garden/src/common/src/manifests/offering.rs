//! Offering manifest schema for multi-mode deployments
//!
//! Defines the structure for offering manifests that support:
//! - Managed mode: Container-based deployment (current system)
//! - Adopted mode: Existing service detection and management
//! - Borrowed mode: External service announcement
//!
//! Philosophy: All advanced fields are OPTIONAL - minimal manifests should be 4-6 lines.
//! Optional fields use `#[serde(skip_serializing_if)]` to ensure they're completely omitted
//! when not present (not serialized as null/{}/[]).

use serde::{Deserialize, Serialize};
use crate::types::{AdoptedControlLevel, HealthMethod, OfferingMode};

/// Offering manifest (template + modes + detection)
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct OfferingManifest {
    /// Offering name (e.g., "mongodb", "postgres")
    pub name: String,

    /// Display category (e.g., "database", "ai", "storage")
    pub category: String,

    /// Brief description
    pub description: String,

    /// Supported deployment modes (default: ["managed"])
    #[serde(default = "default_modes")]
    pub modes: Vec<OfferingMode>,

    /// Tags for filtering/search
    #[serde(skip_serializing_if = "Vec::is_empty", default)]
    pub tags: Vec<String>,

    // ===== Managed Mode Configuration =====
    /// Container image (required for managed mode)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub image: Option<String>,

    /// Port mappings: (host_port, container_port)
    #[serde(skip_serializing_if = "Vec::is_empty", default)]
    pub ports: Vec<(u16, u16)>,

    /// Environment variables
    #[serde(skip_serializing_if = "Vec::is_empty", default)]
    pub environment: Vec<String>,

    /// Volume mounts: (host_path, container_path)
    #[serde(skip_serializing_if = "Vec::is_empty", default)]
    pub volumes: Vec<(String, String)>,

    // ===== Adopted Mode Configuration =====
    /// Detection rules for adopted mode (optional)
    #[serde(skip_serializing_if = "Vec::is_empty", default)]
    pub detection: Vec<DetectionRule>,

    /// Control configuration for adopted offerings
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub control: Option<ControlConfig>,

    // ===== Borrowed Mode Configuration =====
    /// Default location for borrowed offerings
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub location: Option<LocationConfig>,

    /// Health check configuration
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub health: Option<HealthConfig>,

    /// Connection template (Tera format)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub connection_template: Option<String>,
}

fn default_modes() -> Vec<OfferingMode> {
    vec![OfferingMode::Managed]
}

/// Detection rule for adopted offerings
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DetectionRule {
    /// Detection method
    pub method: DetectionMethod,

    /// Method-specific configuration
    #[serde(flatten)]
    pub config: DetectionConfig,

    /// Stability threshold (consecutive successes required)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub stability_threshold: Option<u8>,

    /// Cache TTL in seconds (0 = no cache)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub cache_ttl_secs: Option<u64>,
}

/// Detection method
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum DetectionMethod {
    /// Execute command (e.g., "mongod --version")
    Command,
    /// Inspect Docker container
    ContainerInspect,
    /// HTTP probe
    HttpProbe,
}

/// Detection configuration (method-specific)
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(untagged)]
pub enum DetectionConfig {
    Command(CommandDetection),
    ContainerInspect(ContainerInspectDetection),
    HttpProbe(HttpProbeDetection),
}

/// Command-based detection
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CommandDetection {
    /// Command to execute (e.g., "mongod --version")
    pub command: String,

    /// Expected output pattern (regex)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub expected_pattern: Option<String>,

    /// Expected exit code (default: 0)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub expected_exit_code: Option<i32>,
}

/// Container inspection detection
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ContainerInspectDetection {
    /// Container name pattern (regex)
    pub container_pattern: String,

    /// Expected image pattern (optional)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub image_pattern: Option<String>,
}

/// HTTP probe detection
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HttpProbeDetection {
    /// URL to probe
    pub url: String,

    /// Expected HTTP status code (default: 200)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub expected_status: Option<u16>,

    /// Timeout in milliseconds (default: 2000)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub timeout_ms: Option<u64>,
}

/// Control configuration for adopted offerings
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ControlConfig {
    /// Control level (default: monitor)
    #[serde(default)]
    pub level: AdoptedControlLevel,

    /// Start command (required for full control)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub start_command: Option<String>,

    /// Stop command (required for full control)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub stop_command: Option<String>,

    /// Restart command (optional, defaults to stop + start)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub restart_command: Option<String>,

    /// Health check URL for monitoring
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub health_check_url: Option<String>,
}

/// Location configuration for borrowed offerings
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct LocationConfig {
    pub host: String,
    pub port: u16,
    pub protocol: String,
}

/// Health check configuration
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HealthConfig {
    /// Health check method (default: http)
    #[serde(default = "default_health_method")]
    pub method: HealthMethod,

    /// Interval in seconds (default: 30)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub interval_secs: Option<u64>,

    /// Timeout in milliseconds (default: 2000)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub timeout_ms: Option<u64>,

    /// HTTP-specific: endpoint path
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub http_path: Option<String>,
}

fn default_health_method() -> HealthMethod {
    HealthMethod::Http
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_minimal_managed_manifest() {
        // Tier 1: Minimal managed offering
        let manifest = OfferingManifest {
            name: "mongodb".into(),
            category: "database".into(),
            description: "MongoDB NoSQL database".into(),
            modes: vec![OfferingMode::Managed],
            tags: vec![],
            image: Some("mongo:latest".into()),
            ports: vec![(27017, 27017)],
            environment: vec![],
            volumes: vec![],
            detection: vec![],
            control: None,
            location: None,
            health: None,
            connection_template: None,
        };

        let json = serde_json::to_string(&manifest).unwrap();
        // Ensure optional empty/none fields are not present
        assert!(!json.contains("\"tags\""));
        assert!(!json.contains("\"environment\""));
        assert!(!json.contains("\"volumes\""));
        assert!(!json.contains("\"detection\""));
        assert!(!json.contains("\"control\""));
        assert!(!json.contains("\"location\""));
        assert!(!json.contains("\"health\""));
    }

    #[test]
    fn test_minimal_adopted_manifest() {
        // Tier 1: Minimal adopted offering (4 lines in YAML)
        let manifest = OfferingManifest {
            name: "ollama".into(),
            category: "ai".into(),
            description: "Ollama AI runtime".into(),
            modes: vec![OfferingMode::Adopted],
            tags: vec![],
            image: None,
            ports: vec![],
            environment: vec![],
            volumes: vec![],
            detection: vec![DetectionRule {
                method: DetectionMethod::Command,
                config: DetectionConfig::Command(CommandDetection {
                    command: "ollama --version".into(),
                    expected_pattern: None,
                    expected_exit_code: None,
                }),
                stability_threshold: None,
                cache_ttl_secs: None,
            }],
            control: None,
            location: None,
            health: None,
            connection_template: None,
        };

        let json = serde_json::to_string(&manifest).unwrap();
        assert!(json.contains("adopted"));
        assert!(!json.contains("\"control\""));
    }

    #[test]
    fn test_minimal_borrowed_manifest() {
        // Tier 1: Minimal borrowed offering
        let manifest = OfferingManifest {
            name: "nas-storage".into(),
            category: "storage".into(),
            description: "NAS storage".into(),
            modes: vec![OfferingMode::Borrowed],
            tags: vec![],
            image: None,
            ports: vec![],
            environment: vec![],
            volumes: vec![],
            detection: vec![],
            control: None,
            location: Some(LocationConfig {
                host: "nas.local".into(),
                port: 445,
                protocol: "smb".into(),
            }),
            health: None,
            connection_template: None,
        };

        let json = serde_json::to_string(&manifest).unwrap();
        assert!(json.contains("borrowed"));
        assert!(!json.contains("\"health\""));
    }

    #[test]
    fn test_default_modes() {
        let modes = default_modes();
        assert_eq!(modes.len(), 1);
        assert_eq!(modes[0], OfferingMode::Managed);
    }

    #[test]
    fn test_control_level_default() {
        let config = ControlConfig {
            level: AdoptedControlLevel::default(),
            start_command: None,
            stop_command: None,
            restart_command: None,
            health_check_url: None,
        };
        assert_eq!(config.level, AdoptedControlLevel::Monitor);
    }
}
