//! File system operations
//!
//! Handles all file I/O for moss:
//! - Registry persistence
//! - Config file management
//! - Template loading

use anyhow::{Context, Result};
use std::path::{Path, PathBuf};
use tokio::fs;

/// File system operations
pub struct FileSystem {
    data_dir: PathBuf,
}

impl FileSystem {
    /// Create a new filesystem handler
    #[cfg(target_os = "linux")]
    pub fn new() -> Self {
        Self {
            data_dir: PathBuf::from("/var/lib/zen-garden"),
        }
    }

    #[cfg(target_os = "windows")]
    pub fn new() -> Self {
        use std::env;
        let programdata = env::var("PROGRAMDATA").unwrap_or_else(|_| "C:\\ProgramData".into());
        Self {
            data_dir: PathBuf::from(programdata).join("zen-garden"),
        }
    }

    /// Get the data directory path
    pub fn data_dir(&self) -> &Path {
        &self.data_dir
    }

    /// Ensure data directory exists
    pub async fn ensure_data_dir(&self) -> Result<()> {
        fs::create_dir_all(&self.data_dir)
            .await
            .context("Failed to create data directory")?;
        Ok(())
    }

    /// Get path for a specific file in data dir
    pub fn data_file(&self, filename: &str) -> PathBuf {
        self.data_dir.join(filename)
    }

    /// Read a file from data directory
    pub async fn read_data_file(&self, filename: &str) -> Result<String> {
        let path = self.data_file(filename);
        fs::read_to_string(&path)
            .await
            .with_context(|| format!("Failed to read {}", path.display()))
    }

    /// Write a file to data directory
    pub async fn write_data_file(&self, filename: &str, content: &str) -> Result<()> {
        self.ensure_data_dir().await?;
        let path = self.data_file(filename);
        fs::write(&path, content)
            .await
            .with_context(|| format!("Failed to write {}", path.display()))
    }

    /// Check if data file exists
    pub async fn data_file_exists(&self, filename: &str) -> bool {
        self.data_file(filename).exists()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_filesystem_creation() {
        let fs = FileSystem::new();
        assert!(fs.data_dir().to_string_lossy().contains("zen-garden"));
    }

    #[test]
    fn test_data_file_path() {
        let fs = FileSystem::new();
        let path = fs.data_file("test.json");
        assert!(path.to_string_lossy().contains("test.json"));
    }
}
