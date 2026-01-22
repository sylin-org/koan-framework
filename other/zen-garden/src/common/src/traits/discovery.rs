//! Stone discovery abstraction
//!
//! Priority chain for stone resolution:
//! 1. Explicit `--at stone-name` targeting
//! 2. GARDEN_STONE environment variable
//! 3. Tended stone from config.json
//! 4. UDP broadcast discovery

use async_trait::async_trait;
use std::time::Duration;

/// Discovery result containing stone endpoint
#[derive(Debug, Clone)]
pub struct DiscoveryResult {
    pub stone_name: String,
    pub endpoint: String,
    pub moss_version: String,
    pub lantern_endpoint: Option<String>,
}

/// Error types for discovery operations
#[derive(Debug, thiserror::Error)]
pub enum DiscoveryError {
    #[error("No stones found via broadcast after {0:?}")]
    NoStonesFound(Duration),

    #[error("Stone '{0}' not found")]
    StoneNotFound(String),

    #[error("UDP broadcast failed: {0}")]
    BroadcastFailed(String),

    #[error("Network error: {0}")]
    NetworkError(#[from] std::io::Error),

    #[error("Invalid response: {0}")]
    InvalidResponse(String),
}

/// Stone discovery provider
#[async_trait]
pub trait DiscoveryProvider: Send + Sync {
    /// Discover stones via UDP broadcast
    ///
    /// Sends broadcast on port 3999, waits for responses.
    /// Returns all discovered stones within timeout.
    async fn discover_all(&self, timeout: Duration) -> Result<Vec<DiscoveryResult>, DiscoveryError>;

    /// Find a specific stone by name
    ///
    /// Priority chain:
    /// 1. Check if stone_name matches explicit endpoint format (http://...)
    /// 2. Try UDP broadcast to find stone by name
    /// 3. Check tended stone from config
    /// 4. Check GARDEN_STONE environment variable
    async fn find_stone(&self, stone_name: &str) -> Result<DiscoveryResult, DiscoveryError>;

    /// Resolve stone endpoint using priority chain
    ///
    /// Returns the first valid stone found via:
    /// 1. explicit_target if Some
    /// 2. GARDEN_STONE env var
    /// 3. Tended stone from config
    /// 4. UDP broadcast (first responder)
    async fn resolve_stone(&self, explicit_target: Option<&str>) -> Result<DiscoveryResult, DiscoveryError>;
}
