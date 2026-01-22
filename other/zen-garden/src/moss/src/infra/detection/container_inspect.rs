//! Container-based service detection
//!
//! Detects services by inspecting Docker containers.
//! Supports:
//! - Container name pattern matching (regex)
//! - Image pattern matching (regex)
//! - Running state verification

use anyhow::{Context, Result};
use regex::Regex;
use crate::docker::DockerManager;
use garden_common::manifests::ContainerInspectDetection;
use super::command::DetectionResult;

/// Detect service by inspecting Docker containers
///
/// # Examples
/// ```ignore
/// let config = ContainerInspectDetection {
///     container_pattern: "zen-offering-mongodb".into(),
///     image_pattern: Some("mongo:.*".into()),
/// };
/// let detected = detect_by_container_inspect(&docker, &config).await?;
/// ```
pub async fn detect_by_container_inspect(
    docker: &DockerManager,
    config: &ContainerInspectDetection,
) -> Result<DetectionResult> {
    tracing::debug!(
        container_pattern = %config.container_pattern,
        "Inspecting containers for detection"
    );

    // Compile regex patterns
    let container_re = Regex::new(&config.container_pattern)
        .context("Invalid container pattern regex")?;

    let image_re = if let Some(image_pat) = &config.image_pattern {
        Some(Regex::new(image_pat).context("Invalid image pattern regex")?)
    } else {
        None
    };

    // List all containers (including stopped)
    let containers = docker.list_all_containers().await
        .context("Failed to list containers")?;

    // Find matching container
    for container in containers {
        let container_name = container.name.trim_start_matches('/');

        // Check container name pattern
        if !container_re.is_match(container_name) {
            continue;
        }

        // Check image pattern if specified
        if let Some(ref image_re) = image_re {
            if !image_re.is_match(&container.image) {
                tracing::debug!(
                    container = %container_name,
                    image = %container.image,
                    "Container name matches but image doesn't"
                );
                continue;
            }
        }

        // Check if container is running
        let is_running = container.state.to_lowercase() == "running";

        if !is_running {
            tracing::debug!(
                container = %container_name,
                state = %container.state,
                "Container found but not running"
            );
            return Ok(DetectionResult {
                detected: false,
                version: None,
                details: format!("Container '{}' exists but is {}", container_name, container.state),
            });
        }

        // Extract version from image tag
        let version = extract_version_from_image(&container.image);

        tracing::info!(
            container = %container_name,
            image = %container.image,
            version = ?version,
            "Service detected via container inspection"
        );

        return Ok(DetectionResult {
            detected: true,
            version,
            details: format!("Detected container: {}", container_name),
        });
    }

    tracing::debug!(
        pattern = %config.container_pattern,
        "No matching containers found"
    );

    Ok(DetectionResult {
        detected: false,
        version: None,
        details: format!("No container matching pattern: {}", config.container_pattern),
    })
}

/// Extract version from Docker image tag
fn extract_version_from_image(image: &str) -> Option<String> {
    // Extract tag after colon (e.g., "mongo:7.0.5" -> "7.0.5")
    if let Some(tag_start) = image.rfind(':') {
        let tag = &image[tag_start + 1..];

        // Skip "latest" and other non-version tags
        if tag == "latest" || tag == "stable" || tag == "edge" {
            return None;
        }

        // Check if tag looks like a version
        if tag.chars().any(|c| c.is_numeric()) {
            return Some(tag.to_string());
        }
    }

    None
}

/// Container information for detection
#[derive(Debug, Clone)]
pub struct ContainerInfo {
    pub name: String,
    pub image: String,
    pub state: String,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_extract_version_from_image() {
        assert_eq!(
            extract_version_from_image("mongo:7.0.5"),
            Some("7.0.5".into())
        );
        assert_eq!(
            extract_version_from_image("postgres:15.3"),
            Some("15.3".into())
        );
        assert_eq!(
            extract_version_from_image("redis:alpine"),
            None  // "alpine" is a distribution tag, not a version
        );
        assert_eq!(
            extract_version_from_image("nginx:latest"),
            None
        );
        assert_eq!(
            extract_version_from_image("nginx"),
            None
        );
    }
}
