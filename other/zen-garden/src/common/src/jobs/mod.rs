//! Job pipeline for background tasks
//!
//! Supports async job execution with:
//! - Job queuing and persistence (JSON files)
//! - Progress tracking via events
//! - Executor pattern for different job types
//! - Restart capability on daemon crash

pub mod types;
pub mod persistence;
pub mod manager;
pub mod retry;

pub use types::{Job, JobId, JobStatus, JobInput, JobOutput};
pub use persistence::JsonJobPersistence;
pub use manager::JobManager;
pub use retry::{RetryPolicy, retry_with_policy, retry_simple};
