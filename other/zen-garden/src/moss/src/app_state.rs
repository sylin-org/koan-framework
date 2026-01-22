//! Application state shared across HTTP handlers
//!
//! Holds all dependencies for moss daemon:
//! - Service registry (Vec<ServiceInfo>)
//! - Docker manager
//! - Template loader
//! - Job tracking
//! - Event broadcasting
//! - Hardware capabilities cache
//! - Console printer
//!
//! This is the unified AppState used by both main.rs and all API handlers.

use crate::docker::DockerManager;
use crate::templates::TemplateLoader;
use crate::console::ConsolePrinter;
use crate::tasks::NetworkMonitor;
use garden_common::{HardwareCapabilities, ServiceInfo};
use std::collections::HashMap;
use std::sync::Arc;
use std::time::Instant;
use tokio::sync::RwLock;

/// SSE event for client notifications
#[derive(Clone, Debug, serde::Serialize)]
pub struct MossEvent {
    pub timestamp: String,
    pub level: String,
    pub message: String,
    pub job_id: Option<String>,
}

/// Job execution status
#[derive(Clone, Debug, serde::Serialize, serde::Deserialize)]
pub enum JobStatus {
    Pending,
    Running,
    Completed,
    Failed,
}

/// Background job for tracking long-running operations
#[derive(Clone, Debug, serde::Serialize)]
pub struct Job {
    pub id: String,
    pub offerings: Vec<String>,
    pub status: JobStatus,
    pub completed: Vec<String>,
    pub failed: HashMap<String, String>, // service -> error message
    pub started_at: std::time::SystemTime,
    pub completed_at: Option<std::time::SystemTime>,
}

// Offerings types moved to domain/offerings.rs
pub use crate::domain::{
    CompiledOffering, OfferingsFingerprint, OfferingsIndexCache,
};

// Offering modes types
pub use garden_common::{
    AdoptedOfferingInfo, BorrowedOfferingInfo, OfferingMode,
};
pub use garden_common::manifests::OfferingManifest;

/// Application state for HTTP handlers
///
/// This is the central dependency injection container for moss.
/// All fields are wrapped in Arc for cheap cloning across tasks.
#[derive(Clone)]
pub struct AppState {
    /// Stone identity (e.g., "stone-01", hostname)
    pub stone_name: String,

    /// Service registry (persisted to disk)
    /// Vec format for compatibility with existing persistence layer
    pub registry: Arc<RwLock<Vec<ServiceInfo>>>,

    /// Adopted offerings registry (native/existing services)
    pub adopted_offerings: Arc<RwLock<Vec<AdoptedOfferingInfo>>>,

    /// Borrowed offerings registry (external network services)
    pub borrowed_offerings: Arc<RwLock<Vec<BorrowedOfferingInfo>>>,

    /// Offering manifests (loaded from templates directory)
    pub manifests: Arc<RwLock<Vec<OfferingManifest>>>,

    /// Docker daemon manager
    pub docker: Arc<DockerManager>,

    /// Template loader for service manifests
    pub templates: Arc<TemplateLoader>,

    /// Background job tracker
    pub jobs: Arc<RwLock<HashMap<String, Job>>>,

    /// Event broadcast channel for SSE streaming
    pub event_tx: tokio::sync::broadcast::Sender<MossEvent>,

    /// Shutdown coordination channel
    pub shutdown_tx: Arc<tokio::sync::Notify>,

    /// Daemon start time (for uptime calculation)
    pub start_time: Instant,

    /// Compiled offerings index (with compatibility checks)
    pub offerings_index: Arc<RwLock<Option<OfferingsIndexCache>>>,

    /// Console event printer (for tty/systemd/verbose modes)
    pub console: Arc<ConsolePrinter>,

    /// Hardware capabilities cache (detected at startup, cached to disk)
    pub capabilities: Arc<RwLock<Option<HardwareCapabilities>>>,

    /// Network monitor for IP change detection
    pub network_monitor: Arc<NetworkMonitor>,

    /// API port for constructing endpoint URLs
    pub api_port: u16,
}

impl AppState {
    /// Get stone name
    pub fn stone_name(&self) -> &str {
        &self.stone_name
    }
}
