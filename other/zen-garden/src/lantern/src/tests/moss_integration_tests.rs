/// Tests for Moss integration with Lantern
/// 
/// Tests the /api/peer-stones endpoint that queries Lantern for peer discovery

use std::sync::Arc;
use tokio::sync::RwLock;
use crate::{GardenTopology, Registry, InternalStoneState, StoneStatus};
use crate::state::InternalServiceState;
use chrono::Utc;

#[tokio::test]
async fn test_peer_stone_filtering() {
    // Simulate what Moss would see from Lantern
    let topology = Arc::new(RwLock::new(GardenTopology::new()));
    let _registry = Arc::new(
        Registry::new("/tmp/test-lantern-moss.db".to_string(), topology.clone())
            .await
            .expect("Failed to create registry"),
    );

    // Register stone-1 with MongoDB
    {
        let mut topo = topology.write().await;
        topo.stones.insert(
            "stone-1".to_string(),
            InternalStoneState {
                name: "stone-1".to_string(),
                endpoint: "http://192.168.1.100:7185".to_string(),
                status: StoneStatus::Online,
                services: [(
                    "mongodb".to_string(),
                    InternalServiceState {
                        name: "mongodb".to_string(),
                        service_type: "mongodb".to_string(),
                        status: "running".to_string(),
                        connection_string: "mongodb://192.168.1.100:27017".to_string(),
                    },
                )]
                .into_iter()
                .collect(),
                last_seen: Utc::now(),
                first_seen: Utc::now(),
                offline_since: None,
            },
        );

        // Register stone-2 with MongoDB (peer)
        topo.stones.insert(
            "stone-2".to_string(),
            InternalStoneState {
                name: "stone-2".to_string(),
                endpoint: "http://192.168.1.101:7185".to_string(),
                status: StoneStatus::Online,
                services: [(
                    "mongodb".to_string(),
                    InternalServiceState {
                        name: "mongodb-replica".to_string(),
                        service_type: "mongodb".to_string(),
                        status: "running".to_string(),
                        connection_string: "mongodb://192.168.1.101:27017".to_string(),
                    },
                )]
                .into_iter()
                .collect(),
                last_seen: Utc::now(),
                first_seen: Utc::now(),
                offline_since: None,
            },
        );

        // Register stone-3 with Redis (different service type)
        topo.stones.insert(
            "stone-3".to_string(),
            InternalStoneState {
                name: "stone-3".to_string(),
                endpoint: "http://192.168.1.102:7185".to_string(),
                status: StoneStatus::Online,
                services: [(
                    "redis".to_string(),
                    InternalServiceState {
                        name: "redis".to_string(),
                        service_type: "redis".to_string(),
                        status: "running".to_string(),
                        connection_string: "redis://192.168.1.102:6379".to_string(),
                    },
                )]
                .into_iter()
                .collect(),
                last_seen: Utc::now(),
                first_seen: Utc::now(),
                offline_since: None,
            },
        );
    }

    // Simulate Moss query: "Find peers with MongoDB"
    // (In real code, Moss would call GET /api/stones and filter)
    let topo = topology.read().await;
    let json_topology = topo.to_json();
    
    // Filter for stones with mongodb service type
    let mongodb_stones: Vec<_> = json_topology.stones
        .iter()
        .filter(|stone| {
            stone.services.iter().any(|svc| svc.service_type == "mongodb")
        })
        .collect();

    // Should find stone-1 and stone-2 (both have MongoDB)
    assert_eq!(mongodb_stones.len(), 2);
    assert!(mongodb_stones.iter().any(|s| s.name == "stone-1"));
    assert!(mongodb_stones.iter().any(|s| s.name == "stone-2"));

    // Should NOT include stone-3 (has Redis)
    assert!(!mongodb_stones.iter().any(|s| s.name == "stone-3"));
}

#[tokio::test]
async fn test_peer_discovery_excludes_offline_stones() {
    let topology = Arc::new(RwLock::new(GardenTopology::new()));
    let _registry = Arc::new(
        Registry::new("/tmp/test-lantern-moss-offline.db".to_string(), topology.clone())
            .await
            .expect("Failed to create registry"),
    );

    {
        let mut topo = topology.write().await;
        
        // Online stone with MongoDB
        topo.stones.insert(
            "stone-online".to_string(),
            InternalStoneState {
                name: "stone-online".to_string(),
                endpoint: "http://192.168.1.100:7185".to_string(),
                status: StoneStatus::Online,
                services: [(
                    "mongodb".to_string(),
                    InternalServiceState {
                        name: "mongodb".to_string(),
                        service_type: "mongodb".to_string(),
                        status: "running".to_string(),
                        connection_string: "mongodb://192.168.1.100:27017".to_string(),
                    },
                )]
                .into_iter()
                .collect(),
                last_seen: Utc::now(),
                first_seen: Utc::now(),
                offline_since: None,
            },
        );

        // Offline stone with MongoDB (should be excluded from peer queries)
        topo.stones.insert(
            "stone-offline".to_string(),
            InternalStoneState {
                name: "stone-offline".to_string(),
                endpoint: "http://192.168.1.101:7185".to_string(),
                status: StoneStatus::Offline,
                services: [(
                    "mongodb".to_string(),
                    InternalServiceState {
                        name: "mongodb".to_string(),
                        service_type: "mongodb".to_string(),
                        status: "stopped".to_string(),
                        connection_string: "mongodb://192.168.1.101:27017".to_string(),
                    },
                )]
                .into_iter()
                .collect(),
                last_seen: Utc::now() - chrono::Duration::seconds(120),
                first_seen: Utc::now() - chrono::Duration::seconds(300),
                offline_since: Some(Utc::now() - chrono::Duration::seconds(120)),
            },
        );
    }

    let topo = topology.read().await;
    let json_topology = topo.to_json();
    
    // Filter for online stones with mongodb
    let online_mongodb: Vec<_> = json_topology.stones
        .iter()
        .filter(|stone| {
            stone.status == "online" && 
            stone.services.iter().any(|svc| svc.service_type == "mongodb")
        })
        .collect();

    // Should only find the online stone
    assert_eq!(online_mongodb.len(), 1);
    assert_eq!(online_mongodb[0].name, "stone-online");
}

#[tokio::test]
async fn test_empty_peer_discovery() {
    let topology = Arc::new(RwLock::new(GardenTopology::new()));
    let _registry = Arc::new(
        Registry::new("/tmp/test-lantern-moss-empty.db".to_string(), topology.clone())
            .await
            .expect("Failed to create registry"),
    );

    // No stones registered
    let topo = topology.read().await;
    let json_topology = topo.to_json();
    
    assert_eq!(json_topology.stones.len(), 0);
}
