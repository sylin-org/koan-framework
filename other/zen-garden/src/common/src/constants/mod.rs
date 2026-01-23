//! Zen Common Constants
//! Centralized constants for ports, names, paths, timeouts, limits, and error codes

pub mod timeouts;
pub mod paths;
pub mod limits;

/// Configuration directory path (platform-specific)
///
/// - Linux: `/etc/zen-garden`
/// - Windows: `.zen-garden` (relative to current working directory)
#[cfg(target_os = "windows")]
pub const CONFIG_DIR: &str = ".zen-garden";

#[cfg(not(target_os = "windows"))]
pub const CONFIG_DIR: &str = "/etc/zen-garden";

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

/// Common paths (Linux-only)
pub const STONE_USER: &str = "stone";
pub const STONE_HOME: &str = "/home/stone";
pub const FIRST_RUN_FLAG: &str = "/etc/zen-garden/.first-run-complete";

/// Default stone password (matches STONE_USER by convention)
pub const STONE_PASSWORD: &str = "stone";

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
// Health Status Constants (Moss API)
// ============================================================================

/// Overall daemon health status - healthy
pub const HEALTH_HEALTHY: &str = "healthy";

/// Overall daemon health status - degraded (some components warn/degraded)
pub const HEALTH_DEGRADED: &str = "degraded";

/// Overall daemon health status - unhealthy (critical failure)
pub const HEALTH_UNHEALTHY: &str = "unhealthy";

// ============================================================================
// Health Check Status Constants (HealthCheck struct)
// ============================================================================

/// Health check passed
pub const CHECK_PASS: &str = "pass";

/// Health check warning (degraded but functional)
pub const CHECK_WARN: &str = "warn";

/// Health check failed (critical)
pub const CHECK_FAIL: &str = "fail";

// ============================================================================
// Compatibility Decision Constants
// ============================================================================

/// Offering is fully compatible with stone hardware
pub const COMPAT_PASS: &str = "pass";

/// Offering can run but will use fallback configuration
pub const COMPAT_FALLBACK: &str = "fallback";

/// Offering is incompatible and cannot run on this stone
pub const COMPAT_FAIL: &str = "fail";

// ============================================================================
// Vitality Language Constants (Rake UI)
// ============================================================================

/// Stone/service is healthy and fully operational
pub const VITALITY_THRIVING: &str = "thriving";

/// Stone/service has warnings or degraded performance
pub const VITALITY_NEEDS_ATTENTION: &str = "needs attention";

/// Stone/service is critically unhealthy
pub const VITALITY_WITHERING: &str = "withering";

/// Stone/service is offline or unreachable
pub const VITALITY_DORMANT: &str = "dormant";

// ============================================================================
// Service Status Constants
// ============================================================================

/// Service is running
pub const SERVICE_RUNNING: &str = "running";

/// Service is stopped
pub const SERVICE_STOPPED: &str = "stopped";

/// Service is in maintenance mode
pub const SERVICE_MAINTENANCE: &str = "maintenance";

/// Service is degraded
pub const SERVICE_DEGRADED: &str = "degraded";

/// Service status unknown
pub const SERVICE_UNKNOWN: &str = "unknown";

// ============================================================================
// Environment Variable Names
// ============================================================================

/// Environment variable for stone endpoint override (Rake client)
pub const ENV_GARDEN_STONE: &str = "GARDEN_STONE";

/// Stone name identifier (Moss daemon)
pub const ENV_STONE_NAME: &str = "STONE_NAME";

/// Stone host address (Moss daemon)
pub const ENV_STONE_HOST: &str = "STONE_HOST";

/// Lantern service registry endpoint (Moss discovery)
pub const ENV_LANTERN_ENDPOINT: &str = "LANTERN_ENDPOINT";

/// Environment variable to disable color output (universal standard)
pub const ENV_NO_COLOR: &str = "NO_COLOR";

/// Environment variable to enable Unicode support (Windows override)
pub const ENV_GARDEN_UNICODE: &str = "GARDEN_UNICODE";

// ============================================================================
// Common Default Values
// ============================================================================

/// Unknown value placeholder
pub const VALUE_UNKNOWN: &str = "unknown";

/// Default stone name when no configuration is provided
pub const DEFAULT_STONE_NAME: &str = "stone-01";

// ============================================================================
// HTTP Headers and Authentication
// ============================================================================

/// HTTP Authorization header name (lowercase per HTTP/2 spec)
pub const HEADER_AUTHORIZATION: &str = "authorization";

/// Bearer token authentication scheme prefix
pub const AUTH_BEARER_PREFIX: &str = "Bearer ";

// ============================================================================
// API Endpoint Paths
// ============================================================================

/// Health check endpoint path (used by Moss, Lantern, and Rake)
pub const ENDPOINT_HEALTH: &str = "/health";

/// Hardware capabilities endpoint path (Moss)
pub const ENDPOINT_CAPABILITIES: &str = "/capabilities";

// ============================================================================
// Job/Operation Status Constants
// ============================================================================

/// Job or operation completed successfully
pub const STATUS_COMPLETED: &str = "completed";

/// Operation succeeded (alternative to COMPLETED)
pub const STATUS_SUCCESS: &str = "success";

/// Job or operation failed
pub const STATUS_FAILED: &str = "failed";

/// Error occurred during operation
pub const STATUS_ERROR: &str = "error";

// ============================================================================
// Standard Error Codes Documentation
// ============================================================================
// Mapped to HTTP status codes:
// - 400 Bad Request: INVALID_REQUEST, TEMPLATE_NOT_FOUND, CONTAINER_NOT_RUNNING
// - 404 Not Found: SERVICE_NOT_FOUND, OFFERING_NOT_FOUND, NOT_FOUND, JOB_NOT_FOUND
// - 500 Internal Server Error: DOCKER_ERROR, INTERNAL_ERROR, REMOVE_FAILED, TEMPLATE_LOAD_FAILED, UPGRADE_FAILED
// - 503 Service Unavailable: DOCKER_UNAVAILABLE
