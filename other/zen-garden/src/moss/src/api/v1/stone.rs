// Stone Operations API
//
// Purpose: System-level operations on the stone itself
// Custom actions using single-colon format: :upgrade, :shutdown

use axum::{
    extract::{Json, State},
    http::StatusCode,
};
use serde::Deserialize;

use crate::AppState;

/// POST /api/v1/stone:upgrade
/// Upgrade stone software (moss/rake binaries)
#[derive(Debug, Deserialize)]
pub struct UpgradeRequest {
    pub component: String,
    pub binary_data: String, // base64-encoded
}

pub async fn upgrade_stone_v1(
    State(state): State<AppState>,
    Json(payload): Json<UpgradeRequest>,
) -> (StatusCode, Json<serde_json::Value>) {
    tracing::info!(
        component = %payload.component,
        binary_size = payload.binary_data.len(),
        "Upgrade request received at /api/v1/stone/upgrade"
    );
    
    // This is the same as the old /api/system/refresh endpoint
    // Forward to existing implementation
    crate::refresh_component(
        State(state),
        Json(crate::RefreshPayload {
            component: payload.component,
            binary_data: payload.binary_data,
        }),
    )
    .await
}

/// POST /api/v1/stone:shutdown
/// Gracefully shutdown the Moss daemon
pub async fn shutdown_stone_v1(
    State(state): State<AppState>,
) -> (StatusCode, Json<serde_json::Value>) {
    // This is the same as the old /admin/shutdown endpoint
    crate::admin_shutdown(State(state)).await
}
