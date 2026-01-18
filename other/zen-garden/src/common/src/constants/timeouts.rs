//! Timeout Constants
//! Centralized timeout values with GARDEN_ environment variable overrides

use std::time::Duration;

/// Parse environment variable as duration in seconds, returning default if not set or invalid
fn env_duration_secs(var_name: &str, default_secs: u64) -> Duration {
    std::env::var(var_name)
        .ok()
        .and_then(|v| v.parse::<u64>().ok())
        .map(Duration::from_secs)
        .unwrap_or_else(|| Duration::from_secs(default_secs))
}

/// Parse environment variable as duration in milliseconds, returning default if not set or invalid
fn env_duration_millis(var_name: &str, default_millis: u64) -> Duration {
    std::env::var(var_name)
        .ok()
        .and_then(|v| v.parse::<u64>().ok())
        .map(Duration::from_millis)
        .unwrap_or_else(|| Duration::from_millis(default_millis))
}

// ============================================================================
// Discovery Timeouts
// ============================================================================

/// Discovery broadcast timeout (default 3s)
pub fn discovery_timeout() -> Duration {
    env_duration_secs("GARDEN_DISCOVERY_TIMEOUT_SECS", 3)
}

/// Quick discovery timeout (default 2s)
pub fn discovery_quick_timeout() -> Duration {
    env_duration_secs("GARDEN_DISCOVERY_QUICK_TIMEOUT_SECS", 2)
}

// ============================================================================
// Cache TTL
// ============================================================================

/// Cache time-to-live (default 90s)
pub fn cache_ttl() -> Duration {
    env_duration_secs("GARDEN_CACHE_TTL_SECS", 90)
}

// ============================================================================
// HTTP Timeouts
// ============================================================================

/// HTTP request timeout (default 30s)
pub fn http_request_timeout() -> Duration {
    env_duration_secs("GARDEN_HTTP_REQUEST_TIMEOUT_SECS", 30)
}

/// HTTP connection timeout (default 5s)
pub fn http_connect_timeout() -> Duration {
    env_duration_secs("GARDEN_HTTP_CONNECT_TIMEOUT_SECS", 5)
}

// ============================================================================
// Retry and First-Boot Timeouts
// ============================================================================

/// First-boot retry delay (default 3s)
pub fn first_boot_retry_delay() -> Duration {
    env_duration_secs("GARDEN_FIRST_BOOT_RETRY_DELAY_SECS", 3)
}

/// First-boot total window (default 60s)
pub fn first_boot_window() -> Duration {
    env_duration_secs("GARDEN_FIRST_BOOT_WINDOW_SECS", 60)
}

/// First-boot maximum attempts (default 20)
pub fn first_boot_max_attempts() -> u32 {
    std::env::var("GARDEN_FIRST_BOOT_MAX_ATTEMPTS")
        .ok()
        .and_then(|v| v.parse().ok())
        .unwrap_or(20)
}

// ============================================================================
// Polling Intervals
// ============================================================================

/// Standard polling interval (default 1s)
pub fn poll_interval_1s() -> Duration {
    env_duration_secs("GARDEN_POLL_INTERVAL_1S", 1)
}

/// 2-second polling interval
pub fn poll_interval_2s() -> Duration {
    env_duration_secs("GARDEN_POLL_INTERVAL_2S", 2)
}

/// 5-second polling interval
pub fn poll_interval_5s() -> Duration {
    env_duration_secs("GARDEN_POLL_INTERVAL_5S", 5)
}

/// 10-second polling interval
pub fn poll_interval_10s() -> Duration {
    env_duration_secs("GARDEN_POLL_INTERVAL_10S", 10)
}

/// 15-second polling interval
pub fn poll_interval_15s() -> Duration {
    env_duration_secs("GARDEN_POLL_INTERVAL_15S", 15)
}

/// 30-second polling interval
pub fn poll_interval_30s() -> Duration {
    env_duration_secs("GARDEN_POLL_INTERVAL_30S", 30)
}

/// 45-second polling interval
pub fn poll_interval_45s() -> Duration {
    env_duration_secs("GARDEN_POLL_INTERVAL_45S", 45)
}

/// Short sleep duration (default 100ms)
pub fn sleep_short() -> Duration {
    env_duration_millis("GARDEN_SLEEP_SHORT_MS", 100)
}

/// Medium sleep duration (default 500ms)
pub fn sleep_medium() -> Duration {
    env_duration_millis("GARDEN_SLEEP_MEDIUM_MS", 500)
}
