use axum::{
    extract::{Path, State},
    http::{HeaderMap, StatusCode},
    Json,
};
use crate::{error_response, AppState};
use crate::api::responses::ApiResponse;
use garden_common::api_utils::ApiErrorResponse;
use serde::{Deserialize, Serialize};

#[derive(Deserialize)]
pub struct PondInitRequest {
    // Future: passphrase field for encryption
}

#[derive(Serialize)]
pub struct PondInitResponse {
    pub cornerstone: String,
    pub keystone_path: String,
    pub certificate_expires: String,
    pub status: String,
    pub note: String,
}

#[derive(Serialize)]
pub struct PondInviteResponse {
    pub code: String,
    pub expires_at: String,
    pub ttl_seconds: u64,
    pub inviter_stone: String,
}

#[derive(Deserialize)]
pub struct PondJoinRequest {
    // Future: code field for join invitation
}

#[derive(Serialize)]
pub struct PondJoinResponse {
    pub stone_name: String,
    pub cornerstone: String,
    pub certificate_expires: String,
    pub status: String,
}

#[derive(Serialize)]
pub struct PondStatusResponse {
    pub active: bool,
    pub cornerstone: Option<String>,
    pub stones: Vec<PondStoneInfo>,
    pub tier: String,
    pub note: String,
}

#[derive(Serialize)]
pub struct PondStoneInfo {
    pub name: String,
    pub is_cornerstone: bool,
    pub certificate_expires: Option<String>,
    pub joined_at: String,
}

/// POST /api/v1/pond/init - Initialize pond security
pub async fn pond_init_v1(
    State(_state): State<AppState>,
    _headers: HeaderMap,
    Json(_payload): Json<PondInitRequest>,
) -> Result<Json<ApiResponse<PondInitResponse>>, (StatusCode, Json<ApiErrorResponse>)> {
    Err(error_response(
        StatusCode::NOT_IMPLEMENTED,
        "POND_NOT_IMPLEMENTED",
        "Pond security implementation pending (Phase 3b - cryptographic implementation)".to_string(),
        Some(std::collections::HashMap::from([(
            "phase".to_string(),
            serde_json::json!("3b"),
        ), (
            "feature".to_string(),
            serde_json::json!("pond-security"),
        )])),
    ))
}

/// DELETE /api/v1/pond - Remove pond from all stones
pub async fn pond_remove_v1(
    State(_state): State<AppState>,
    _headers: HeaderMap,
) -> Result<Json<ApiResponse<serde_json::Value>>, (StatusCode, Json<ApiErrorResponse>)> {
    Err(error_response(
        StatusCode::NOT_IMPLEMENTED,
        "POND_NOT_IMPLEMENTED",
        "Pond security implementation pending (Phase 3b)".to_string(),
        None,
    ))
}

/// POST /api/v1/pond/invite - Generate TOTP invitation code
pub async fn pond_invite_v1(
    State(_state): State<AppState>,
    _headers: HeaderMap,
) -> Result<Json<ApiResponse<PondInviteResponse>>, (StatusCode, Json<ApiErrorResponse>)> {
    Err(error_response(
        StatusCode::NOT_IMPLEMENTED,
        "POND_NOT_IMPLEMENTED",
        "Pond security implementation pending (Phase 3b)".to_string(),
        None,
    ))
}

/// POST /api/v1/pond/join - Join pond with invitation code
pub async fn pond_join_v1(
    State(_state): State<AppState>,
    _headers: HeaderMap,
    Json(_payload): Json<PondJoinRequest>,
) -> Result<Json<ApiResponse<PondJoinResponse>>, (StatusCode, Json<ApiErrorResponse>)> {
    Err(error_response(
        StatusCode::NOT_IMPLEMENTED,
        "POND_NOT_IMPLEMENTED",
        "Pond security implementation pending (Phase 3b)".to_string(),
        None,
    ))
}

/// DELETE /api/v1/pond/stones/:stone_name - Remove stone from pond
pub async fn pond_untrust_v1(
    State(_state): State<AppState>,
    Path(_stone_name): Path<String>,
    _headers: HeaderMap,
) -> Result<Json<ApiResponse<serde_json::Value>>, (StatusCode, Json<ApiErrorResponse>)> {
    Err(error_response(
        StatusCode::NOT_IMPLEMENTED,
        "POND_NOT_IMPLEMENTED",
        "Pond security implementation pending (Phase 3b)".to_string(),
        None,
    ))
}

/// GET /api/v1/pond/status - Get pond status and membership
pub async fn pond_status_v1(
    State(_state): State<AppState>,
    _headers: HeaderMap,
) -> Result<Json<ApiResponse<PondStatusResponse>>, (StatusCode, Json<ApiErrorResponse>)> {
    // Return inactive status (pond not implemented yet)
    let response = PondStatusResponse {
        active: false,
        cornerstone: None,
        stones: vec![],
        tier: "garden-pond".to_string(),
        note: "Pond security not initialized. Run 'garden-rake place keystone' to secure your garden.".to_string(),
    };

    Ok(Json(ApiResponse::new(response)))
}
