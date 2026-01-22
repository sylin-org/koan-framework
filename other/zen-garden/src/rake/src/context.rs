//! Command execution context
//!
//! Provides shared state and utilities for command handlers.
//! This eliminates repetitive setup code in each command.

use crate::ui::{OutputWriter, TerminalInfo};

/// Context passed to command handlers
///
/// Contains all the shared state needed to execute a command:
/// - HTTP client for API calls
/// - Resolved endpoint (if applicable)
/// - Stone name (if resolved)
/// - Output formatting utilities
/// - Mode flags (quiet, fresh)
pub struct CommandContext {
    /// HTTP client with connection pooling
    pub client: reqwest::Client,
    /// Resolved stone endpoint (e.g., "http://10.0.0.5:7185")
    pub endpoint: Option<String>,
    /// Stone name (e.g., "stone-01")
    pub stone_name: Option<String>,
    /// Whether to suppress non-essential output
    pub quiet_mode: bool,
    /// Whether to bypass cache
    pub fresh_mode: bool,
    /// Terminal info for formatting
    pub term: TerminalInfo,
    /// Output writer for consistent formatting
    pub output: OutputWriter,
}

impl CommandContext {
    /// Create context with resolved endpoint
    pub fn with_endpoint(
        client: reqwest::Client,
        endpoint: String,
        stone_name: Option<String>,
        quiet_mode: bool,
        fresh_mode: bool,
    ) -> Self {
        let term = TerminalInfo::detect();
        let output = OutputWriter::new();
        Self {
            client,
            endpoint: Some(endpoint),
            stone_name,
            quiet_mode,
            fresh_mode,
            term,
            output,
        }
    }

    /// Create context without endpoint (for local-only commands)
    pub fn without_endpoint(
        client: reqwest::Client,
        quiet_mode: bool,
        fresh_mode: bool,
    ) -> Self {
        let term = TerminalInfo::detect();
        let output = OutputWriter::new();
        Self {
            client,
            endpoint: None,
            stone_name: None,
            quiet_mode,
            fresh_mode,
            term,
            output,
        }
    }

    /// Get endpoint, returning error if not resolved
    pub fn endpoint(&self) -> anyhow::Result<&str> {
        self.endpoint
            .as_deref()
            .ok_or_else(|| anyhow::anyhow!("No stone endpoint available"))
    }

    /// Build URL for API endpoint
    pub fn api_url(&self, path: &str) -> anyhow::Result<String> {
        let base = self.endpoint()?;
        let base = base.trim_end_matches('/');
        let path = path.trim_start_matches('/');
        Ok(format!("{}/{}", base, path))
    }

    /// Build URL for v1 API endpoint
    pub fn api_v1_url(&self, path: &str) -> anyhow::Result<String> {
        let path = path.trim_start_matches('/');
        self.api_url(&format!("api/v1/{}", path))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_api_url_building() {
        let ctx = CommandContext::with_endpoint(
            reqwest::Client::new(),
            "http://10.0.0.5:7185".to_string(),
            Some("stone-01".to_string()),
            false,
            false,
        );

        assert_eq!(
            ctx.api_url("health").unwrap(),
            "http://10.0.0.5:7185/health"
        );
        assert_eq!(
            ctx.api_v1_url("services").unwrap(),
            "http://10.0.0.5:7185/api/v1/services"
        );
        assert_eq!(
            ctx.api_v1_url("/services").unwrap(),
            "http://10.0.0.5:7185/api/v1/services"
        );
    }

    #[test]
    fn test_endpoint_without_resolution() {
        let ctx = CommandContext::without_endpoint(
            reqwest::Client::new(),
            false,
            false,
        );

        assert!(ctx.endpoint().is_err());
        assert!(ctx.api_url("health").is_err());
    }
}
