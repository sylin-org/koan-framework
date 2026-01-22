//! HTTP-based service detection
//!
//! Detects services by probing HTTP endpoints.
//! Supports:
//! - HTTP status code validation
//! - Timeout handling
//! - Both HTTP and HTTPS

use anyhow::{Context, Result};
use std::time::Duration;
use garden_common::manifests::HttpProbeDetection;
use super::command::DetectionResult;

/// Detect service by probing HTTP endpoint
///
/// # Examples
/// ```ignore
/// let config = HttpProbeDetection {
///     url: "http://localhost:11434/api/tags".into(),
///     expected_status: Some(200),
///     timeout_ms: Some(2000),
/// };
/// let detected = detect_by_http_probe(&config).await?;
/// ```
pub async fn detect_by_http_probe(
    config: &HttpProbeDetection,
) -> Result<DetectionResult> {
    tracing::debug!(url = %config.url, "Probing HTTP endpoint");

    let timeout = Duration::from_millis(config.timeout_ms.unwrap_or(2000));
    let expected_status = config.expected_status.unwrap_or(200);

    // Create HTTP client with timeout
    let client = reqwest::Client::builder()
        .timeout(timeout)
        .build()
        .context("Failed to create HTTP client")?;

    // Probe endpoint
    match client.get(&config.url).send().await {
        Ok(response) => {
            let status = response.status().as_u16();

            if status != expected_status {
                tracing::debug!(
                    url = %config.url,
                    expected = expected_status,
                    actual = status,
                    "HTTP status code mismatch"
                );
                return Ok(DetectionResult {
                    detected: false,
                    version: None,
                    details: format!(
                        "HTTP status mismatch: expected {}, got {}",
                        expected_status,
                        status
                    ),
                });
            }

            // Try to extract version from response headers or body
            let version = extract_version_from_response(&response).await;

            tracing::info!(
                url = %config.url,
                status = status,
                version = ?version,
                "Service detected via HTTP probe"
            );

            Ok(DetectionResult {
                detected: true,
                version,
                details: format!("Detected via HTTP probe: {}", config.url),
            })
        }
        Err(e) => {
            tracing::debug!(
                url = %config.url,
                error = ?e,
                "HTTP probe failed"
            );
            Ok(DetectionResult {
                detected: false,
                version: None,
                details: format!("HTTP probe failed: {}", e),
            })
        }
    }
}

/// Attempt to extract version from HTTP response
async fn extract_version_from_response(response: &reqwest::Response) -> Option<String> {
    // Check common version headers
    let headers = response.headers();

    // Common version header names
    let version_headers = [
        "x-version",
        "server",
        "x-powered-by",
        "x-api-version",
    ];

    for header_name in &version_headers {
        if let Some(value) = headers.get(*header_name) {
            if let Ok(value_str) = value.to_str() {
                if let Some(version) = extract_version_from_text(value_str) {
                    return Some(version);
                }
            }
        }
    }

    None
}

/// Extract version from text using common patterns
fn extract_version_from_text(text: &str) -> Option<String> {
    use regex::Regex;

    let patterns = [
        r"version[:\s]+([0-9]+\.[0-9]+(?:\.[0-9]+)?)",
        r"v([0-9]+\.[0-9]+(?:\.[0-9]+)?)",
        r"([0-9]+\.[0-9]+(?:\.[0-9]+)?)",
    ];

    for pattern_str in &patterns {
        if let Ok(re) = Regex::new(pattern_str) {
            if let Some(caps) = re.captures(text) {
                if let Some(version) = caps.get(1) {
                    return Some(version.as_str().to_string());
                }
            }
        }
    }

    None
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_extract_version_from_text() {
        assert_eq!(
            extract_version_from_text("Server: nginx/1.25.3"),
            Some("1.25.3".into())
        );
        assert_eq!(
            extract_version_from_text("X-Version: v5.4.2"),
            Some("5.4.2".into())
        );
        assert_eq!(
            extract_version_from_text("API version 2.1.0"),
            Some("2.1.0".into())
        );
        assert_eq!(
            extract_version_from_text("no version here"),
            None
        );
    }

    #[tokio::test]
    async fn test_detect_by_http_probe_not_found() {
        let config = HttpProbeDetection {
            url: "http://localhost:99999/nonexistent".into(),
            expected_status: None,
            timeout_ms: Some(500),
        };

        let result = detect_by_http_probe(&config).await.unwrap();
        assert!(!result.detected);
    }
}
