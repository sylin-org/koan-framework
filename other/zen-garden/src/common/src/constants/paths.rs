//! Path Constants
//! File system paths with GARDEN_ environment variable overrides

/// Get config directory (default: /etc/zen-garden)
pub fn config_dir() -> String {
    std::env::var("GARDEN_CONFIG_DIR").unwrap_or_else(|_| "/etc/zen-garden".to_string())
}

/// Get stone home directory (default: /home/stone)
pub fn stone_home() -> String {
    std::env::var("GARDEN_STONE_HOME").unwrap_or_else(|_| "/home/stone".to_string())
}

/// Get first-run flag path (default: /etc/zen-garden/.first-run-complete)
pub fn first_run_flag() -> String {
    std::env::var("GARDEN_FIRST_RUN_FLAG")
        .unwrap_or_else(|_| "/etc/zen-garden/.first-run-complete".to_string())
}

/// Get stone username (default: stone)
pub fn stone_user() -> String {
    std::env::var("GARDEN_STONE_USER").unwrap_or_else(|_| "stone".to_string())
}
