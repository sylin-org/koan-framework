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
    /// Unique stone identifier (GUID v7, immutable once generated)
    pub stone_id: String,

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

    /// Topology cache for discovered stones (in-memory only)
    pub topology_cache: crate::domain::topology::TopologyCache,

    /// Self topology entry (this stone's current state)
    pub self_entry: Arc<RwLock<crate::domain::TopologyEntry>>,
}

impl AppState {
    /// Get stone ID (GUID v7)
    pub fn stone_id(&self) -> &str {
        &self.stone_id
    }

    /// Get stone name
    pub fn stone_name(&self) -> &str {
        &self.stone_name
    }

    /// Persist registry to disk
    ///
    /// Reads the current registry and saves to disk atomically.
    pub async fn persist_registry(&self) -> anyhow::Result<()> {
        let registry = self.registry.read().await;
        crate::infra::save_registry_vec(&registry).await
    }
    
    /// Sync self_entry services from registry
    /// 
    /// Converts ServiceInfo → TopologyServiceEntry and updates self_entry.
    /// Optionally triggers immediate chirp announcement.
    /// Called after any registry modification.
    pub async fn sync_self_services(&self, auto_chirp: bool) {
        let registry = self.registry.read().await;
        let topology_services = garden_common::TopologyServiceEntry::from_service_infos(&registry);
        
        {
            let mut entry = self.self_entry.write().await;
            entry.services = topology_services;
            entry.last_seen = chrono::Utc::now();
        }
        
        tracing::debug!(count = registry.len(), "Synced self_entry services from registry");
        
        if auto_chirp {
            let entry = self.self_entry.read().await.clone();
            if let Err(e) = crate::announcement::announce(&entry).await {
                tracing::warn!(error = ?e, "Failed to auto-chirp after service sync");
            }
        }
    }
    
    /// Add or update a single service in registry and self_entry
    /// 
    /// Immediately syncs to self_entry and triggers chirp.
    /// This is the primary method for service state changes.
    pub async fn upsert_service(&self, service: ServiceInfo, auto_chirp: bool) {
        {
            let mut registry = self.registry.write().await;
            if let Some(pos) = registry.iter().position(|s| s.name == service.name) {
                registry[pos] = service;
            } else {
                registry.push(service);
            }
        }
        
        self.sync_self_services(auto_chirp).await;
        
        if let Err(e) = self.persist_registry().await {
            tracing::error!(error = ?e, "Failed to persist registry after upsert");
        }
    }
    
    /// Remove a service from registry and self_entry
    /// 
    /// Immediately syncs to self_entry and triggers chirp.
    pub async fn remove_service(&self, service_name: &str, auto_chirp: bool) {
        {
            let mut registry = self.registry.write().await;
            registry.retain(|s| s.name != service_name);
        }
        
        self.sync_self_services(auto_chirp).await;
        
        if let Err(e) = self.persist_registry().await {
            tracing::error!(error = ?e, "Failed to persist registry after removal");
        }
    }
    
    /// Batch update services (for reconciliation/adoption)
    /// 
    /// Replaces entire registry and triggers chirp.
    pub async fn replace_services(&self, services: Vec<ServiceInfo>, auto_chirp: bool) {
        {
            let mut registry = self.registry.write().await;
            *registry = services;
        }
        
        self.sync_self_services(auto_chirp).await;
        
        if let Err(e) = self.persist_registry().await {
            tracing::error!(error = ?e, "Failed to persist registry after batch update");
        }
    }
    
    /// Get snapshot of services (read-only)
    pub async fn get_services(&self) -> Vec<ServiceInfo> {
        self.registry.read().await.clone()
    }
}
