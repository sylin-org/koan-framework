//! Service discovery domain logic
//!
//! Provides service discovery across the garden with connection string resolution.
//! Supports search by name, category, or tags with cache-first architecture.

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use std::time::Duration;

use crate::domain::connection::{self, ResolvedConnection};
use crate::domain::topology;
use crate::AppState;
use garden_common::manifests::get_category_registry;
use garden_common::ServiceStatus;

/// Search criteria for service discovery
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServiceSearchCriteria {
    /// Search by exact service name
    #[serde(skip_serializing_if = "Option::is_none")]
    pub name: Option<String>,

    /// Search by category
    #[serde(skip_serializing_if = "Option::is_none")]
    pub category: Option<String>,

    /// Search by tag (any match)
    #[serde(skip_serializing_if = "Option::is_none")]
    pub tag: Option<String>,
}

impl ServiceSearchCriteria {
    pub fn by_name(name: &str) -> Self {
        Self {
            name: Some(name.to_string()),
            category: None,
            tag: None,
        }
    }

    pub fn by_category(category: &str) -> Self {
        Self {
            name: None,
            category: Some(category.to_string()),
            tag: None,
        }
    }

    pub fn by_tag(tag: &str) -> Self {
        Self {
            name: None,
            category: None,
            tag: Some(tag.to_string()),
        }
    }

    /// Parse search query with prefix detection
    ///
    /// Supports:
    /// - `mongodb` - name search (or implicit category if known)
    /// - `c:database`, `cat:database`, `category:database` - category search
    /// - `t:nosql`, `tag:nosql`, `tags:nosql` - tag search
    pub fn parse(query: &str) -> Self {
        let query = query.trim();

        // Check for category prefix
        if let Some(cat) = query
            .strip_prefix("c:")
            .or_else(|| query.strip_prefix("cat:"))
            .or_else(|| query.strip_prefix("category:"))
        {
            return Self::by_category(cat);
        }

        // Check for tag prefix
        if let Some(tag) = query
            .strip_prefix("t:")
            .or_else(|| query.strip_prefix("tag:"))
            .or_else(|| query.strip_prefix("tags:"))
        {
            return Self::by_tag(tag);
        }

        // Check if it's a known category (implicit category search)
        // Uses data-driven category registry instead of hardcoded list
        let lower = query.to_lowercase();
        if get_category_registry().resolve_token(&lower).is_some() {
            return Self::by_category(&lower);
        }

        // Default to name search
        Self::by_name(query)
    }

    /// Check if this is a name-based search (exact match required)
    pub fn is_name_search(&self) -> bool {
        self.name.is_some()
    }
}

/// Found service with connection information
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FoundService {
    /// Service name
    pub name: String,

    /// Offering type (e.g., "mongodb", "redis")
    pub offering: String,

    /// Service category
    pub category: String,

    /// Service tags
    pub tags: Vec<String>,

    /// Current status
    pub status: String,

    /// Stone hosting this service
    pub stone: StoneRef,

    /// Resolved connection information
    pub connection: ResolvedConnection,
}

/// Reference to a stone
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StoneRef {
    pub id: String,
    pub name: String,
    pub endpoint: String,
}

/// Service discovery response
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServiceDiscoveryResponse {
    /// Whether services were found
    pub found: bool,

    /// Found services
    pub services: Vec<FoundService>,

    /// Data source ("cache" or "fresh")
    pub source: String,

    /// Cache age in seconds (if from cache)
    #[serde(skip_serializing_if = "Option::is_none")]
    pub cache_age_seconds: Option<u64>,

    /// Response timestamp
    pub timestamp: DateTime<Utc>,
}

/// Find services matching criteria on local stone
///
/// Zero-latency local search using registry and offerings index.
pub async fn find_local_services(
    criteria: &ServiceSearchCriteria,
    state: &AppState,
) -> Vec<FoundService> {
    let registry = state.registry.read().await;
    let offerings_index = state.offerings_index.read().await;

    let mut results = Vec::new();

    for service in registry.iter() {
        // Skip non-running services
        if service.status != ServiceStatus::Running {
            continue;
        }

        // Get offering metadata (category, tags)
        let (category, tags, connection_template) = offerings_index
            .as_ref()
            .and_then(|idx| idx.offerings.iter().find(|o| o.name == service.offering))
            .map(|o| (o.category.clone(), o.tags.clone(), None::<String>)) // TODO: Add connection_template to CompiledOffering
            .unwrap_or_else(|| (service.offering.clone(), vec![], None));

        // Check if matches criteria
        if !matches_criteria(criteria, &service.name, &service.offering, &category, &tags) {
            continue;
        }

        // Resolve connection
        let protocol = connection::infer_protocol(&service.offering, &category, state).await;
        let port = service.ports.native;

        let conn = connection::resolve_connection(
            &state.stone_name,
            &format!("http://127.0.0.1:{}", state.api_port),
            port,
            &protocol,
            connection_template.as_deref(),
        );

        results.push(FoundService {
            name: service.name.clone(),
            offering: service.offering.clone(),
            category,
            tags,
            status: format!("{:?}", service.status),
            stone: StoneRef {
                id: state.stone_id.clone(),
                name: state.stone_name.clone(),
                endpoint: format!("http://127.0.0.1:{}", state.api_port),
            },
            connection: conn,
        });
    }

    results
}

/// List all local services (regardless of criteria) for the unified /api/v1/services endpoint
///
/// Returns all services from registry with full connection info.
/// Includes both running and non-running services.
pub async fn list_all_local_services(state: &AppState) -> ServiceDiscoveryResponse {
    let registry = state.registry.read().await;
    let offerings_index = state.offerings_index.read().await;

    let mut services = Vec::new();

    for service in registry.iter() {
        // Get offering metadata (category, tags)
        let (category, tags, connection_template) = offerings_index
            .as_ref()
            .and_then(|idx| idx.offerings.iter().find(|o| o.name == service.offering))
            .map(|o| (o.category.clone(), o.tags.clone(), None::<String>))
            .unwrap_or_else(|| (service.offering.clone(), vec![], None));

        // Resolve connection
        let protocol = connection::infer_protocol(&service.offering, &category, state).await;
        let port = service.ports.native;

        let conn = connection::resolve_connection(
            &state.stone_name,
            &format!("http://127.0.0.1:{}", state.api_port),
            port,
            &protocol,
            connection_template.as_deref(),
        );

        services.push(FoundService {
            name: service.name.clone(),
            offering: service.offering.clone(),
            category,
            tags,
            status: format!("{:?}", service.status),
            stone: StoneRef {
                id: state.stone_id.clone(),
                name: state.stone_name.clone(),
                endpoint: format!("http://127.0.0.1:{}", state.api_port),
            },
            connection: conn,
        });
    }

    ServiceDiscoveryResponse {
        found: !services.is_empty(),
        services,
        source: "local".to_string(),
        cache_age_seconds: None,
        timestamp: Utc::now(),
    }
}

/// Find services across the garden (local + remote stones)
///
/// Always checks both local registry and topology cache.
/// The `fresh` parameter controls whether to do active network discovery
/// (UDP broadcast) in addition to checking the cache.
pub async fn find_services(
    criteria: &ServiceSearchCriteria,
    state: &AppState,
    fresh: bool,
) -> ServiceDiscoveryResponse {
    let start = std::time::Instant::now();
    let mut all_services = Vec::new();

    // 1. Search local stone first (zero latency)
    let local_services = find_local_services(criteria, state).await;
    all_services.extend(local_services);

    // 2. Always check topology cache for remote services
    // The cache is populated by chirps from other stones
    let cached_services = find_services_in_topology_cache(criteria, state).await;
    all_services.extend(cached_services);

    // 3. If fresh requested, do active network discovery
    // This triggers UDP broadcast and waits for responses
    if fresh {
        // TODO: Implement active discovery that triggers UDP broadcast
        // For now, fresh just ensures we check the cache (which we always do now)
        tracing::debug!("Fresh mode: topology cache already checked");
    }

    let elapsed = start.elapsed();
    tracing::debug!(
        criteria = ?criteria,
        found = all_services.len(),
        duration_ms = elapsed.as_millis(),
        "Service discovery completed"
    );

    ServiceDiscoveryResponse {
        found: !all_services.is_empty(),
        services: all_services,
        source: if fresh { "fresh" } else { "cache" }.to_string(),
        cache_age_seconds: None, // TODO: Track cache age
        timestamp: Utc::now(),
    }
}

/// Find services from topology cache (populated by chirps from other stones)
///
/// This is the primary method for cross-garden discovery.
/// Each stone chirps its services every 30s, and we cache that data.
/// No network requests needed - just read from cache.
async fn find_services_in_topology_cache(
    criteria: &ServiceSearchCriteria,
    state: &AppState,
) -> Vec<FoundService> {
    let stones = topology::get_all_stones(&state.topology_cache).await;
    let mut results = Vec::new();

    for stone in stones {
        // Skip if no services (stone hasn't chirped yet or has none)
        if stone.services.is_empty() {
            continue;
        }

        for svc in &stone.services {
            // Only include running services
            if svc.status != garden_common::SERVICE_RUNNING {
                continue;
            }

            // Check if matches criteria
            if !matches_criteria(criteria, &svc.name, &svc.offering, &svc.category, &[]) {
                continue;
            }

            // Infer protocol and resolve connection
            let protocol = connection::infer_protocol(&svc.offering, &svc.category, state).await;

            // Get port from offering manifest
            let port = get_offering_port(&svc.offering, state).await;

            let conn = connection::resolve_connection(
                &stone.stone_name,
                &stone.endpoint,
                port,
                &protocol,
                None,
            );

            results.push(FoundService {
                name: svc.name.clone(),
                offering: svc.offering.clone(),
                category: svc.category.clone(),
                tags: vec![],
                status: svc.status.clone(),
                stone: StoneRef {
                    id: stone.stone_id.clone(),
                    name: stone.stone_name.clone(),
                    endpoint: stone.endpoint.clone(),
                },
                connection: conn,
            });
        }
    }

    results
}

/// Get default port from offering manifest
///
/// Looks up the offering's manifest and returns the first port mapping.
/// Returns 8080 as fallback if manifest not found or has no ports.
async fn get_offering_port(offering: &str, state: &AppState) -> u16 {
    let manifests = state.manifests.read().await;
    
    // Find manifest by name
    if let Some(manifest) = manifests.iter().find(|m| m.name.eq_ignore_ascii_case(offering)) {
        // Return first port from port mappings (host_port, container_port)
        if let Some((host_port, _)) = manifest.ports.first() {
            return *host_port;
        }
    }
    
    tracing::warn!(
        offering = %offering,
        "Offering manifest not found or has no port mappings, using default 8080"
    );
    8080 // Generic default
}

/// Find services on remote stones via HTTP requests (legacy, slower)
///
/// This is the fallback method that makes HTTP requests to each stone.
/// Prefer find_services_in_topology_cache for better performance.
#[allow(dead_code)]
async fn find_remote_services(
    criteria: &ServiceSearchCriteria,
    state: &AppState,
) -> Vec<FoundService> {
    let stones = topology::get_all_stones(&state.topology_cache).await;
    let mut results = Vec::new();

    // Query each remote stone in parallel
    let timeout = Duration::from_secs(2);
    let tasks: Vec<_> = stones
        .into_iter()
        .map(|stone| {
            let criteria = criteria.clone();
            let state_clone = state.clone();
            tokio::spawn(async move {
                fetch_remote_services(&stone.endpoint, &criteria, &stone, timeout, &state_clone).await
            })
        })
        .collect();

    for task in tasks {
        match task.await {
            Ok(Ok(services)) => results.extend(services),
            Ok(Err(e)) => {
                tracing::debug!(error = ?e, "Failed to fetch services from remote stone");
            }
            Err(e) => {
                tracing::debug!(error = ?e, "Task join error while fetching remote services");
            }
        }
    }

    results
}

/// Fetch services from a single remote stone
async fn fetch_remote_services(
    endpoint: &str,
    criteria: &ServiceSearchCriteria,
    stone: &topology::TopologyEntry,
    timeout: Duration,
    state: &AppState,
) -> anyhow::Result<Vec<FoundService>> {
    let client = reqwest::Client::builder()
        .timeout(timeout)
        .build()?;

    // Build query URL
    let mut url = format!("{}/api/v1/services", endpoint.trim_end_matches('/'));
    let mut query_params = Vec::new();

    if let Some(ref name) = criteria.name {
        query_params.push(format!("name={}", name));
    }
    if let Some(ref category) = criteria.category {
        query_params.push(format!("category={}", category));
    }
    if let Some(ref tag) = criteria.tag {
        query_params.push(format!("tag={}", tag));
    }

    if !query_params.is_empty() {
        url = format!("{}?{}", url, query_params.join("&"));
    }

    let response = client.get(&url).send().await?;

    if !response.status().is_success() {
        anyhow::bail!("Remote stone returned error: {}", response.status());
    }

    // Parse response
    let services: Vec<garden_common::ServiceInfo> = response.json().await?;

    // Convert to FoundService with connection resolution
    let mut results = Vec::new();
    for service in services {
        if service.status != ServiceStatus::Running {
            continue;
        }

        // Infer protocol and resolve connection
        let category = service.offering.clone(); // Use offering as category fallback
        let protocol = connection::infer_protocol(&service.offering, &category, state).await;

        let conn = connection::resolve_connection(
            &stone.stone_name,
            &stone.endpoint,
            service.ports.native,
            &protocol,
            None,
        );

        results.push(FoundService {
            name: service.name,
            offering: service.offering,
            category,
            tags: vec![],
            status: format!("{:?}", service.status),
            stone: StoneRef {
                id: stone.stone_id.clone(),
                name: stone.stone_name.clone(),
                endpoint: stone.endpoint.clone(),
            },
            connection: conn,
        });
    }

    Ok(results)
}

/// Check if a service matches the search criteria
fn matches_criteria(
    criteria: &ServiceSearchCriteria,
    name: &str,
    offering: &str,
    category: &str,
    tags: &[String],
) -> bool {
    // Name match (exact or offering match)
    if let Some(ref search_name) = criteria.name {
        let lower_search = search_name.to_lowercase();
        let lower_name = name.to_lowercase();
        let lower_offering = offering.to_lowercase();

        if lower_name != lower_search && lower_offering != lower_search {
            return false;
        }
    }

    // Category match
    if let Some(ref search_cat) = criteria.category {
        if category.to_lowercase() != search_cat.to_lowercase() {
            return false;
        }
    }

    // Tag match (any tag matches)
    if let Some(ref search_tag) = criteria.tag {
        let lower_search = search_tag.to_lowercase();
        let has_tag = tags.iter().any(|t| t.to_lowercase() == lower_search);
        if !has_tag {
            return false;
        }
    }

    true
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_parse_name_search() {
        let criteria = ServiceSearchCriteria::parse("mongodb");
        assert_eq!(criteria.name, Some("mongodb".to_string()));
        assert!(criteria.category.is_none());
        assert!(criteria.tag.is_none());
    }

    #[test]
    fn test_parse_category_prefix() {
        let criteria = ServiceSearchCriteria::parse("c:database");
        assert!(criteria.name.is_none());
        assert_eq!(criteria.category, Some("database".to_string()));

        let criteria = ServiceSearchCriteria::parse("cat:cache");
        assert_eq!(criteria.category, Some("cache".to_string()));

        let criteria = ServiceSearchCriteria::parse("category:search");
        assert_eq!(criteria.category, Some("search".to_string()));
    }

    #[test]
    fn test_parse_tag_prefix() {
        let criteria = ServiceSearchCriteria::parse("t:nosql");
        assert!(criteria.name.is_none());
        assert_eq!(criteria.tag, Some("nosql".to_string()));

        let criteria = ServiceSearchCriteria::parse("tag:document");
        assert_eq!(criteria.tag, Some("document".to_string()));

        let criteria = ServiceSearchCriteria::parse("tags:realtime");
        assert_eq!(criteria.tag, Some("realtime".to_string()));
    }

    #[test]
    fn test_parse_implicit_category() {
        // Known categories should be detected
        let criteria = ServiceSearchCriteria::parse("database");
        assert_eq!(criteria.category, Some("database".to_string()));

        let criteria = ServiceSearchCriteria::parse("cache");
        assert_eq!(criteria.category, Some("cache".to_string()));

        // Unknown words default to name search
        let criteria = ServiceSearchCriteria::parse("myservice");
        assert_eq!(criteria.name, Some("myservice".to_string()));
    }

    #[test]
    fn test_matches_criteria_name() {
        let criteria = ServiceSearchCriteria::by_name("mongodb");

        assert!(matches_criteria(&criteria, "mongodb", "mongodb", "database", &[]));
        assert!(matches_criteria(&criteria, "zen-offering-mongodb", "mongodb", "database", &[]));
        assert!(!matches_criteria(&criteria, "redis", "redis", "cache", &[]));
    }

    #[test]
    fn test_matches_criteria_category() {
        let criteria = ServiceSearchCriteria::by_category("database");

        assert!(matches_criteria(&criteria, "mongodb", "mongodb", "database", &[]));
        assert!(matches_criteria(&criteria, "postgres", "postgres", "database", &[]));
        assert!(!matches_criteria(&criteria, "redis", "redis", "cache", &[]));
    }

    #[test]
    fn test_matches_criteria_tag() {
        let criteria = ServiceSearchCriteria::by_tag("nosql");

        assert!(matches_criteria(
            &criteria,
            "mongodb",
            "mongodb",
            "database",
            &["document".to_string(), "nosql".to_string()]
        ));
        assert!(!matches_criteria(
            &criteria,
            "postgres",
            "postgres",
            "database",
            &["sql".to_string(), "relational".to_string()]
        ));
    }

    #[test]
    fn test_is_name_search() {
        assert!(ServiceSearchCriteria::by_name("mongodb").is_name_search());
        assert!(!ServiceSearchCriteria::by_category("database").is_name_search());
        assert!(!ServiceSearchCriteria::by_tag("nosql").is_name_search());
    }
}
