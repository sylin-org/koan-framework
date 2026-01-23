//! Input sanitization utilities for API handlers
//!
//! Provides sanitization functions for query parameters, service names,
//! and other user-provided inputs to prevent injection attacks and
//! ensure consistent data handling.

/// Maximum length for query strings
pub const MAX_QUERY_LENGTH: usize = 256;

/// Maximum length for service/resource names
pub const MAX_NAME_LENGTH: usize = 128;

/// Maximum length for category/tag values
pub const MAX_TAG_LENGTH: usize = 64;

/// Sanitization result with the cleaned value and any warnings
#[derive(Debug, Clone)]
pub struct SanitizeResult {
    pub value: String,
    pub was_modified: bool,
    pub original_length: usize,
}

impl SanitizeResult {
    fn new(value: String, original: &str) -> Self {
        let was_modified = value != original;
        Self {
            value,
            was_modified,
            original_length: original.len(),
        }
    }

    /// Returns the sanitized value, consuming the result
    pub fn into_value(self) -> String {
        self.value
    }
}

/// Sanitize a search query string
///
/// - Trims whitespace
/// - Removes control characters
/// - Limits length to MAX_QUERY_LENGTH
/// - Preserves search prefixes (c:, cat:, t:, tag:, etc.)
pub fn sanitize_query(input: &str) -> SanitizeResult {
    let cleaned: String = input
        .trim()
        .chars()
        .filter(|c| !c.is_control() || *c == '\t')
        .take(MAX_QUERY_LENGTH)
        .collect();

    SanitizeResult::new(cleaned, input)
}

/// Sanitize a service or resource name
///
/// - Trims whitespace
/// - Converts to lowercase
/// - Removes characters not in [a-z0-9_-]
/// - Limits length to MAX_NAME_LENGTH
pub fn sanitize_name(input: &str) -> SanitizeResult {
    let cleaned: String = input
        .trim()
        .to_lowercase()
        .chars()
        .filter(|c| c.is_ascii_alphanumeric() || *c == '_' || *c == '-')
        .take(MAX_NAME_LENGTH)
        .collect();

    SanitizeResult::new(cleaned, input)
}

/// Sanitize a category or tag value
///
/// - Trims whitespace
/// - Converts to lowercase
/// - Removes characters not in [a-z0-9_-]
/// - Limits length to MAX_TAG_LENGTH
pub fn sanitize_tag(input: &str) -> SanitizeResult {
    let cleaned: String = input
        .trim()
        .to_lowercase()
        .chars()
        .filter(|c| c.is_ascii_alphanumeric() || *c == '_' || *c == '-')
        .take(MAX_TAG_LENGTH)
        .collect();

    SanitizeResult::new(cleaned, input)
}

/// Sanitize a path segment (for URL construction)
///
/// - Trims whitespace
/// - URL-encodes special characters
/// - Removes path traversal attempts (../, ..\)
/// - Limits length to MAX_NAME_LENGTH
pub fn sanitize_path_segment(input: &str) -> SanitizeResult {
    let trimmed = input.trim();

    // Remove path traversal patterns
    let no_traversal = trimmed
        .replace("../", "")
        .replace("..\\", "")
        .replace("..", "");

    // Keep only safe characters for path segments
    let cleaned: String = no_traversal
        .chars()
        .filter(|c| c.is_ascii_alphanumeric() || *c == '_' || *c == '-' || *c == '.')
        .take(MAX_NAME_LENGTH)
        .collect();

    SanitizeResult::new(cleaned, input)
}

/// Check if a string contains potentially dangerous patterns
///
/// Returns true if the input looks suspicious (SQL injection, path traversal, etc.)
pub fn is_suspicious(input: &str) -> bool {
    let lower = input.to_lowercase();

    // SQL injection patterns
    let sql_patterns = ["'--", "';", "' or ", "' and ", "1=1", "drop ", "select ", "insert ", "delete ", "update "];

    // Path traversal
    let path_patterns = ["../", "..\\", "%2e%2e"];

    // Script injection
    let script_patterns = ["<script", "javascript:", "onerror=", "onload="];

    for pattern in sql_patterns.iter().chain(path_patterns.iter()).chain(script_patterns.iter()) {
        if lower.contains(pattern) {
            return true;
        }
    }

    false
}

/// Validate that a name is well-formed after sanitization
///
/// Returns an error message if invalid, None if valid
pub fn validate_name(name: &str) -> Option<&'static str> {
    if name.is_empty() {
        return Some("Name cannot be empty");
    }
    if name.len() > MAX_NAME_LENGTH {
        return Some("Name exceeds maximum length");
    }
    if name.starts_with('-') || name.starts_with('_') {
        return Some("Name cannot start with - or _");
    }
    if !name.chars().next().map(|c| c.is_ascii_alphabetic()).unwrap_or(false) {
        return Some("Name must start with a letter");
    }
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_sanitize_query() {
        // Normal query
        let result = sanitize_query("mongodb");
        assert_eq!(result.value, "mongodb");
        assert!(!result.was_modified);

        // Query with prefix
        let result = sanitize_query("c:database");
        assert_eq!(result.value, "c:database");

        // Query with whitespace
        let result = sanitize_query("  mongodb  ");
        assert_eq!(result.value, "mongodb");
        assert!(result.was_modified);

        // Query with control chars
        let result = sanitize_query("mongo\x00db");
        assert_eq!(result.value, "mongodb");
        assert!(result.was_modified);
    }

    #[test]
    fn test_sanitize_name() {
        // Normal name
        let result = sanitize_name("my-service");
        assert_eq!(result.value, "my-service");

        // Name with uppercase
        let result = sanitize_name("My-Service");
        assert_eq!(result.value, "my-service");
        assert!(result.was_modified);

        // Name with special chars
        let result = sanitize_name("my service!");
        assert_eq!(result.value, "myservice");
        assert!(result.was_modified);
    }

    #[test]
    fn test_sanitize_path_segment() {
        // Normal path
        let result = sanitize_path_segment("service-name");
        assert_eq!(result.value, "service-name");

        // Path traversal attempt
        let result = sanitize_path_segment("../../../etc/passwd");
        assert!(!result.value.contains(".."));
        assert!(result.was_modified);
    }

    #[test]
    fn test_is_suspicious() {
        assert!(is_suspicious("'; DROP TABLE users;--"));
        assert!(is_suspicious("../../../etc/passwd"));
        assert!(is_suspicious("<script>alert(1)</script>"));
        assert!(!is_suspicious("mongodb"));
        assert!(!is_suspicious("my-normal-service"));
    }

    #[test]
    fn test_validate_name() {
        assert!(validate_name("my-service").is_none());
        assert!(validate_name("").is_some());
        assert!(validate_name("-invalid").is_some());
        assert!(validate_name("123service").is_some());
    }
}
