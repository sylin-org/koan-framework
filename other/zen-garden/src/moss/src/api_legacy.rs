//! Legacy API helpers
//!
//! Compatibility shims for existing API handlers.
//! These will be removed as handlers are migrated to use common/api_utils.

use axum::{http::StatusCode, Json};
use garden_common::api_utils::ApiErrorResponse;
use std::collections::HashMap;

/// Legacy error response type
pub type ApiError = ApiErrorResponse;

/// Create an error response (legacy helper matching main.rs signature)
pub fn error_response(
    status_code: StatusCode,
    error_code: impl Into<String>,
    message: impl Into<String>,
    details: Option<HashMap<String, serde_json::Value>>,
) -> (StatusCode, Json<ApiError>) {
    let response = if let Some(details) = details {
        ApiErrorResponse::with_details(error_code, message, details)
    } else {
        ApiErrorResponse::new(error_code, message)
    };
    (status_code, Json(response))
}

/// Persist registry to disk.
///
/// Converts HashMap to Vec for JSON serialization and persists atomically.
pub async fn persist_registry_to_disk(registry: &std::collections::HashMap<String, garden_common::ServiceInfo>) -> anyhow::Result<()> {
    let dir = std::path::PathBuf::from(garden_common::names::CONFIG_DIR);
    let path = dir.join("moss-registry.json");
    tokio::fs::create_dir_all(&dir).await?;

    // Convert HashMap to Vec for serialization
    let services: Vec<_> = registry.values().cloned().collect();
    let tmp_path = path.with_extension("json.tmp");
    let content = serde_json::to_string_pretty(&services)?;
    tokio::fs::write(&tmp_path, content).await?;

    match tokio::fs::rename(&tmp_path, &path).await {
        Ok(_) => Ok(()),
        Err(e) => {
            // Windows won't rename over an existing file
            if cfg!(windows) {
                let _ = tokio::fs::remove_file(&path).await;
                tokio::fs::rename(&tmp_path, &path).await?;
                Ok(())
            } else {
                Err(e.into())
            }
        }
    }
}

// Re-export error codes from common
pub use garden_common::error_codes;
