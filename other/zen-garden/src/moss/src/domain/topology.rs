//! Topology management and stone discovery
//!
//! In-memory cache of discovered stones for placement, service discovery,
//! and Wake-on-LAN support.
//!
//! ## Design: Offline Marking vs TTL Eviction
//!
//! Instead of evicting stones after a short TTL, stones are marked as Offline
//! when not seen for 90 seconds. This preserves MAC addresses for Wake-on-LAN
//! even after stones shut down.
//!
//! - Max 64 offline stones tracked (LRU eviction when cap reached)
//! - Offline stones evicted after 24 hours
//! - No disk persistence - cache rebuilds on moss restart

use chrono::{DateTime, Duration, Utc};
use garden_common::{TopologyServiceEntry, HardwareCapabilities, StoneStatus};
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::RwLock;

/// Maximum number of offline stones to track
const MAX_OFFLINE_STONES: usize = 64;

/// Threshold for marking a stone as offline (seconds since last seen)
const OFFLINE_THRESHOLD_SECS: i64 = 90;

/// TTL for offline stones before eviction (hours)
const OFFLINE_EVICTION_HOURS: i64 = 24;

/// Discovered stone entry in topology cache
///
/// Used for both peer topology cache and self topology entry.
/// Health progresses: starting → initializing → thriving/degraded
/// This is also the chirp payload - chirps broadcast the full TopologyEntry.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TopologyEntry {
    pub stone_id: String,
    pub stone_name: String,
    pub endpoint: String,
    pub moss_version: String,
    /// Services running on this stone (lightweight topology representation)
    pub services: Vec<TopologyServiceEntry>,
    /// MAC address for Wake-on-LAN support
    pub mac: Option<String>,
    /// Health status: use health_status constants (STARTING, INITIALIZING, THRIVING, DEGRADED)
    pub health: String,
    /// Hardware capabilities - available after detection (None during early boot)
    pub capabilities: Option<HardwareCapabilities>,
    /// Current connectivity status
    pub status: StoneStatus,
    pub discovered_at: DateTime<Utc>,
    pub last_seen: DateTime<Utc>,
}

/// In-memory topology cache
///
/// Stores all discovered stones indexed by stone_id.
/// Populated from UDP discovery responses and mDNS announcements.
pub type TopologyCache = Arc<RwLock<HashMap<String, TopologyEntry>>>;

/// Add or update a stone from a chirp (received TopologyEntry)
///
/// Primary method for updating topology cache from stone chirps.
/// The chirp IS a TopologyEntry being broadcast.
pub async fn upsert_from_chirp(cache: &TopologyCache, mut chirped_entry: TopologyEntry) {
    let mut map = cache.write().await;
    let now = Utc::now();

    if let Some(entry) = map.get_mut(&chirped_entry.stone_id) {
        // Update existing entry - stone is back online
        entry.stone_name = chirped_entry.stone_name;
        entry.endpoint = chirped_entry.endpoint;
        entry.moss_version = chirped_entry.moss_version;
        entry.services = chirped_entry.services;
        entry.health = chirped_entry.health;
        entry.capabilities = chirped_entry.capabilities;
        // Only update MAC if provided (preserve existing if None)
        if chirped_entry.mac.is_some() {
            entry.mac = chirped_entry.mac;
        }
        entry.status = StoneStatus::Online;
        entry.last_seen = now;
    } else {
        // Insert new entry with current timestamp
        chirped_entry.status = StoneStatus::Online;
        chirped_entry.discovered_at = now;
        chirped_entry.last_seen = now;
        map.insert(chirped_entry.stone_id.clone(), chirped_entry);
    }
}

/// Get all stones from topology cache (both online and offline)
pub async fn get_all_stones(cache: &TopologyCache) -> Vec<TopologyEntry> {
    let map = cache.read().await;
    map.values().cloned().collect()
}

/// Get only online stones from topology cache
pub async fn get_online_stones(cache: &TopologyCache) -> Vec<TopologyEntry> {
    let map = cache.read().await;
    map.values()
        .filter(|e| e.status == StoneStatus::Online)
        .cloned()
        .collect()
}

/// Get a specific stone by ID (regardless of status)
pub async fn get_stone_by_id(cache: &TopologyCache, stone_id: &str) -> Option<TopologyEntry> {
    let map = cache.read().await;
    map.get(stone_id).cloned()
}

/// Get a specific stone by name (regardless of status)
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

/// Count online stones in topology cache
pub async fn count_online_stones(cache: &TopologyCache) -> usize {
    let map = cache.read().await;
    map.values().filter(|e| e.status == StoneStatus::Online).count()
}

/// Mark stale stones as offline and evict very old offline stones
///
/// This replaces the old `prune_stale_stones` function with offline-marking semantics:
/// 1. Stones not seen for 90s → marked Offline (but retained)
/// 2. Offline stones older than 24h → evicted
/// 3. If more than MAX_OFFLINE_STONES offline → evict oldest (LRU)
///
/// Returns (marked_offline_count, evicted_count)
pub async fn maintain_topology(cache: &TopologyCache) -> (usize, usize) {
    let mut map = cache.write().await;
    let now = Utc::now();
    let offline_threshold = Duration::seconds(OFFLINE_THRESHOLD_SECS);
    let eviction_threshold = Duration::hours(OFFLINE_EVICTION_HOURS);

    let mut marked_offline = 0;
    let mut evicted;

    // Phase 1: Mark stale online stones as offline
    for entry in map.values_mut() {
        if entry.status == StoneStatus::Online {
            let age = now.signed_duration_since(entry.last_seen);
            if age > offline_threshold {
                entry.status = StoneStatus::Offline;
                marked_offline += 1;
                tracing::debug!(
                    stone_name = %entry.stone_name,
                    last_seen = %entry.last_seen,
                    "Stone marked offline"
                );
            }
        }
    }

    // Phase 2: Evict offline stones older than 24h
    let initial_count = map.len();
    map.retain(|_, entry| {
        if entry.status == StoneStatus::Offline {
            let age = now.signed_duration_since(entry.last_seen);
            age <= eviction_threshold
        } else {
            true
        }
    });
    evicted = initial_count - map.len();

    // Phase 3: Enforce max offline stone cap (LRU eviction)
    let offline_count = map.values().filter(|e| e.status == StoneStatus::Offline).count();
    if offline_count > MAX_OFFLINE_STONES {
        let excess = offline_count - MAX_OFFLINE_STONES;

        // Collect offline stones sorted by last_seen (oldest first)
        let mut offline_stones: Vec<_> = map.iter()
            .filter(|(_, e)| e.status == StoneStatus::Offline)
            .map(|(id, e)| (id.clone(), e.last_seen))
            .collect();
        offline_stones.sort_by_key(|(_, last_seen)| *last_seen);

        // Remove the oldest ones
        for (stone_id, _) in offline_stones.into_iter().take(excess) {
            if let Some(entry) = map.remove(&stone_id) {
                tracing::debug!(
                    stone_name = %entry.stone_name,
                    "Evicted offline stone (LRU cap reached)"
                );
                evicted += 1;
            }
        }
    }

    (marked_offline, evicted)
}

/// Legacy function for compatibility - now calls maintain_topology
pub async fn prune_stale_stones(cache: &TopologyCache, _stale_threshold_minutes: i64) -> usize {
    let (marked, evicted) = maintain_topology(cache).await;
    marked + evicted
}

/// Mark a stone as offline by stone_id (graceful goodbye)
///
/// Called when receiving a STONE_GOODBYE announcement.
/// Returns true if the stone was found and marked offline.
pub async fn mark_stone_offline(cache: &TopologyCache, stone_id: &str) -> bool {
    let mut map = cache.write().await;
    if let Some(entry) = map.get_mut(stone_id) {
        if entry.status != StoneStatus::Offline {
            entry.status = StoneStatus::Offline;
            tracing::info!(
                stone_name = %entry.stone_name,
                "Stone marked offline (goodbye received)"
            );
            return true;
        }
    }
    false
}

/// Remove a specific stone from the cache (explicit forget)
pub async fn forget_stone(cache: &TopologyCache, stone_name: &str) -> bool {
    let mut map = cache.write().await;
    let stone_id = map.values()
        .find(|e| e.stone_name == stone_name)
        .map(|e| e.stone_id.clone());

    if let Some(id) = stone_id {
        map.remove(&id).is_some()
    } else {
        false
    }
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
        let stone = stone.unwrap();
        assert_eq!(stone.stone_name, "oak");
        assert_eq!(stone.status, StoneStatus::Online);

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
    async fn test_upsert_preserves_mac() {
        let cache = make_test_cache();

        // First upsert with MAC
        upsert_stone_full(
            &cache,
            "stone-123".to_string(),
            "oak".to_string(),
            "http://192.168.1.10:7123".to_string(),
            "0.1.0".to_string(),
            vec![],
            Some("AA:BB:CC:DD:EE:FF".to_string()),
        ).await;

        // Update without MAC - should preserve existing
        upsert_stone_full(
            &cache,
            "stone-123".to_string(),
            "oak".to_string(),
            "http://192.168.1.99:7123".to_string(),
            "0.1.1".to_string(),
            vec![],
            None,
        ).await;

        let stone = get_stone_by_id(&cache, "stone-123").await.unwrap();
        assert_eq!(stone.mac, Some("AA:BB:CC:DD:EE:FF".to_string()));
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

    #[tokio::test]
    async fn test_stone_status_display() {
        assert_eq!(format!("{}", StoneStatus::Online), "online");
        assert_eq!(format!("{}", StoneStatus::Offline), "offline");
    }

    #[tokio::test]
    async fn test_forget_stone() {
        let cache = make_test_cache();

        upsert_stone(&cache, "s1".to_string(), "oak".to_string(), "http://10.0.0.1:7123".to_string(), "0.1.0".to_string()).await;
        upsert_stone(&cache, "s2".to_string(), "cedar".to_string(), "http://10.0.0.2:7123".to_string(), "0.1.0".to_string()).await;

        assert_eq!(count_stones(&cache).await, 2);

        let removed = forget_stone(&cache, "oak").await;
        assert!(removed);
        assert_eq!(count_stones(&cache).await, 1);

        let removed_again = forget_stone(&cache, "oak").await;
        assert!(!removed_again);
    }
}
