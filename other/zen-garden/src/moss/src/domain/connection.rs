//! Connection string resolution utilities
//!
//! Provides reusable functions for resolving service connection URIs.
//! Supports hostname-first resolution with IP fallback for resilience.

use serde::{Deserialize, Serialize};

/// Resolved connection information for a service
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ResolvedConnection {
    /// Hostname (e.g., "stone-02.local")
    pub hostname: String,
    /// IP address (e.g., "192.168.1.102")
    pub ip: String,
    /// Service port
    pub port: u16,
    /// Protocol (e.g., "mongodb", "postgresql", "redis")
    pub protocol: String,
    /// Connection URIs - hostname-first, then IP (for resilience)
    pub uris: Vec<String>,
}

/// Default connection templates by protocol
///
/// Used when offering manifest doesn't specify a connection_template.
pub fn default_template(protocol: &str) -> String {
    match protocol.to_lowercase().as_str() {
        "mongodb" => "mongodb://{host}:{port}".to_string(),
        "postgresql" | "postgres" => "postgresql://{host}:{port}".to_string(),
        "mysql" | "mariadb" => "mysql://{host}:{port}".to_string(),
        "redis" => "redis://{host}:{port}".to_string(),
        "elasticsearch" => "http://{host}:{port}".to_string(),
        "meilisearch" => "http://{host}:{port}".to_string(),
        "minio" | "s3" => "http://{host}:{port}".to_string(),
        "nats" => "nats://{host}:{port}".to_string(),
        "rabbitmq" | "amqp" => "amqp://{host}:{port}".to_string(),
        "http" | "https" => "{protocol}://{host}:{port}".to_string(),
        _ => "{protocol}://{host}:{port}".to_string(),
    }
}

/// Infer protocol from offering category and name
///
/// Used when protocol isn't explicitly specified.
pub fn infer_protocol(offering_name: &str, category: &str) -> String {
    // First try by name (more specific)
    match offering_name.to_lowercase().as_str() {
        "mongodb" | "mongo" => "mongodb".to_string(),
        "postgresql" | "postgres" => "postgresql".to_string(),
        "mysql" | "mariadb" => "mysql".to_string(),
        "redis" | "dragonfly" | "keydb" => "redis".to_string(),
        "elasticsearch" | "opensearch" => "http".to_string(),
        "meilisearch" | "typesense" => "http".to_string(),
        "minio" => "http".to_string(),
        "nats" => "nats".to_string(),
        "rabbitmq" => "amqp".to_string(),
        _ => {
            // Fall back to category
            match category.to_lowercase().as_str() {
                "database" => "tcp".to_string(),
                "cache" => "redis".to_string(),
                "search" => "http".to_string(),
                "messaging" => "tcp".to_string(),
                "storage" => "http".to_string(),
                "monitoring" => "http".to_string(),
                _ => "tcp".to_string(),
            }
        }
    }
}

/// Extract IP address from endpoint URL
///
/// # Example
/// ```ignore
/// let ip = extract_ip("http://192.168.1.102:7185");
/// assert_eq!(ip, "192.168.1.102");
/// ```
pub fn extract_ip(endpoint: &str) -> String {
    // Remove protocol prefix
    let without_protocol = endpoint
        .strip_prefix("http://")
        .or_else(|| endpoint.strip_prefix("https://"))
        .unwrap_or(endpoint);

    // Extract host:port or just host
    let host_port = without_protocol.split('/').next().unwrap_or(without_protocol);

    // Remove port if present
    if let Some(bracket_end) = host_port.find(']') {
        // IPv6 address like [::1]:8080
        host_port[1..bracket_end].to_string()
    } else if let Some(colon_pos) = host_port.rfind(':') {
        // Check if it's a port separator (not part of IPv6)
        let potential_host = &host_port[..colon_pos];
        if potential_host.contains(':') {
            // IPv6 without brackets
            host_port.to_string()
        } else {
            potential_host.to_string()
        }
    } else {
        host_port.to_string()
    }
}

/// Build hostname from stone name
///
/// Appends `.local` suffix for mDNS resolution.
pub fn build_hostname(stone_name: &str) -> String {
    if stone_name.contains('.') {
        // Already has domain suffix
        stone_name.to_string()
    } else {
        format!("{}.local", stone_name)
    }
}

/// Resolve connection URIs from template
///
/// Applies template substitution and returns both hostname and IP-based URIs.
///
/// # Arguments
/// * `template` - Connection template with placeholders (e.g., "mongodb://{host}:{port}")
/// * `hostname` - mDNS hostname (e.g., "stone-02.local")
/// * `ip` - IP address (e.g., "192.168.1.102")
/// * `port` - Service port
/// * `protocol` - Protocol name (used for {protocol} placeholder)
///
/// # Returns
/// Vector of URIs with hostname-based first (more resilient), IP-based second (fallback)
pub fn resolve_uris(
    template: &str,
    hostname: &str,
    ip: &str,
    port: u16,
    protocol: &str,
) -> Vec<String> {
    let uri_hostname = template
        .replace("{host}", hostname)
        .replace("{port}", &port.to_string())
        .replace("{protocol}", protocol);

    let uri_ip = template
        .replace("{host}", ip)
        .replace("{port}", &port.to_string())
        .replace("{protocol}", protocol);

    // Hostname first (more resilient to IP changes), IP second (fallback)
    if uri_hostname != uri_ip {
        vec![uri_hostname, uri_ip]
    } else {
        vec![uri_hostname]
    }
}

/// Full connection resolution from service and stone info
///
/// This is the main entry point for resolving a complete connection.
pub fn resolve_connection(
    stone_name: &str,
    stone_endpoint: &str,
    port: u16,
    protocol: &str,
    template: Option<&str>,
) -> ResolvedConnection {
    let hostname = build_hostname(stone_name);
    let ip = extract_ip(stone_endpoint);

    let effective_template = template
        .map(|t| t.to_string())
        .unwrap_or_else(|| default_template(protocol));

    let uris = resolve_uris(&effective_template, &hostname, &ip, port, protocol);

    ResolvedConnection {
        hostname,
        ip,
        port,
        protocol: protocol.to_string(),
        uris,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_extract_ip_with_port() {
        assert_eq!(extract_ip("http://192.168.1.102:7185"), "192.168.1.102");
    }

    #[test]
    fn test_extract_ip_without_port() {
        assert_eq!(extract_ip("http://192.168.1.102"), "192.168.1.102");
    }

    #[test]
    fn test_extract_ip_no_protocol() {
        assert_eq!(extract_ip("192.168.1.102:7185"), "192.168.1.102");
    }

    #[test]
    fn test_extract_ip_with_path() {
        assert_eq!(extract_ip("http://192.168.1.102:7185/api/v1"), "192.168.1.102");
    }

    #[test]
    fn test_build_hostname_simple() {
        assert_eq!(build_hostname("stone-02"), "stone-02.local");
    }

    #[test]
    fn test_build_hostname_already_qualified() {
        assert_eq!(build_hostname("stone-02.local"), "stone-02.local");
        assert_eq!(build_hostname("server.example.com"), "server.example.com");
    }

    #[test]
    fn test_resolve_uris_mongodb() {
        let uris = resolve_uris(
            "mongodb://{host}:{port}",
            "stone-02.local",
            "192.168.1.102",
            27017,
            "mongodb",
        );
        assert_eq!(uris.len(), 2);
        assert_eq!(uris[0], "mongodb://stone-02.local:27017");
        assert_eq!(uris[1], "mongodb://192.168.1.102:27017");
    }

    #[test]
    fn test_resolve_uris_with_protocol_placeholder() {
        let uris = resolve_uris(
            "{protocol}://{host}:{port}",
            "stone-01.local",
            "10.0.0.1",
            8080,
            "http",
        );
        assert_eq!(uris[0], "http://stone-01.local:8080");
        assert_eq!(uris[1], "http://10.0.0.1:8080");
    }

    #[test]
    fn test_resolve_connection_full() {
        let conn = resolve_connection(
            "stone-02",
            "http://192.168.1.102:7185",
            27017,
            "mongodb",
            Some("mongodb://{host}:{port}"),
        );

        assert_eq!(conn.hostname, "stone-02.local");
        assert_eq!(conn.ip, "192.168.1.102");
        assert_eq!(conn.port, 27017);
        assert_eq!(conn.protocol, "mongodb");
        assert_eq!(conn.uris.len(), 2);
        assert_eq!(conn.uris[0], "mongodb://stone-02.local:27017");
    }

    #[test]
    fn test_resolve_connection_default_template() {
        let conn = resolve_connection(
            "stone-01",
            "http://10.0.0.1:7185",
            6379,
            "redis",
            None,
        );

        assert_eq!(conn.uris[0], "redis://stone-01.local:6379");
    }

    #[test]
    fn test_infer_protocol() {
        assert_eq!(infer_protocol("mongodb", "database"), "mongodb");
        assert_eq!(infer_protocol("postgres", "database"), "postgresql");
        assert_eq!(infer_protocol("redis", "cache"), "redis");
        assert_eq!(infer_protocol("unknown-db", "database"), "tcp");
        assert_eq!(infer_protocol("unknown", "cache"), "redis");
    }

    #[test]
    fn test_default_template() {
        assert!(default_template("mongodb").contains("mongodb://"));
        assert!(default_template("postgresql").contains("postgresql://"));
        assert!(default_template("redis").contains("redis://"));
        assert!(default_template("unknown").contains("{protocol}://"));
    }
}
