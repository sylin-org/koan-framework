# Rust Refactoring Proposal: Modular Architecture

**Status:** ✅ Substantially Implemented (75-80%)
**Created:** 2026-01-20
**Implementation Status:** [RUST-REFACTORING-STATUS.md](../RUST-REFACTORING-STATUS.md)
**Achievement:** main.rs reduced 74% (3,976 → 1,014 lines), domain/infra/API separation complete

## Executive Summary

This proposal outlines a comprehensive refactoring of the Zen Garden Rust codebase to establish:

- **Separation of Concerns (SoC)** as the primary architectural principle
- **Single-purpose modules** with clear, thin boundaries
- **Centralized event handling** for observability and SSE integration
- **Elimination of code duplication** through composition pipelines
- **Common-first architecture** - all shared code lives in `common/`
- **Async job pipeline** with restart capability for long-running operations

**Impact:** Transforms moss/main.rs from 3,976 lines to ~200, establishes maintainable module boundaries, enables testing without mocks, provides robust async task execution.

---

## Principles

### 1. Separation of Concerns is King

**Rule:** One module = One concern = One reason to change

```
❌ BEFORE: moss/src/main.rs handles everything
   - Configuration loading
   - Compatibility evaluation
   - Registry persistence
   - API routing
   - Business logic

✅ AFTER: Each concern gets its own module
   - config/ handles configuration only
   - compat/ handles compatibility only
   - registry/ handles registry only
   - api/ handles HTTP only
   - orchestration/ coordinates across concerns
```

### 2. Eliminate Repetition Through Composition

**Rule:** One implementation per concept, compose via traits/pipelines

```
❌ BEFORE: Discovery logic scattered across 3 files
   - moss/src/discovery.rs (UDP listener)
   - rake/src/discovery.rs (UDP client)
   - moss/src/mdns.rs (mDNS announcer)

✅ AFTER: Unified discovery pipeline
   - discovery-core/ (shared protocol)
   - discovery-server/ (implements DiscoveryResponder)
   - discovery-client/ (implements DiscoveryQuery)
   - mdns/ (implements DiscoveryAnnouncer)
```

### 3. Thin, Clear Code Scopes

**Rule:** Files ≤ 300 lines, functions ≤ 50 lines, modules do one thing

```
Complexity Limits:
- File:     ≤ 300 lines (exception: generated code)
- Function: ≤ 50 lines (exception: match statements on enums)
- Module:   1 primary trait/struct per file
- Nesting:  ≤ 3 levels deep
```

### 4. Centralized Event Handling

**Rule:** All observable actions emit events to a unified bus

```
✅ Event-Driven Architecture:
   Business Logic → Events → Handlers

   Examples:
   - DockerService::stop() emits ServiceStopping event
   - ConsoleHandler subscribes to ServiceStopping → renders to terminal
   - SSEHandler subscribes to ServiceStopping → broadcasts to clients
   - MetricsHandler subscribes to ServiceStopping → updates Prometheus
```

### 5. Common-First Architecture

**Rule:** All shared functionality lives in `common/`, components contain only component-specific code

```
✅ Common-First Decision Tree:

   Question: "Where does this code belong?"

   ┌─ Used by 2+ components?
   │  └─ YES → common/
   │  └─ NO  → component/
   │
   ┌─ Contains business logic specific to moss/rake/lantern?
   │  └─ YES → component/domain/
   │  └─ NO  → common/
   │
   ┌─ Is it a contract (trait, type, event)?
   │  └─ YES → common/
   │  └─ NO  → Check above questions

   Examples:
   - Discovery protocol?          → common/discovery/ (used by all)
   - Retry policy?                → common/jobs/ (used by all)
   - Moss-specific health checks? → moss/domain/health/ (moss only)
   - StoneInfo type?              → common/types.rs (contract)
```

**Guideline:** If you're about to copy-paste code between components, stop and move it to `common/` instead.

### 6. Async Job Pipeline

**Rule:** Small tasks wait, long tasks create jobs

```
✅ Task Classification:

   Quick Operation (<500ms):
   - Return synchronous response
   - Example: GET /health, GET /services

   Medium Operation (500ms - 5s):
   - Return job ID immediately
   - Poll via GET /jobs/{id}
   - Example: Install service, restart container

   Long Operation (>5s):
   - Return job ID immediately
   - Subscribe to job events via SSE
   - Jobs are persisted, restartable
   - Example: Pull large Docker image, migrate data

   Job Lifecycle:
   Pending → Running → [Completed | Failed | Cancelled]
                  ↓
            (restart on crash)
```

**Key Requirements:**

- Jobs persisted to disk (survive daemon restarts)
- Jobs emit events (progress tracking via SSE)
- Failed jobs can be retried
- Jobs can be cancelled mid-execution

---

## Proposed Architecture

### Module Hierarchy

```
common/                          # 🎯 COMPREHENSIVE SHARED LIBRARY
  ├── types.rs                   # Core domain models (StoneInfo, ServiceInfo, etc.)
  ├── errors.rs                  # Unified error types (GardenError)
  ├── config/                    # Configuration utilities
  │   ├── mod.rs                 # Config loading, validation
  │   ├── defaults.rs            # Default values for all components
  │   └── env.rs                 # Environment variable helpers
  ├── constants/                 # Centralized constants (existing)
  │   ├── mod.rs                 # Re-exports
  │   ├── ports.rs               # MOSS_PORT, RAKE_PORT, LANTERN_PORT
  │   ├── paths.rs               # Config paths, data dirs
  │   └── errors.rs              # Error codes
  ├── client/                    # HTTP client abstractions
  │   ├── mod.rs                 # GardenHttpClient (existing)
  │   ├── retry.rs               # Retry middleware
  │   └── timeout.rs             # Timeout middleware
  ├── events/                    # ✨ Event system (NEW)
  │   ├── mod.rs                 # EventBus trait
  │   ├── bus.rs                 # InMemoryEventBus implementation
  │   ├── domain.rs              # Domain events (ServiceEvent, etc.)
  │   ├── system.rs              # System events (ConfigLoaded, etc.)
  │   └── job.rs                 # Job events (JobStarted, JobProgress, etc.)
  ├── jobs/                      # ✨ Async job pipeline (ENHANCED)
  │   ├── mod.rs                 # Job trait, JobManager
  │   ├── types.rs               # Job, JobId, JobStatus, JobResult
  │   ├── executor.rs            # Job execution engine
  │   ├── persistence.rs         # Job persistence (SQLite)
  │   ├── restart.rs             # Restart on crash
  │   ├── retry.rs               # Retry policies (existing, enhanced)
  │   └── progress.rs            # Progress tracking
  ├── discovery/                 # ✨ Discovery protocol (NEW - shared)
  │   ├── mod.rs                 # Discovery traits + protocol
  │   ├── types.rs               # DiscoveryQuery, StoneAnnouncement
  │   ├── udp.rs                 # UDP protocol implementation
  │   └── election.rs            # Election delay calculation (from lantern)
  ├── persistence/               # ✨ Storage abstractions (NEW)
  │   ├── mod.rs                 # PersistenceProvider, TransactionalStorage
  │   ├── atomic.rs              # Atomic file writes
  │   └── sqlite.rs              # SQLite helpers
  ├── api/                       # ✨ API utilities (NEW)
  │   ├── mod.rs                 # Shared API types
  │   ├── responses.rs           # ApiResponse, ApiError builders
  │   ├── pagination.rs          # Pagination helpers
  │   └── validation.rs          # Request validation
  ├── formatters/                # Output formatting utilities
  │   ├── mod.rs                 # Re-exports
  │   ├── bytes.rs               # format_bytes() (existing)
  │   ├── duration.rs            # format_uptime() (existing)
  │   └── table.rs               # ASCII table rendering (for rake)
  ├── crypto/                    # ✨ Cryptographic utilities (NEW)
  │   ├── mod.rs                 # Re-exports
  │   ├── hash.rs                # Blake3 hashing (for election delay)
  │   └── random.rs              # Secure random ID generation
  └── testing/                   # ✨ Test utilities (NEW)
      ├── mod.rs                 # Test helpers
      ├── mocks.rs               # Mock implementations of traits
      └── fixtures.rs            # Test fixtures

moss/                            # 🔧 MOSS-SPECIFIC CODE ONLY
  ├── main.rs                    # Bootstrap (~150 lines)
  ├── domain/                    # Moss-specific business logic
  │   ├── compat/                # Compatibility evaluation (Moss-specific)
  │   │   ├── mod.rs             # CompatibilityPipeline orchestrator
  │   │   └── evaluators/        # Moss-specific evaluators
  │   │       ├── cpu.rs         # CPU compatibility
  │   │       ├── memory.rs      # Memory compatibility
  │   │       ├── gpu.rs         # GPU compatibility
  │   │       └── ai_runtime.rs  # AI runtime compatibility
  │   ├── services/              # Service lifecycle (Moss-specific)
  │   │   ├── mod.rs             # ServiceOrchestrator
  │   │   ├── lifecycle.rs       # Start/stop/restart
  │   │   ├── adoption.rs        # Container adoption logic
  │   │   └── templates.rs       # Template compilation
  │   └── health.rs              # Moss-specific health checks
  ├── infra/                     # Moss-specific infrastructure
  │   ├── docker/                # Docker integration (Moss-only)
  │   │   ├── mod.rs             # DockerService
  │   │   ├── containers.rs      # Container operations
  │   │   └── compose.rs         # Compose file generation
  │   ├── metrics/               # Hardware detection (Moss-only)
  │   │   ├── mod.rs             # MetricsCollector impl
  │   │   ├── hardware.rs        # CPU/RAM/Disk detection
  │   │   └── gpu.rs             # GPU/AI runtime detection
  │   ├── templates/             # Template loading (Moss-only)
  │   │   ├── mod.rs             # TemplateProvider impl
  │   │   ├── filesystem.rs      # Load from /etc/zen-garden/templates
  │   │   └── embedded.rs        # Load from binary
  │   └── mdns.rs                # mDNS announcer (Moss-only)
  ├── api/                       # Moss HTTP API
  │   ├── mod.rs                 # Router setup
  │   ├── middleware/
  │   │   ├── auth.rs            # Auth middleware (uses common/AuthProvider)
  │   │   └── logging.rs         # Request logging
  │   └── v1/
  │       ├── services.rs        # Service endpoints (THIN: <20 lines each)
  │       ├── offerings.rs       # Offering endpoints (THIN)
  │       ├── jobs.rs            # Job status endpoints (uses common/jobs)
  │       ├── health.rs          # Health check endpoint (THIN)
  │       └── sse.rs             # SSE event stream
  └── handlers/                  # Event handlers (Moss-specific output)
      ├── console.rs             # ConsolePrinter (Moss-only)
      └── sse.rs                 # SSE broadcaster

rake/                            # 🔧 RAKE-SPECIFIC CODE ONLY
  ├── main.rs                    # Command router (~200 lines)
  ├── domain/
  │   └── commands/              # Command implementations (Rake-specific)
  │       ├── mod.rs             # Command dispatcher
  │       ├── discover.rs        # garden-rake discover (uses common/discovery)
  │       ├── offer.rs           # garden-rake offer
  │       ├── list.rs            # garden-rake list
  │       └── health.rs          # garden-rake health
  ├── infra/
  │   ├── cache.rs               # Stone endpoint cache (Rake-specific)
  │   └── tending.rs             # Active stone tracking (Rake-specific)
  └── ui/                        # Terminal UI (Rake-specific)
      ├── mod.rs                 # Terminal rendering
      ├── colors.rs              # Color scheme
      ├── formatters.rs          # Output formatting (uses common/formatters)
      └── indicators.rs          # Status indicators

lantern/                         # 🔧 LANTERN-SPECIFIC CODE ONLY
  ├── main.rs                    # Server bootstrap (~100 lines)
  ├── domain/
  │   ├── election/              # Leader election (Lantern-specific)
  │   │   ├── mod.rs             # Election orchestrator
  │   │   └── state_machine.rs   # Dormant → Candidate → Active
  │   └── topology/              # Topology management (Lantern-specific)
  │       ├── mod.rs             # TopologyManager
  │       └── ttl.rs             # TTL-based cleanup
  ├── infra/
  │   ├── registry/              # Registry storage (Lantern-specific)
  │   │   ├── mod.rs             # RegistryProvider impl
  │   │   ├── memory.rs          # In-memory (current)
  │   │   └── sqlite.rs          # SQLite persistence
  │   └── auth/                  # Authentication (Lantern-specific)
  │       ├── mod.rs             # AuthProvider impl
  │       └── jwt.rs             # JWT validation
  └── api/                       # Lantern HTTP API
      ├── mod.rs                 # Router setup
      ├── v1/
      │   ├── registry.rs        # Registration endpoints (THIN)
      │   └── topology.rs        # Topology endpoints (THIN)
      └── middleware/
          └── auth.rs            # Auth middleware
```

**Key Observations:**

- `common/` is now ~2,000 LOC (up from ~1,500)
- `moss/` reduced to ~800 LOC (down from ~12,000) - mostly Docker + compatibility
- `rake/` reduced to ~300 LOC (down from ~4,300) - mostly UI + commands
- `lantern/` reduced to ~200 LOC (essentially unchanged, already thin)

**What moved to common:**

- Discovery protocol (moss/discovery.rs → common/discovery/)
- Retry policies (moss/jobs.rs → common/jobs/retry.rs)
- Atomic writes (moss/persistence → common/persistence/atomic.rs)
- HTTP client (already in common, enhanced)
- Event system (new in common)
- Job pipeline (new in common)
- API responses (moss → common/api/responses.rs)
- Formatters (rake → common/formatters/)
- Blake3 hashing (lantern/election → common/crypto/hash.rs)

**What stayed in components:**

- Docker operations (moss-only)
- Hardware metrics (moss-only)
- Template loading (moss-only)
- mDNS announcer (moss-only)
- Compatibility evaluators (moss-only)
- Terminal UI (rake-only)
- Command implementations (rake-only)
- Leader election state machine (lantern-only)
- Topology management (lantern-only)

---

## Async Job Pipeline Architecture

### Overview

The job pipeline provides a robust system for executing long-running operations with:

- **Persistence**: Jobs survive daemon restarts
- **Restart capability**: Failed jobs can resume from checkpoints
- **Progress tracking**: Real-time status updates via events
- **Cancellation**: Jobs can be gracefully cancelled
- **Retry logic**: Automatic retry with exponential backoff

### Job Classification

**Decision Matrix:**

| Operation Duration | Response Type    | Example          | Job Tracking         |
| ------------------ | ---------------- | ---------------- | -------------------- |
| <500ms             | Synchronous      | GET /health      | No job               |
| 500ms - 5s         | Job ID + polling | Install service  | Simple job           |
| >5s                | Job ID + SSE     | Pull large image | Job with checkpoints |

### Core Types

**Location:** `common/src/jobs/types.rs`

```rust
/// Unique job identifier (UUID v7 for time-ordering)
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct JobId(pub String);

impl JobId {
    pub fn new() -> Self {
        Self(uuid::Uuid::now_v7().to_string())
    }
}

/// Job status lifecycle
#[derive(Clone, Debug, Serialize, Deserialize)]
pub enum JobStatus {
    Pending,                     // Queued, not started
    Running { progress: f32 },   // 0.0 to 1.0
    Completed { result: serde_json::Value },
    Failed { error: String, retryable: bool },
    Cancelled,
}

/// Job metadata stored in database
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct Job {
    pub id: JobId,
    pub job_type: String,        // "install_service", "pull_image", etc.
    pub status: JobStatus,
    pub created_at: DateTime<Utc>,
    pub updated_at: DateTime<Utc>,
    pub completed_at: Option<DateTime<Utc>>,

    // Execution details
    pub input: serde_json::Value,   // Original request
    pub output: Option<serde_json::Value>,  // Final result
    pub checkpoint: Option<serde_json::Value>,  // Resume state

    // Retry configuration
    pub max_retries: u32,
    pub retry_count: u32,
    pub next_retry_at: Option<DateTime<Utc>>,
}

/// Job execution result
pub type JobResult = Result<serde_json::Value, JobError>;

#[derive(Debug, thiserror::Error)]
pub enum JobError {
    #[error("Job failed: {0}")]
    ExecutionFailed(String),

    #[error("Job cancelled")]
    Cancelled,

    #[error("Job timeout after {0}s")]
    Timeout(u64),

    #[error("Checkpoint invalid: {0}")]
    CheckpointInvalid(String),
}
```

### Job Trait

**Location:** `common/src/jobs/mod.rs`

```rust
/// Trait for implementing job logic
#[async_trait]
pub trait JobExecutor: Send + Sync {
    /// Execute the job
    async fn execute(
        &self,
        input: serde_json::Value,
        checkpoint: Option<serde_json::Value>,
        progress_tx: Sender<f32>,
    ) -> JobResult;

    /// Job type identifier
    fn job_type(&self) -> &'static str;

    /// Maximum execution time (None = no timeout)
    fn timeout(&self) -> Option<Duration> {
        Some(Duration::from_secs(300)) // 5 minutes default
    }

    /// Whether this job can be retried on failure
    fn retryable(&self) -> bool {
        true
    }

    /// Cleanup on cancellation
    async fn cancel(&self, _checkpoint: Option<serde_json::Value>) -> Result<()> {
        Ok(())  // Optional cleanup
    }
}
```

### Job Manager

**Location:** `common/src/jobs/executor.rs`

```rust
pub struct JobManager {
    persistence: Arc<dyn JobPersistence>,
    executors: Arc<RwLock<HashMap<String, Arc<dyn JobExecutor>>>>,
    event_bus: Arc<dyn EventBus>,
    running: Arc<RwLock<HashMap<JobId, JoinHandle<()>>>>,
}

impl JobManager {
    pub fn new(
        persistence: Arc<dyn JobPersistence>,
        event_bus: Arc<dyn EventBus>,
    ) -> Self {
        Self {
            persistence,
            executors: Arc::new(RwLock::new(HashMap::new())),
            event_bus,
            running: Arc::new(RwLock::new(HashMap::new())),
        }
    }

    /// Register a job executor
    pub async fn register_executor(&self, executor: Arc<dyn JobExecutor>) {
        let mut executors = self.executors.write().await;
        executors.insert(executor.job_type().to_string(), executor);
    }

    /// Submit a new job
    pub async fn submit(
        &self,
        job_type: &str,
        input: serde_json::Value,
    ) -> Result<JobId> {
        let job_id = JobId::new();
        let job = Job {
            id: job_id.clone(),
            job_type: job_type.to_string(),
            status: JobStatus::Pending,
            created_at: Utc::now(),
            updated_at: Utc::now(),
            completed_at: None,
            input: input.clone(),
            output: None,
            checkpoint: None,
            max_retries: 3,
            retry_count: 0,
            next_retry_at: None,
        };

        // Persist job
        self.persistence.save(&job).await?;

        // Emit event
        self.event_bus.publish(JobEvent::Created {
            job_id: job_id.clone(),
            job_type: job_type.to_string(),
        }).await?;

        // Start execution
        self.execute_job(job).await?;

        Ok(job_id)
    }

    /// Execute a job (called on submit or restart)
    async fn execute_job(&self, mut job: Job) -> Result<()> {
        let executors = self.executors.read().await;
        let executor = executors
            .get(&job.job_type)
            .ok_or_else(|| anyhow!("No executor for job type: {}", job.job_type))?
            .clone();
        drop(executors);

        let persistence = self.persistence.clone();
        let event_bus = self.event_bus.clone();
        let running = self.running.clone();
        let job_id = job.id.clone();

        // Spawn job execution task
        let handle = tokio::spawn(async move {
            // Update status to Running
            job.status = JobStatus::Running { progress: 0.0 };
            job.updated_at = Utc::now();
            persistence.save(&job).await.ok();

            event_bus.publish(JobEvent::Started {
                job_id: job.id.clone(),
            }).await.ok();

            // Create progress channel
            let (progress_tx, mut progress_rx) = tokio::sync::mpsc::channel(100);

            // Spawn progress tracker
            let job_id_clone = job.id.clone();
            let persistence_clone = persistence.clone();
            let event_bus_clone = event_bus.clone();
            tokio::spawn(async move {
                while let Some(progress) = progress_rx.recv().await {
                    // Update job in database
                    if let Ok(Some(mut job)) = persistence_clone.load(&job_id_clone).await {
                        job.status = JobStatus::Running { progress };
                        job.updated_at = Utc::now();
                        persistence_clone.save(&job).await.ok();

                        // Emit progress event
                        event_bus_clone.publish(JobEvent::Progress {
                            job_id: job_id_clone.clone(),
                            progress,
                        }).await.ok();
                    }
                }
            });

            // Execute job with timeout
            let timeout_duration = executor.timeout().unwrap_or(Duration::from_secs(300));
            let result = tokio::time::timeout(
                timeout_duration,
                executor.execute(job.input.clone(), job.checkpoint.clone(), progress_tx),
            ).await;

            // Process result
            match result {
                Ok(Ok(output)) => {
                    // Success
                    job.status = JobStatus::Completed { result: output.clone() };
                    job.output = Some(output);
                    job.completed_at = Some(Utc::now());
                    job.updated_at = Utc::now();
                    persistence.save(&job).await.ok();

                    event_bus.publish(JobEvent::Completed {
                        job_id: job.id.clone(),
                        output,
                    }).await.ok();
                }
                Ok(Err(e)) => {
                    // Execution failed
                    let retryable = executor.retryable();
                    job.status = JobStatus::Failed {
                        error: e.to_string(),
                        retryable,
                    };
                    job.retry_count += 1;
                    job.updated_at = Utc::now();

                    if retryable && job.retry_count < job.max_retries {
                        // Schedule retry
                        let delay = 2_u64.pow(job.retry_count) * 5; // Exponential backoff
                        job.next_retry_at = Some(Utc::now() + chrono::Duration::seconds(delay as i64));
                        persistence.save(&job).await.ok();

                        event_bus.publish(JobEvent::Failed {
                            job_id: job.id.clone(),
                            error: e.to_string(),
                            retry_in_secs: Some(delay),
                        }).await.ok();

                        // Schedule retry
                        tokio::time::sleep(Duration::from_secs(delay)).await;
                        // TODO: Recursive retry logic
                    } else {
                        // Final failure
                        job.completed_at = Some(Utc::now());
                        persistence.save(&job).await.ok();

                        event_bus.publish(JobEvent::Failed {
                            job_id: job.id.clone(),
                            error: e.to_string(),
                            retry_in_secs: None,
                        }).await.ok();
                    }
                }
                Err(_) => {
                    // Timeout
                    job.status = JobStatus::Failed {
                        error: format!("Timeout after {}s", timeout_duration.as_secs()),
                        retryable: false,
                    };
                    job.completed_at = Some(Utc::now());
                    job.updated_at = Utc::now();
                    persistence.save(&job).await.ok();

                    event_bus.publish(JobEvent::Failed {
                        job_id: job.id.clone(),
                        error: "Timeout".to_string(),
                        retry_in_secs: None,
                    }).await.ok();
                }
            }

            // Remove from running jobs
            running.write().await.remove(&job.id);
        });

        // Track running job
        let mut running = self.running.write().await;
        running.insert(job_id, handle);

        Ok(())
    }

    /// Cancel a running job
    pub async fn cancel(&self, job_id: &JobId) -> Result<()> {
        // Remove from running jobs
        let mut running = self.running.write().await;
        if let Some(handle) = running.remove(job_id) {
            handle.abort();
        }
        drop(running);

        // Update job status
        if let Some(mut job) = self.persistence.load(job_id).await? {
            job.status = JobStatus::Cancelled;
            job.completed_at = Some(Utc::now());
            job.updated_at = Utc::now();
            self.persistence.save(&job).await?;

            self.event_bus.publish(JobEvent::Cancelled {
                job_id: job_id.clone(),
            }).await?;
        }

        Ok(())
    }

    /// Restart jobs on daemon startup
    pub async fn restart_pending_jobs(&self) -> Result<()> {
        let jobs = self.persistence.load_pending().await?;

        for job in jobs {
            tracing::info!("Restarting job: {} (type: {})", job.id.0, job.job_type);
            self.execute_job(job).await?;
        }

        Ok(())
    }
}
```

### Job Persistence

**Decision: JSON file storage over SQLite**

**Rationale:**

- ✅ **Simpler**: No SQL setup, no migrations, no schema versioning
- ✅ **Human-readable**: Easy to debug, inspect, and manually edit if needed
- ✅ **Lower complexity**: Uses existing `atomic_write_file()` from common/persistence
- ✅ **Adequate for scale**: Zen Garden jobs are low-volume (10-100 max)
- ✅ **Faster cold start**: No DB connection pool to initialize
- ✅ **Philosophy fit**: Aligns with "joy in infrastructure" - simple, transparent

**Location:** `common/src/jobs/persistence.rs`

```rust
/// Trait for persisting jobs
#[async_trait]
pub trait JobPersistence: Send + Sync {
    /// Save a job
    async fn save(&self, job: &Job) -> Result<()>;

    /// Load a job by ID
    async fn load(&self, id: &JobId) -> Result<Option<Job>>;

    /// Load all pending/running jobs (for restart)
    async fn load_pending(&self) -> Result<Vec<Job>>;

    /// Delete completed jobs older than retention period
    async fn cleanup_old_jobs(&self, retention: Duration) -> Result<usize>;
}

/// JSON file implementation (recommended)
pub struct JsonJobPersistence {
    jobs_file: PathBuf,  // /var/lib/zen-garden/jobs.json
}

impl JsonJobPersistence {
    pub fn new(jobs_file: impl Into<PathBuf>) -> Self {
        Self {
            jobs_file: jobs_file.into(),
        }
    }

    async fn load_all(&self) -> Result<HashMap<JobId, Job>> {
        if !self.jobs_file.exists() {
            return Ok(HashMap::new());
        }

        let content = tokio::fs::read_to_string(&self.jobs_file).await?;
        let jobs: HashMap<JobId, Job> = serde_json::from_str(&content)?;
        Ok(jobs)
    }

    async fn save_all(&self, jobs: &HashMap<JobId, Job>) -> Result<()> {
        let json = serde_json::to_string_pretty(jobs)?;
        atomic_write_file(&self.jobs_file, json.as_bytes()).await?;
        Ok(())
    }
}

#[async_trait]
impl JobPersistence for JsonJobPersistence {
    async fn save(&self, job: &Job) -> Result<()> {
        let mut jobs = self.load_all().await?;
        jobs.insert(job.id.clone(), job.clone());
        self.save_all(&jobs).await?;
        Ok(())
    }

    async fn load(&self, id: &JobId) -> Result<Option<Job>> {
        let jobs = self.load_all().await?;
        Ok(jobs.get(id).cloned())
    }

    async fn load_pending(&self) -> Result<Vec<Job>> {
        let jobs = self.load_all().await?;
        let pending: Vec<Job> = jobs
            .values()
            .filter(|job| matches!(job.status, JobStatus::Pending | JobStatus::Running { .. }))
            .cloned()
            .collect();
        Ok(pending)
    }

    async fn cleanup_old_jobs(&self, retention: Duration) -> Result<usize> {
        let cutoff = Utc::now() - chrono::Duration::from_std(retention)?;
        let mut jobs = self.load_all().await?;
        let before_count = jobs.len();

        jobs.retain(|_, job| {
            match &job.status {
                JobStatus::Completed { .. } | JobStatus::Failed { .. } | JobStatus::Cancelled => {
                    // Keep if completed recently
                    job.completed_at.map(|dt| dt > cutoff).unwrap_or(true)
                }
                _ => true, // Keep pending/running jobs
            }
        });

        let removed = before_count - jobs.len();
        if removed > 0 {
            self.save_all(&jobs).await?;
        }

        Ok(removed)
    }
}
```

**File Structure:**

```json
// /var/lib/zen-garden/jobs.json
{
  "01h2x3y4z5a6b7c8d9": {
    "id": "01h2x3y4z5a6b7c8d9",
    "job_type": "install_service",
    "status": {
      "Running": {
        "progress": 0.45
      }
    },
    "created_at": "2026-01-20T10:30:00Z",
    "updated_at": "2026-01-20T10:30:15Z",
    "completed_at": null,
    "input": {
      "offering": "mongodb",
      "adopt": false
    },
    "output": null,
    "checkpoint": {
      "image_pulled": true
    },
    "max_retries": 3,
    "retry_count": 0,
    "next_retry_at": null
  }
}
```

**Benefits over SQLite:**

- No sqlx dependency (~30MB of dependencies removed)
- No schema migrations
- No connection pool management
- Instant daemon startup (no DB init)
- Human-readable for debugging
- Easy to backup/restore (copy one file)

**Performance:**

- Read: O(1) after initial file load
- Write: O(n) for serialize + atomic write (acceptable for <100 jobs)
- Typical file size: <50KB for 100 jobs
- Atomic write: <10ms on SSD

**When to reconsider:**

- If jobs exceed 1,000 active jobs (unlikely for home labs)
- If concurrent access from multiple processes needed (not in current architecture)
- If complex queries needed (current queries are simple: by ID, by status)

### Job Events

**Location:** `common/src/events/job.rs`

```rust
#[derive(Clone, Debug, Serialize)]
pub enum JobEvent {
    Created { job_id: JobId, job_type: String },
    Started { job_id: JobId },
    Progress { job_id: JobId, progress: f32 },
    Completed { job_id: JobId, output: serde_json::Value },
    Failed { job_id: JobId, error: String, retry_in_secs: Option<u64> },
    Cancelled { job_id: JobId },
}

impl DomainEvent for JobEvent {
    fn event_type(&self) -> &'static str { "job" }
    fn timestamp(&self) -> DateTime<Utc> { Utc::now() }
    fn to_json(&self) -> serde_json::Value {
        serde_json::to_value(self).unwrap()
    }
}
```

### Example: Install Service Job

**Location:** `moss/src/domain/services/install_job.rs`

```rust
pub struct InstallServiceJob {
    docker: Arc<DockerService>,
    template_loader: Arc<dyn TemplateProvider>,
}

#[async_trait]
impl JobExecutor for InstallServiceJob {
    fn job_type(&self) -> &'static str {
        "install_service"
    }

    fn timeout(&self) -> Option<Duration> {
        Some(Duration::from_secs(600)) // 10 minutes for image pull
    }

    async fn execute(
        &self,
        input: serde_json::Value,
        checkpoint: Option<serde_json::Value>,
        progress_tx: Sender<f32>,
    ) -> JobResult {
        let req: InstallServiceRequest = serde_json::from_value(input)?;

        // Load template (10% progress)
        progress_tx.send(0.1).await.ok();
        let template = self.template_loader.load(&req.offering).await?;

        // Check checkpoint: did we already pull the image?
        let image_pulled = checkpoint
            .as_ref()
            .and_then(|c| c.get("image_pulled"))
            .and_then(|v| v.as_bool())
            .unwrap_or(false);

        if !image_pulled {
            // Pull Docker image (10% → 70%)
            self.docker.pull_image(&template.image, |progress| {
                progress_tx.blocking_send(0.1 + (progress * 0.6)).ok();
            }).await?;

            // Save checkpoint: image pulled
            // (if job crashes here, we won't re-pull on restart)
            progress_tx.send(0.7).await.ok();
        }

        // Create container (70% → 90%)
        progress_tx.send(0.8).await.ok();
        let container_id = self.docker.create_container(&req.name, &template).await?;

        // Start container (90% → 100%)
        progress_tx.send(0.9).await.ok();
        self.docker.start_container(&container_id).await?;

        progress_tx.send(1.0).await.ok();

        Ok(serde_json::json!({
            "container_id": container_id,
            "service_name": req.name,
        }))
    }

    async fn cancel(&self, checkpoint: Option<serde_json::Value>) -> Result<()> {
        // Cleanup: stop container if started
        // ...
        Ok(())
    }
}
```

### API Integration

**Location:** `moss/src/api/v1/services.rs`

```rust
// Create service endpoint (now returns job ID)
pub async fn create_service_v1(
    State(job_manager): State<Arc<JobManager>>,
    Json(req): Json<InstallServiceRequest>,
) -> Result<Json<ApiResponse<CreateServiceResponse>>> {
    let job_id = job_manager.submit("install_service", serde_json::to_value(req)?).await?;

    Ok(Json(ApiResponse::success(CreateServiceResponse {
        job_id,
        message: "Service installation started. Poll GET /api/v1/jobs/{job_id} for status.".to_string(),
    })))
}

// Get job status endpoint
pub async fn get_job_status_v1(
    State(persistence): State<Arc<dyn JobPersistence>>,
    Path(job_id): Path<String>,
) -> Result<Json<ApiResponse<Job>>> {
    let job_id = JobId(job_id);
    let job = persistence.load(&job_id).await?
        .ok_or_else(|| anyhow!("Job not found"))?;

    Ok(Json(ApiResponse::success(job)))
}

// Cancel job endpoint
pub async fn cancel_job_v1(
    State(job_manager): State<Arc<JobManager>>,
    Path(job_id): Path<String>,
) -> Result<Json<ApiResponse<()>>> {
    let job_id = JobId(job_id);
    job_manager.cancel(&job_id).await?;

    Ok(Json(ApiResponse::success(())))
}
```

### Restart on Daemon Startup

**Location:** `moss/src/main.rs`

```rust
#[tokio::main]
async fn main() -> Result<()> {
    // ... initialization ...

    // Create job manager
    let job_persistence = Arc::new(SqliteJobPersistence::new("/var/lib/zen-garden/jobs.db").await?);
    let job_manager = Arc::new(JobManager::new(job_persistence, event_bus.clone()));

    // Register job executors
    job_manager.register_executor(Arc::new(InstallServiceJob::new(
        docker_service.clone(),
        template_loader.clone(),
    ))).await;

    // Restart pending jobs from previous run
    job_manager.restart_pending_jobs().await?;

    // ... start API server ...
}
```

**Benefits:**

- ✅ Long operations don't block API responses
- ✅ Jobs survive daemon crashes
- ✅ Progress tracking via events (SSE)
- ✅ Automatic retry with exponential backoff
- ✅ Graceful cancellation
- ✅ Checkpointing prevents wasted work (re-pulling images)

---

## Centralized Hot-Cache Architecture

### Overview

The cache module provides in-memory graph storage with:

- **Hot-cache philosophy**: Keep frequently accessed data in RAM
- **TTL-based eviction**: Automatic cleanup of stale data
- **Event-driven updates**: Cache stays current via event subscriptions
- **Topology graphs**: Stone network, service mappings, capabilities
- **Manifest caching**: Template offerings, compatibility rules

### Cache Types

**Location:** `common/src/cache/mod.rs`

```rust
/// Centralized cache manager
pub struct CacheManager {
    topology: Arc<RwLock<TopologyCache>>,
    manifests: Arc<RwLock<ManifestCache>>,
    capabilities: Arc<RwLock<CapabilityCache>>,
    event_bus: Arc<dyn EventBus>,
}

impl CacheManager {
    pub fn new(event_bus: Arc<dyn EventBus>) -> Self {
        let manager = Self {
            topology: Arc::new(RwLock::new(TopologyCache::new())),
            manifests: Arc::new(RwLock::new(ManifestCache::new())),
            capabilities: Arc::new(RwLock::new(CapabilityCache::new())),
            event_bus,
        };

        // Subscribe to events for cache updates
        manager.setup_event_subscriptions();

        manager
    }

    fn setup_event_subscriptions(&self) {
        // Updates happen automatically via event handlers
    }

    pub fn topology(&self) -> Arc<RwLock<TopologyCache>> {
        self.topology.clone()
    }

    pub fn manifests(&self) -> Arc<RwLock<ManifestCache>> {
        self.manifests.clone()
    }

    pub fn capabilities(&self) -> Arc<RwLock<CapabilityCache>> {
        self.capabilities.clone()
    }
}
```

### Topology Cache

**Location:** `common/src/cache/topology.rs`

```rust
/// In-memory graph of stones, services, and relationships
pub struct TopologyCache {
    stones: HashMap<StoneId, StoneEntry>,
    services: HashMap<ServiceId, ServiceEntry>,
    service_by_type: HashMap<String, Vec<ServiceId>>,  // "mongodb" → [service1, service2]
    ttl: Duration,
}

#[derive(Clone, Debug)]
pub struct StoneEntry {
    pub info: StoneInfo,
    pub last_seen: Instant,
    pub capabilities: Capabilities,
    pub services: Vec<ServiceId>,  // Services running on this stone
}

#[derive(Clone, Debug)]
pub struct ServiceEntry {
    pub info: ServiceInfo,
    pub stone_id: StoneId,  // Which stone hosts this service
    pub last_updated: Instant,
}

impl TopologyCache {
    pub fn new() -> Self {
        Self {
            stones: HashMap::new(),
            services: HashMap::new(),
            service_by_type: HashMap::new(),
            ttl: Duration::from_secs(90),  // 90s TTL (same as current)
        }
    }

    /// Add or update a stone
    pub fn upsert_stone(&mut self, stone: StoneInfo, capabilities: Capabilities) {
        let stone_id = stone.id.clone();
        let entry = StoneEntry {
            info: stone,
            last_seen: Instant::now(),
            capabilities,
            services: self.get_services_for_stone(&stone_id),
        };
        self.stones.insert(stone_id, entry);
    }

    /// Add or update a service
    pub fn upsert_service(&mut self, service: ServiceInfo, stone_id: StoneId) {
        let service_id = service.id.clone();
        let service_type = service.service_type.clone();

        let entry = ServiceEntry {
            info: service,
            stone_id: stone_id.clone(),
            last_updated: Instant::now(),
        };

        self.services.insert(service_id.clone(), entry);

        // Update reverse index
        self.service_by_type
            .entry(service_type)
            .or_insert_with(Vec::new)
            .push(service_id.clone());

        // Update stone's service list
        if let Some(stone) = self.stones.get_mut(&stone_id) {
            if !stone.services.contains(&service_id) {
                stone.services.push(service_id);
            }
        }
    }

    /// Get stone by ID
    pub fn get_stone(&self, id: &StoneId) -> Option<&StoneEntry> {
        self.stones.get(id).filter(|entry| {
            entry.last_seen.elapsed() < self.ttl
        })
    }

    /// Get all live stones
    pub fn get_all_stones(&self) -> Vec<StoneInfo> {
        self.stones
            .values()
            .filter(|entry| entry.last_seen.elapsed() < self.ttl)
            .map(|entry| entry.info.clone())
            .collect()
    }

    /// Find services by type (e.g., "mongodb")
    pub fn find_services_by_type(&self, service_type: &str) -> Vec<ServiceInfo> {
        self.service_by_type
            .get(service_type)
            .map(|ids| {
                ids.iter()
                    .filter_map(|id| self.services.get(id))
                    .filter(|entry| entry.last_updated.elapsed() < self.ttl)
                    .map(|entry| entry.info.clone())
                    .collect()
            })
            .unwrap_or_default()
    }

    /// Get stone hosting a service
    pub fn get_stone_for_service(&self, service_id: &ServiceId) -> Option<StoneInfo> {
        self.services
            .get(service_id)
            .and_then(|service| self.stones.get(&service.stone_id))
            .filter(|stone| stone.last_seen.elapsed() < self.ttl)
            .map(|stone| stone.info.clone())
    }

    /// Evict stale entries (called periodically)
    pub fn evict_stale(&mut self) -> usize {
        let now = Instant::now();
        let mut evicted = 0;

        // Evict stale stones
        self.stones.retain(|_, entry| {
            let alive = now.duration_since(entry.last_seen) < self.ttl;
            if !alive {
                evicted += 1;
            }
            alive
        });

        // Evict stale services
        self.services.retain(|_, entry| {
            let alive = now.duration_since(entry.last_updated) < self.ttl;
            if !alive {
                evicted += 1;
            }
            alive
        });

        // Rebuild service_by_type index
        self.rebuild_service_index();

        evicted
    }

    fn rebuild_service_index(&mut self) {
        self.service_by_type.clear();
        for (service_id, entry) in &self.services {
            self.service_by_type
                .entry(entry.info.service_type.clone())
                .or_insert_with(Vec::new)
                .push(service_id.clone());
        }
    }

    fn get_services_for_stone(&self, stone_id: &StoneId) -> Vec<ServiceId> {
        self.services
            .iter()
            .filter(|(_, entry)| &entry.stone_id == stone_id)
            .map(|(id, _)| id.clone())
            .collect()
    }

    /// Query topology graph
    pub fn query(&self) -> TopologyQuery<'_> {
        TopologyQuery::new(self)
    }
}

/// Fluent query interface for topology
pub struct TopologyQuery<'a> {
    cache: &'a TopologyCache,
    stone_filter: Option<Box<dyn Fn(&StoneEntry) -> bool + 'a>>,
    service_filter: Option<Box<dyn Fn(&ServiceEntry) -> bool + 'a>>,
}

impl<'a> TopologyQuery<'a> {
    fn new(cache: &'a TopologyCache) -> Self {
        Self {
            cache,
            stone_filter: None,
            service_filter: None,
        }
    }

    pub fn with_capability(mut self, capability: &'a str) -> Self {
        self.stone_filter = Some(Box::new(move |stone: &StoneEntry| {
            // Check if stone has capability
            // (implementation depends on Capabilities structure)
            true
        }));
        self
    }

    pub fn offering_service(mut self, service_type: &'a str) -> Self {
        self.stone_filter = Some(Box::new(move |stone: &StoneEntry| {
            stone.services.iter().any(|service_id| {
                // Check if any service matches type
                true
            })
        }));
        self
    }

    pub fn stones(self) -> Vec<StoneInfo> {
        self.cache
            .stones
            .values()
            .filter(|entry| {
                entry.last_seen.elapsed() < self.cache.ttl &&
                self.stone_filter.as_ref().map(|f| f(entry)).unwrap_or(true)
            })
            .map(|entry| entry.info.clone())
            .collect()
    }

    pub fn services(self) -> Vec<ServiceInfo> {
        self.cache
            .services
            .values()
            .filter(|entry| {
                entry.last_updated.elapsed() < self.cache.ttl &&
                self.service_filter.as_ref().map(|f| f(entry)).unwrap_or(true)
            })
            .map(|entry| entry.info.clone())
            .collect()
    }
}
```

### Manifest Cache

**Location:** `common/src/cache/manifests.rs`

```rust
/// In-memory cache of service offerings and compatibility rules
pub struct ManifestCache {
    offerings: HashMap<String, OfferingEntry>,  // "mongodb" → OfferingEntry
    fingerprints: HashMap<String, String>,  // offering → fingerprint (for change detection)
}

#[derive(Clone, Debug)]
pub struct OfferingEntry {
    pub offering: Offering,
    pub cached_at: Instant,
}

impl ManifestCache {
    pub fn new() -> Self {
        Self {
            offerings: HashMap::new(),
            fingerprints: HashMap::new(),
        }
    }

    /// Cache an offering
    pub fn insert(&mut self, name: String, offering: Offering, fingerprint: String) {
        self.offerings.insert(name.clone(), OfferingEntry {
            offering,
            cached_at: Instant::now(),
        });
        self.fingerprints.insert(name, fingerprint);
    }

    /// Get cached offering
    pub fn get(&self, name: &str) -> Option<&Offering> {
        self.offerings.get(name).map(|entry| &entry.offering)
    }

    /// Check if offering has changed (by fingerprint)
    pub fn has_changed(&self, name: &str, new_fingerprint: &str) -> bool {
        self.fingerprints
            .get(name)
            .map(|fp| fp != new_fingerprint)
            .unwrap_or(true)
    }

    /// Get all cached offerings
    pub fn list_all(&self) -> Vec<String> {
        self.offerings.keys().cloned().collect()
    }

    /// Clear cache (for manual refresh)
    pub fn clear(&mut self) {
        self.offerings.clear();
        self.fingerprints.clear();
    }
}
```

### Capability Cache

**Location:** `common/src/cache/capabilities.rs`

```rust
/// In-memory cache of stone capabilities
pub struct CapabilityCache {
    capabilities: HashMap<StoneId, CapabilitiesEntry>,
}

#[derive(Clone, Debug)]
pub struct CapabilitiesEntry {
    pub capabilities: Capabilities,
    pub cached_at: Instant,
    pub ttl: Duration,
}

impl CapabilityCache {
    pub fn new() -> Self {
        Self {
            capabilities: HashMap::new(),
        }
    }

    /// Cache capabilities for a stone
    pub fn insert(&mut self, stone_id: StoneId, capabilities: Capabilities) {
        self.capabilities.insert(stone_id, CapabilitiesEntry {
            capabilities,
            cached_at: Instant::now(),
            ttl: Duration::from_secs(300),  // 5 minutes (hardware doesn't change often)
        });
    }

    /// Get cached capabilities
    pub fn get(&self, stone_id: &StoneId) -> Option<&Capabilities> {
        self.capabilities.get(stone_id).and_then(|entry| {
            if entry.cached_at.elapsed() < entry.ttl {
                Some(&entry.capabilities)
            } else {
                None
            }
        })
    }

    /// Evict stale capabilities
    pub fn evict_stale(&mut self) -> usize {
        let before = self.capabilities.len();
        self.capabilities.retain(|_, entry| {
            entry.cached_at.elapsed() < entry.ttl
        });
        before - self.capabilities.len()
    }
}
```

### Event-Driven Cache Updates

**Location:** `moss/src/handlers/cache.rs`

```rust
/// Event handler that keeps cache synchronized
pub struct CacheUpdateHandler {
    cache: Arc<CacheManager>,
}

impl CacheUpdateHandler {
    pub fn new(cache: Arc<CacheManager>) -> Self {
        Self { cache }
    }
}

#[async_trait]
impl EventHandler<DiscoveryEvent> for CacheUpdateHandler {
    async fn handle(&self, event: &DiscoveryEvent) -> Result<()> {
        match event {
            DiscoveryEvent::StoneDiscovered { stone } => {
                let mut topology = self.cache.topology().write().await;
                topology.upsert_stone(stone.info.clone(), stone.capabilities.clone());
            }
            _ => {}
        }
        Ok(())
    }
}

#[async_trait]
impl EventHandler<ServiceEvent> for CacheUpdateHandler {
    async fn handle(&self, event: &ServiceEvent) -> Result<()> {
        match event {
            ServiceEvent::Started { service_name, container_id } => {
                let mut topology = self.cache.topology().write().await;
                // Update topology with new service
                // (requires service info from event payload)
            }
            ServiceEvent::Stopped { service_name } => {
                let mut topology = self.cache.topology().write().await;
                // Remove service from topology
            }
            _ => {}
        }
        Ok(())
    }
}
```

### Periodic Eviction Task

**Location:** `moss/src/main.rs`

```rust
#[tokio::main]
async fn main() -> Result<()> {
    // ... initialization ...

    // Create cache manager
    let cache_manager = Arc::new(CacheManager::new(event_bus.clone()));

    // Subscribe cache update handler to events
    let cache_handler = Arc::new(CacheUpdateHandler::new(cache_manager.clone()));
    event_bus.subscribe_handler::<DiscoveryEvent>(cache_handler.clone()).await?;
    event_bus.subscribe_handler::<ServiceEvent>(cache_handler.clone()).await?;

    // Start periodic eviction task
    let cache_clone = cache_manager.clone();
    tokio::spawn(async move {
        let mut interval = tokio::time::interval(Duration::from_secs(30));
        loop {
            interval.tick().await;

            // Evict stale topology entries
            let evicted_topology = {
                let mut topology = cache_clone.topology().write().await;
                topology.evict_stale()
            };

            // Evict stale capabilities
            let evicted_caps = {
                let mut capabilities = cache_clone.capabilities().write().await;
                capabilities.evict_stale()
            };

            if evicted_topology > 0 || evicted_caps > 0 {
                tracing::debug!(
                    "Evicted {} topology entries, {} capability entries",
                    evicted_topology,
                    evicted_caps
                );
            }
        }
    });

    // ... start API server ...
}
```

### Usage Example: Discovery with Cache

**Location:** `rake/src/domain/commands/discover.rs`

```rust
pub async fn discover_command(cache: Arc<CacheManager>) -> Result<()> {
    // Check cache first (hot path)
    let cached_stones = {
        let topology = cache.topology().read().await;
        topology.get_all_stones()
    };

    if !cached_stones.is_empty() {
        // Return cached results (< 1ms)
        println!("Found {} stones (cached):", cached_stones.len());
        for stone in cached_stones {
            println!("  - {} at {}", stone.name, stone.endpoint);
        }
        return Ok(());
    }

    // Cache miss: perform UDP discovery
    let discovery = UdpDiscovery::new(7184);
    let stones = discovery.discover(Duration::from_secs(3)).await?;

    // Update cache for next time
    {
        let mut topology = cache.topology().write().await;
        for stone in &stones {
            topology.upsert_stone(stone.info.clone(), stone.capabilities.clone());
        }
    }

    println!("Found {} stones (discovered):", stones.len());
    for stone in stones {
        println!("  - {} at {}", stone.info.name, stone.info.endpoint);
    }

    Ok(())
}
```

### Cache Policies

**Location:** `common/src/cache/policies.rs`

```rust
/// TTL-based eviction policy
pub struct TtlPolicy {
    pub default_ttl: Duration,
    pub max_ttl: Duration,
}

impl TtlPolicy {
    pub fn should_evict(&self, cached_at: Instant) -> bool {
        cached_at.elapsed() > self.default_ttl
    }

    pub fn should_refresh(&self, cached_at: Instant) -> bool {
        cached_at.elapsed() > self.default_ttl / 2
    }
}

/// Size-based eviction policy (LRU)
pub struct SizePolicy {
    pub max_entries: usize,
}

/// Adaptive TTL based on access patterns
pub struct AdaptivePolicy {
    pub min_ttl: Duration,
    pub max_ttl: Duration,
}

impl AdaptivePolicy {
    pub fn calculate_ttl(&self, access_count: u32) -> Duration {
        // Frequently accessed entries get longer TTL
        let factor = (access_count as f32).log2().clamp(1.0, 10.0) / 10.0;
        let ttl_secs = self.min_ttl.as_secs() +
            ((self.max_ttl.as_secs() - self.min_ttl.as_secs()) as f32 * factor) as u64;
        Duration::from_secs(ttl_secs)
    }
}
```

**Benefits:**

- ✅ Sub-millisecond lookups (no network I/O)
- ✅ Automatic cache invalidation via events
- ✅ TTL-based eviction prevents stale data
- ✅ Graph queries for topology analysis
- ✅ Manifest caching reduces template parsing
- ✅ Capability caching eliminates redundant hardware detection

---

## Core Abstractions (Traits)

### 1. Discovery Abstraction

**Location:** `common/src/discovery/mod.rs` (moved from traits/)

```rust
/// Trait for responding to discovery queries
#[async_trait]
pub trait DiscoveryResponder {
    /// Handle a discovery query and return this stone's info
    async fn respond(&self, query: &DiscoveryQuery) -> Result<StoneAnnouncement>;
}

/// Trait for querying the network for stones
#[async_trait]
pub trait DiscoveryQuery {
    /// Discover stones matching the query
    async fn discover(&self, timeout: Duration) -> Result<Vec<StoneAnnouncement>>;
}

/// Trait for announcing services via broadcast protocols
#[async_trait]
pub trait DiscoveryAnnouncer {
    /// Announce this stone's presence
    async fn announce(&self, stone: &StoneInfo) -> Result<()>;

    /// Stop announcing
    async fn stop(&self) -> Result<()>;
}
```

**Implementations:**

- `moss/infra/discovery/udp.rs` → implements `DiscoveryResponder`
- `moss/infra/discovery/mdns.rs` → implements `DiscoveryAnnouncer`
- `rake/infra/discovery/udp.rs` → implements `DiscoveryQuery`
- `lantern/infra/discovery/udp.rs` → implements `DiscoveryAnnouncer`

**Benefit:** Single discovery protocol definition, 4 focused implementations.

---

### 2. Persistence Abstraction

**Location:** `common/src/traits/persistence.rs`

```rust
/// Trait for persisting and loading data
#[async_trait]
pub trait PersistenceProvider<T> {
    /// Load data from storage
    async fn load(&self) -> Result<Option<T>>;

    /// Save data to storage (atomic)
    async fn save(&self, data: &T) -> Result<()>;

    /// Delete data from storage
    async fn delete(&self) -> Result<()>;
}

/// Trait for transactional updates
#[async_trait]
pub trait TransactionalStorage<T> {
    type Transaction;

    /// Begin a transaction
    async fn begin(&self) -> Result<Self::Transaction>;

    /// Commit a transaction (atomic)
    async fn commit(&self, tx: Self::Transaction) -> Result<()>;

    /// Rollback a transaction
    async fn rollback(&self, tx: Self::Transaction) -> Result<()>;
}
```

**Implementations:**

- `moss/infra/persistence/registry.rs` → `PersistenceProvider<Vec<ServiceInfo>>`
- `moss/infra/persistence/transactions.rs` → `TransactionalStorage<RegistryState>`
- `lantern/infra/registry/memory.rs` → `PersistenceProvider<TopologyState>`
- `lantern/infra/registry/sqlite.rs` → `PersistenceProvider<TopologyState>` (future)

**Benefit:** Swap storage backends without changing business logic.

---

### 3. Event System Abstraction

**Location:** `common/src/events/mod.rs`

```rust
/// Domain event that can be emitted by the system
pub trait DomainEvent: Clone + Send + Sync + 'static {
    /// Event type identifier
    fn event_type(&self) -> &'static str;

    /// Timestamp when event occurred
    fn timestamp(&self) -> DateTime<Utc>;

    /// Convert to JSON for serialization
    fn to_json(&self) -> serde_json::Value;
}

/// Event bus for publishing and subscribing to events
#[async_trait]
pub trait EventBus: Send + Sync {
    /// Publish an event to all subscribers
    async fn publish<E: DomainEvent>(&self, event: E) -> Result<()>;

    /// Subscribe to events of a specific type
    async fn subscribe<E: DomainEvent>(&self) -> Result<Receiver<E>>;

    /// Subscribe to all events
    async fn subscribe_all(&self) -> Result<Receiver<Box<dyn DomainEvent>>>;
}

/// Event handler that reacts to specific events
#[async_trait]
pub trait EventHandler<E: DomainEvent>: Send + Sync {
    /// Handle an event
    async fn handle(&self, event: &E) -> Result<()>;
}
```

**Event Definitions:**

```rust
// common/src/events/domain.rs
#[derive(Clone, Debug, Serialize)]
pub enum ServiceEvent {
    Starting { service_name: String },
    Started { service_name: String, container_id: String },
    Stopping { service_name: String },
    Stopped { service_name: String },
    Failed { service_name: String, error: String },
    Restarting { service_name: String, attempt: u32 },
}

impl DomainEvent for ServiceEvent {
    fn event_type(&self) -> &'static str { "service" }
    // ...
}

#[derive(Clone, Debug, Serialize)]
pub enum DiscoveryEvent {
    QueryReceived { from: SocketAddr },
    ResponseSent { to: SocketAddr, stone: String },
    StoneDiscovered { stone: StoneInfo },
}

#[derive(Clone, Debug, Serialize)]
pub enum RegistryEvent {
    ServiceAdded { service: ServiceInfo },
    ServiceRemoved { service_name: String },
    RegistryPersisted { path: String },
}

#[derive(Clone, Debug, Serialize)]
pub enum SystemEvent {
    ConfigLoaded { source: String },
    HealthCheckCompleted { status: HealthStatus },
    TemplateIndexRefreshed { count: usize },
}
```

**Event Bus Implementation:**

```rust
// moss/src/events/bus.rs
pub struct InMemoryEventBus {
    // Type-erased channels for each event type
    channels: Arc<RwLock<HashMap<TypeId, Sender<Box<dyn DomainEvent>>>>>,
}

impl EventBus for InMemoryEventBus {
    async fn publish<E: DomainEvent>(&self, event: E) -> Result<()> {
        let type_id = TypeId::of::<E>();
        let channels = self.channels.read().await;

        if let Some(tx) = channels.get(&type_id) {
            let boxed: Box<dyn DomainEvent> = Box::new(event.clone());
            tx.send(boxed).ok(); // Non-blocking
        }

        Ok(())
    }

    // ... other methods
}
```

**Event Handlers:**

```rust
// moss/src/events/handlers/console.rs
pub struct ConsoleEventHandler {
    console: Arc<ConsolePrinter>,
}

#[async_trait]
impl EventHandler<ServiceEvent> for ConsoleEventHandler {
    async fn handle(&self, event: &ServiceEvent) -> Result<()> {
        match event {
            ServiceEvent::Starting { service_name } => {
                self.console.emit(
                    EventStatus::Starting,
                    &format!("Starting service: {}", service_name),
                    None,
                );
            }
            ServiceEvent::Started { service_name, container_id } => {
                self.console.emit(
                    EventStatus::Ready,
                    &format!("Service started: {} ({})", service_name, container_id),
                    None,
                );
            }
            // ... other cases
        }
        Ok(())
    }
}

// moss/src/events/handlers/sse.rs
pub struct SSEEventHandler {
    broadcaster: Arc<Mutex<Vec<Sender<Event>>>>,
}

#[async_trait]
impl EventHandler<ServiceEvent> for SSEEventHandler {
    async fn handle(&self, event: &ServiceEvent) -> Result<()> {
        let json = event.to_json();
        let sse_event = Event::default()
            .event(event.event_type())
            .json_data(json)?;

        let broadcaster = self.broadcaster.lock().await;
        for tx in broadcaster.iter() {
            let _ = tx.send(sse_event.clone()).await;
        }

        Ok(())
    }
}

// moss/src/events/handlers/persistence.rs
pub struct PersistenceEventHandler {
    registry_provider: Arc<dyn PersistenceProvider<Vec<ServiceInfo>>>,
}

#[async_trait]
impl EventHandler<RegistryEvent> for PersistenceEventHandler {
    async fn handle(&self, event: &RegistryEvent) -> Result<()> {
        match event {
            RegistryEvent::ServiceAdded { .. } |
            RegistryEvent::ServiceRemoved { .. } => {
                // Trigger async persistence
                // (actual save happens via transaction in domain layer)
            }
            _ => {}
        }
        Ok(())
    }
}
```

**Event Pipeline Setup:**

```rust
// moss/src/main.rs
#[tokio::main]
async fn main() -> Result<()> {
    // 1. Create event bus
    let event_bus = Arc::new(InMemoryEventBus::new());

    // 2. Register event handlers
    let console_handler = Arc::new(ConsoleEventHandler::new(console));
    let sse_handler = Arc::new(SSEEventHandler::new(broadcaster));
    let persistence_handler = Arc::new(PersistenceEventHandler::new(registry_provider));

    // 3. Subscribe handlers to events
    event_bus.subscribe_handler::<ServiceEvent>(console_handler.clone()).await?;
    event_bus.subscribe_handler::<ServiceEvent>(sse_handler.clone()).await?;
    event_bus.subscribe_handler::<RegistryEvent>(persistence_handler).await?;

    // 4. Pass event bus to domain services
    let service_orchestrator = ServiceOrchestrator::new(
        docker_service,
        template_loader,
        registry_manager,
        event_bus.clone(), // Services emit events
    );

    // 5. Start API server
    let app = create_router(service_orchestrator, event_bus);
    // ...
}
```

**Usage in Domain Logic:**

```rust
// moss/src/domain/services/lifecycle.rs
pub struct ServiceLifecycle {
    docker: Arc<DockerService>,
    events: Arc<dyn EventBus>,
}

impl ServiceLifecycle {
    pub async fn start_service(&self, name: &str) -> Result<String> {
        // Emit starting event
        self.events.publish(ServiceEvent::Starting {
            service_name: name.to_string()
        }).await?;

        // Perform operation
        let container_id = match self.docker.start_container(name).await {
            Ok(id) => {
                // Emit success event
                self.events.publish(ServiceEvent::Started {
                    service_name: name.to_string(),
                    container_id: id.clone(),
                }).await?;
                id
            }
            Err(e) => {
                // Emit failure event
                self.events.publish(ServiceEvent::Failed {
                    service_name: name.to_string(),
                    error: e.to_string(),
                }).await?;
                return Err(e);
            }
        };

        Ok(container_id)
    }
}
```

**Benefit:**

- ✅ Business logic (ServiceLifecycle) knows nothing about console, SSE, or metrics
- ✅ Add new event handlers (webhooks, Prometheus, audit logs) without touching domain code
- ✅ Easy to test: mock event bus, assert events were published
- ✅ SSE implementation becomes trivial: subscribe to events, forward to clients

---

### 4. Authentication Abstraction

**Location:** `common/src/traits/auth.rs`

```rust
/// Trait for authenticating requests
#[async_trait]
pub trait AuthProvider: Send + Sync {
    /// Validate credentials and return principal
    async fn authenticate(&self, token: &str) -> Result<Principal>;

    /// Check if principal has permission
    async fn authorize(&self, principal: &Principal, action: &str, resource: &str) -> Result<bool>;
}

/// Authenticated principal (user, service, etc.)
#[derive(Clone, Debug)]
pub struct Principal {
    pub id: String,
    pub name: String,
    pub roles: Vec<String>,
    pub metadata: HashMap<String, String>,
}
```

**Implementations:**

- `lantern/infra/auth/jwt.rs` → `JwtAuthProvider`
- `moss/api/middleware/auth.rs` → uses `AuthProvider` trait

**Benefit:** Swap auth mechanisms (JWT, mTLS, API keys) without changing API layer.

---

## Composition Pipelines

### Discovery Pipeline Example

**Current State:** 3 different implementations, duplicated logic

**Proposed Pipeline:**

```rust
// common/src/traits/discovery.rs
pub struct DiscoveryPipeline {
    strategies: Vec<Box<dyn DiscoveryQuery>>,
}

impl DiscoveryPipeline {
    pub fn new() -> Self {
        Self { strategies: vec![] }
    }

    pub fn with_strategy(mut self, strategy: Box<dyn DiscoveryQuery>) -> Self {
        self.strategies.push(strategy);
        self
    }

    pub async fn discover_first(&self, timeout: Duration) -> Result<StoneAnnouncement> {
        for strategy in &self.strategies {
            if let Ok(stones) = strategy.discover(timeout).await {
                if let Some(stone) = stones.first() {
                    return Ok(stone.clone());
                }
            }
        }
        Err(anyhow!("No stones found"))
    }
}

// rake/src/main.rs - Usage
let discovery = DiscoveryPipeline::new()
    .with_strategy(Box::new(ExplicitEndpoint::new(cli.at)))  // --at flag
    .with_strategy(Box::new(EnvVarEndpoint::new("GARDEN_STONE")))  // env var
    .with_strategy(Box::new(CachedEndpoint::new(&STONE_CACHE)))  // cache
    .with_strategy(Box::new(UdpDiscovery::new(7184)));  // UDP broadcast

let stone = discovery.discover_first(Duration::from_secs(3)).await?;
```

**Benefit:** Single discovery orchestration, reusable strategies, testable in isolation.

---

### Compatibility Evaluation Pipeline

**Current State:** 300-line monolithic function in main.rs

**Proposed Pipeline:**

```rust
// moss/src/domain/compat/mod.rs
pub struct CompatibilityPipeline {
    evaluators: Vec<Box<dyn CompatEvaluator>>,
}

#[async_trait]
pub trait CompatEvaluator: Send + Sync {
    /// Evaluate compatibility for rules this evaluator understands
    async fn evaluate(
        &self,
        rule: &RuleCondition,
        caps: &Capabilities,
    ) -> Option<CompatibilityDecision>;
}

impl CompatibilityPipeline {
    pub fn standard() -> Self {
        Self {
            evaluators: vec![
                Box::new(CpuEvaluator),
                Box::new(MemoryEvaluator),
                Box::new(GpuEvaluator),
                Box::new(AiRuntimeEvaluator),
                Box::new(PlatformEvaluator),
            ],
        }
    }

    pub async fn evaluate(
        &self,
        offering: &Offering,
        caps: &Capabilities,
    ) -> CompatibilityDecision {
        for rule in &offering.requires {
            for evaluator in &self.evaluators {
                if let Some(decision) = evaluator.evaluate(rule, caps).await {
                    if !decision.is_compatible() {
                        return decision;
                    }
                }
            }
        }
        CompatibilityDecision::Compatible
    }
}

// moss/src/domain/compat/evaluators/cpu.rs (50 lines)
pub struct CpuEvaluator;

#[async_trait]
impl CompatEvaluator for CpuEvaluator {
    async fn evaluate(&self, rule: &RuleCondition, caps: &Capabilities) -> Option<CompatibilityDecision> {
        match rule {
            RuleCondition::CpuArch { value } => {
                if caps.cpu.arch != *value {
                    return Some(CompatibilityDecision::Incompatible {
                        reason: format!("CPU arch mismatch: need {}, have {}", value, caps.cpu.arch),
                    });
                }
                Some(CompatibilityDecision::Compatible)
            }
            RuleCondition::CpuCores { min } => {
                if caps.cpu.cores < *min {
                    return Some(CompatibilityDecision::Incompatible {
                        reason: format!("Not enough cores: need {}, have {}", min, caps.cpu.cores),
                    });
                }
                Some(CompatibilityDecision::Compatible)
            }
            _ => None, // Not a CPU rule, skip
        }
    }
}
```

**Benefit:** Each evaluator is ~50 lines, testable independently, easy to add new rules.

---

## Thin API Handlers

### Before (Fat Handler)

```rust
// moss/src/api/v1/services.rs - CURRENT (120 lines)
pub async fn create_service_v1(
    State(state): State<Arc<AppState>>,
    Json(req): Json<CreateServiceRequest>,
) -> Result<Json<ApiResponse<CreateServiceResponse>>> {
    // 1. Validation (15 lines)
    // 2. Container adoption (20 lines)
    // 3. Template loading (25 lines)
    // 4. Compatibility checking (30 lines)
    // 5. Registry updates (20 lines)
    // 6. Job creation (10 lines)
    // Total: 120 lines of mixed concerns
}
```

### After (Thin Handler)

```rust
// moss/src/api/v1/services.rs - PROPOSED (15 lines)
pub async fn create_service_v1(
    State(orchestrator): State<Arc<ServiceOrchestrator>>,
    Json(req): Json<CreateServiceRequest>,
) -> Result<Json<ApiResponse<CreateServiceResponse>>> {
    let result = orchestrator
        .create_service(req)
        .await
        .map_err(|e| ApiError::from(e))?;

    Ok(Json(ApiResponse::success(result)))
}

// moss/src/domain/services/mod.rs - Business logic (well-tested)
impl ServiceOrchestrator {
    pub async fn create_service(
        &self,
        req: CreateServiceRequest,
    ) -> Result<CreateServiceResponse> {
        // 1. Validation
        self.validate_request(&req)?;

        // 2. Adoption or installation
        let service_info = if req.adopt {
            self.adopt_service(&req).await?
        } else {
            self.install_service(&req).await?
        };

        // 3. Emit event
        self.events.publish(RegistryEvent::ServiceAdded {
            service: service_info.clone(),
        }).await?;

        Ok(CreateServiceResponse {
            service: service_info,
        })
    }
}
```

**Benefit:** API layer is 15 lines of plumbing, domain layer is unit-testable without HTTP mocks.

---

## Migration Plan

### Phase 1: Foundation (Week 1)

**Goal:** Establish core abstractions without breaking existing code

1. **Create trait definitions**
   - [ ] `common/src/traits/discovery.rs` (2 hours)
   - [ ] `common/src/traits/persistence.rs` (2 hours)
   - [ ] `common/src/traits/auth.rs` (1 hour)
   - [ ] `common/src/events/mod.rs` (4 hours)

2. **Implement event bus**
   - [ ] `common/src/events/bus.rs` - InMemoryEventBus (4 hours)
   - [ ] `common/src/events/domain.rs` - Event definitions (2 hours)
   - [ ] Unit tests for event bus (2 hours)

3. **Extract utilities**
   - [ ] `common/src/utils.rs::atomic_write_file()` (1 hour)
   - [ ] Config getter macro (1 hour)
   - [ ] Error response trait (2 hours)

**Deliverable:** Core abstractions available, no moss/rake changes yet

---

### Phase 2: Moss Infra Layer (Week 2)

**Goal:** Extract infrastructure concerns from main.rs

1. **Create infra modules**
   - [ ] `moss/infra/persistence/` (6 hours)
     - `atomic.rs` - Atomic file writes
     - `registry.rs` - Implements PersistenceProvider
     - `transactions.rs` - Implements TransactionalStorage

   - [ ] `moss/infra/discovery/` (4 hours)
     - `udp.rs` - Implements DiscoveryResponder
     - `mdns.rs` - Implements DiscoveryAnnouncer

   - [ ] `moss/infra/metrics/` (3 hours)
     - Move hardware.rs, gpu.rs from current location
     - Implement MetricsCollector trait

2. **Refactor DockerService to emit events**
   - [ ] Add EventBus parameter to DockerService (2 hours)
   - [ ] Replace console params with event emissions (4 hours)
   - [ ] Update callers to use events (2 hours)

**Deliverable:** All I/O moved to infra/, domain logic decoupled from I/O

---

### Phase 3: Moss Domain Layer (Week 3)

**Goal:** Extract business logic from main.rs

1. **Create domain modules**
   - [ ] `moss/domain/config/` (4 hours)
     - Move MossConfig out of main.rs
     - Apply config getter macro

   - [ ] `moss/domain/compat/` (8 hours)
     - `evaluators/cpu.rs`, `memory.rs`, `gpu.rs`, `ai_runtime.rs`
     - `mod.rs` - CompatibilityPipeline
     - Unit tests for each evaluator

   - [ ] `moss/domain/services/` (12 hours)
     - `lifecycle.rs` - Start/stop/restart with events
     - `adoption.rs` - Container adoption logic
     - `templates.rs` - Template compilation logic
     - `mod.rs` - ServiceOrchestrator

2. **Add transaction support**
   - [ ] RegistryTransaction implementation (4 hours)
   - [ ] Update all registry mutations to use transactions (4 hours)

**Deliverable:** Business logic in domain/, fully testable, main.rs shrinks to ~1000 lines

---

### Phase 4: Moss API Layer (Week 4)

**Goal:** Make API handlers thin, set up event pipeline

1. **Create event handlers**
   - [ ] `moss/events/handlers/console.rs` (3 hours)
   - [ ] `moss/events/handlers/sse.rs` (4 hours)
   - [ ] `moss/events/handlers/persistence.rs` (2 hours)

2. **Refactor API handlers**
   - [ ] Update services.rs to use ServiceOrchestrator (3 hours)
   - [ ] Update offerings.rs to use domain layer (2 hours)
   - [ ] Update health.rs to use domain layer (1 hour)
   - [ ] Add SSE endpoint (3 hours)

3. **Add middleware**
   - [ ] `moss/api/middleware/auth.rs` (stubbed, ready for JWT) (2 hours)
   - [ ] `moss/api/middleware/logging.rs` (2 hours)
   - [ ] `moss/api/middleware/errors.rs` (2 hours)

4. **Refactor main.rs**
   - [ ] Move route definitions to `api/mod.rs` (2 hours)
   - [ ] Bootstrap becomes ~200 lines (2 hours)

**Deliverable:** moss/main.rs is ~200 lines, API handlers are <20 lines each, SSE works

---

### Phase 5: Rake Refactoring (Week 5)

**Goal:** Apply same patterns to rake

1. **Create domain modules**
   - [ ] `rake/domain/commands/` (8 hours)
     - Extract command logic from main.rs
     - `discover.rs`, `offer.rs`, `list.rs`, `health.rs`

2. **Implement discovery pipeline**
   - [ ] `rake/infra/discovery/udp.rs` implements DiscoveryQuery (2 hours)
   - [ ] Use DiscoveryPipeline in main.rs (2 hours)

3. **Refactor main.rs**
   - [ ] Command routing only (~200 lines) (2 hours)

**Deliverable:** rake follows same architecture as moss

---

### Phase 6: Lantern Completion (Week 6)

**Goal:** Complete lantern implementation

1. **Add SQLite persistence**
   - [ ] `lantern/infra/registry/sqlite.rs` (6 hours)
   - [ ] Implement PersistenceProvider trait (2 hours)
   - [ ] Migration from in-memory to SQLite (2 hours)

2. **Add JWT authentication**
   - [ ] `lantern/infra/auth/jwt.rs` (4 hours)
   - [ ] Implement AuthProvider trait (2 hours)
   - [ ] Add auth middleware to protected routes (2 hours)

3. **Implement SSE**
   - [ ] Election state change events (2 hours)
   - [ ] Registration events (2 hours)
   - [ ] `/api/v1/events/stream` endpoint (2 hours)

**Deliverable:** Lantern feature-complete

---

### Phase 7: Testing & Documentation (Week 7)

1. **Unit tests**
   - [ ] Test all evaluators (4 hours)
   - [ ] Test event bus (2 hours)
   - [ ] Test persistence layer (4 hours)
   - [ ] Test domain orchestrators (6 hours)

2. **Integration tests**
   - [ ] End-to-end service lifecycle (4 hours)
   - [ ] Discovery pipeline (2 hours)
   - [ ] SSE event flow (2 hours)

3. **Documentation**
   - [ ] Architecture diagram (2 hours)
   - [ ] Module responsibilities (2 hours)
   - [ ] Adding new features guide (2 hours)

**Deliverable:** >80% test coverage, clear documentation

---

## Success Metrics

### Code Quality Metrics

| Metric                 | Current      | Target                 |
| ---------------------- | ------------ | ---------------------- |
| moss/main.rs lines     | 3,976        | ≤ 200                  |
| Largest file           | 3,976        | ≤ 300                  |
| Functions > 50 lines   | ~15          | 0                      |
| Duplicated code blocks | 8            | 0                      |
| Unused code (YAGNI)    | ~15 variants | 0                      |
| Test coverage          | ~30%         | >80%                   |
| Module count           | 60           | 120 (smaller, focused) |

### Architecture Metrics

| Metric                 | Current | Target |
| ---------------------- | ------- | ------ |
| Circular dependencies  | 2       | 0      |
| Trait abstractions     | 0       | 8      |
| Event types            | 0       | 15+    |
| Event handlers         | 0       | 6+     |
| API handler avg lines  | 120     | <20    |
| Testable without mocks | 20%     | 90%    |

### Developer Experience Metrics

| Metric                        | Current | Target   |
| ----------------------------- | ------- | -------- |
| Time to understand module     | ~30 min | <5 min   |
| Time to add new feature       | ~2 days | ~4 hours |
| Time to add new event handler | N/A     | <30 min  |
| Time to swap storage backend  | ~1 week | <2 hours |
| Build time                    | ~45s    | <60s     |

---

## Risks & Mitigations

### Risk 1: Breaking Changes During Migration

**Mitigation:**

- ✅ **Full refactoring** - no feature flags needed (greenfield pre-production)
- ✅ **Delete deprecated code** immediately - clean slate for v0.1.0
- ✅ Migrate one concern at a time (persistence → events → discovery → jobs → cache)
- ✅ All changes happen in `dev` branch, merge to `main` only when complete

### Risk 2: Event Bus Overhead

**Concern:** Event-driven architecture adds latency

**Mitigation:**

- Use in-memory broadcast channels (zero-copy for multiple subscribers)
- Make event handlers non-blocking (spawn tasks for slow I/O)
- Benchmark: event publish should be <100µs

### Risk 3: Over-Abstraction

**Concern:** Too many traits make code hard to follow

**Mitigation:**

- Only create traits where multiple implementations exist or are planned
- Keep trait methods to 3-5 per trait
- Provide concrete implementation alongside trait definition

### Risk 4: Testing Complexity

**Concern:** Async code is hard to test

**Mitigation:**

- Use `tokio::test` for async tests
- Provide mock implementations of traits for testing
- Test event handlers independently (inject test event bus)

---

## Appendix A: File Size Reduction Forecast

### moss/src/main.rs

| Code Block           | Current Lines | New Location             | New Lines         |
| -------------------- | ------------- | ------------------------ | ----------------- |
| Configuration        | 183           | domain/config/mod.rs     | 150               |
| Compatibility        | 387           | domain/compat/ (5 files) | 250               |
| Job management       | 31            | (keep in main, tiny)     | 31                |
| Registry persistence | 32            | infra/persistence/       | 40                |
| API handlers         | 2,500         | api/v1/ (already split)  | 2,000             |
| Route definitions    | 400           | api/mod.rs               | 350               |
| Bootstrap            | -             | main.rs (new)            | 200               |
| **Total**            | **3,976**     |                          | **200** (main.rs) |

---

## Appendix B: Example Event Flow

### Scenario: User runs `garden-rake offer mongodb`

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. rake CLI                                                     │
│    - Parses command                                             │
│    - Discovers stone via DiscoveryPipeline                      │
│    - Sends POST /api/v1/services to moss                        │
└────────────────────┬────────────────────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────────────────────┐
│ 2. moss API Handler (thin)                                      │
│    - Extracts JSON                                              │
│    - Calls ServiceOrchestrator.create_service()                 │
└────────────────────┬────────────────────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────────────────────┐
│ 3. ServiceOrchestrator (domain)                                 │
│    - Validates request                                          │
│    - Loads template via TemplateProvider                        │
│    - Checks compat via CompatibilityPipeline                    │
│    - Emits: ServiceEvent::Installing                            │
│    - Calls DockerService.create_container()                     │
└────────────────────┬────────────────────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────────────────────┐
│ 4. DockerService (infra)                                        │
│    - Emits: ServiceEvent::Starting                              │
│    - Creates Docker container                                   │
│    - Emits: ServiceEvent::Started                               │
│    - Returns container_id                                       │
└────────────────────┬────────────────────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────────────────────┐
│ 5. ServiceOrchestrator (domain)                                 │
│    - Begins RegistryTransaction                                 │
│    - Adds service to registry                                   │
│    - Emits: RegistryEvent::ServiceAdded                         │
│    - Commits transaction                                        │
│    - Returns success to API handler                             │
└────────────────────┬────────────────────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────────────────────┐
│ 6. Event Handlers (parallel)                                    │
│                                                                  │
│    ConsoleHandler:                                              │
│    - Receives ServiceEvent::Starting                            │
│    - Prints "Starting MongoDB..."                               │
│    - Receives ServiceEvent::Started                             │
│    - Prints "✓ MongoDB started (container abc123)"             │
│                                                                  │
│    SSEHandler:                                                  │
│    - Receives ServiceEvent::Starting                            │
│    - Broadcasts to connected clients                            │
│    - Receives ServiceEvent::Started                             │
│    - Broadcasts to connected clients                            │
│                                                                  │
│    PersistenceHandler:                                          │
│    - Receives RegistryEvent::ServiceAdded                       │
│    - Triggers background registry sync (already done via txn)   │
│                                                                  │
│    MetricsHandler: (future)                                     │
│    - Receives ServiceEvent::Started                             │
│    - Updates Prometheus gauge: services_running++               │
└─────────────────────────────────────────────────────────────────┘
```

**Event Timeline:**

```
t=0ms   API request received
t=5ms   ServiceEvent::Installing → ConsoleHandler → "Installing..."
t=50ms  CompatibilityPipeline completes
t=51ms  ServiceEvent::Starting → ConsoleHandler → "Starting..."
t=51ms  ServiceEvent::Starting → SSEHandler → broadcast
t=200ms Docker container created
t=201ms ServiceEvent::Started → ConsoleHandler → "✓ Started"
t=201ms ServiceEvent::Started → SSEHandler → broadcast
t=205ms RegistryEvent::ServiceAdded → PersistenceHandler
t=210ms Transaction committed
t=210ms API response sent
```

**Key Benefits:**

- Console, SSE, and Metrics all react to the same events
- Adding new handler (e.g., webhook) requires zero changes to domain logic
- Easy to test: mock event bus, assert correct events were emitted

---

## Appendix C: Testing Examples

### Unit Test: Evaluator

```rust
// moss/src/domain/compat/evaluators/cpu.rs
#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn test_cpu_arch_mismatch() {
        let evaluator = CpuEvaluator;
        let rule = RuleCondition::CpuArch { value: "arm64".to_string() };
        let caps = Capabilities {
            cpu: CpuInfo { arch: "x86_64".to_string(), cores: 4 },
            // ... other fields
        };

        let result = evaluator.evaluate(&rule, &caps).await;

        assert!(matches!(result, Some(CompatibilityDecision::Incompatible { .. })));
    }

    #[tokio::test]
    async fn test_cpu_cores_sufficient() {
        let evaluator = CpuEvaluator;
        let rule = RuleCondition::CpuCores { min: 2 };
        let caps = Capabilities {
            cpu: CpuInfo { arch: "x86_64".to_string(), cores: 4 },
            // ...
        };

        let result = evaluator.evaluate(&rule, &caps).await;

        assert!(matches!(result, Some(CompatibilityDecision::Compatible)));
    }
}
```

### Integration Test: Event Flow

```rust
// moss/src/events/tests.rs
#[tokio::test]
async fn test_service_lifecycle_emits_events() {
    // Arrange
    let event_bus = Arc::new(InMemoryEventBus::new());
    let mut rx = event_bus.subscribe::<ServiceEvent>().await.unwrap();

    let docker_service = Arc::new(MockDockerService::new());
    let lifecycle = ServiceLifecycle::new(docker_service, event_bus.clone());

    // Act
    lifecycle.start_service("test-service").await.unwrap();

    // Assert
    let event1 = tokio::time::timeout(Duration::from_secs(1), rx.recv()).await.unwrap().unwrap();
    assert!(matches!(event1, ServiceEvent::Starting { service_name } if service_name == "test-service"));

    let event2 = tokio::time::timeout(Duration::from_secs(1), rx.recv()).await.unwrap().unwrap();
    assert!(matches!(event2, ServiceEvent::Started { service_name, .. } if service_name == "test-service"));
}
```

### Mock Implementation

```rust
// moss/src/domain/services/tests/mocks.rs
pub struct MockDockerService {
    calls: Arc<Mutex<Vec<String>>>,
}

impl MockDockerService {
    pub fn new() -> Self {
        Self { calls: Arc::new(Mutex::new(vec![])) }
    }

    pub async fn verify_called(&self, method: &str) -> bool {
        let calls = self.calls.lock().await;
        calls.iter().any(|c| c == method)
    }
}

#[async_trait]
impl DockerServiceTrait for MockDockerService {
    async fn start_container(&self, name: &str) -> Result<String> {
        let mut calls = self.calls.lock().await;
        calls.push(format!("start_container({})", name));
        Ok("mock-container-id".to_string())
    }
}
```

---

## Questions for Discussion

1. **Event Bus Implementation:** In-memory broadcast vs external (Redis Pub/Sub, NATS)?
   - **Recommendation:** Start in-memory, add external option later via trait

2. **Transaction Scope:** Should transactions span multiple registries (services + offerings)?
   - **Recommendation:** Yes, add multi-registry transaction support

3. **Middleware Order:** Auth → Logging → Handler or Logging → Auth → Handler?
   - **Recommendation:** Logging → Auth → Handler (log all attempts, even unauthorized)

4. **SSE Connection Limits:** How many concurrent SSE clients per stone?
   - **Recommendation:** 100 clients max (configurable), close oldest on overflow

5. **Event Retention:** Should event bus keep history for late subscribers?
   - **Recommendation:** No history (use event sourcing pattern if needed later)

6. **Testing Strategy:** Integration tests in same repo or separate test suite?
   - **Recommendation:** Same repo under `tests/` directory

7. **Migration Timing:** All at once or incremental releases?
   - **Recommendation:** All at once.

---

## Rake CLI: Complete API Mirror with Dual Syntax

### Overview

Rake must expose **every Moss API endpoint** as a CLI command with **dual syntax support**:

- **Zen syntax (primary)**: Poetic verbs with positional keywords (`at {stone}`, `quietly`, `until`)
- **Normative syntax (standard)**: Industry verbs with flags (`--at {stone}`, `-q`, `--until`)
- **Syntax follows vocabulary**: Zen verbs enforce zen syntax, normative verbs enforce flags
- **No mixing**: `offer --at stone-02` is rejected → must use `offer at stone-02`

### Complete API Coverage Matrix

| Moss Endpoint | Zen Syntax (Primary) | Normative Syntax (Standard) | Scope |
|---------------|---------------------|----------------------------|--------|
| **Services** ||||
| POST /api/v1/services | `offer <service>` | `services create <service>` | Stone |
| | `offer <service> at <stone>` | `services create <service> --at <stone>` | Stone |
| GET /api/v1/services | `observe` | `status` | Stone |
| | `observe at <stone>` | `status --at <stone>` | Stone |
| | `observe all` | `status --all` | Garden |
| GET /api/v1/services/:id | `touch <service>` | `services inspect <service>` | Stone |
| DELETE /api/v1/services/:id | `release <service>` | `services delete <service>` | Stone |
| POST /api/v1/services/:id/start | `wake <service>` | `services start <service>` | Stone |
| POST /api/v1/services/:id/stop | `rest <service>` | `services stop <service>` | Stone |
| POST /api/v1/services/:id/restart | `wake <service>` | `services restart <service>` | Stone |
| **Offerings** ||||
| GET /api/v1/offerings | `explore` | `list` | Stone |
| | `explore at <stone>` | `list --at <stone>` | Stone |
| GET /api/v1/offerings/:name | `touch offering <name>` | `offerings inspect <name>` | Stone |
| POST /api/v1/services (upgrade) | `nourish <service>` | `services update <service>` | Stone |
| | `nourish --all` | `services update --all` | Garden |
| **Jobs** ||||
| GET /api/v1/jobs/:id | `observe job <id>` | `jobs status <id>` | Stone |
| DELETE /api/v1/jobs/:id | `release job <id>` | `jobs cancel <id>` | Stone |
| GET /api/v1/jobs | `observe jobs` | `jobs list` | Stone |
| | `observe jobs all` | `jobs list --all` | Garden |
| GET /api/v1/jobs/:id/logs | `watch job <id>` | `jobs logs <id>` | Stone |
| | `watch job <id> until <pattern>` | `jobs logs <id> --until <pattern>` | Stone |
| **Health** ||||
| GET /health | `observe` | `status` | Stone |
| | `observe all` | `status --all` | Garden |
| GET /api/v1/capabilities | `touch capabilities` | `capabilities show` | Stone |
| GET /api/v1/metrics | `observe metrics` | `metrics show` | Stone |
| **Discovery** ||||
| UDP discovery | `discover` | `discover` | Garden |
| Topology view | `garden` | `topology` | Garden |
| | `garden watch` | `topology watch` | Garden |
| List stones | `garden stones` | `stones list` | Garden |
| **Context** ||||
| Set active stone | `tend <stone>` | `context set <stone>` | - |
| Clear active | `untend` | `context clear` | - |
| Show active | `tended` | `context show` | - |

**Syntax Rules:**

**Zen (positional keywords):**
```bash
garden-rake offer mongodb              # → tended stone
garden-rake offer mongodb at stone-02  # → specific stone
garden-rake rest grafana at stone-02 quietly
garden-rake watch mongodb until 'ready'
garden-rake observe all                # → garden-scope
```

**Normative (flags):**
```bash
garden-rake services create mongodb              # → tended stone
garden-rake services create mongodb --at stone-02  # → specific stone
garden-rake services stop grafana --at stone-02 -q
garden-rake jobs logs mongodb --until 'ready'
garden-rake status --all                         # → garden-scope
```

**Mixing prevention:**
```bash
# ✗ Zen verb with flags
$ garden-rake offer mongodb --at stone-02
Error: Zen commands use natural syntax. Try: garden-rake offer mongodb at stone-02

# ✗ Normative verb with positional
$ garden-rake services create mongodb at stone-02
Error: Standard commands use flags. Try: garden-rake services create mongodb --at stone-02
```

### Discovery Priority Chain

```rust
// rake/src/domain/commands/mod.rs
pub async fn resolve_target_stone(cli: &Cli, cache: Arc<CacheManager>) -> Result<StoneEndpoint> {
    // 1. --at flag (highest priority)
    if let Some(at) = &cli.at {
        return Ok(StoneEndpoint::from_explicit(at));
    }

    // 2. GARDEN_STONE environment variable
    if let Ok(stone) = std::env::var("GARDEN_STONE") {
        return Ok(StoneEndpoint::from_env(stone));
    }

    // 3. Tending state (cached stone, 90s TTL)
    if let Some(tending) = TendingState::load()? {
        if tending.is_valid() {
            return Ok(StoneEndpoint::from_tending(tending));
        }
    }

    // 4. UDP discovery (fallback)
    let discovery = UdpDiscovery::new(7184);
    let stones = discovery.discover(Duration::from_secs(3)).await?;
    let stone = stones.first().ok_or_else(|| anyhow!("No stones found"))?;

    Ok(StoneEndpoint::from_discovery(stone))
}
```

**Benefits:**

- ✅ **Complete API parity** - Every Moss endpoint has a Rake command
- ✅ **Scope flexibility** - Stone-scope (tended or `at {stone}`) and Garden-scope (`--all`)
- ✅ **Discovery pipeline** - `--at` → `GARDEN_STONE` → tending cache → UDP
- ✅ **SSE streaming** - Real-time job progress via `rake jobs logs --follow`
- ✅ **Cache integration** - Garden-scope commands use hot-cache for sub-ms responses

---

## Background Workers & Ceremony Workflows

### Overview

The job pipeline enables complex ceremony workflows (vacate, transfer, nourish) by chaining async tasks and delegating to background workers.

### Ceremony Example: Vacate Stone

**Scenario:** User runs `garden-rake vacate stone-01` to gracefully remove a stone from the garden.

**Workflow:**

1. **Submit vacate job** → Returns job ID immediately
2. **Background worker executes steps:**
   - Step 1: Inventory services on stone-01
   - Step 2: For each service, find alternative stone
   - Step 3: Transfer service (pull image → create container → start → verify)
   - Step 4: Stop service on stone-01
   - Step 5: Update topology cache
   - Step 6: Mark stone-01 as vacant
3. **Progress tracking** → SSE events for each step
4. **Job completion** → stone-01 can be safely powered off

### Task Chaining with JobExecutor

**Location:** `moss/src/domain/ceremonies/vacate_job.rs`

```rust
pub struct VacateStoneJob {
    service_orchestrator: Arc<ServiceOrchestrator>,
    topology_cache: Arc<RwLock<TopologyCache>>,
    job_manager: Arc<JobManager>,  // For chaining sub-jobs
}

#[async_trait]
impl JobExecutor for VacateStoneJob {
    fn job_type(&self) -> &'static str {
        "vacate_stone"
    }

    fn timeout(&self) -> Option<Duration> {
        Some(Duration::from_secs(1800)) // 30 minutes
    }

    async fn execute(
        &self,
        input: serde_json::Value,
        checkpoint: Option<serde_json::Value>,
        progress_tx: Sender<f32>,
    ) -> JobResult {
        let req: VacateStoneRequest = serde_json::from_value(input)?;

        // Step 1: Inventory services (0% → 10%)
        progress_tx.send(0.0).await.ok();
        let services = self.get_services_on_stone(&req.stone_id).await?;
        let total_services = services.len();
        progress_tx.send(0.1).await.ok();

        // Step 2-5: Transfer each service (10% → 90%)
        for (i, service) in services.iter().enumerate() {
            let service_progress = 0.1 + (i as f32 / total_services as f32) * 0.8;
            progress_tx.send(service_progress).await.ok();

            // Submit transfer job (sub-job)
            let transfer_job_id = self.job_manager.submit(
                "transfer_service",
                serde_json::json!({
                    "service_id": service.id,
                    "from_stone": req.stone_id,
                    "to_stone": self.find_alternative_stone(service).await?,
                }),
            ).await?;

            // Wait for transfer job to complete
            self.wait_for_job(transfer_job_id).await?;
        }

        // Step 6: Mark stone as vacant (90% → 100%)
        progress_tx.send(0.9).await.ok();
        self.mark_stone_vacant(&req.stone_id).await?;
        progress_tx.send(1.0).await.ok();

        Ok(serde_json::json!({
            "stone_id": req.stone_id,
            "services_transferred": total_services,
        }))
    }
}
```

### Delegation to Background Workers

**Pattern:** Long-running tasks spawn tokio tasks, emit events for coordination

```rust
// moss/src/domain/services/transfer_job.rs
pub struct TransferServiceJob {
    docker: Arc<DockerService>,
    template_loader: Arc<dyn TemplateProvider>,
    event_bus: Arc<dyn EventBus>,
}

#[async_trait]
impl JobExecutor for TransferServiceJob {
    async fn execute(&self, input: serde_json::Value, ...) -> JobResult {
        let req: TransferServiceRequest = serde_json::from_value(input)?;

        // Pull image on new stone (background task)
        progress_tx.send(0.2).await.ok();
        let pull_handle = tokio::spawn({
            let docker = self.docker.clone();
            let image = req.image.clone();
            async move {
                docker.pull_image(&image, |_| {}).await
            }
        });

        // Wait for pull to complete
        pull_handle.await??;

        // Create + start container
        progress_tx.send(0.6).await.ok();
        let container_id = self.docker.create_container(&req.service_name, &template).await?;

        progress_tx.send(0.8).await.ok();
        self.docker.start_container(&container_id).await?;

        // Verify health before proceeding
        progress_tx.send(0.9).await.ok();
        self.wait_for_healthy(&container_id).await?;

        // Emit event for coordination
        self.event_bus.publish(ServiceEvent::Transferred {
            service_name: req.service_name.clone(),
            from_stone: req.from_stone,
            to_stone: req.to_stone,
        }).await?;

        progress_tx.send(1.0).await.ok();

        Ok(serde_json::json!({
            "service_name": req.service_name,
            "container_id": container_id,
        }))
    }
}
```

### Benefits of Background Worker Pattern

- ✅ **Non-blocking API** - Returns job ID immediately, user polls/subscribes
- ✅ **Restart capability** - Jobs resume from checkpoints on crash
- ✅ **Event coordination** - Multiple workers coordinate via events
- ✅ **Progress tracking** - Real-time updates via SSE
- ✅ **Task chaining** - Parent jobs spawn child jobs
- ✅ **Resource efficiency** - Workers run in tokio runtime, no OS threads
- ✅ **Ceremony support** - Complex multi-step workflows (vacate, transfer, nourish)

---

## Next Steps

1. **Review this proposal** - Gather feedback from project maintainer
2. **Approve migration plan** - Confirm 7-week timeline or adjust
3. **Begin Phase 1** - Foundation (Week 1): traits, events, utilities
4. **Full refactoring** - Delete deprecated code as we go (clean slate)
5. **Weekly progress checks** - Demo completed phases
6. **Release v0.1.0** - After Phase 7 completion (7 weeks)

**Migration Philosophy:**

- ✅ Greenfield refactoring - no backwards compatibility needed
- ✅ Common-first architecture - move all shared code to `common/`
- ✅ Async job pipeline - long tasks return job IDs
- ✅ Hot-cache everything - topology, manifests, capabilities
- ✅ Event-driven coordination - background workers use events
- ✅ Full test coverage - >80% target before v0.1.0

---

**Document Status:** DRAFT - Ready for Review
**Author:** Claude (AI Assistant)
**Review Requested From:** Project Maintainer
**Target Approval Date:** 2026-01-27
**Estimated Effort:** 7 weeks (280 hours)
**Impact:** moss: 12,000 → 800 LOC, common: 1,500 → 3,500 LOC, total: -50% duplication
