//! Zen Common Constants
//! Centralized constants for ports, names, paths, timeouts, limits, and error codes

pub mod timeouts;
pub mod paths;
pub mod limits;

// ============================================================================
// Network Ports
// ============================================================================

/// UDP port for stone discovery broadcasts
pub const DISCOVERY_UDP: u16 = 7184;

/// HTTP port for Moss API (default)
pub const MOSS_HTTP: u16 = 7185;

/// HTTP port for Lantern API
pub const LANTERN_HTTP: u16 = 7186;

// ============================================================================
// Component Names
// ============================================================================

/// Binary names
pub const MOSS_BINARY: &str = "garden-moss";
pub const RAKE_BINARY: &str = "garden-rake";
pub const LANTERN_BINARY: &str = "garden-lantern";

/// Config file names
pub const MOSS_CONFIG: &str = "garden-moss.toml";
pub const LANTERN_CONFIG: &str = "garden-lantern.toml";

/// Systemd service names
pub const MOSS_SERVICE: &str = "garden-moss.service";
pub const LANTERN_SERVICE: &str = "garden-lantern.service";

// ============================================================================
// File System Paths
// ============================================================================

/// Common paths
pub const CONFIG_DIR: &str = "/etc/zen-garden";
pub const STONE_USER: &str = "stone";
pub const STONE_HOME: &str = "/home/stone";
pub const FIRST_RUN_FLAG: &str = "/etc/zen-garden/.first-run-complete";
pub const MOSS_REGISTRY: &str = "/etc/zen-garden/moss-registry.json";
pub const MOSS_OFFERINGS_INDEX: &str = "/etc/zen-garden/moss-offerings-index.json";

// ============================================================================
// Standard Error Codes
// ============================================================================

/// Standard error codes for consistent API error responses
/// 
/// Mapped to HTTP status codes:
/// - 400 Bad Request: INVALID_REQUEST, TEMPLATE_NOT_FOUND, CONTAINER_NOT_RUNNING
/// - 404 Not Found: SERVICE_NOT_FOUND, OFFERING_NOT_FOUND, NOT_FOUND, JOB_NOT_FOUND
/// - 500 Internal Server Error: DOCKER_ERROR, INTERNAL_ERROR, REMOVE_FAILED, TEMPLATE_LOAD_FAILED, UPGRADE_FAILED
/// - 503 Service Unavailable: DOCKER_UNAVAILABLE

// 400 Bad Request
pub const INVALID_REQUEST: &str = "INVALID_REQUEST";
pub const TEMPLATE_NOT_FOUND: &str = "TEMPLATE_NOT_FOUND";
pub const CONTAINER_NOT_RUNNING: &str = "CONTAINER_NOT_RUNNING";
pub const INVALID_COMPONENT: &str = "INVALID_COMPONENT";
pub const COMPATIBILITY_FAILED: &str = "COMPATIBILITY_FAILED";

// 404 Not Found
pub const SERVICE_NOT_FOUND: &str = "SERVICE_NOT_FOUND";
pub const OFFERING_NOT_FOUND: &str = "OFFERING_NOT_FOUND";
pub const NOT_FOUND: &str = "NOT_FOUND";
pub const JOB_NOT_FOUND: &str = "JOB_NOT_FOUND";

// 500 Internal Server Error
pub const DOCKER_ERROR: &str = "DOCKER_ERROR";
pub const INTERNAL_ERROR: &str = "INTERNAL_ERROR";
pub const REMOVE_FAILED: &str = "REMOVE_FAILED";
pub const TEMPLATE_LOAD_FAILED: &str = "TEMPLATE_LOAD_FAILED";
pub const UPGRADE_FAILED: &str = "UPGRADE_FAILED";
pub const INSUFFICIENT_RESOURCES: &str = "INSUFFICIENT_RESOURCES";

// 503 Service Unavailable
pub const DOCKER_UNAVAILABLE: &str = "DOCKER_UNAVAILABLE";

// ============================================================================
// Standard Error Codes Documentation
// ============================================================================
// Mapped to HTTP status codes:
// - 400 Bad Request: INVALID_REQUEST, TEMPLATE_NOT_FOUND, CONTAINER_NOT_RUNNING
// - 404 Not Found: SERVICE_NOT_FOUND, OFFERING_NOT_FOUND, NOT_FOUND, JOB_NOT_FOUND
// - 500 Internal Server Error: DOCKER_ERROR, INTERNAL_ERROR, REMOVE_FAILED, TEMPLATE_LOAD_FAILED, UPGRADE_FAILED
// - 503 Service Unavailable: DOCKER_UNAVAILABLE
