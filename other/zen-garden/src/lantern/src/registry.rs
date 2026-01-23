use anyhow::Result;
use std::sync::Arc;
use tokio::sync::RwLock;

use crate::state::GardenTopology;

pub struct Registry {
    #[allow(dead_code)]
    db_path: String,
    topology: Arc<RwLock<GardenTopology>>,
}

impl Registry {
    pub async fn new(db_path: String, topology: Arc<RwLock<GardenTopology>>) -> Result<Self> {
        // TODO: Initialize SQLite database
        // CREATE TABLE IF NOT EXISTS topology (id INTEGER PRIMARY KEY CHECK (id = 1), state JSON, last_updated TIMESTAMP)
        // CREATE TABLE IF NOT EXISTS events (id INTEGER PRIMARY KEY AUTOINCREMENT, timestamp TIMESTAMP, event JSON)

        tracing::info!(db_path = %db_path, "Registry initialized (SQLite pending)");

        Ok(Self { db_path, topology })
    }

    /// Register a stone with Lantern
    ///
    /// Uses stone_id as the cache key when available, falling back to stone_name.
    /// This ensures stable topology caching even when hostname changes.
    pub async fn register_stone(
        &self,
        stone_id: Option<&str>,
        stone_name: &str,
        endpoint: &str,
        services: Vec<garden_common::RegisterServiceInfo>,
    ) -> Result<()> {
        use chrono::Utc;
        use crate::state::{InternalStoneState, InternalServiceState, StoneStatus};

        let mut topology = self.topology.write().await;

        let now = Utc::now();

        // Use stone_id as cache key when available, otherwise fall back to stone_name
        let cache_key = stone_id.unwrap_or(stone_name).to_string();
        let stone_exists = topology.stones.contains_key(&cache_key);

        if let Some(stone) = topology.stones.get_mut(&cache_key) {
            // Update existing stone
            stone.last_seen = now;
            stone.status = StoneStatus::Online;
            stone.offline_since = None;
            stone.endpoint = endpoint.to_string();

            // Update stone_name if it changed (hostname was updated)
            if stone.name != stone_name {
                tracing::info!(
                    stone_id = ?stone_id,
                    old_name = %stone.name,
                    new_name = %stone_name,
                    "Stone hostname changed"
                );
                stone.name = stone_name.to_string();
            }

            // Update services
            stone.services.clear();
            for svc in services {
                stone.services.insert(
                    svc.name.clone(),
                    InternalServiceState {
                        name: svc.name,
                        service_type: svc.service_type,
                        status: svc.status,
                        connection_string: svc.connection_string,
                    },
                );
            }
        } else {
            // New stone registration
            let mut services_map = std::collections::HashMap::new();
            for svc in services {
                services_map.insert(
                    svc.name.clone(),
                    InternalServiceState {
                        name: svc.name,
                        service_type: svc.service_type,
                        status: svc.status,
                        connection_string: svc.connection_string,
                    },
                );
            }

            topology.stones.insert(
                cache_key.clone(),
                InternalStoneState {
                    stone_id: stone_id.map(|s| s.to_string()),
                    name: stone_name.to_string(),
                    endpoint: endpoint.to_string(),
                    status: StoneStatus::Online,
                    services: services_map,
                    last_seen: now,
                    first_seen: now,
                    offline_since: None,
                },
            );
        }

        topology.last_updated = now;

        tracing::info!(
            stone_id = ?stone_id,
            stone_name = %stone_name,
            cache_key = %cache_key,
            endpoint = %endpoint,
            is_new = !stone_exists,
            "Stone registered"
        );

        Ok(())
    }

    pub async fn resolve_service(
        &self,
        service_type: &str,
    ) -> Result<Option<garden_common::ResolveResponse>> {
        use crate::state::StoneStatus;

        let topology = self.topology.read().await;

        // Find first online stone with matching service type
        for stone in topology.stones.values() {
            if stone.status != StoneStatus::Online {
                continue;
            }

            for service in stone.services.values() {
                if service.service_type == service_type && service.status == "running" {
                    return Ok(Some(garden_common::ResolveResponse {
                        stone_name: stone.name.clone(),
                        endpoint: stone.endpoint.clone(),
                        service: garden_common::ResolveServiceInfo {
                            name: service.name.clone(),
                            service_type: service.service_type.clone(),
                            connection_string: service.connection_string.clone(),
                        },
                    }));
                }
            }
        }

        Ok(None)
    }

    #[allow(dead_code)]
    pub async fn persist_topology(&self) -> Result<()> {
        let topology = self.topology.read().await;
        let json = serde_json::to_string(&topology.to_json())?;

        // TODO: Persist to SQLite
        tracing::debug!(topology_size = json.len(), "Topology persisted (placeholder)");

        Ok(())
    }
}

#[allow(dead_code)]
pub async fn run_ttl_cleanup(registry: Arc<Registry>) {
    use chrono::{Duration, Utc};
    use crate::state::StoneStatus;

    loop {
        tokio::time::sleep(tokio::time::Duration::from_secs(10)).await;

        let mut topology = registry.topology.write().await;
        let now = Utc::now();
        let ttl_threshold = Duration::seconds(60);

        for stone in topology.stones.values_mut() {
            if stone.status == StoneStatus::Online {
                let elapsed = now.signed_duration_since(stone.last_seen);
                if elapsed > ttl_threshold {
                    stone.status = StoneStatus::Offline;
                    stone.offline_since = Some(now);
                    
                    tracing::warn!(
                        stone_name = %stone.name,
                        last_seen = %stone.last_seen,
                        "Stone marked offline (TTL expired)"
                    );
                }
            }
        }
    }
}
