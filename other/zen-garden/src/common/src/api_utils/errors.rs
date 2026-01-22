//! Standard API error responses
//!
//! Provides consistent error formatting across all Zen Garden APIs.

use serde::{Deserialize, Serialize};
use std::collections::HashMap;

/// Standard API error response
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ApiErrorResponse {
    pub error: ErrorDetails,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ErrorDetails {
    pub code: String,
    pub message: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub details: Option<HashMap<String, serde_json::Value>>,
}

impl ApiErrorResponse {
    /// Create a new error response
    pub fn new(code: impl Into<String>, message: impl Into<String>) -> Self {
        Self {
            error: ErrorDetails {
                code: code.into(),
                message: message.into(),
                details: None,
            },
        }
    }

    /// Create error with additional details
    pub fn with_details(
        code: impl Into<String>,
        message: impl Into<String>,
        details: HashMap<String, serde_json::Value>,
    ) -> Self {
        Self {
            error: ErrorDetails {
                code: code.into(),
                message: message.into(),
                details: Some(details),
            },
        }
    }

    /// Convert to JSON
    pub fn to_json(&self) -> Result<String, serde_json::Error> {
        serde_json::to_string(self)
    }

    /// Convert to JSON bytes
    pub fn to_json_bytes(&self) -> Result<Vec<u8>, serde_json::Error> {
        serde_json::to_vec(self)
    }
}

// ============================================================================
// Common Error Constructors
// ============================================================================

/// Create a generic error response
pub fn error_response(code: impl Into<String>, message: impl Into<String>) -> ApiErrorResponse {
    ApiErrorResponse::new(code, message)
}

/// Create an internal server error response
pub fn internal_error(message: impl Into<String>) -> ApiErrorResponse {
    ApiErrorResponse::new("INTERNAL_ERROR", message)
}

/// Create a not found error response
pub fn not_found(resource: impl Into<String>) -> ApiErrorResponse {
    ApiErrorResponse::new("NOT_FOUND", format!("Resource not found: {}", resource.into()))
}

/// Create a bad request error response
pub fn bad_request(message: impl Into<String>) -> ApiErrorResponse {
    ApiErrorResponse::new("INVALID_REQUEST", message)
}

/// Create a service not found error response
pub fn service_not_found(service_name: impl Into<String>) -> ApiErrorResponse {
    ApiErrorResponse::new(
        "SERVICE_NOT_FOUND",
        format!("Service not found: {}", service_name.into()),
    )
}

/// Create an offering not found error response
pub fn offering_not_found(offering_name: impl Into<String>) -> ApiErrorResponse {
    ApiErrorResponse::new(
        "OFFERING_NOT_FOUND",
        format!("Offering not found: {}", offering_name.into()),
    )
}

/// Create a job not found error response
pub fn job_not_found(job_id: impl Into<String>) -> ApiErrorResponse {
    ApiErrorResponse::new(
        "JOB_NOT_FOUND",
        format!("Job not found: {}", job_id.into()),
    )
}

/// Create a Docker error response
pub fn docker_error(message: impl Into<String>) -> ApiErrorResponse {
    ApiErrorResponse::new("DOCKER_ERROR", message)
}

/// Create a compatibility failed error response
pub fn compatibility_failed(message: impl Into<String>) -> ApiErrorResponse {
    ApiErrorResponse::new("COMPATIBILITY_FAILED", message)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_error_response_creation() {
        let err = ApiErrorResponse::new("TEST_ERROR", "Test message");
        assert_eq!(err.error.code, "TEST_ERROR");
        assert_eq!(err.error.message, "Test message");
        assert!(err.error.details.is_none());
    }

    #[test]
    fn test_error_response_with_details() {
        let mut details = HashMap::new();
        details.insert("field".into(), serde_json::json!("value"));

        let err = ApiErrorResponse::with_details("TEST_ERROR", "Test message", details);
        assert_eq!(err.error.code, "TEST_ERROR");
        assert!(err.error.details.is_some());
    }

    #[test]
    fn test_error_response_serialization() {
        let err = ApiErrorResponse::new("TEST_ERROR", "Test message");
        let json = err.to_json().unwrap();

        assert!(json.contains("TEST_ERROR"));
        assert!(json.contains("Test message"));

        let deserialized: ApiErrorResponse = serde_json::from_str(&json).unwrap();
        assert_eq!(deserialized.error.code, "TEST_ERROR");
    }

    #[test]
    fn test_common_error_constructors() {
        let err1 = internal_error("Something went wrong");
        assert_eq!(err1.error.code, "INTERNAL_ERROR");

        let err2 = not_found("user/123");
        assert_eq!(err2.error.code, "NOT_FOUND");

        let err3 = bad_request("Invalid input");
        assert_eq!(err3.error.code, "INVALID_REQUEST");

        let err4 = service_not_found("mongodb");
        assert_eq!(err4.error.code, "SERVICE_NOT_FOUND");
        assert!(err4.error.message.contains("mongodb"));
    }
}
