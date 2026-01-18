//! Limit Constants
//! System limits and thresholds with GARDEN_ environment variable overrides

/// Minimum free disk space in bytes (default: 1GB)
pub fn min_disk_free_bytes() -> u64 {
    std::env::var("GARDEN_MIN_DISK_FREE_GB")
        .ok()
        .and_then(|v| v.parse::<u64>().ok())
        .map(|gb| gb * 1024 * 1024 * 1024)
        .unwrap_or(1_073_741_824) // 1GB default
}

/// Minimum free memory in bytes (default: 512MB)
pub fn min_memory_free_bytes() -> u64 {
    std::env::var("GARDEN_MIN_MEMORY_FREE_MB")
        .ok()
        .and_then(|v| v.parse::<u64>().ok())
        .map(|mb| mb * 1024 * 1024)
        .unwrap_or(536_870_912) // 512MB default
}

/// Maximum concurrent operations (default: 10)
pub fn max_concurrent_ops() -> usize {
    std::env::var("GARDEN_MAX_CONCURRENT_OPS")
        .ok()
        .and_then(|v| v.parse().ok())
        .unwrap_or(10)
}

/// Maximum log retention days (default: 7)
pub fn max_log_retention_days() -> u32 {
    std::env::var("GARDEN_MAX_LOG_RETENTION_DAYS")
        .ok()
        .and_then(|v| v.parse().ok())
        .unwrap_or(7)
}
