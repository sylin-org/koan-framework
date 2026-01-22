//! Job persistence using JSON file storage

use super::types::{Job, JobId};
use crate::persistence::JsonStorage;
use crate::traits::persistence::{PersistenceProvider, PersistenceError};
use std::collections::HashMap;
use std::path::PathBuf;

/// JSON-based job persistence
///
/// Stores all jobs in a single JSON file: /var/lib/zen-garden/jobs.json
/// Uses atomic writes to prevent corruption on crashes.
pub struct JsonJobPersistence {
    storage: JsonStorage<HashMap<String, Job>>,
}

impl JsonJobPersistence {
    /// Create a new JSON job persistence instance
    ///
    /// Default path: /var/lib/zen-garden/jobs.json
    pub fn new(file_path: PathBuf) -> Self {
        Self {
            storage: JsonStorage::new(file_path),
        }
    }

    /// Create with default path
    #[cfg(target_os = "linux")]
    pub fn default() -> Self {
        Self::new(PathBuf::from("/var/lib/zen-garden/jobs.json"))
    }

    #[cfg(target_os = "windows")]
    pub fn default() -> Self {
        use std::env;
        let programdata = env::var("PROGRAMDATA").unwrap_or_else(|_| "C:\\ProgramData".into());
        let path = PathBuf::from(programdata).join("zen-garden").join("jobs.json");
        Self::new(path)
    }

    /// Load all jobs from storage
    pub async fn load_all(&self) -> Result<HashMap<JobId, Job>, PersistenceError> {
        let jobs_map = self.storage.load().await?.unwrap_or_default();

        // Convert from HashMap<String, Job> to HashMap<JobId, Job>
        let jobs = jobs_map
            .into_iter()
            .map(|(id, job)| (JobId::from_string(id), job))
            .collect();

        Ok(jobs)
    }

    /// Save all jobs to storage
    pub async fn save_all(&self, jobs: &HashMap<JobId, Job>) -> Result<(), PersistenceError> {
        // Convert from HashMap<JobId, Job> to HashMap<String, Job>
        let jobs_map: HashMap<String, Job> = jobs
            .iter()
            .map(|(id, job)| (id.as_str().to_string(), job.clone()))
            .collect();

        self.storage.save(&jobs_map).await
    }

    /// Load a single job by ID
    pub async fn load(&self, job_id: &JobId) -> Result<Option<Job>, PersistenceError> {
        let jobs = self.load_all().await?;
        Ok(jobs.get(job_id).cloned())
    }

    /// Save a single job (loads all, updates one, saves all)
    ///
    /// Note: This is not optimal for high-throughput scenarios.
    /// For production with many concurrent jobs, consider a proper database.
    pub async fn save(&self, job: &Job) -> Result<(), PersistenceError> {
        let mut jobs = self.load_all().await?;
        jobs.insert(job.id.clone(), job.clone());
        self.save_all(&jobs).await
    }

    /// Delete a job by ID
    pub async fn delete(&self, job_id: &JobId) -> Result<(), PersistenceError> {
        let mut jobs = self.load_all().await?;
        jobs.remove(job_id);
        self.save_all(&jobs).await
    }

    /// Delete all completed or failed jobs
    pub async fn cleanup_terminal_jobs(&self) -> Result<usize, PersistenceError> {
        let mut jobs = self.load_all().await?;
        let before_count = jobs.len();

        jobs.retain(|_, job| !job.is_terminal());

        let removed_count = before_count - jobs.len();
        self.save_all(&jobs).await?;

        Ok(removed_count)
    }

    /// Get count of jobs by status
    pub async fn count_by_status(&self) -> Result<HashMap<String, usize>, PersistenceError> {
        let jobs = self.load_all().await?;
        let mut counts: HashMap<String, usize> = HashMap::new();

        for job in jobs.values() {
            let status_key = format!("{:?}", job.status).to_lowercase();
            *counts.entry(status_key).or_insert(0) += 1;
        }

        Ok(counts)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::jobs::types::JobStatus;
    use serde_json::json;
    use tempfile::TempDir;

    #[tokio::test]
    async fn test_json_job_persistence_save_and_load() {
        let temp_dir = TempDir::new().unwrap();
        let persistence = JsonJobPersistence::new(temp_dir.path().join("jobs.json"));

        let job = Job::new(
            "install-service",
            json!({"service": "mongodb"}),
            Some("stone-01".into()),
        );

        // Save
        persistence.save(&job).await.unwrap();

        // Load
        let loaded = persistence.load(&job.id).await.unwrap().unwrap();
        assert_eq!(job.id, loaded.id);
        assert_eq!(job.job_type, loaded.job_type);
    }

    #[tokio::test]
    async fn test_json_job_persistence_load_all() {
        let temp_dir = TempDir::new().unwrap();
        let persistence = JsonJobPersistence::new(temp_dir.path().join("jobs.json"));

        let job1 = Job::new("test1", json!({}), None);
        let job2 = Job::new("test2", json!({}), None);

        persistence.save(&job1).await.unwrap();
        persistence.save(&job2).await.unwrap();

        let jobs = persistence.load_all().await.unwrap();
        assert_eq!(jobs.len(), 2);
        assert!(jobs.contains_key(&job1.id));
        assert!(jobs.contains_key(&job2.id));
    }

    #[tokio::test]
    async fn test_json_job_persistence_delete() {
        let temp_dir = TempDir::new().unwrap();
        let persistence = JsonJobPersistence::new(temp_dir.path().join("jobs.json"));

        let job = Job::new("test", json!({}), None);

        persistence.save(&job).await.unwrap();
        assert!(persistence.load(&job.id).await.unwrap().is_some());

        persistence.delete(&job.id).await.unwrap();
        assert!(persistence.load(&job.id).await.unwrap().is_none());
    }

    #[tokio::test]
    async fn test_json_job_persistence_cleanup_terminal() {
        let temp_dir = TempDir::new().unwrap();
        let persistence = JsonJobPersistence::new(temp_dir.path().join("jobs.json"));

        let mut job1 = Job::new("test1", json!({}), None);
        job1.status = JobStatus::Completed;

        let mut job2 = Job::new("test2", json!({}), None);
        job2.status = JobStatus::Failed;

        let job3 = Job::new("test3", json!({}), None);
        // job3 is Pending

        persistence.save(&job1).await.unwrap();
        persistence.save(&job2).await.unwrap();
        persistence.save(&job3).await.unwrap();

        let removed = persistence.cleanup_terminal_jobs().await.unwrap();
        assert_eq!(removed, 2);

        let jobs = persistence.load_all().await.unwrap();
        assert_eq!(jobs.len(), 1);
        assert!(jobs.contains_key(&job3.id));
    }

    #[tokio::test]
    async fn test_json_job_persistence_count_by_status() {
        let temp_dir = TempDir::new().unwrap();
        let persistence = JsonJobPersistence::new(temp_dir.path().join("jobs.json"));

        let mut job1 = Job::new("test1", json!({}), None);
        job1.status = JobStatus::Running;

        let mut job2 = Job::new("test2", json!({}), None);
        job2.status = JobStatus::Running;

        let mut job3 = Job::new("test3", json!({}), None);
        job3.status = JobStatus::Completed;

        persistence.save(&job1).await.unwrap();
        persistence.save(&job2).await.unwrap();
        persistence.save(&job3).await.unwrap();

        let counts = persistence.count_by_status().await.unwrap();
        assert_eq!(counts.get("running"), Some(&2));
        assert_eq!(counts.get("completed"), Some(&1));
    }
}
