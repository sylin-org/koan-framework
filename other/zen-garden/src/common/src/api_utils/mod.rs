//! API utilities for HTTP handlers
//!
//! Provides:
//! - Standard error response formatting
//! - Standard response wrappers (ApiResponse<T>)
//! - SSE (Server-Sent Events) streaming helpers
//! - Input sanitization for query parameters and names

pub mod errors;
pub mod responses;
pub mod sse;
pub mod sanitize;

pub use errors::{ApiErrorResponse, error_response, internal_error, not_found, bad_request};
pub use responses::ApiResponse;
pub use sse::{SseEvent, sse_stream};
pub use sanitize::{
    sanitize_query, sanitize_name, sanitize_tag, sanitize_path_segment,
    is_suspicious, validate_name, SanitizeResult,
    MAX_QUERY_LENGTH, MAX_NAME_LENGTH, MAX_TAG_LENGTH,
};
