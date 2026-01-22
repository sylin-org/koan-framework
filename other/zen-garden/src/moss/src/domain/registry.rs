//! Service registry management
//!
//! Persists running services to survive daemon restarts

use anyhow::Result;
use garden_common::persistence::JsonStorage;
use garden_common::traits::persistence::PersistenceProvider;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::path::PathBuf;

/// Registry entry for a running service
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RegistryEntry {
    pub service_name: String,
    pub offering: String,
    pub version: String,
    pub status: String,
    pub created_at: String,
}

/// Service registry
pub struct Registry {
    storage: JsonStorage<HashMap<String, RegistryEntry>>,
}

impl Registry {
    /// Create a new registry
    pub fn new(file_path: PathBuf) -> Self {
        Self {
            storage: JsonStorage::new(file_path),
        }
    }

    /// Load all registry entries
    pub async fn load_all(&self) -> Result<HashMap<String, RegistryEntry>> {
        Ok(self.storage.load().await?.unwrap_or_default())
    }

    /// Save all registry entries
    pub async fn save_all(&self, entries: &HashMap<String, RegistryEntry>) -> Result<()> {
        Ok(self.storage.save(entries).await?)
    }

    /// Register a service
    pub async fn register(&self, entry: RegistryEntry) -> Result<()> {
        let mut entries = self.load_all().await?;
        entries.insert(entry.service_name.clone(), entry);
        self.save_all(&entries).await
    }

    /// Unregister a service
    pub async fn unregister(&self, service_name: &str) -> Result<()> {
        let mut entries = self.load_all().await?;
        entries.remove(service_name);
        self.save_all(&entries).await
    }

    /// Get a specific service entry
    pub async fn get(&self, service_name: &str) -> Result<Option<RegistryEntry>> {
        let entries = self.load_all().await?;
        Ok(entries.get(service_name).cloned())
    }
}
