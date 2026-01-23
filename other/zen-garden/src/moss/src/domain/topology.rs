//! Topology management and stone discovery
//!
//! In-memory cache of discovered stones for placement and service discovery.
//! No disk persistence - rebuilt from mDNS/UDP discovery on each startup.

use chrono::{DateTime, Utc};
use garden_common::ChirpServiceInfo;
use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::RwLock;

/// Discovered stone entry in topology cache
#[derive(Debug, Clone)]
pub struct TopologyEntry {
    pub stone_id: String,
    pub stone_name: String,
    pub endpoint: String,
    pub moss_version: String,
    /// Services running on this stone (from chirps)
    pub services: Vec<ChirpServiceInfo>,
    pub discovered_at: DateTime<Utc>,
    pub last_seen: DateTime<Utc>,
}

/// In-memory topology cache
///
/// Stores all discovered stones indexed by stone_id.
/// Populated from UDP discovery responses and mDNS announcements.
pub type TopologyCache = Arc<RwLock<HashMap<String, TopologyEntry>>>;

/// Add or update a stone in the topology cache (without services)
///
/// Use `upsert_stone_with_services` when service info is available from chirps.
pub async fn upsert_stone(
    cache: &TopologyCache,
    stone_id: String,
    stone_name: String,
    endpoint: String,
    moss_version: String,
) {
    upsert_stone_with_services(cache, stone_id, stone_name, endpoint, moss_version, vec![]).await;
}

/// Add or update a stone in the topology cache with service information
///
/// Called when processing stone chirps that include service inventory.
pub async fn upsert_stone_with_services(
    cache: &TopologyCache,
    stone_id: String,
    stone_name: String,
    endpoint: String,
    moss_version: String,
    services: Vec<ChirpServiceInfo>,
) {
    let mut map = cache.write().await;
    let now = Utc::now();

    if let Some(entry) = map.get_mut(&stone_id) {
        // Update existing entry
        entry.stone_name = stone_name;
        entry.endpoint = endpoint;
        entry.moss_version = moss_version;
        entry.services = services;
        entry.last_seen = now;
    } else {
        // Insert new entry
        map.insert(stone_id.clone(), TopologyEntry {
            stone_id,
            stone_name,
            endpoint,
            moss_version,
            services,
            discovered_at: now,
            last_seen: now,
        });
    }
}

/// Get all stones from topology cache
pub async fn get_all_stones(cache: &TopologyCache) -> Vec<TopologyEntry> {
    let map = cache.read().await;
    map.values().cloned().collect()
}

/// Get a specific stone by ID
pub async fn get_stone_by_id(cache: &TopologyCache, stone_id: &str) -> Option<TopologyEntry> {
    let map = cache.read().await;
    map.get(stone_id).cloned()
}

/// Get a specific stone by name
pub async fn get_stone_by_name(cache: &TopologyCache, stone_name: &str) -> Option<TopologyEntry> {
    let map = cache.read().await;
    map.values()
        .find(|entry| entry.stone_name == stone_name)
        .cloned()
}

/// Count stones in topology cache
pub async fn count_stones(cache: &TopologyCache) -> usize {
    let map = cache.read().await;
    map.len()
}

/// Remove stale stones (not seen in X minutes)
pub async fn prune_stale_stones(cache: &TopologyCache, stale_threshold_minutes: i64) -> usize {
    let mut map = cache.write().await;
    let now = Utc::now();
    let threshold = chrono::Duration::minutes(stale_threshold_minutes);
    
    let initial_count = map.len();
    map.retain(|_, entry| {
        let age = now.signed_duration_since(entry.last_seen);
        age < threshold
    });
    
    initial_count - map.len()
}

#[cfg(test)]
mod tests {
    use super::*;
    
    fn make_test_cache() -> TopologyCache {
        Arc::new(RwLock::new(HashMap::new()))
    }
    
    #[tokio::test]
    async fn test_upsert_and_get() {
        let cache = make_test_cache();
        
        upsert_stone(
            &cache,
            "stone-123".to_string(),
            "oak".to_string(),
            "http://192.168.1.10:7123".to_string(),
            "0.1.0".to_string(),
        ).await;
        
        let stone = get_stone_by_id(&cache, "stone-123").await;
        assert!(stone.is_some());
        assert_eq!(stone.unwrap().stone_name, "oak");
        
        assert_eq!(count_stones(&cache).await, 1);
    }
    
    #[tokio::test]
    async fn test_upsert_updates_existing() {
        let cache = make_test_cache();
        
        upsert_stone(
            &cache,
            "stone-123".to_string(),
            "oak".to_string(),
            "http://192.168.1.10:7123".to_string(),
            "0.1.0".to_string(),
        ).await;
        
        // Update with new endpoint
        upsert_stone(
            &cache,
            "stone-123".to_string(),
            "oak".to_string(),
            "http://192.168.1.99:7123".to_string(),
            "0.1.1".to_string(),
        ).await;
        
        let stone = get_stone_by_id(&cache, "stone-123").await.unwrap();
        assert_eq!(stone.endpoint, "http://192.168.1.99:7123");
        assert_eq!(stone.moss_version, "0.1.1");
        assert_eq!(count_stones(&cache).await, 1); // Still only one entry
    }
    
    #[tokio::test]
    async fn test_get_by_name() {
        let cache = make_test_cache();
        
        upsert_stone(
            &cache,
            "stone-123".to_string(),
            "oak".to_string(),
            "http://192.168.1.10:7123".to_string(),
            "0.1.0".to_string(),
        ).await;
        
        upsert_stone(
            &cache,
            "stone-456".to_string(),
            "cedar".to_string(),
            "http://192.168.1.11:7123".to_string(),
            "0.1.0".to_string(),
        ).await;
        
        let stone = get_stone_by_name(&cache, "cedar").await;
        assert!(stone.is_some());
        assert_eq!(stone.unwrap().stone_id, "stone-456");
    }
    
    #[tokio::test]
    async fn test_get_all_stones() {
        let cache = make_test_cache();
        
        upsert_stone(&cache, "s1".to_string(), "oak".to_string(), "http://10.0.0.1:7123".to_string(), "0.1.0".to_string()).await;
        upsert_stone(&cache, "s2".to_string(), "cedar".to_string(), "http://10.0.0.2:7123".to_string(), "0.1.0".to_string()).await;
        upsert_stone(&cache, "s3".to_string(), "maple".to_string(), "http://10.0.0.3:7123".to_string(), "0.1.0".to_string()).await;
        
        let all = get_all_stones(&cache).await;
        assert_eq!(all.len(), 3);
    }
}
