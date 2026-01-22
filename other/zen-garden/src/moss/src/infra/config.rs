//! Moss configuration management
//!
//! Provides centralized configuration loading, validation, and persistence.
//! Configuration is stored in TOML format at platform-specific locations.

/// Moss daemon configuration
///
/// Configuration file format (TOML):
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
pub struct MossConfig {
    /// Stone name identifier - default: "stone-01"
    pub stone_name: Option<String>,

    /// HTTP server port - default: 7185
    pub port: Option<u16>,

    /// Log level (trace/debug/info/warn/error) - default: "info"
    pub log_level: Option<String>,

    /// Fast sync timeout in seconds for rapid offering deployments - default: None (disabled)
    pub fast_sync_timeout: Option<u64>,

    /// Console output mode (silent/minimal/informative/verbose) - default: platform-specific
    pub console_mode: Option<String>,

    /// Event deduplication TTL in seconds - default: 10
    #[serde(default)]
    pub event_dedup_ttl_secs: Option<u64>,

    /// Docker connection retry delay in seconds - default: 3
    #[serde(default)]
    pub docker_retry_delay_secs: Option<u64>,

    /// Health check interval in seconds - default: 30
    #[serde(default)]
    pub health_check_interval_secs: Option<u64>,

    /// Docker reconnect interval in seconds - default: 5
    #[serde(default)]
    pub docker_reconnect_interval_secs: Option<u64>,

    /// HTTP capabilities fetch timeout in seconds - default: 5
    #[serde(default)]
    pub http_capabilities_timeout_secs: Option<u64>,

    /// HTTP health check timeout in seconds - default: 2
    #[serde(default)]
    pub http_health_timeout_secs: Option<u64>,

    /// HTTP quick health check timeout in milliseconds - default: 200
    #[serde(default)]
    pub http_quick_health_timeout_millis: Option<u64>,

    /// HTTP long operation timeout in seconds - default: 300 (5 minutes)
    #[serde(default)]
    pub http_long_operation_timeout_secs: Option<u64>,

    /// Adoption settings for adopted offerings
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub adoption: Option<AdoptionConfig>,
}

/// Adoption configuration for auto-detection and management
#[derive(Debug, Clone, serde::Deserialize, serde::Serialize)]
pub struct AdoptionConfig {
    /// Enable auto-adoption at bootstrap (default: true for regular, false for USB/container)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub enabled: Option<bool>,

    /// Default control level for adopted offerings (default: "monitor")
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub default_control_level: Option<String>,

    /// Exclude patterns for offerings to never adopt (regex patterns)
    #[serde(skip_serializing_if = "Vec::is_empty", default)]
    pub exclude: Vec<String>,

    /// Detection cache TTL in seconds (default: 300)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub detection_cache_ttl_secs: Option<u64>,

    /// Stability threshold - consecutive successes before adoption (default: 2)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub stability_threshold: Option<u8>,
}

impl AdoptionConfig {
    /// Check if adoption is enabled (with deployment profile detection)
    pub fn is_enabled(&self) -> bool {
        if let Some(enabled) = self.enabled {
            enabled
        } else {
            // Auto-detect deployment profile
            Self::default_enabled_for_deployment()
        }
    }

    /// Determine default adoption enabled state based on deployment profile
    fn default_enabled_for_deployment() -> bool {
        // Check for container deployment (ZEN_GARDEN_CONTAINER env var)
        if std::env::var("ZEN_GARDEN_CONTAINER").is_ok() {
            return false; // Container deployment: isolated, no host adoption
        }

        // Check for USB/removable media deployment
        if let Ok(exe_path) = std::env::current_exe() {
            if let Ok(is_removable) = crate::infra::is_running_from_removable_media(&exe_path) {
                if is_removable {
                    return false; // USB Moss: self-contained, no auto-adoption
                }
            }
        }

        // Regular deployment: enable auto-adoption by default
        true
    }

    /// Get default control level
    pub fn default_control_level(&self) -> &str {
        self.default_control_level
            .as_deref()
            .unwrap_or("monitor")
    }

    /// Get detection cache TTL in seconds
    pub fn detection_cache_ttl_secs(&self) -> u64 {
        self.detection_cache_ttl_secs.unwrap_or(300)
    }

    /// Get stability threshold
    pub fn stability_threshold(&self) -> u8 {
        self.stability_threshold.unwrap_or(2)
    }

    /// Check if offering should be excluded from adoption
    pub fn is_excluded(&self, offering: &str) -> bool {
        use regex::Regex;

        for pattern in &self.exclude {
            if let Ok(re) = Regex::new(pattern) {
                if re.is_match(offering) {
                    return true;
                }
            }
        }
        false
    }
}

impl MossConfig {
    /// Load configuration from platform-specific path
    ///
    /// Searches for garden-moss.toml at:
    /// - Linux: /etc/zen-garden/garden-moss.toml
    /// - Windows: ./.zen-garden/garden-moss.toml (current directory)
    ///
    /// Returns None if file not found or contains errors (falls back to defaults)
    pub fn load() -> Option<Self> {
        let config_path = std::path::PathBuf::from(garden_common::names::CONFIG_DIR)
            .join(garden_common::names::MOSS_CONFIG);

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
    pub fn event_dedup_ttl_secs(&self) -> u64 {
        self.event_dedup_ttl_secs.unwrap_or(10)
    }

    /// Get Docker retry delay in seconds (default: 3)
    pub fn docker_retry_delay_secs(&self) -> u64 {
        self.docker_retry_delay_secs.unwrap_or(3)
    }

    /// Get health check interval in seconds (default: 30)
    pub fn health_check_interval_secs(&self) -> u64 {
        self.health_check_interval_secs.unwrap_or(30)
    }

    /// Get Docker reconnect interval in seconds (default: 5)
    pub fn docker_reconnect_interval_secs(&self) -> u64 {
        self.docker_reconnect_interval_secs.unwrap_or(5)
    }

    /// Get HTTP capabilities timeout in seconds (default: 5)
    pub fn http_capabilities_timeout_secs(&self) -> u64 {
        self.http_capabilities_timeout_secs.unwrap_or(5)
    }

    /// Get HTTP health timeout in seconds (default: 2)
    pub fn http_health_timeout_secs(&self) -> u64 {
        self.http_health_timeout_secs.unwrap_or(2)
    }

    /// Get HTTP quick health timeout in milliseconds (default: 200)
    pub fn http_quick_health_timeout_millis(&self) -> u64 {
        self.http_quick_health_timeout_millis.unwrap_or(200)
    }

    /// Get HTTP long operation timeout in seconds (default: 300)
    pub fn http_long_operation_timeout_secs(&self) -> u64 {
        self.http_long_operation_timeout_secs.unwrap_or(300)
    }

    /// Get adoption configuration (with defaults)
    pub fn adoption(&self) -> AdoptionConfig {
        self.adoption.clone().unwrap_or_else(|| AdoptionConfig {
            enabled: None, // Will use deployment profile detection
            default_control_level: None,
            exclude: Vec::new(),
            detection_cache_ttl_secs: None,
            stability_threshold: None,
        })
    }

    /// Save configuration to platform-specific path
    ///
    /// Saves garden-moss.toml to:
    /// - Linux: /etc/zen-garden/garden-moss.toml
    /// - Windows: ./garden-moss.toml (current directory)
    ///
    /// Returns Ok(()) on success, Err on write failure
    pub fn save(&self) -> Result<(), std::io::Error> {
        let config_dir = std::path::PathBuf::from(garden_common::names::CONFIG_DIR);
        std::fs::create_dir_all(&config_dir)?;

        let config_path = config_dir.join(garden_common::names::MOSS_CONFIG);

        let toml_content = toml::to_string_pretty(self)
            .map_err(|e| std::io::Error::new(std::io::ErrorKind::Other, e.to_string()))?;

        std::fs::write(&config_path, toml_content)?;

        tracing::info!(path = ?config_path, "Saved configuration to file");
        Ok(())
    }
}
