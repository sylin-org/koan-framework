use anyhow::Result;
use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};
use garden_common::HardwareCapabilities;

const CACHE_TTL: Duration = Duration::from_secs(90);

#[derive(Clone)]
pub struct CachedStone {
    pub endpoint: String,
    pub capabilities: HardwareCapabilities,
    pub last_seen: Instant,
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

    #[allow(dead_code)]
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

    pub fn insert(&self, stone_name: String, endpoint: String, capabilities: HardwareCapabilities) {
        let mut cache = self.stones.lock().unwrap();
        cache.insert(
            stone_name.clone(),
            CachedStone {
                endpoint,
                capabilities,
                last_seen: Instant::now(),
            },
        );
        tracing::debug!(stone = %stone_name, "Cached stone discovery");
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

    #[allow(dead_code)]
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

    #[allow(dead_code)]
    pub fn clear(&self) {
        let mut cache = self.stones.lock().unwrap();
        cache.clear();
        tracing::debug!("Cleared stone cache");
    }

    #[allow(dead_code)]
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

/// Fetch stone capabilities from endpoint and cache them
#[allow(dead_code)]
pub async fn fetch_and_cache_stone(
    client: &reqwest::Client,
    endpoint: &str,
    cache: &StoneCache,
) -> Result<CachedStone> {
    let caps_url = format!("{}/capabilities", endpoint.trim_end_matches('/'));
    let capabilities: HardwareCapabilities = client
        .get(&caps_url)
        .timeout(Duration::from_secs(5))
        .send()
        .await?
        .json()
        .await?;
    
    let stone_name = capabilities.stone_name.clone();
    cache.insert(stone_name.clone(), endpoint.to_string(), capabilities.clone());
    
    Ok(CachedStone {
        endpoint: endpoint.to_string(),
        capabilities,
        last_seen: Instant::now(),
    })
}
