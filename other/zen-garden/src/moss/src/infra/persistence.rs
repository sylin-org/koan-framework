//! Persistence layer - disk I/O for registry, offerings cache, etc.
//!
//! Composable functions for atomic file operations with proper error handling.
//! All persistence uses temp file + rename for atomic writes.

use anyhow::Result;
use std::path::PathBuf;
use garden_common::ServiceInfo;
use std::collections::HashMap;

/// Get offerings cache file path
fn offerings_cache_path() -> PathBuf {
    PathBuf::from(garden_common::names::CONFIG_DIR).join("offerings_cache.json")
}

/// Load registry from disk
///
/// Returns empty vec if file doesn't exist.
pub async fn load_registry() -> Result<Vec<ServiceInfo>> {
    let path = PathBuf::from(garden_common::names::CONFIG_DIR).join("moss-registry.json");

    match tokio::fs::read_to_string(path).await {
        Ok(content) => {
            let services = serde_json::from_str(&content)?;
            Ok(services)
        }
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(Vec::new()),
        Err(e) => Err(e.into()),
    }
}

/// Save registry to disk (atomic write)
///
/// Converts HashMap to Vec for JSON serialization.
pub async fn save_registry(registry: &HashMap<String, ServiceInfo>) -> Result<()> {
    let dir = PathBuf::from(garden_common::names::CONFIG_DIR);
    let path = dir.join("moss-registry.json");
    tokio::fs::create_dir_all(&dir).await?;

    // Convert HashMap to Vec for serialization
    let services: Vec<_> = registry.values().cloned().collect();

    atomic_write(path, &services).await
}

/// Save registry from Vec to disk (atomic write)
///
/// Direct Vec version for AppState integration.
pub async fn save_registry_vec(services: &[ServiceInfo]) -> Result<()> {
    let dir = PathBuf::from(garden_common::names::CONFIG_DIR);
    let path = dir.join("moss-registry.json");
    tokio::fs::create_dir_all(&dir).await?;

    atomic_write(path, &services).await
}

/// Load offerings cache from disk
///
/// Returns None if cache doesn't exist or is invalid.
pub async fn load_offerings_cache<T: serde::de::DeserializeOwned>() -> Result<Option<T>> {
    let path = offerings_cache_path();

    match tokio::fs::read_to_string(&path).await {
        Ok(content) => {
            match serde_json::from_str(&content) {
                Ok(cache) => Ok(Some(cache)),
                Err(e) => {
                    tracing::warn!(error = ?e, "Invalid offerings cache, will rebuild");
                    Ok(None)
                }
            }
        }
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(None),
        Err(e) => Err(e.into()),
    }
}

/// Save offerings cache to disk (atomic write)
pub async fn save_offerings_cache<T: serde::Serialize>(cache: &T) -> Result<()> {
    let dir = PathBuf::from(garden_common::names::CONFIG_DIR);
    tokio::fs::create_dir_all(&dir).await?;

    let path = offerings_cache_path();
    atomic_write(&path, cache).await
}

/// Atomic file write helper
///
/// Uses temp file + rename for atomic writes.
/// Handles Windows rename-over-existing-file issue.
async fn atomic_write<T: serde::Serialize, P: AsRef<std::path::Path>>(
    path: P,
    data: &T,
) -> Result<()> {
    let path = path.as_ref();
    let tmp_path = path.with_extension("tmp");

    let content = serde_json::to_string_pretty(data)?;
    tokio::fs::write(&tmp_path, content).await?;

    match tokio::fs::rename(&tmp_path, path).await {
        Ok(_) => Ok(()),
        Err(e) => {
            // Windows doesn't allow rename over existing file
            if cfg!(windows) {
                let _ = tokio::fs::remove_file(path).await;
                tokio::fs::rename(&tmp_path, path).await?;
                Ok(())
            } else {
                Err(e.into())
            }
        }
    }
}

/// Stone ID file path
fn stone_id_path() -> PathBuf {
    PathBuf::from(garden_common::names::CONFIG_DIR).join("stone-id")
}

/// Load or generate the stone ID (GUID v7)
///
/// The stone ID is a persistent, immutable identifier for this stone.
/// It survives hostname changes, IP changes, and most upgrades.
/// Generated once on first boot using GUID v7 (timestamp-encoded).
pub async fn load_or_generate_stone_id() -> String {
    let path = stone_id_path();

    // Try to load existing stone ID
    if let Ok(content) = tokio::fs::read_to_string(&path).await {
        let id = content.trim().to_string();
        if !id.is_empty() {
            tracing::debug!(stone_id = %id, "Loaded existing stone ID");
            return id;
        }
    }

    // Generate new stone ID (GUID v7 for timestamp ordering)
    let new_id = uuid::Uuid::now_v7().to_string();
    tracing::info!(stone_id = %new_id, "Generated new stone ID");

    // Persist to disk
    if let Err(e) = save_stone_id(&new_id).await {
        tracing::warn!(error = ?e, "Failed to persist stone ID (will regenerate on restart)");
    }

    new_id
}

/// Save stone ID to disk
async fn save_stone_id(stone_id: &str) -> Result<()> {
    let dir = PathBuf::from(garden_common::names::CONFIG_DIR);
    tokio::fs::create_dir_all(&dir).await?;

    let path = stone_id_path();
    tokio::fs::write(&path, stone_id).await?;
    tracing::debug!(path = ?path, "Persisted stone ID");
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde::{Serialize, Deserialize};

    #[derive(Serialize, Deserialize, PartialEq, Debug)]
    struct TestData {
        value: String,
    }

    #[tokio::test]
    async fn test_atomic_write() {
        let temp_dir = std::env::temp_dir();
        let test_file = temp_dir.join("test_atomic.json");

        let data = TestData { value: "test".into() };
        atomic_write(&test_file, &data).await.expect("write failed");

        let content = tokio::fs::read_to_string(&test_file).await.expect("read failed");
        let loaded: TestData = serde_json::from_str(&content).expect("parse failed");

        assert_eq!(loaded, data);

        // Cleanup
        let _ = tokio::fs::remove_file(&test_file).await;
    }
}
