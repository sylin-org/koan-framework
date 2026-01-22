//! Event streaming API endpoints
//!
//! Provides Server-Sent Events (SSE) for real-time notifications:
//! - Job progress updates
//! - System events
//! - Error notifications
//!
//! Events are broadcast through a tokio channel with automatic backpressure handling.

use axum::{
    extract::State,
    response::sse::{Event, KeepAlive, Sse},
};
use futures_util::stream::Stream;
use std::convert::Infallible;
use tokio_stream::wrappers::BroadcastStream;
use tokio_stream::StreamExt;

use crate::{AppState, MossEvent};

/// GET /api/v1/events - Server-Sent Events stream
///
/// Returns a long-lived SSE connection that broadcasts real-time events:
/// - Job status changes (pending → running → completed/failed)
/// - System notifications (warnings, errors)
/// - Background task progress
///
/// # Event Format
/// ```json
/// {
///   "timestamp": "2026-01-21T12:34:56Z",
///   "level": "info" | "warn" | "error" | "debug",
///   "message": "Event description",
///   "job_id": "optional-job-uuid"
/// }
/// ```
///
/// # Backpressure Handling
/// If a client falls behind, lagged messages are dropped with a warning.
/// The client receives a lag notification but continues receiving new events.
pub async fn stream_events(
    State(state): State<AppState>,
) -> Sse<impl Stream<Item = Result<Event, Infallible>>> {
    // Subscribe to broadcast channel
    let rx = state.event_tx.subscribe();

    // Transform broadcast stream to SSE events
    let stream = BroadcastStream::new(rx)
        .filter_map(|result| match result {
            Ok(event) => Some(Ok::<MossEvent, tokio_stream::wrappers::errors::BroadcastStreamRecvError>(event)),
            Err(tokio_stream::wrappers::errors::BroadcastStreamRecvError::Lagged(n)) => {
                tracing::warn!("SSE client lagged {} messages", n);
                None
            }
        })
        .map(|event_result| {
            let event = event_result.unwrap();
            let data = serde_json::to_string(&event).unwrap_or_default();
            Event::default()
                .event("moss-event")
                .data(data)
        })
        .map(Ok);

    Sse::new(stream).keep_alive(KeepAlive::default())
}

/// Emit an event to all SSE subscribers and log it
///
/// This is a composable helper for broadcasting events from anywhere in the application.
/// Events are sent to:
/// 1. All connected SSE clients (via broadcast channel)
/// 2. Tracing/logging system (for persistence and debugging)
///
/// # Arguments
/// * `state` - Application state containing event broadcast channel
/// * `level` - Event severity: "info", "warn", "error", "debug"
/// * `message` - Human-readable event description
/// * `job_id` - Optional job UUID for job-related events
///
/// # Example
/// ```rust,ignore
/// emit_event(&state, "info", "Service started successfully".to_string(), None);
/// emit_event(&state, "error", "Installation failed".to_string(), Some(job_id));
/// ```
pub fn emit_event(state: &AppState, level: &str, message: String, job_id: Option<String>) {
    let event = MossEvent {
        timestamp: chrono::Utc::now().to_rfc3339(),
        level: level.to_string(),
        message: message.clone(),
        job_id,
    };

    // Broadcast to SSE subscribers (ignore if no receivers)
    let _ = state.event_tx.send(event);

    // Also log to tracing for persistence
    match level {
        "error" => tracing::error!("{}", message),
        "warn" => tracing::warn!("{}", message),
        "debug" => tracing::debug!("{}", message),
        _ => tracing::info!("{}", message),
    }
}
