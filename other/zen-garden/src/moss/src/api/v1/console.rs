use axum::{
    extract::State,
    http::StatusCode,
    Json,
};
use serde::{Deserialize, Serialize};
use crate::{AppState, console::ConsoleMode, error_response};
use garden_common::api_utils::ApiErrorResponse;

#[derive(Debug, Deserialize)]
pub struct ConsoleModeRequest {
    pub mode: String,
    #[serde(default)]
    pub persist: bool,
    #[serde(default = "default_timeout")]
    pub timeout_minutes: u64,
}

fn default_timeout() -> u64 {
    30
}

#[derive(Debug, Serialize)]
pub struct ConsoleModeResponse {
    pub mode: String,
    pub previous_mode: String,
    pub timeout_minutes: Option<u64>,
    pub persisted: bool,
}

/// POST /api/v1/console/mode - Change console output verbosity
pub async fn set_console_mode_v1(
    State(state): State<AppState>,
    Json(request): Json<ConsoleModeRequest>,
) -> Result<(StatusCode, Json<ConsoleModeResponse>), (StatusCode, Json<ApiErrorResponse>)> {
    // Parse requested mode
    let new_mode: ConsoleMode = request.mode.parse().map_err(|_| {
        error_response(
            StatusCode::BAD_REQUEST,
            "INVALID_MODE",
            format!(
                "Invalid console mode '{}'. Valid modes: silent, minimal, informative, verbose",
                request.mode
            ),
            None,
        )
    })?;

    // Get previous mode
    let previous_mode = state.console.get_mode();

    // Set new mode
    state.console.set_mode(new_mode);

    // Emit mode change event
    state.console.emit(crate::console::ConsoleEvent::new(
        crate::console::EventCategory::Ops,
        crate::console::EventStatus::Active,
        format!("Console mode: {} → {}", previous_mode, new_mode)
    ));

    // Spawn timeout task if requested and not forever
    let timeout_opt = if request.timeout_minutes > 0 {
        let state_clone = state.clone();
        let original_mode = previous_mode;
        let timeout_minutes = request.timeout_minutes;
        
        tokio::spawn(async move {
            tokio::time::sleep(tokio::time::Duration::from_secs(timeout_minutes * 60)).await;
            
            // Revert to original mode
            state_clone.console.set_mode(original_mode);
            state_clone.console.emit(crate::console::ConsoleEvent::new(
                crate::console::EventCategory::Ops,
                crate::console::EventStatus::Active,
                format!("Console mode timeout: {} → {}", new_mode, original_mode)
            ));
        });
        
        Some(timeout_minutes)
    } else {
        None
    };

    // Persist to moss-config.toml if requested
    let persisted = if request.persist {
        match persist_console_mode(&state, new_mode).await {
            Ok(_) => {
                state.console.emit(crate::console::ConsoleEvent::new(
                    crate::console::EventCategory::Config,
                    crate::console::EventStatus::Saving,
                    format!("Console mode saved: {}", new_mode)
                ));
                true
            },
            Err(e) => {
                tracing::warn!(error = ?e, "Failed to persist console mode");
                state.console.emit(crate::console::ConsoleEvent::new(
                    crate::console::EventCategory::Config,
                    crate::console::EventStatus::SaveError,
                    format!("Failed to save console mode: {}", e)
                ));
                false
            }
        }
    } else {
        false
    };

    Ok((
        StatusCode::OK,
        Json(ConsoleModeResponse {
            mode: new_mode.to_string(),
            previous_mode: previous_mode.to_string(),
            timeout_minutes: timeout_opt,
            persisted,
        }),
    ))
}

/// GET /api/v1/console/mode - Get current console mode
pub async fn get_console_mode_v1(
    State(state): State<AppState>,
) -> Result<(StatusCode, Json<serde_json::Value>), (StatusCode, Json<ApiErrorResponse>)> {
    let current_mode = state.console.get_mode();
    
    Ok((
        StatusCode::OK,
        Json(serde_json::json!({
            "mode": current_mode.to_string(),
        })),
    ))
}

/// Helper function to persist console mode to config file
async fn persist_console_mode(
    _state: &AppState,
    mode: ConsoleMode,
) -> Result<(), Box<dyn std::error::Error>> {
    use std::path::PathBuf;
    
    let config_path = if cfg!(windows) {
        PathBuf::from(format!("./{}", garden_common::names::MOSS_CONFIG))
    } else {
        PathBuf::from(format!("{}/{}", garden_common::names::CONFIG_DIR, garden_common::names::MOSS_CONFIG))
    };

    // Read existing config or create new one
    let mut config: serde_json::Value = if config_path.exists() {
        let content = tokio::fs::read_to_string(&config_path).await?;
        toml::from_str(&content)?
    } else {
        serde_json::json!({})
    };

    // Update console_mode field
    config["console_mode"] = serde_json::Value::String(mode.to_string());

    // Convert back to TOML and write
    let toml_content = toml::to_string_pretty(&config)?;
    tokio::fs::write(&config_path, toml_content).await?;

    Ok(())
}
