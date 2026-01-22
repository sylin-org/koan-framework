//! Job type definitions

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::fmt;

/// Unique job identifier
#[derive(Debug, Clone, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub struct JobId(String);

impl JobId {
    /// Generate a new unique job ID
    pub fn new() -> Self {
        use std::sync::atomic::{AtomicU64, Ordering};
        static COUNTER: AtomicU64 = AtomicU64::new(0);

        let count = COUNTER.fetch_add(1, Ordering::SeqCst);
        let timestamp = Utc::now().timestamp_millis();

        Self(format!("job-{}-{}", timestamp, count))
    }

    /// Create from string (for deserialization)
    pub fn from_string(s: String) -> Self {
        Self(s)
    }

    pub fn as_str(&self) -> &str {
        &self.0
    }
}

impl fmt::Display for JobId {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}", self.0)
    }
}

impl Default for JobId {
    fn default() -> Self {
        Self::new()
    }
}

/// Job execution status
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum JobStatus {
    /// Job queued, not yet started
    Pending,

    /// Job currently executing
    Running,

    /// Job completed successfully
    Completed,

    /// Job failed with error
    Failed,
}

/// Job input data (opaque JSON)
pub type JobInput = Value;

/// Job output data (opaque JSON)
pub type JobOutput = Value;

/// Background job
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Job {
    /// Unique job identifier
    pub id: JobId,

    /// Job type (determines which executor handles it)
    pub job_type: String,

    /// Current status
    pub status: JobStatus,

    /// Job input data (job-type specific)
    pub input: JobInput,

    /// Job output data (populated on completion)
    pub output: Option<JobOutput>,

    /// Error message (populated on failure)
    pub error: Option<String>,

    /// Stone name (if job is stone-specific)
    pub stone_name: Option<String>,

    /// Job creation timestamp
    pub created_at: DateTime<Utc>,

    /// Job start timestamp
    pub started_at: Option<DateTime<Utc>>,

    /// Job completion timestamp
    pub completed_at: Option<DateTime<Utc>>,

    /// Progress message (updated during execution)
    pub progress: Option<String>,
}

impl Job {
    /// Create a new pending job
    pub fn new(job_type: impl Into<String>, input: JobInput, stone_name: Option<String>) -> Self {
        Self {
            id: JobId::new(),
            job_type: job_type.into(),
            status: JobStatus::Pending,
            input,
            output: None,
            error: None,
            stone_name,
            created_at: Utc::now(),
            started_at: None,
            completed_at: None,
            progress: None,
        }
    }

    /// Mark job as running
    pub fn mark_running(&mut self) {
        self.status = JobStatus::Running;
        self.started_at = Some(Utc::now());
    }

    /// Mark job as completed with output
    pub fn mark_completed(&mut self, output: JobOutput) {
        self.status = JobStatus::Completed;
        self.output = Some(output);
        self.completed_at = Some(Utc::now());
    }

    /// Mark job as failed with error
    pub fn mark_failed(&mut self, error: impl Into<String>) {
        self.status = JobStatus::Failed;
        self.error = Some(error.into());
        self.completed_at = Some(Utc::now());
    }

    /// Update job progress message
    pub fn update_progress(&mut self, progress: impl Into<String>) {
        self.progress = Some(progress.into());
    }

    /// Check if job is terminal (completed or failed)
    pub fn is_terminal(&self) -> bool {
        matches!(self.status, JobStatus::Completed | JobStatus::Failed)
    }

    /// Get job duration (if started)
    pub fn duration_seconds(&self) -> Option<i64> {
        self.started_at.map(|start| {
            let end = self.completed_at.unwrap_or_else(Utc::now);
            (end - start).num_seconds()
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn test_job_id_generation() {
        let id1 = JobId::new();
        let id2 = JobId::new();

        assert_ne!(id1, id2);
        assert!(id1.as_str().starts_with("job-"));
    }

    #[test]
    fn test_job_lifecycle() {
        let mut job = Job::new(
            "install-service",
            json!({"service": "mongodb", "version": "7.0"}),
            Some("stone-01".into()),
        );

        assert_eq!(job.status, JobStatus::Pending);
        assert!(!job.is_terminal());

        job.mark_running();
        assert_eq!(job.status, JobStatus::Running);
        assert!(job.started_at.is_some());

        job.update_progress("Pulling image...");
        assert_eq!(job.progress, Some("Pulling image...".into()));

        job.mark_completed(json!({"container_id": "abc123"}));
        assert_eq!(job.status, JobStatus::Completed);
        assert!(job.is_terminal());
        assert!(job.completed_at.is_some());
    }

    #[test]
    fn test_job_failure() {
        let mut job = Job::new("upgrade-service", json!({}), None);

        job.mark_running();
        job.mark_failed("Image not found");

        assert_eq!(job.status, JobStatus::Failed);
        assert_eq!(job.error, Some("Image not found".into()));
        assert!(job.is_terminal());
    }

    #[test]
    fn test_job_duration() {
        let mut job = Job::new("test", json!({}), None);

        assert_eq!(job.duration_seconds(), None);

        job.mark_running();
        std::thread::sleep(std::time::Duration::from_millis(100));
        job.mark_completed(json!({}));

        let duration = job.duration_seconds().unwrap();
        assert!(duration >= 0);
    }

    #[test]
    fn test_job_serialization() {
        let job = Job::new(
            "install-service",
            json!({"service": "postgres"}),
            Some("stone-01".into()),
        );

        let json = serde_json::to_string(&job).unwrap();
        let deserialized: Job = serde_json::from_str(&json).unwrap();

        assert_eq!(job.id, deserialized.id);
        assert_eq!(job.job_type, deserialized.job_type);
        assert_eq!(job.status, deserialized.status);
    }
}
