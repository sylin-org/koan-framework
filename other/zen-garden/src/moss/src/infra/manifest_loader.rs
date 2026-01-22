//! Offering manifest loader
//!
//! Loads offering manifests from YAML files in the offerings directory.
//! Validates schema and populates the AppState manifest registry.

use anyhow::{Context, Result};
use garden_common::manifests::OfferingManifest;
use std::path::{Path, PathBuf};
use walkdir::WalkDir;

/// Default offerings directory path
pub fn default_offerings_dir() -> PathBuf {
    PathBuf::from("offerings")
}

/// Load all offering manifests from a directory
///
/// Recursively walks the directory tree and loads all .yaml and .yml files.
/// Invalid manifests are logged as warnings and skipped.
///
/// # Example
/// ```rust,ignore
/// let manifests = load_offerings("offerings").await?;
/// println!("Loaded {} offerings", manifests.len());
/// ```
pub async fn load_offerings<P: AsRef<Path>>(dir: P) -> Result<Vec<OfferingManifest>> {
    let dir = dir.as_ref();

    if !dir.exists() {
        tracing::warn!(
            path = %dir.display(),
            "Offerings directory not found, starting with empty manifest registry"
        );
        return Ok(Vec::new());
    }

    let mut manifests = Vec::new();
    let mut errors = Vec::new();

    for entry in WalkDir::new(dir)
        .follow_links(true)
        .into_iter()
        .filter_map(|e| e.ok())
    {
        let path = entry.path();

        // Skip directories and non-YAML files
        if !path.is_file() {
            continue;
        }

        let extension = path.extension().and_then(|s| s.to_str());
        if !matches!(extension, Some("yaml") | Some("yml")) {
            continue;
        }

        match load_manifest_file(path).await {
            Ok(manifest) => {
                tracing::debug!(
                    name = %manifest.name,
                    path = %path.display(),
                    modes = ?manifest.modes,
                    "Loaded offering manifest"
                );
                manifests.push(manifest);
            }
            Err(e) => {
                let error_msg = format!("Failed to load {}: {}", path.display(), e);
                tracing::warn!("{}", error_msg);
                errors.push(error_msg);
            }
        }
    }

    if !errors.is_empty() {
        tracing::warn!(
            "Loaded {} manifests with {} errors",
            manifests.len(),
            errors.len()
        );
    } else {
        tracing::info!(
            "Loaded {} offering manifests from {}",
            manifests.len(),
            dir.display()
        );
    }

    Ok(manifests)
}

/// Load a single manifest file
async fn load_manifest_file<P: AsRef<Path>>(path: P) -> Result<OfferingManifest> {
    let path = path.as_ref();

    let content = tokio::fs::read_to_string(path)
        .await
        .with_context(|| format!("Failed to read file: {}", path.display()))?;

    let manifest: OfferingManifest = serde_yaml::from_str(&content)
        .with_context(|| format!("Failed to parse YAML: {}", path.display()))?;

    // Basic validation
    validate_manifest(&manifest, path)?;

    Ok(manifest)
}

/// Validate manifest structure
fn validate_manifest(manifest: &OfferingManifest, path: &Path) -> Result<()> {
    // Name validation
    if manifest.name.is_empty() {
        anyhow::bail!("Manifest at {} has empty name", path.display());
    }

    // Category validation
    if manifest.category.is_empty() {
        anyhow::bail!(
            "Manifest '{}' at {} has empty category",
            manifest.name,
            path.display()
        );
    }

    // Mode validation
    if manifest.modes.is_empty() {
        anyhow::bail!(
            "Manifest '{}' at {} has no modes specified",
            manifest.name,
            path.display()
        );
    }

    // Mode-specific validation
    for mode in &manifest.modes {
        match mode {
            garden_common::OfferingMode::Managed => {
                // Managed mode requires image
                if manifest.image.is_none() {
                    tracing::warn!(
                        offering = %manifest.name,
                        path = %path.display(),
                        "Managed mode offering has no image specified"
                    );
                }
            }
            garden_common::OfferingMode::Adopted => {
                // Adopted mode should have detection rules
                if manifest.detection.is_empty() {
                    tracing::warn!(
                        offering = %manifest.name,
                        path = %path.display(),
                        "Adopted mode offering has no detection rules"
                    );
                }
            }
            garden_common::OfferingMode::Borrowed => {
                // Borrowed mode requires location
                if manifest.location.is_none() {
                    tracing::warn!(
                        offering = %manifest.name,
                        path = %path.display(),
                        "Borrowed mode offering has no location specified"
                    );
                }
            }
        }
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use garden_common::manifests::{CommandDetection, DetectionConfig, DetectionMethod, DetectionRule};

    #[tokio::test]
    async fn test_load_nonexistent_directory() {
        let result = load_offerings("nonexistent_dir_12345").await;
        assert!(result.is_ok());
        assert_eq!(result.unwrap().len(), 0);
    }

    #[test]
    fn test_validate_minimal_manifest() {
        let manifest = OfferingManifest {
            name: "test".into(),
            category: "test".into(),
            description: "Test offering".into(),
            modes: vec![garden_common::OfferingMode::Adopted],
            tags: vec![],
            image: None,
            ports: vec![],
            environment: vec![],
            volumes: vec![],
            detection: vec![DetectionRule {
                method: DetectionMethod::Command,
                config: DetectionConfig::Command(CommandDetection {
                    command: "test --version".into(),
                    expected_pattern: None,
                    expected_exit_code: None,
                }),
                stability_threshold: None,
                cache_ttl_secs: None,
            }],
            control: None,
            location: None,
            health: None,
            connection_template: None,
        };

        let result = validate_manifest(&manifest, Path::new("test.yaml"));
        assert!(result.is_ok());
    }

    #[test]
    fn test_validate_empty_name() {
        let manifest = OfferingManifest {
            name: "".into(),
            category: "test".into(),
            description: "Test".into(),
            modes: vec![garden_common::OfferingMode::Adopted],
            tags: vec![],
            image: None,
            ports: vec![],
            environment: vec![],
            volumes: vec![],
            detection: vec![],
            control: None,
            location: None,
            health: None,
            connection_template: None,
        };

        let result = validate_manifest(&manifest, Path::new("test.yaml"));
        assert!(result.is_err());
        assert!(result.unwrap_err().to_string().contains("empty name"));
    }

    #[test]
    fn test_validate_no_modes() {
        let manifest = OfferingManifest {
            name: "test".into(),
            category: "test".into(),
            description: "Test".into(),
            modes: vec![],
            tags: vec![],
            image: None,
            ports: vec![],
            environment: vec![],
            volumes: vec![],
            detection: vec![],
            control: None,
            location: None,
            health: None,
            connection_template: None,
        };

        let result = validate_manifest(&manifest, Path::new("test.yaml"));
        assert!(result.is_err());
        assert!(result.unwrap_err().to_string().contains("no modes"));
    }
}
