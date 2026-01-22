//! Typed API response parsing
//!
//! Eliminates repetitive `.get().and_then().unwrap_or()` chains
//! that appear 32+ times in command handlers.

use garden_common::ServiceInfo;
use serde::de::DeserializeOwned;
use serde_json::Value;

/// Result type for API operations
pub type ApiResult<T> = anyhow::Result<T>;

/// Extract typed data from API response
///
/// Handles both wrapped `{ "data": T }` and unwrapped `T` formats.
///
/// # Example
/// ```ignore
/// let services: Vec<ServiceInfo> = extract_data(&body)?;
/// ```
pub fn extract_data<T: DeserializeOwned>(body: &Value) -> Option<T> {
    // Try wrapped format first: { "data": T }
    body.get("data")
        .and_then(|d| serde_json::from_value(d.clone()).ok())
        // Fall back to unwrapped: T
        .or_else(|| serde_json::from_value(body.clone()).ok())
}

/// Extract array from API response
///
/// Handles both `{ "data": [...] }` and `[...]` formats.
pub fn extract_array(body: &Value) -> Option<&Vec<Value>> {
    body.get("data")
        .and_then(|d| d.as_array())
        .or_else(|| body.as_array())
}

/// Extract string field from JSON value
pub fn extract_string<'a>(body: &'a Value, key: &str) -> Option<&'a str> {
    body.get(key).and_then(|v| v.as_str())
}

/// Extract bool field from JSON value
pub fn extract_bool(body: &Value, key: &str) -> Option<bool> {
    body.get(key).and_then(|v| v.as_bool())
}

/// Extract i64 field from JSON value
pub fn extract_i64(body: &Value, key: &str) -> Option<i64> {
    body.get(key).and_then(|v| v.as_i64())
}

/// Extract services list from API response
///
/// Specialized helper for the common pattern of fetching services.
pub fn extract_services(body: &Value) -> Vec<ServiceInfo> {
    extract_data::<Vec<ServiceInfo>>(body).unwrap_or_default()
}

/// Parse nested field with dot notation
///
/// # Example
/// ```ignore
/// let status = extract_nested_string(&body, "health.status");
/// ```
pub fn extract_nested_string<'a>(body: &'a Value, path: &str) -> Option<&'a str> {
    let mut current = body;
    for part in path.split('.') {
        current = current.get(part)?;
    }
    current.as_str()
}

/// Helper to check if response indicates success
pub fn is_success_response(body: &Value) -> bool {
    // Check for explicit status field
    if let Some(status) = extract_string(body, "status") {
        return matches!(status, "ok" | "success" | "created" | "updated" | "deleted");
    }
    // Check for success boolean
    if let Some(success) = extract_bool(body, "success") {
        return success;
    }
    // If there's data, consider it success
    body.get("data").is_some()
}

/// Extract error message from API response
pub fn extract_error_message(body: &Value) -> Option<String> {
    // Try various error field names
    extract_string(body, "error")
        .or_else(|| extract_string(body, "message"))
        .or_else(|| extract_string(body, "detail"))
        .or_else(|| extract_nested_string(body, "error.message"))
        .map(|s| s.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn test_extract_data_wrapped() {
        let body = json!({
            "data": [
                {"name": "nginx", "status": "running"}
            ]
        });
        let result: Option<Vec<Value>> = extract_data(&body);
        assert!(result.is_some());
        assert_eq!(result.unwrap().len(), 1);
    }

    #[test]
    fn test_extract_data_unwrapped() {
        let body = json!([
            {"name": "nginx", "status": "running"}
        ]);
        let result: Option<Vec<Value>> = extract_data(&body);
        assert!(result.is_some());
        assert_eq!(result.unwrap().len(), 1);
    }

    #[test]
    fn test_extract_array() {
        let wrapped = json!({"data": [1, 2, 3]});
        let unwrapped = json!([1, 2, 3]);

        assert_eq!(extract_array(&wrapped).unwrap().len(), 3);
        assert_eq!(extract_array(&unwrapped).unwrap().len(), 3);
    }

    #[test]
    fn test_extract_string() {
        let body = json!({"status": "ok", "name": "test"});
        assert_eq!(extract_string(&body, "status"), Some("ok"));
        assert_eq!(extract_string(&body, "name"), Some("test"));
        assert_eq!(extract_string(&body, "missing"), None);
    }

    #[test]
    fn test_extract_nested_string() {
        let body = json!({
            "health": {
                "status": "healthy",
                "details": {
                    "cpu": "ok"
                }
            }
        });
        assert_eq!(extract_nested_string(&body, "health.status"), Some("healthy"));
        assert_eq!(extract_nested_string(&body, "health.details.cpu"), Some("ok"));
        assert_eq!(extract_nested_string(&body, "health.missing"), None);
    }

    #[test]
    fn test_is_success_response() {
        assert!(is_success_response(&json!({"status": "ok"})));
        assert!(is_success_response(&json!({"status": "success"})));
        assert!(is_success_response(&json!({"success": true})));
        assert!(is_success_response(&json!({"data": []})));
        assert!(!is_success_response(&json!({"error": "failed"})));
    }

    #[test]
    fn test_extract_error_message() {
        assert_eq!(
            extract_error_message(&json!({"error": "not found"})),
            Some("not found".to_string())
        );
        assert_eq!(
            extract_error_message(&json!({"message": "forbidden"})),
            Some("forbidden".to_string())
        );
        assert_eq!(
            extract_error_message(&json!({"error": {"message": "nested"}})),
            Some("nested".to_string())
        );
    }
}
