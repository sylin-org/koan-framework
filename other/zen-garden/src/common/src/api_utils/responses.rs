//! Standard API response wrappers
//!
//! Shared response types used by both moss (server) and rake (client).

use serde::{Deserialize, Serialize};

/// Standard API response wrapper
///
/// All API endpoints return data wrapped in this structure for consistency.
/// The `suggestions` field provides contextual hints for CLI users.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ApiResponse<T> {
    /// The response payload
    pub data: T,
    /// Optional suggestions for next actions (shown in CLI)
    #[serde(skip_serializing_if = "Option::is_none")]
    pub suggestions: Option<Vec<String>>,
}

impl<T> ApiResponse<T> {
    /// Create a new API response with data only
    pub fn new(data: T) -> Self {
        Self {
            data,
            suggestions: None,
        }
    }

    /// Create a new API response with data and suggestions
    pub fn with_suggestions(data: T, suggestions: Vec<String>) -> Self {
        Self {
            data,
            suggestions: if suggestions.is_empty() {
                None
            } else {
                Some(suggestions)
            },
        }
    }
}
