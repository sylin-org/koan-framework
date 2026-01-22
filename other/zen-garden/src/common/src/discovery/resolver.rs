//! Discovery resolver with priority chain
//!
//! Resolves stone endpoints using:
//! 1. Explicit target (--at parameter)
//! 2. GARDEN_STONE environment variable
//! 3. Tended stone from config.json
//! 4. UDP broadcast (first responder)

use crate::traits::discovery::{DiscoveryProvider, DiscoveryResult, DiscoveryError};
use crate::discovery::UdpDiscovery;
use async_trait::async_trait;
use std::time::Duration;

/// Discovery resolver implementing priority chain
pub struct DiscoveryResolver {
    udp: UdpDiscovery,
    config_path: Option<std::path::PathBuf>,
}

impl DiscoveryResolver {
    /// Create a new discovery resolver
    pub fn new() -> Self {
        Self {
            udp: UdpDiscovery::default(),
            config_path: None,
        }
    }

    /// Set config file path for tended stone resolution
    pub fn with_config(mut self, config_path: std::path::PathBuf) -> Self {
        self.config_path = Some(config_path);
        self
    }

    /// Get tended stone from config.json
    async fn get_tended_stone(&self) -> Option<String> {
        let config_path = self.config_path.as_ref()?;

        let content = tokio::fs::read_to_string(config_path).await.ok()?;
        let config: serde_json::Value = serde_json::from_str(&content).ok()?;

        config.get("tended_stone")
            .and_then(|v| v.as_str())
            .map(|s| s.to_string())
    }

    /// Get stone from GARDEN_STONE environment variable
    fn get_env_stone() -> Option<String> {
        std::env::var("GARDEN_STONE").ok()
    }

    /// Check if target is an explicit endpoint (http://, https://)
    fn is_explicit_endpoint(target: &str) -> bool {
        target.starts_with("http://") || target.starts_with("https://")
    }
}

impl Default for DiscoveryResolver {
    fn default() -> Self {
        Self::new()
    }
}

#[async_trait]
impl DiscoveryProvider for DiscoveryResolver {
    async fn discover_all(&self, timeout: Duration) -> Result<Vec<DiscoveryResult>, DiscoveryError> {
        self.udp.discover_all(timeout).await
    }

    async fn find_stone(&self, stone_name: &str) -> Result<DiscoveryResult, DiscoveryError> {
        // If stone_name is an explicit endpoint, return it directly
        if Self::is_explicit_endpoint(stone_name) {
            return Ok(DiscoveryResult {
                stone_name: "explicit".into(),
                endpoint: stone_name.to_string(),
                moss_version: "unknown".into(),
                lantern_endpoint: None,
            });
        }

        // Try UDP discovery with 2 second timeout
        self.udp.find_stone(stone_name, Duration::from_secs(2)).await
    }

    async fn resolve_stone(&self, explicit_target: Option<&str>) -> Result<DiscoveryResult, DiscoveryError> {
        // 1. Explicit target (--at parameter)
        if let Some(target) = explicit_target {
            return self.find_stone(target).await;
        }

        // 2. GARDEN_STONE environment variable
        if let Some(env_stone) = Self::get_env_stone() {
            return self.find_stone(&env_stone).await;
        }

        // 3. Tended stone from config
        if let Some(tended_stone) = self.get_tended_stone().await {
            return self.find_stone(&tended_stone).await;
        }

        // 4. UDP broadcast (first responder)
        let mut stones = self.discover_all(Duration::from_secs(2)).await?;

        stones
            .pop()
            .ok_or_else(|| DiscoveryError::NoStonesFound(Duration::from_secs(2)))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_is_explicit_endpoint() {
        assert!(DiscoveryResolver::is_explicit_endpoint("http://localhost:3001"));
        assert!(DiscoveryResolver::is_explicit_endpoint("https://stone-01.local:3001"));
        assert!(!DiscoveryResolver::is_explicit_endpoint("stone-01"));
        assert!(!DiscoveryResolver::is_explicit_endpoint("192.168.1.100"));
    }

    #[tokio::test]
    async fn test_find_stone_with_explicit_endpoint() {
        let resolver = DiscoveryResolver::new();
        let result = resolver.find_stone("http://localhost:3001").await.unwrap();

        assert_eq!(result.endpoint, "http://localhost:3001");
    }

    #[test]
    fn test_get_env_stone() {
        std::env::set_var("GARDEN_STONE", "test-stone");
        let stone = DiscoveryResolver::get_env_stone();
        assert_eq!(stone, Some("test-stone".into()));
        std::env::remove_var("GARDEN_STONE");
    }

    #[tokio::test]
    async fn test_get_tended_stone() {
        use tempfile::TempDir;

        let temp_dir = TempDir::new().unwrap();
        let config_path = temp_dir.path().join("config.json");

        tokio::fs::write(
            &config_path,
            r#"{"tended_stone": "stone-01"}"#,
        )
        .await
        .unwrap();

        let resolver = DiscoveryResolver::new().with_config(config_path);
        let tended = resolver.get_tended_stone().await;

        assert_eq!(tended, Some("stone-01".into()));
    }
}
