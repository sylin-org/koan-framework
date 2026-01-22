//! Command-based service detection
//!
//! Executes shell commands to detect installed services (e.g., "mongod --version").
//! Supports:
//! - Exit code validation
//! - Output pattern matching (regex)
//! - Timeout handling

use anyhow::{Context, Result};
use regex::Regex;
use std::process::Command;
use std::time::Duration;
use garden_common::manifests::CommandDetection;

/// Detect service by executing a command
///
/// # Examples
/// ```ignore
/// let config = CommandDetection {
///     command: "mongod --version".into(),
///     expected_pattern: Some("db version".into()),
///     expected_exit_code: None,
/// };
/// let detected = detect_by_command(&config, Duration::from_secs(5)).await?;
/// ```
pub async fn detect_by_command(
    config: &CommandDetection,
    timeout: Duration,
) -> Result<DetectionResult> {
    tracing::debug!(command = %config.command, "Executing command detection");

    // Parse command into program and args
    let parts: Vec<&str> = config.command.split_whitespace().collect();
    if parts.is_empty() {
        anyhow::bail!("Empty command");
    }

    let program = parts[0];
    let args = &parts[1..];

    // Execute command with timeout
    let output = tokio::time::timeout(
        timeout,
        tokio::task::spawn_blocking({
            let program = program.to_string();
            let args: Vec<String> = args.iter().map(|s| s.to_string()).collect();
            move || {
                Command::new(&program)
                    .args(&args)
                    .output()
            }
        })
    )
    .await
    .context("Command execution timeout")?
    .context("Failed to spawn command")?
    .context("Failed to execute command")?;

    // Check exit code
    let expected_code = config.expected_exit_code.unwrap_or(0);
    let actual_code = output.status.code().unwrap_or(-1);
    if actual_code != expected_code {
        tracing::debug!(
            command = %config.command,
            expected = expected_code,
            actual = actual_code,
            "Command exit code mismatch"
        );
        return Ok(DetectionResult {
            detected: false,
            version: None,
            details: format!("Exit code mismatch: expected {}, got {}", expected_code, actual_code),
        });
    }

    // Check output pattern if specified
    if let Some(pattern_str) = &config.expected_pattern {
        let pattern = Regex::new(pattern_str)
            .context("Invalid regex pattern")?;

        let stdout = String::from_utf8_lossy(&output.stdout);
        let stderr = String::from_utf8_lossy(&output.stderr);
        let combined = format!("{}\n{}", stdout, stderr);

        if !pattern.is_match(&combined) {
            tracing::debug!(
                command = %config.command,
                pattern = %pattern_str,
                "Command output pattern mismatch"
            );
            return Ok(DetectionResult {
                detected: false,
                version: None,
                details: format!("Output pattern not found: {}", pattern_str),
            });
        }

        // Try to extract version from output
        let version = extract_version(&combined);

        tracing::info!(
            command = %config.command,
            version = ?version,
            "Service detected via command"
        );

        return Ok(DetectionResult {
            detected: true,
            version,
            details: format!("Detected via command: {}", config.command),
        });
    }

    // No pattern check - just exit code was enough
    let stdout = String::from_utf8_lossy(&output.stdout);
    let version = extract_version(&stdout);

    tracing::info!(
        command = %config.command,
        version = ?version,
        "Service detected via command"
    );

    Ok(DetectionResult {
        detected: true,
        version,
        details: format!("Detected via command: {}", config.command),
    })
}

/// Attempt to extract version from command output
fn extract_version(text: &str) -> Option<String> {
    // Common version patterns
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

/// Detection result
#[derive(Debug, Clone)]
pub struct DetectionResult {
    /// Whether service was detected
    pub detected: bool,
    /// Extracted version (if available)
    pub version: Option<String>,
    /// Human-readable detection details
    pub details: String,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_extract_version() {
        assert_eq!(extract_version("MongoDB version 7.0.5"), Some("7.0.5".into()));
        assert_eq!(extract_version("v5.4.2"), Some("5.4.2".into()));
        assert_eq!(extract_version("PostgreSQL 15.3"), Some("15.3".into()));
        assert_eq!(extract_version("no version here"), None);
    }

    #[tokio::test]
    async fn test_detect_by_command_success() {
        let config = CommandDetection {
            command: if cfg!(windows) { "cmd /c echo test" } else { "echo test" }.into(),
            expected_pattern: Some("test".into()),
            expected_exit_code: None,
        };

        let result = detect_by_command(&config, Duration::from_secs(5)).await.unwrap();
        assert!(result.detected);
    }

    #[tokio::test]
    async fn test_detect_by_command_not_found() {
        let config = CommandDetection {
            command: "nonexistent_command_12345".into(),
            expected_pattern: None,
            expected_exit_code: None,
        };

        let result = detect_by_command(&config, Duration::from_secs(5)).await;
        assert!(result.is_err());
    }

    #[tokio::test]
    async fn test_detect_by_command_pattern_mismatch() {
        let config = CommandDetection {
            command: if cfg!(windows) { "cmd /c echo test" } else { "echo test" }.into(),
            expected_pattern: Some("nonexistent_pattern".into()),
            expected_exit_code: None,
        };

        let result = detect_by_command(&config, Duration::from_secs(5)).await.unwrap();
        assert!(!result.detected);
    }
}
