//! Persistence utilities for atomic file writes
//!
//! All file writes use atomic pattern:
//! 1. Write to .tmp file
//! 2. Sync to disk
//! 3. Rename to final name (atomic on POSIX, best-effort on Windows)

pub mod atomic_file;
pub mod json_storage;

pub use atomic_file::atomic_write_file;
pub use json_storage::JsonStorage;
