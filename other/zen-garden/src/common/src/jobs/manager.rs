//! Job manager for background task execution

use super::persistence::JsonJobPersistence;
use super::types::{Job, JobId, JobInput, JobStatus};
use crate::events::{EventBus, DomainEvent, JobEvent};
use crate::traits::job_executor::{JobExecutor, JobExecutionError};
use crate::traits::persistence::PersistenceError;
use chrono::Utc;
use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::RwLock;

/// Job manager orchestrates job lifecycle
///
/// Responsibilities:
/// - Accept job submissions
/// - Persist jobs to survive daemon restarts
/// - Dispatch jobs to appropriate executors
/// - Publish progress events
/// - Track running jobs
pub struct JobManager {
    persistence: Arc<JsonJobPersistence>,
    event_bus: Arc<EventBus>,
    executors: Arc<RwLock<HashMap<String, Arc<dyn JobExecutor>>>>,
}

impl JobManager {
    /// Create a new job manager
    pub fn new(persistence: JsonJobPersistence, event_bus: Arc<EventBus>) -> Self {
        Self {
            persistence: Arc::new(persistence),
            event_bus,
            executors: Arc::new(RwLock::new(HashMap::new())),
        }
    }

    /// Register a job executor
    ///
    /// Executors handle specific job types (install-service, upgrade-service, etc.)
    pub async fn register_executor(&self, executor: Arc<dyn JobExecutor>) {
        let mut executors = self.executors.write().await;
        executors.insert(executor.job_type().to_string(), executor);
    }

    /// Submit a new job
    ///
    /// Returns job_id immediately. Job executes asynchronously.
    /// Progress is tracked via JobEvents on the event bus.
    pub async fn submit(
        &self,
        job_type: impl Into<String>,
        input: JobInput,
        stone_name: Option<String>,
    ) -> Result<JobId, JobManagerError> {
        let job_type = job_type.into();

        // Validate that we have an executor for this job type
        {
            let executors = self.executors.read().await;
            let executor = executors
                .get(&job_type)
                .ok_or_else(|| JobManagerError::ExecutorNotFound(job_type.clone()))?;

            // Validate input
            executor
                .validate_input(&input)
                .map_err(|e| JobManagerError::InvalidInput(e.to_string()))?;
        }

        // Create job
        let job = Job::new(job_type, input, stone_name.clone());
        let job_id = job.id.clone();

        // Persist before execution
        self.persistence
            .save(&job)
            .await
            .map_err(JobManagerError::PersistenceError)?;

        // Publish created event
        let _ = self
            .event_bus
            .publish(DomainEvent::Job(JobEvent::Created {
                job_id: job_id.as_str().to_string(),
                job_type: job.job_type.clone(),
                stone_name,
                timestamp: Utc::now(),
            }))
            .await;

        // Execute asynchronously
        let manager = self.clone();
        let job_id_clone = job_id.clone();
        tokio::spawn(async move {
            if let Err(e) = manager.execute_job(&job_id_clone).await {
                eprintln!("Job execution failed: {}", e);
            }
        });

        Ok(job_id)
    }

    /// Execute a job (called by submit or on daemon restart for pending jobs)
    async fn execute_job(&self, job_id: &JobId) -> Result<(), JobManagerError> {
        // Load job
        let mut job = self
            .persistence
            .load(job_id)
            .await
            .map_err(JobManagerError::PersistenceError)?
            .ok_or_else(|| JobManagerError::JobNotFound(job_id.clone()))?;

        // Get executor
        let executors = self.executors.read().await;
        let executor = executors
            .get(&job.job_type)
            .ok_or_else(|| JobManagerError::ExecutorNotFound(job.job_type.clone()))?
            .clone();
        drop(executors); // Release lock

        // Mark as running
        job.mark_running();
        self.persistence
            .save(&job)
            .await
            .map_err(JobManagerError::PersistenceError)?;

        // Publish started event
        let _ = self
            .event_bus
            .publish(DomainEvent::Job(JobEvent::Started {
                job_id: job_id.as_str().to_string(),
                job_type: job.job_type.clone(),
                stone_name: job.stone_name.clone(),
                timestamp: Utc::now(),
            }))
            .await;

        // Execute
        let result = executor.execute(job_id.as_str(), job.input.clone()).await;

        // Update job based on result
        match result {
            Ok(job_result) => {
                if job_result.success {
                    job.mark_completed(job_result.output.unwrap_or(serde_json::json!({})));

                    // Publish completed event
                    let _ = self
                        .event_bus
                        .publish(DomainEvent::Job(JobEvent::Completed {
                            job_id: job_id.as_str().to_string(),
                            job_type: job.job_type.clone(),
                            result_message: job_result.message,
                            stone_name: job.stone_name.clone(),
                            timestamp: Utc::now(),
                        }))
                        .await;
                } else {
                    job.mark_failed(&job_result.message);

                    // Publish failed event
                    let _ = self
                        .event_bus
                        .publish(DomainEvent::Job(JobEvent::Failed {
                            job_id: job_id.as_str().to_string(),
                            job_type: job.job_type.clone(),
                            error: job_result.message,
                            stone_name: job.stone_name.clone(),
                            timestamp: Utc::now(),
                        }))
                        .await;
                }
            }
            Err(e) => {
                job.mark_failed(e.to_string());

                // Publish failed event
                let _ = self
                    .event_bus
                    .publish(DomainEvent::Job(JobEvent::Failed {
                        job_id: job_id.as_str().to_string(),
                        job_type: job.job_type.clone(),
                        error: e.to_string(),
                        stone_name: job.stone_name.clone(),
                        timestamp: Utc::now(),
                    }))
                    .await;
            }
        }

        // Persist final state
        self.persistence
            .save(&job)
            .await
            .map_err(JobManagerError::PersistenceError)?;

        Ok(())
    }

    /// Get job status
    pub async fn get_job(&self, job_id: &JobId) -> Result<Option<Job>, JobManagerError> {
        self.persistence
            .load(job_id)
            .await
            .map_err(JobManagerError::PersistenceError)
    }

    /// List all jobs
    pub async fn list_jobs(&self) -> Result<Vec<Job>, JobManagerError> {
        let jobs = self
            .persistence
            .load_all()
            .await
            .map_err(JobManagerError::PersistenceError)?;

        let mut job_list: Vec<Job> = jobs.into_values().collect();
        job_list.sort_by(|a, b| b.created_at.cmp(&a.created_at));

        Ok(job_list)
    }

    /// Resume pending jobs on daemon startup
    ///
    /// Call this when moss starts to resume any pending jobs from previous run.
    pub async fn resume_pending_jobs(&self) -> Result<usize, JobManagerError> {
        let jobs = self
            .persistence
            .load_all()
            .await
            .map_err(JobManagerError::PersistenceError)?;

        let pending_jobs: Vec<JobId> = jobs
            .iter()
            .filter(|(_, job)| job.status == JobStatus::Pending)
            .map(|(id, _)| id.clone())
            .collect();

        let count = pending_jobs.len();

        for job_id in pending_jobs {
            let manager = self.clone();
            tokio::spawn(async move {
                if let Err(e) = manager.execute_job(&job_id).await {
                    eprintln!("Failed to resume job {}: {}", job_id, e);
                }
            });
        }

        Ok(count)
    }

    /// Cleanup completed and failed jobs older than specified days
    pub async fn cleanup_old_jobs(&self, _days: u32) -> Result<usize, JobManagerError> {
        // For now, just cleanup all terminal jobs
        // In production, you'd filter by age
        self.persistence
            .cleanup_terminal_jobs()
            .await
            .map_err(JobManagerError::PersistenceError)
    }
}

impl Clone for JobManager {
    fn clone(&self) -> Self {
        Self {
            persistence: Arc::clone(&self.persistence),
            event_bus: Arc::clone(&self.event_bus),
            executors: Arc::clone(&self.executors),
        }
    }
}

/// Job manager errors
#[derive(Debug, thiserror::Error)]
pub enum JobManagerError {
    #[error("Executor not found for job type: {0}")]
    ExecutorNotFound(String),

    #[error("Job not found: {0}")]
    JobNotFound(JobId),

    #[error("Invalid input: {0}")]
    InvalidInput(String),

    #[error("Persistence error: {0}")]
    PersistenceError(#[from] PersistenceError),

    #[error("Execution error: {0}")]
    ExecutionError(#[from] JobExecutionError),
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::traits::job_executor::JobResult;
    use async_trait::async_trait;
    use serde_json::{json, Value};
    use tempfile::TempDir;

    // Mock executor for testing
    struct MockExecutor {
        job_type: String,
        should_succeed: bool,
    }

    #[async_trait]
    impl JobExecutor for MockExecutor {
        fn job_type(&self) -> &str {
            &self.job_type
        }

        async fn execute(&self, _job_id: &str, _input: Value) -> Result<JobResult, JobExecutionError> {
            if self.should_succeed {
                Ok(JobResult::success("Mock success"))
            } else {
                Ok(JobResult::failure("Mock failure"))
            }
        }

        fn validate_input(&self, _input: &Value) -> Result<(), JobExecutionError> {
            Ok(())
        }
    }

    #[tokio::test]
    async fn test_job_manager_submit_and_execute() {
        let temp_dir = TempDir::new().unwrap();
        let persistence = JsonJobPersistence::new(temp_dir.path().join("jobs.json"));
        let event_bus = Arc::new(EventBus::new(100));
        let manager = JobManager::new(persistence, event_bus);

        // Register executor
        let executor = Arc::new(MockExecutor {
            job_type: "test-job".into(),
            should_succeed: true,
        });
        manager.register_executor(executor).await;

        // Submit job
        let job_id = manager
            .submit("test-job", json!({"test": "data"}), None)
            .await
            .unwrap();

        // Poll for completion with timeout (up to 2 seconds)
        let mut completed = false;
        for _ in 0..20 {
            tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;
            if let Ok(Some(job)) = manager.get_job(&job_id).await {
                if job.status == JobStatus::Completed {
                    completed = true;
                    break;
                }
            }
        }

        assert!(completed, "Job should complete within timeout");
    }

    #[tokio::test]
    async fn test_job_manager_executor_not_found() {
        let temp_dir = TempDir::new().unwrap();
        let persistence = JsonJobPersistence::new(temp_dir.path().join("jobs.json"));
        let event_bus = Arc::new(EventBus::new(100));
        let manager = JobManager::new(persistence, event_bus);

        // Try to submit without registering executor
        let result = manager
            .submit("nonexistent-job", json!({}), None)
            .await;

        assert!(matches!(result, Err(JobManagerError::ExecutorNotFound(_))));
    }

    #[tokio::test]
    async fn test_job_manager_list_jobs() {
        let temp_dir = TempDir::new().unwrap();
        let persistence = JsonJobPersistence::new(temp_dir.path().join("jobs.json"));
        let event_bus = Arc::new(EventBus::new(100));
        let manager = JobManager::new(persistence, event_bus);

        let executor = Arc::new(MockExecutor {
            job_type: "test-job".into(),
            should_succeed: true,
        });
        manager.register_executor(executor).await;

        // Submit first job and wait for it to be persisted
        let job_id1 = manager.submit("test-job", json!({"a": 1}), None).await.unwrap();

        // Wait for first job to complete before submitting second
        for _ in 0..20 {
            tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;
            if let Ok(Some(job)) = manager.get_job(&job_id1).await {
                if job.status == JobStatus::Completed {
                    break;
                }
            }
        }

        // Submit second job
        let job_id2 = manager.submit("test-job", json!({"b": 2}), None).await.unwrap();

        // Wait for second job to complete
        for _ in 0..20 {
            tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;
            if let Ok(Some(job)) = manager.get_job(&job_id2).await {
                if job.status == JobStatus::Completed {
                    break;
                }
            }
        }

        // List jobs - should have both
        let jobs = manager.list_jobs().await.unwrap();
        assert_eq!(jobs.len(), 2);
    }
}
