//! Legacy helper functions
//!
//! Temporary compatibility layer for functions extracted from main.rs.
//! These will be gradually migrated to proper modules in domain/ or infra/.

use crate::AppState;
use anyhow::Result;
use axum::{
    http::StatusCode,
    response::sse::{Event, KeepAlive, Sse},
    Json,
};
use futures_util::Stream;
use std::collections::HashMap;
use std::convert::Infallible;
use serde::{Deserialize, Serialize};

// ============================================================================
// Types
// ============================================================================

/// Legacy Job type (will be replaced with garden_common::jobs::Job)
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Job {
    pub id: String,
    pub offerings: Vec<String>,
    pub status: JobStatus,
    pub completed: Vec<String>,
    pub failed: HashMap<String, String>,
    pub started_at: std::time::SystemTime,
    pub completed_at: Option<std::time::SystemTime>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum JobStatus {
    Pending,
    Running,
    Completed,
    Failed,
}

#[derive(Debug, Deserialize)]
pub struct ReconcileRequest {
    pub scope: Option<String>,
    #[serde(default)]
    pub drop_invalid: bool,
}

#[derive(Debug, Deserialize)]
pub struct RefreshPayload {
    pub component: String,
    pub binary_data: String,
}

// ============================================================================
// Offering Index Functions
// ============================================================================

/// Ensure offerings index is loaded
pub async fn ensure_offerings_index(_state: &AppState, _force_rebuild: bool) -> Result<()> {
    // TODO: Implement using template_manager
    // For now, no-op
    Ok(())
}

/// Get a compiled offering by name
pub async fn get_compiled_offering(
    _state: &AppState,
    _offering: &str,
) -> Result<Option<serde_json::Value>> {
    // TODO: Implement using template_manager
    Ok(None)
}

// ============================================================================
// Service Installation Functions
// ============================================================================

/// Install a service (legacy version)
pub async fn install_service_task(
    _state: AppState,
    _service_name: String,
    _offering_name: String,
) -> Result<()> {
    // TODO: Use JobManager and InstallServiceExecutor instead
    Ok(())
}

/// Adopt an existing offering container
pub async fn adopt_offering_container(
    _state: &AppState,
    _service_name: &str,
) -> Result<Option<crate::ServiceInfo>> {
    // TODO: Implement container adoption logic
    Ok(None)
}

// ============================================================================
// Registry Persistence Functions
// ============================================================================

/// Persist registry state to disk (legacy)
pub async fn persist_registry_state(
    _state: &AppState,
) -> Result<()> {
    // Registry now handles its own persistence
    Ok(())
}

// ============================================================================
// Log Streaming Functions
// ============================================================================

/// Stream logs for a service
pub async fn stream_logs(
    _service: String,
    _state: AppState,
) -> Result<Sse<impl Stream<Item = Result<Event, Infallible>>>, (StatusCode, Json<crate::ApiError>)> {
    // TODO: Implement log streaming
    use async_stream::stream;
    let log_stream = stream! {
        // Empty stream for now - yield placeholder event to satisfy type requirements
        yield Ok(Event::default().data("Log streaming not yet implemented"));
    };
    Ok(Sse::new(log_stream).keep_alive(KeepAlive::default()))
}

// ============================================================================
// Reconciliation Functions
// ============================================================================

/// Trigger service reconciliation
pub async fn reconcile_now(
    _state: &AppState,
    _request: ReconcileRequest,
) -> Result<String> {
    // TODO: Implement reconciliation logic
    Ok("Reconciliation triggered".into())
}

// ============================================================================
// Admin Functions
// ============================================================================

/// Shutdown the daemon
pub async fn admin_shutdown(_state: &AppState) -> Result<()> {
    // TODO: Implement graceful shutdown
    tracing::warn!("Shutdown requested but not yet implemented");
    Ok(())
}

/// Refresh a component
pub async fn refresh_component(
    _state: &AppState,
    _payload: RefreshPayload,
) -> Result<String> {
    // TODO: Implement component refresh
    Ok("Component refreshed".into())
}
