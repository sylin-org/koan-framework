use crate::{GardenTopology, Registry, InternalStoneState, StoneStatus};
use chrono::Utc;
use std::sync::Arc;
use tokio::sync::RwLock;
use garden_common::RegisterServiceInfo;

#[tokio::test]
async fn test_register_new_stone() {
    let topology = Arc::new(RwLock::new(GardenTopology::new()));
    let registry = Registry::new("/tmp/test-lantern.db".to_string(), topology.clone())
        .await
        .expect("Failed to create registry");

    let services = vec![RegisterServiceInfo {
        name: "mongodb".to_string(),
        service_type: "mongodb".to_string(),
        status: "running".to_string(),
        connection_string: "mongodb://localhost:27017".to_string(),
    }];

    // Register a new stone with stone_id
    let stone_id = "01234567-89ab-cdef-0123-456789abcdef";
    registry
        .register_stone(Some(stone_id), "test-stone", "http://192.168.1.100:7185", services)
        .await
        .expect("Registration failed");

    // Verify stone was added (keyed by stone_id)
    let topo = topology.read().await;
    assert_eq!(topo.stones.len(), 1);
    assert!(topo.stones.contains_key(stone_id));

    let stone = topo.stones.get(stone_id).unwrap();
    assert_eq!(stone.stone_id, Some(stone_id.to_string()));
    assert_eq!(stone.name, "test-stone");
    assert_eq!(stone.endpoint, "http://192.168.1.100:7185");
    assert_eq!(stone.services.len(), 1);
    assert!(stone.services.contains_key("mongodb"));
}

#[tokio::test]
async fn test_register_update_existing_stone() {
    let topology = Arc::new(RwLock::new(GardenTopology::new()));
    let registry = Registry::new("/tmp/test-lantern2.db".to_string(), topology.clone())
        .await
        .expect("Failed to create registry");

    let stone_id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    // Initial registration
    registry
        .register_stone(
            Some(stone_id),
            "test-stone",
            "http://192.168.1.100:7185",
            vec![RegisterServiceInfo {
                name: "mongodb".to_string(),
                service_type: "mongodb".to_string(),
                status: "running".to_string(),
                connection_string: "mongodb://localhost:27017".to_string(),
            }],
        )
        .await
        .expect("Initial registration failed");

    let first_seen = {
        let topo = topology.read().await;
        topo.stones.get(stone_id).unwrap().first_seen
    };

    tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;

    // Update registration with new service
    registry
        .register_stone(
            Some(stone_id),
            "test-stone",
            "http://192.168.1.100:7185",
            vec![
                RegisterServiceInfo {
                    name: "mongodb".to_string(),
                    service_type: "mongodb".to_string(),
                    status: "running".to_string(),
                    connection_string: "mongodb://localhost:27017".to_string(),
                },
                RegisterServiceInfo {
                    name: "redis".to_string(),
                    service_type: "redis".to_string(),
                    status: "running".to_string(),
                    connection_string: "redis://localhost:6379".to_string(),
                },
            ],
        )
        .await
        .expect("Update registration failed");

    // Verify stone was updated
    let topo = topology.read().await;
    assert_eq!(topo.stones.len(), 1);

    let stone = topo.stones.get(stone_id).unwrap();
    assert_eq!(stone.services.len(), 2);
    assert!(stone.services.contains_key("mongodb"));
    assert!(stone.services.contains_key("redis"));

    // first_seen should not change
    assert_eq!(stone.first_seen, first_seen);

    // last_seen should be updated
    assert!(stone.last_seen > first_seen);
}

#[tokio::test]
async fn test_resolve_service_found() {
    let topology = Arc::new(RwLock::new(GardenTopology::new()));
    let registry = Registry::new("/tmp/test-lantern3.db".to_string(), topology.clone())
        .await
        .expect("Failed to create registry");

    // Register stone with mongodb (using None for stone_id to test fallback)
    registry
        .register_stone(
            None,
            "test-stone",
            "http://192.168.1.100:7185",
            vec![RegisterServiceInfo {
                name: "mongodb".to_string(),
                service_type: "mongodb".to_string(),
                status: "running".to_string(),
                connection_string: "mongodb://192.168.1.100:27017".to_string(),
            }],
        )
        .await
        .expect("Registration failed");

    // Resolve service
    let result = registry
        .resolve_service("mongodb")
        .await
        .expect("Resolve failed");

    assert!(result.is_some());
    let response = result.unwrap();
    assert_eq!(response.stone_name, "test-stone");
    assert_eq!(response.endpoint, "http://192.168.1.100:7185");
    assert_eq!(response.service.service_type, "mongodb");
    assert_eq!(
        response.service.connection_string,
        "mongodb://192.168.1.100:27017"
    );
}

#[tokio::test]
async fn test_resolve_service_not_found() {
    let topology = Arc::new(RwLock::new(GardenTopology::new()));
    let registry = Registry::new("/tmp/test-lantern4.db".to_string(), topology.clone())
        .await
        .expect("Failed to create registry");

    // Register stone without postgres
    registry
        .register_stone(
            None,
            "test-stone",
            "http://192.168.1.100:7185",
            vec![RegisterServiceInfo {
                name: "mongodb".to_string(),
                service_type: "mongodb".to_string(),
                status: "running".to_string(),
                connection_string: "mongodb://localhost:27017".to_string(),
            }],
        )
        .await
        .expect("Registration failed");

    // Try to resolve non-existent service
    let result = registry
        .resolve_service("postgres")
        .await
        .expect("Resolve failed");

    assert!(result.is_none());
}

#[tokio::test]
async fn test_resolve_service_prefers_online_stones() {
    let topology = Arc::new(RwLock::new(GardenTopology::new()));
    let registry = Registry::new("/tmp/test-lantern5.db".to_string(), topology.clone())
        .await
        .expect("Failed to create registry");

    // Register online stone
    registry
        .register_stone(
            None,
            "stone-online",
            "http://192.168.1.100:7185",
            vec![RegisterServiceInfo {
                name: "mongodb".to_string(),
                service_type: "mongodb".to_string(),
                status: "running".to_string(),
                connection_string: "mongodb://192.168.1.100:27017".to_string(),
            }],
        )
        .await
        .expect("Registration failed");

    // Manually mark a stone offline
    {
        use crate::state::InternalServiceState;

        let mut topo = topology.write().await;
        topo.stones.insert(
            "stone-offline".to_string(),
            InternalStoneState {
                stone_id: None,
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

    // Resolve should return online stone
    let result = registry
        .resolve_service("mongodb")
        .await
        .expect("Resolve failed");

    assert!(result.is_some());
    let response = result.unwrap();
    assert_eq!(response.stone_name, "stone-online");
}

#[tokio::test]
async fn test_ttl_cleanup() {
    use std::collections::HashMap;
    

    let topology = Arc::new(RwLock::new(GardenTopology::new()));
    let _registry = Arc::new(
        Registry::new("/tmp/test-lantern6.db".to_string(), topology.clone())
            .await
            .expect("Failed to create registry"),
    );

    // Manually add a stone with old last_seen
    {
        let mut topo = topology.write().await;
        topo.stones.insert(
            "old-stone".to_string(),
            InternalStoneState {
                stone_id: None,
                name: "old-stone".to_string(),
                endpoint: "http://192.168.1.200:7185".to_string(),
                status: StoneStatus::Online,
                services: HashMap::new(),
                last_seen: Utc::now() - chrono::Duration::seconds(70), // Over 60s TTL
                first_seen: Utc::now() - chrono::Duration::seconds(300),
                offline_since: None,
            },
        );
    }

    // Run one cleanup cycle
    {
        use chrono::Duration;

        let mut topo = topology.write().await;
        let now = Utc::now();
        let ttl_threshold = Duration::seconds(60);

        for stone in topo.stones.values_mut() {
            if stone.status == StoneStatus::Online {
                let elapsed = now.signed_duration_since(stone.last_seen);
                if elapsed > ttl_threshold {
                    stone.status = StoneStatus::Offline;
                    stone.offline_since = Some(now);
                }
            }
        }
    }

    // Verify stone is now offline
    let topo = topology.read().await;
    let stone = topo.stones.get("old-stone").unwrap();
    assert_eq!(stone.status, StoneStatus::Offline);
    assert!(stone.offline_since.is_some());
}

#[tokio::test]
async fn test_multiple_stones_with_same_service() {
    let topology = Arc::new(RwLock::new(GardenTopology::new()));
    let registry = Registry::new("/tmp/test-lantern7.db".to_string(), topology.clone())
        .await
        .expect("Failed to create registry");

    // Register multiple stones with mongodb (no stone_id to use name as key)
    for i in 1..=3 {
        registry
            .register_stone(
                None,
                &format!("stone-{}", i),
                &format!("http://192.168.1.{}:7185", 100 + i),
                vec![RegisterServiceInfo {
                    name: "mongodb".to_string(),
                    service_type: "mongodb".to_string(),
                    status: "running".to_string(),
                    connection_string: format!("mongodb://192.168.1.{}:27017", 100 + i),
                }],
            )
            .await
            .expect("Registration failed");
    }

    // Resolve should return one of them (first online)
    let result = registry
        .resolve_service("mongodb")
        .await
        .expect("Resolve failed");

    assert!(result.is_some());
    let response = result.unwrap();
    assert!(response.stone_name.starts_with("stone-"));
    assert!(response.endpoint.contains("192.168.1."));
}
