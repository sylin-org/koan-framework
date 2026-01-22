//! Atomic file write operations
//!
//! Prevents file corruption by writing to temp file first, then renaming.

use std::path::Path;
use tokio::fs;
use tokio::io::AsyncWriteExt;

/// Write data to file atomically
///
/// Process:
/// 1. Write to {path}.tmp
/// 2. Sync file to disk
/// 3. Rename to {path} (atomic on POSIX, best-effort on Windows)
///
/// If any step fails, the original file (if it exists) remains unchanged.
pub async fn atomic_write_file(
    path: impl AsRef<Path>,
    data: &[u8],
) -> Result<(), std::io::Error> {
    let path = path.as_ref();
    let tmp_path = path.with_extension("tmp");

    // Create parent directory if it doesn't exist
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).await?;
    }

    // Write to temp file
    let mut file = fs::File::create(&tmp_path).await?;
    file.write_all(data).await?;
    file.sync_all().await?;
    drop(file); // Close before rename

    // Atomic rename
    // On POSIX: truly atomic
    // On Windows: best-effort (rename is not guaranteed atomic)
    fs::rename(&tmp_path, path).await?;

    Ok(())
}

/// Read file contents
pub async fn read_file(path: impl AsRef<Path>) -> Result<Vec<u8>, std::io::Error> {
    fs::read(path).await
}

/// Check if file exists
pub async fn file_exists(path: impl AsRef<Path>) -> bool {
    fs::metadata(path).await.is_ok()
}

/// Delete file if it exists
pub async fn delete_file(path: impl AsRef<Path>) -> Result<(), std::io::Error> {
    let path = path.as_ref();
    if file_exists(path).await {
        fs::remove_file(path).await?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::TempDir;

    #[tokio::test]
    async fn test_atomic_write_and_read() {
        let temp_dir = TempDir::new().unwrap();
        let file_path = temp_dir.path().join("test.txt");

        let data = b"Hello, World!";
        atomic_write_file(&file_path, data).await.unwrap();

        let read_data = read_file(&file_path).await.unwrap();
        assert_eq!(data, read_data.as_slice());
    }

    #[tokio::test]
    async fn test_atomic_write_creates_parent_dir() {
        let temp_dir = TempDir::new().unwrap();
        let file_path = temp_dir.path().join("subdir/nested/test.txt");

        let data = b"Test data";
        atomic_write_file(&file_path, data).await.unwrap();

        assert!(file_exists(&file_path).await);
    }

    #[tokio::test]
    async fn test_atomic_write_overwrites_existing() {
        let temp_dir = TempDir::new().unwrap();
        let file_path = temp_dir.path().join("test.txt");

        // First write
        atomic_write_file(&file_path, b"First").await.unwrap();
        let data1 = read_file(&file_path).await.unwrap();
        assert_eq!(b"First", data1.as_slice());

        // Second write (overwrite)
        atomic_write_file(&file_path, b"Second").await.unwrap();
        let data2 = read_file(&file_path).await.unwrap();
        assert_eq!(b"Second", data2.as_slice());
    }

    #[tokio::test]
    async fn test_file_exists() {
        let temp_dir = TempDir::new().unwrap();
        let file_path = temp_dir.path().join("test.txt");

        assert!(!file_exists(&file_path).await);

        atomic_write_file(&file_path, b"data").await.unwrap();
        assert!(file_exists(&file_path).await);
    }

    #[tokio::test]
    async fn test_delete_file() {
        let temp_dir = TempDir::new().unwrap();
        let file_path = temp_dir.path().join("test.txt");

        atomic_write_file(&file_path, b"data").await.unwrap();
        assert!(file_exists(&file_path).await);

        delete_file(&file_path).await.unwrap();
        assert!(!file_exists(&file_path).await);

        // Deleting non-existent file should not error
        delete_file(&file_path).await.unwrap();
    }
}
