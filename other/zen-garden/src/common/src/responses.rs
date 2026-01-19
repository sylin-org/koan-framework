//! Standardized API Response Builders
//! Server-side utilities for constructing error responses
//!
//! Note: GardenApiResponse (success wrapper) is now in the client module
//! since it's primarily used for HTTP client deserialization of JSON APIs.

use crate::types::{ApiError, ErrorDetails};
use std::collections::HashMap;

/// Standard error response builder
pub struct ApiErrorBuilder {
    code: String,
    message: String,
    details: Option<HashMap<String, serde_json::Value>>,
}

impl ApiErrorBuilder {
    pub fn new(code: impl Into<String>, message: impl Into<String>) -> Self {
        Self {
            code: code.into(),
            message: message.into(),
            details: None,
        }
    }

    pub fn with_detail(mut self, key: impl Into<String>, value: serde_json::Value) -> Self {
        self.details
            .get_or_insert_with(HashMap::new)
            .insert(key.into(), value);
        self
    }

    pub fn with_details(mut self, details: HashMap<String, serde_json::Value>) -> Self {
        self.details = Some(details);
        self
    }

    pub fn build(self) -> ApiError {
        ApiError {
            error: ErrorDetails {
                code: self.code,
                message: self.message,
                details: self.details,
            },
        }
    }
}

/// Create a standard API error
pub fn api_error(code: impl Into<String>, message: impl Into<String>) -> ApiError {
    ApiErrorBuilder::new(code, message).build()
}

/// Create a standard API error with details
pub fn api_error_with_details(
    code: impl Into<String>,
    message: impl Into<String>,
    details: HashMap<String, serde_json::Value>,
) -> ApiError {
    ApiErrorBuilder::new(code, message)
        .with_details(details)
        .build()
}
