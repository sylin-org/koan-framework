//! Standardized API Response Wrappers
//! Consistent response envelopes for all HTTP endpoints

use serde::{Deserialize, Serialize};
use crate::types::{ApiError, ErrorDetails};
use std::collections::HashMap;

/// Standard success response wrapper with optional suggestions
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ApiResponse<T> {
    pub data: T,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub suggestions: Option<Vec<String>>,
}

impl<T> ApiResponse<T> {
    pub fn new(data: T) -> Self {
        Self {
            data,
            suggestions: None,
        }
    }

    pub fn with_suggestions(data: T, suggestions: Vec<String>) -> Self {
        Self {
            data,
            suggestions: Some(suggestions),
        }
    }
}

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
