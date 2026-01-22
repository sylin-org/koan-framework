//! API utilities for HTTP handlers
//!
//! Provides:
//! - Standard error response formatting
//! - SSE (Server-Sent Events) streaming helpers

pub mod errors;
pub mod sse;

pub use errors::{ApiErrorResponse, error_response, internal_error, not_found, bad_request};
pub use sse::{SseEvent, sse_stream};
