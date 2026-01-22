//! Generic JSON file storage with atomic writes

use super::atomic_file::{atomic_write_file, read_file, file_exists, delete_file};
use crate::traits::persistence::{PersistenceProvider, PersistenceError};
use async_trait::async_trait;
use serde::{Deserialize, Serialize};
use std::path::PathBuf;

/// Generic JSON file storage
///
/// Implements PersistenceProvider trait for any JSON-serializable type.
/// Uses atomic file writes to prevent corruption.
pub struct JsonStorage<T> {
    file_path: PathBuf,
    _phantom: std::marker::PhantomData<T>,
}

impl<T> JsonStorage<T> {
    /// Create a new JSON storage instance
    pub fn new(file_path: PathBuf) -> Self {
        Self {
            file_path,
            _phantom: std::marker::PhantomData,
        }
    }

    /// Get the storage file path
    pub fn path(&self) -> &PathBuf {
        &self.file_path
    }
}

#[async_trait]
impl<T> PersistenceProvider<T> for JsonStorage<T>
where
    T: Serialize + for<'de> Deserialize<'de> + Send + Sync,
{
    async fn load(&self) -> Result<Option<T>, PersistenceError> {
        if !file_exists(&self.file_path).await {
            return Ok(None);
        }

        let bytes = read_file(&self.file_path)
            .await
            .map_err(|e| PersistenceError::ReadFailed {
                path: self.file_path.display().to_string(),
                source: e,
            })?;

        let data: T = serde_json::from_slice(&bytes).map_err(|e| {
            PersistenceError::CorruptedData {
                path: self.file_path.display().to_string(),
                reason: e.to_string(),
            }
        })?;

        Ok(Some(data))
    }

    async fn save(&self, data: &T) -> Result<(), PersistenceError> {
        let json = serde_json::to_string_pretty(data)?;

        atomic_write_file(&self.file_path, json.as_bytes())
            .await
            .map_err(|e| PersistenceError::WriteFailed {
                path: self.file_path.display().to_string(),
                source: e,
            })?;

        Ok(())
    }

    async fn delete(&self) -> Result<(), PersistenceError> {
        delete_file(&self.file_path)
            .await
            .map_err(|e| PersistenceError::WriteFailed {
                path: self.file_path.display().to_string(),
                source: e,
            })
    }

    async fn exists(&self) -> bool {
        file_exists(&self.file_path).await
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde::{Deserialize, Serialize};
    use tempfile::TempDir;

    #[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
    struct TestData {
        name: String,
        value: i32,
    }

    #[tokio::test]
    async fn test_json_storage_save_and_load() {
        let temp_dir = TempDir::new().unwrap();
        let storage: JsonStorage<TestData> = JsonStorage::new(
            temp_dir.path().join("test.json"),
        );

        let data = TestData {
            name: "test".into(),
            value: 42,
        };

        // Save
        storage.save(&data).await.unwrap();
        assert!(storage.exists().await);

        // Load
        let loaded = storage.load().await.unwrap().unwrap();
        assert_eq!(data, loaded);
    }

    #[tokio::test]
    async fn test_json_storage_load_nonexistent() {
        let temp_dir = TempDir::new().unwrap();
        let storage: JsonStorage<TestData> = JsonStorage::new(
            temp_dir.path().join("nonexistent.json"),
        );

        let loaded = storage.load().await.unwrap();
        assert!(loaded.is_none());
    }

    #[tokio::test]
    async fn test_json_storage_overwrite() {
        let temp_dir = TempDir::new().unwrap();
        let storage: JsonStorage<TestData> = JsonStorage::new(
            temp_dir.path().join("test.json"),
        );

        // First save
        let data1 = TestData {
            name: "first".into(),
            value: 1,
        };
        storage.save(&data1).await.unwrap();

        // Second save (overwrite)
        let data2 = TestData {
            name: "second".into(),
            value: 2,
        };
        storage.save(&data2).await.unwrap();

        // Load should return second data
        let loaded = storage.load().await.unwrap().unwrap();
        assert_eq!(data2, loaded);
    }

    #[tokio::test]
    async fn test_json_storage_delete() {
        let temp_dir = TempDir::new().unwrap();
        let storage: JsonStorage<TestData> = JsonStorage::new(
            temp_dir.path().join("test.json"),
        );

        let data = TestData {
            name: "test".into(),
            value: 42,
        };

        storage.save(&data).await.unwrap();
        assert!(storage.exists().await);

        storage.delete().await.unwrap();
        assert!(!storage.exists().await);

        // Delete again should not error
        storage.delete().await.unwrap();
    }

    #[tokio::test]
    async fn test_json_storage_corrupted_data() {
        let temp_dir = TempDir::new().unwrap();
        let file_path = temp_dir.path().join("corrupted.json");

        // Write invalid JSON
        tokio::fs::write(&file_path, b"{ invalid json }").await.unwrap();

        let storage: JsonStorage<TestData> = JsonStorage::new(file_path);
        let result = storage.load().await;

        assert!(matches!(result, Err(PersistenceError::CorruptedData { .. })));
    }
}
