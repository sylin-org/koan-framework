use chrono::{DateTime, Utc};
use std::collections::HashMap;
use garden_common::{LanternStoneState, LanternServiceState, LanternTopology};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum StoneStatus {
    Online,
    Offline,
}

pub struct GardenTopology {
    pub stones: HashMap<String, InternalStoneState>,
    pub last_updated: DateTime<Utc>,
}

impl Default for GardenTopology {
    fn default() -> Self {
        Self::new()
    }
}

impl GardenTopology {
    pub fn new() -> Self {
        Self {
            stones: HashMap::new(),
            last_updated: Utc::now(),
        }
    }

    pub fn stones_online_count(&self) -> usize {
        self.stones
            .values()
            .filter(|s| s.status == StoneStatus::Online)
            .count()
    }

    pub fn to_json(&self) -> LanternTopology {
        let stones: Vec<LanternStoneState> = self.stones.values().map(|s| {
            LanternStoneState {
                stone_id: s.stone_id.clone(),
                name: s.name.clone(),
                endpoint: s.endpoint.clone(),
                status: if s.status == StoneStatus::Online { "online" } else { "offline" }.to_string(),
                services: s.services.values().map(|svc| LanternServiceState {
                    name: svc.name.clone(),
                    service_type: svc.service_type.clone(),
                    status: svc.status.clone(),
                    connection_string: svc.connection_string.clone(),
                }).collect(),
                last_seen: s.last_seen.to_rfc3339(),
                first_seen: s.first_seen.to_rfc3339(),
                offline_since: s.offline_since.map(|dt| dt.to_rfc3339()),
            }
        }).collect();

        LanternTopology {
            stones,
            last_updated: self.last_updated.to_rfc3339(),
        }
    }
}

pub struct InternalStoneState {
    /// Unique stone identifier (GUID v7) - stable across hostname changes
    pub stone_id: Option<String>,
    pub name: String,
    pub endpoint: String,
    pub status: StoneStatus,
    pub services: HashMap<String, InternalServiceState>,
    pub last_seen: DateTime<Utc>,
    pub first_seen: DateTime<Utc>,
    pub offline_since: Option<DateTime<Utc>>,
}

// Make InternalServiceState public for tests
pub struct InternalServiceState {
    pub name: String,
    pub service_type: String,
    pub status: String,
    pub connection_string: String,
}
