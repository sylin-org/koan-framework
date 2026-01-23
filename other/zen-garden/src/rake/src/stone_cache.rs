use anyhow::Result;
use once_cell::sync::Lazy;
use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};
use garden_common::{GardenApiResponse, HardwareCapabilities};
use crate::client::{CachedStoneOps, CachedStoneInfo};

const CACHE_TTL: Duration = Duration::from_secs(90);

/// Global stone cache singleton (hot cache architecture)
///
/// Single source of truth for stone discovery caching across all commands.
/// Used by observe, dispatch, and any command needing cached stone lookups.
/// TTL is 90 seconds to balance freshness with performance.
pub static GLOBAL_CACHE: Lazy<StoneCache> = Lazy::new(StoneCache::new);

#[derive(Clone)]
pub struct CachedStone {
    pub endpoint: String,
    pub capabilities: HardwareCapabilities,
    pub last_seen: Instant,
}

impl CachedStone {
    /// Get the cache key for this stone (stone_id if available, otherwise stone_name)
    pub fn cache_key(&self) -> String {
        self.capabilities.stone_id.clone()
            .unwrap_or_else(|| self.capabilities.stone_name.clone())
    }
}

pub struct StoneCache {
    stones: Arc<Mutex<HashMap<String, CachedStone>>>,
}

impl StoneCache {
    pub fn new() -> Self {
        Self {
            stones: Arc::new(Mutex::new(HashMap::new())),
        }
    }

    pub fn get(&self, stone_name: &str) -> Option<CachedStone> {
        let mut cache = self.stones.lock().unwrap();
        
        if let Some(cached) = cache.get(stone_name) {
            // Check if still valid (TTL not expired)
            if cached.last_seen.elapsed() < CACHE_TTL {
                tracing::info!(stone = %stone_name, age_secs = %cached.last_seen.elapsed().as_secs(), "Cache hit - returning cached stone");
                return Some(cached.clone());
            } else {
                // Expired, remove from cache
                tracing::info!(stone = %stone_name, "Cache entry expired (TTL 90s)");
                cache.remove(stone_name);
            }
        }
        
        tracing::debug!(stone = %stone_name, "Cache miss");
        None
    }

    /// Insert a stone into the cache
    ///
    /// Uses stone_id as the cache key when available, falling back to stone_name.
    /// This ensures stable caching even when hostname changes.
    pub fn insert(&self, endpoint: String, capabilities: HardwareCapabilities) {
        let mut cache = self.stones.lock().unwrap();

        // Use stone_id as key when available, otherwise use stone_name
        let cache_key = capabilities.stone_id.clone()
            .unwrap_or_else(|| capabilities.stone_name.clone());

        let stone_name = capabilities.stone_name.clone();

        cache.insert(
            cache_key.clone(),
            CachedStone {
                endpoint,
                capabilities,
                last_seen: Instant::now(),
            },
        );
        tracing::debug!(stone = %stone_name, key = %cache_key, "Cached stone discovery");
    }

    pub fn get_all(&self) -> Vec<CachedStone> {
        let mut cache = self.stones.lock().unwrap();
        
        // Remove expired entries
        cache.retain(|stone_name, cached| {
            let valid = cached.last_seen.elapsed() < CACHE_TTL;
            if !valid {
                tracing::debug!(stone = %stone_name, "Cache entry expired during get_all");
            }
            valid
        });
        
        cache.values().cloned().collect()
    }

    pub fn refresh_stone(&self, stone_name: &str) -> bool {
        let mut cache = self.stones.lock().unwrap();
        if let Some(cached) = cache.get_mut(stone_name) {
            cached.last_seen = Instant::now();
            tracing::debug!(stone = %stone_name, "Refreshed cache TTL");
            true
        } else {
            false
        }
    }

    pub fn clear(&self) {
        let mut cache = self.stones.lock().unwrap();
        cache.clear();
        tracing::debug!("Cleared stone cache");
    }

    pub fn count(&self) -> usize {
        let cache = self.stones.lock().unwrap();
        cache.len()
    }
}

impl Default for StoneCache {
    fn default() -> Self {
        Self::new()
    }
}

// Implement CachedStoneOps trait for StoneCache
// This allows GLOBAL_CACHE to be used via the trait interface in dispatch/client
impl CachedStoneOps for StoneCache {
    fn get(&self, stone_name: &str) -> Option<CachedStoneInfo> {
        StoneCache::get(self, stone_name).map(|cached| CachedStoneInfo {
            endpoint: cached.endpoint,
        })
    }

    fn insert(&self, endpoint: String, capabilities: HardwareCapabilities) {
        StoneCache::insert(self, endpoint, capabilities);
    }
}

/// Fetch stone capabilities from endpoint and cache them
pub async fn fetch_and_cache_stone(
    client: &reqwest::Client,
    endpoint: &str,
    cache: &StoneCache,
) -> Result<CachedStone> {
    let caps_url = format!("{}/capabilities", endpoint.trim_end_matches('/'));
    let response: GardenApiResponse<HardwareCapabilities> = client
        .get(&caps_url)
        .timeout(Duration::from_secs(5))
        .send()
        .await?
        .json()
        .await?;
    let capabilities = response.data;

    cache.insert(endpoint.to_string(), capabilities.clone());

    Ok(CachedStone {
        endpoint: endpoint.to_string(),
        capabilities,
        last_seen: Instant::now(),
    })
}
