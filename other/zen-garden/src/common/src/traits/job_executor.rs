//! Job execution abstraction
//!
//! Executors implement specific job types (install-service, upgrade-service, etc.)
//! JobManager dispatches to appropriate executor based on job_type.

use async_trait::async_trait;
use serde_json::Value;

/// Result of job execution
#[derive(Debug, Clone)]
pub struct JobResult {
    /// Whether job succeeded
    pub success: bool,

    /// Human-readable message
    pub message: String,

    /// Structured output data (optional)
    pub output: Option<Value>,
}

impl JobResult {
    pub fn success(message: impl Into<String>) -> Self {
        Self {
            success: true,
            message: message.into(),
            output: None,
        }
    }

    pub fn success_with_output(message: impl Into<String>, output: Value) -> Self {
        Self {
            success: true,
            message: message.into(),
            output: Some(output),
        }
    }

    pub fn failure(message: impl Into<String>) -> Self {
        Self {
            success: false,
            message: message.into(),
            output: None,
        }
    }

    pub fn failure_with_output(message: impl Into<String>, output: Value) -> Self {
        Self {
            success: false,
            message: message.into(),
            output: Some(output),
        }
    }
}

/// Job execution errors
#[derive(Debug, thiserror::Error)]
pub enum JobExecutionError {
    #[error("Job execution failed: {0}")]
    ExecutionFailed(String),

    #[error("Invalid input: {0}")]
    InvalidInput(String),

    #[error("Job timeout after {0} seconds")]
    Timeout(u64),

    #[error("Executor not found for job type: {0}")]
    ExecutorNotFound(String),

    #[error("Internal error: {0}")]
    Internal(String),
}

/// Job executor trait
///
/// Each executor handles a specific job type:
/// - InstallServiceExecutor: Pulls images, creates containers
/// - UpgradeServiceExecutor: Stops old container, starts new one
/// - VacateStoneExecutor: Migrates all services off stone
/// - TransferServiceExecutor: Moves service to different stone
#[async_trait]
pub trait JobExecutor: Send + Sync {
    /// Get the job type this executor handles
    fn job_type(&self) -> &str;

    /// Execute the job with given input
    ///
    /// Input format is job-type specific (validated by executor).
    /// Executor should publish progress events via EventBus.
    async fn execute(&self, job_id: &str, input: Value) -> Result<JobResult, JobExecutionError>;

    /// Validate input without executing
    ///
    /// Returns Ok(()) if input is valid for this executor
    fn validate_input(&self, input: &Value) -> Result<(), JobExecutionError>;
}
