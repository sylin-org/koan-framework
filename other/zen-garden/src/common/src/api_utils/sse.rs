//! Server-Sent Events (SSE) utilities
//!
//! Provides helpers for streaming events to HTTP clients.
//! Used by moss to stream job progress, service events, etc.

use crate::events::DomainEvent;
use std::fmt;
use tokio::sync::broadcast;

/// SSE event with id, event type, and data
#[derive(Debug, Clone)]
pub struct SseEvent {
    pub id: Option<String>,
    pub event: Option<String>,
    pub data: String,
}

impl SseEvent {
    /// Create a new SSE event
    pub fn new(data: impl Into<String>) -> Self {
        Self {
            id: None,
            event: None,
            data: data.into(),
        }
    }

    /// Create event with ID
    pub fn with_id(mut self, id: impl Into<String>) -> Self {
        self.id = Some(id.into());
        self
    }

    /// Create event with event type
    pub fn with_event(mut self, event: impl Into<String>) -> Self {
        self.event = Some(event.into());
        self
    }

    /// Create from DomainEvent
    pub fn from_domain_event(event: &DomainEvent) -> Result<Self, serde_json::Error> {
        let json = event.to_json()?;
        Ok(Self {
            id: None,
            event: Some("domain-event".into()),
            data: json,
        })
    }

    /// Format as SSE protocol message
    ///
    /// Format:
    /// ```text
    /// id: event-123
    /// event: message
    /// data: {"key": "value"}
    ///
    /// ```
    pub fn to_sse_format(&self) -> String {
        let mut output = String::new();

        if let Some(id) = &self.id {
            output.push_str(&format!("id: {}\n", id));
        }

        if let Some(event) = &self.event {
            output.push_str(&format!("event: {}\n", event));
        }

        // Data can be multi-line, each line prefixed with "data: "
        for line in self.data.lines() {
            output.push_str(&format!("data: {}\n", line));
        }

        // SSE messages end with double newline
        output.push('\n');

        output
    }

    /// Convert to bytes for sending over HTTP
    pub fn to_bytes(&self) -> Vec<u8> {
        self.to_sse_format().into_bytes()
    }
}

impl fmt::Display for SseEvent {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}", self.to_sse_format())
    }
}

/// Create an SSE stream from a broadcast receiver
///
/// Returns an async stream that yields SSE-formatted events.
/// Suitable for use with web frameworks like Axum, Actix, etc.
///
/// Example with Axum:
/// ```ignore
/// use axum::response::sse::Sse;
/// use futures::stream::StreamExt;
///
/// async fn events_handler(event_bus: EventBus) -> Sse<impl Stream<Item = Result<Event, Infallible>>> {
///     let rx = event_bus.subscribe();
///     let stream = sse_stream(rx);
///     Sse::new(stream)
/// }
/// ```
pub fn sse_stream(
    mut rx: broadcast::Receiver<DomainEvent>,
) -> impl futures::Stream<Item = Result<SseEvent, std::io::Error>> {
    async_stream::stream! {
        while let Ok(event) = rx.recv().await {
            match SseEvent::from_domain_event(&event) {
                Ok(sse_event) => yield Ok(sse_event),
                Err(e) => {
                    eprintln!("Failed to serialize event: {}", e);
                    // Continue streaming despite serialization errors
                    continue;
                }
            }
        }
    }
}

/// Create a heartbeat stream that sends keepalive events
///
/// Useful to prevent proxy timeouts on long-lived SSE connections.
pub fn heartbeat_stream(
    interval: std::time::Duration,
) -> impl futures::Stream<Item = Result<SseEvent, std::io::Error>> {
    use tokio_stream::wrappers::IntervalStream;
    use tokio_stream::StreamExt;

    IntervalStream::new(tokio::time::interval(interval))
        .map(|_| Ok(SseEvent::new(":heartbeat\n")))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::events::ServiceEvent;
    use chrono::Utc;

    #[test]
    fn test_sse_event_creation() {
        let event = SseEvent::new("test data");
        assert_eq!(event.data, "test data");
        assert!(event.id.is_none());
        assert!(event.event.is_none());
    }

    #[test]
    fn test_sse_event_with_id_and_event() {
        let event = SseEvent::new("test data")
            .with_id("event-123")
            .with_event("message");

        assert_eq!(event.id, Some("event-123".into()));
        assert_eq!(event.event, Some("message".into()));
    }

    #[test]
    fn test_sse_event_formatting() {
        let event = SseEvent::new("hello world")
            .with_id("123")
            .with_event("greeting");

        let formatted = event.to_sse_format();

        assert!(formatted.contains("id: 123\n"));
        assert!(formatted.contains("event: greeting\n"));
        assert!(formatted.contains("data: hello world\n"));
        assert!(formatted.ends_with("\n\n"));
    }

    #[test]
    fn test_sse_event_multiline_data() {
        let event = SseEvent::new("line 1\nline 2\nline 3");
        let formatted = event.to_sse_format();

        assert!(formatted.contains("data: line 1\n"));
        assert!(formatted.contains("data: line 2\n"));
        assert!(formatted.contains("data: line 3\n"));
    }

    #[test]
    fn test_sse_event_from_domain_event() {
        let service_event = ServiceEvent::Started {
            stone_name: "stone-01".into(),
            service_name: "mongodb".into(),
            timestamp: Utc::now(),
        };

        let domain_event = DomainEvent::Service(service_event);
        let sse_event = SseEvent::from_domain_event(&domain_event).unwrap();

        assert_eq!(sse_event.event, Some("domain-event".into()));
        assert!(sse_event.data.contains("Started"));
        assert!(sse_event.data.contains("stone-01"));
    }
}
