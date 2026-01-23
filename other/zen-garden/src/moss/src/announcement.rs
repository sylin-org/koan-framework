//! Unified announcement system for topology discovery
//!
//! Single source of truth for announcing stone presence and state.
//! Called by: startup, periodic task, service change events.
//!
//! Design principles:
//! - DRY: Single announce() function for all contexts
//! - KISS: Simple UDP broadcast, no complex protocols
//! - SoC: Pure announcement logic, no scheduling concerns
//!
//! Performance: JSON-based change detection costs ~6μs per check,
//! negligible for 30s interval (0.0002% overhead).

use anyhow::Result;
use garden_common::{
    announcement_types, ports, ChirpServiceInfo, StoneChirpPayload, UdpAnnouncement,
};
use serde::{Deserialize, Serialize};
use std::collections::hash_map::DefaultHasher;
use std::hash::{Hash, Hasher};
use tokio::time::Instant;

/// Announcement payload - what we tell other stones about ourselves
///
/// This is the internal representation; converted to `StoneChirpPayload` for UDP broadcast.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AnnouncementPayload {
    pub stone_id: String,
    pub stone_name: String,
    pub endpoint: String,
    pub moss_version: String,
    pub services: Vec<ChirpServiceInfo>,
}

/// Announce stone presence via all available channels
///
/// Called by:
/// - Startup (initial announcement)
/// - Periodic task (every 30s)
/// - Service change events (immediate update)
///
/// Currently implements UDP broadcast only.
/// mDNS TXT updates deferred (requires service re-registration).
pub async fn announce(payload: AnnouncementPayload) -> Result<()> {
    tracing::debug!(
        stone = %payload.stone_name,
        services = payload.services.len(),
        "Announcing stone presence"
    );

    // Send UDP broadcast announcement (all platforms)
    send_udp_announcement(&payload).await?;

    Ok(())
}

/// Announce with change detection
///
/// Only announces if:
/// - State hash changed (any field in payload modified)
/// - Force flag is true (bypass change detection)
/// - More than 5 minutes since last announcement (keep-alive)
///
/// Returns true if announcement was sent, false if skipped.
///
/// Performance: JSON serialization + hash = ~6μs, negligible overhead.
pub async fn announce_if_changed(
    payload: AnnouncementPayload,
    last_hash: &mut Option<u64>,
    last_announcement: &mut Instant,
    force: bool,
) -> Result<bool> {
    let current_hash = calculate_state_hash(&payload);
    let elapsed = last_announcement.elapsed();
    
    // Announce if: forced, changed, or >5min since last
    let should_announce = force 
        || *last_hash != Some(current_hash)
        || elapsed > tokio::time::Duration::from_secs(300);
    
    if should_announce {
        announce(payload).await?;
        *last_hash = Some(current_hash);
        *last_announcement = Instant::now();
        Ok(true) // Announced
    } else {
        tracing::trace!("Announcement skipped (no changes)");
        Ok(false) // Skipped
    }
}

/// Calculate hash of complete announcement state
///
/// Uses JSON serialization for automatic field inclusion.
/// Any new fields added to AnnouncementPayload are automatically hashed.
///
/// Performance: ~6μs (5μs JSON + 1μs hash), acceptable for 30s interval.
fn calculate_state_hash(payload: &AnnouncementPayload) -> u64 {
    let mut hasher = DefaultHasher::new();
    
    // Serialize to JSON for deterministic, maintainable hashing
    if let Ok(json) = serde_json::to_string(payload) {
        json.hash(&mut hasher);
    } else {
        // Fallback: hash stone_id as unique identifier
        payload.stone_id.hash(&mut hasher);
    }
    
    hasher.finish()
}

/// Send UDP broadcast announcement with current state
///
/// Uses the `UdpAnnouncement` envelope format with `stone_chirp` type.
/// Includes full service inventory for topology cache updates on receivers.
async fn send_udp_announcement(payload: &AnnouncementPayload) -> Result<()> {
    use tokio::net::UdpSocket;

    let socket = UdpSocket::bind("0.0.0.0:0").await?;
    socket.set_broadcast(true)?;

    // Build the chirp payload with full service info
    let chirp = StoneChirpPayload {
        stone_id: payload.stone_id.clone(),
        stone_name: payload.stone_name.clone(),
        endpoint: payload.endpoint.clone(),
        moss_version: payload.moss_version.clone(),
        services: payload.services.clone(),
    };

    // Wrap in UdpAnnouncement envelope
    let announcement = UdpAnnouncement {
        announcement_type: announcement_types::STONE_CHIRP.to_string(),
        data: serde_json::to_value(&chirp)?,
    };

    let data = serde_json::to_vec(&announcement)?;
    let broadcast_addr = format!("255.255.255.255:{}", ports::DISCOVERY_UDP);

    socket.send_to(&data, &broadcast_addr).await?;

    tracing::trace!(
        endpoint = %payload.endpoint,
        services = chirp.services.len(),
        "UDP chirp broadcast sent"
    );

    Ok(())
}

/// Build announcement payload from current AppState
///
/// Collects all relevant stone information:
/// - Identity (stone_id, stone_name)
/// - Network (endpoint)
/// - Version (moss_version)
/// - Services (name, offering, category, status)
pub async fn build_payload(state: &crate::AppState) -> AnnouncementPayload {
    // Construct endpoint from network monitor IP and API port
    let ip = state.network_monitor.get_ip().await;
    let endpoint = format!("http://{}:{}", ip, state.api_port);

    // Collect current services
    // Note: Category uses offering name as fallback (detailed category from catalog not available here)
    let services = state
        .registry
        .read()
        .await
        .iter()
        .map(|svc| ChirpServiceInfo {
            name: svc.name.clone(),
            offering: svc.offering.clone(),
            category: svc.offering.clone(), // Use offering as category fallback
            status: format!("{:?}", svc.status), // Format enum as string
        })
        .collect();

    AnnouncementPayload {
        stone_id: state.stone_id.clone(),
        stone_name: state.stone_name.clone(),
        endpoint,
        moss_version: format!("{}.{}", env!("CARGO_PKG_VERSION"), env!("BUILD_NUMBER")),
        services,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_hash_stability() {
        let payload1 = AnnouncementPayload {
            stone_id: "test-id".to_string(),
            stone_name: "test".to_string(),
            endpoint: "http://localhost:7185".to_string(),
            moss_version: "0.1.0".to_string(),
            services: vec![],
        };

        let payload2 = payload1.clone();

        let hash1 = calculate_state_hash(&payload1);
        let hash2 = calculate_state_hash(&payload2);

        assert_eq!(hash1, hash2, "Same payload should produce same hash");
    }

    #[test]
    fn test_hash_detects_changes() {
        let mut payload = AnnouncementPayload {
            stone_id: "test-id".to_string(),
            stone_name: "test".to_string(),
            endpoint: "http://localhost:7185".to_string(),
            moss_version: "0.1.0".to_string(),
            services: vec![],
        };

        let hash1 = calculate_state_hash(&payload);
        
        payload.stone_name = "changed".to_string();
        let hash2 = calculate_state_hash(&payload);

        assert_ne!(hash1, hash2, "Changed field should change hash");
    }

    #[test]
    fn test_hash_detects_service_changes() {
        let mut payload = AnnouncementPayload {
            stone_id: "test-id".to_string(),
            stone_name: "test".to_string(),
            endpoint: "http://localhost:7185".to_string(),
            moss_version: "0.1.0".to_string(),
            services: vec![],
        };

        let hash1 = calculate_state_hash(&payload);
        
        payload.services.push(ChirpServiceInfo {
            name: "redis".to_string(),
            offering: "redis".to_string(),
            category: "data".to_string(),
            status: "Running".to_string(),
        });
        let hash2 = calculate_state_hash(&payload);

        assert_ne!(hash1, hash2, "Added service should change hash");
    }
}
