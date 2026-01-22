//! API helper utilities
//!
//! Common utilities for HTTP API handlers including error response creation.

use axum::{http::StatusCode, Json};
use garden_common::api_utils::ApiErrorResponse;
use std::collections::HashMap;

/// Create an error response for API handlers
///
/// This is a convenience helper that wraps ApiErrorResponse creation
/// with the common (StatusCode, Json<ApiErrorResponse>) return type.
pub fn error_response(
    status_code: StatusCode,
    error_code: impl Into<String>,
    message: impl Into<String>,
    details: Option<HashMap<String, serde_json::Value>>,
) -> (StatusCode, Json<ApiErrorResponse>) {
    let response = if let Some(details) = details {
        ApiErrorResponse::with_details(error_code, message, details)
    } else {
        ApiErrorResponse::new(error_code, message)
    };
    (status_code, Json(response))
}

// Re-export error codes from common for convenience
pub use garden_common::error_codes;
