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
    announcement_types, ports, StoneGoodbyePayload,
    UdpAnnouncement,
};
use std::collections::hash_map::DefaultHasher;
use std::hash::{Hash, Hasher};
use tokio::time::Instant;

use crate::domain::TopologyEntry;

/// Announce stone presence via all available channels
///
/// Called by:
/// - Startup (initial announcement)
/// - Periodic task (every 30s)
/// - Service change events (immediate update)
///
/// Currently implements UDP broadcast only.
/// mDNS TXT updates deferred (requires service re-registration).
pub async fn announce(entry: &TopologyEntry) -> Result<()> {
    tracing::debug!(
        stone = %entry.stone_name,
        services = entry.services.len(),
        health = %entry.health,
        "Announcing stone presence"
    );

    // Send UDP broadcast announcement (all platforms)
    send_udp_announcement(entry).await?;

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
    entry: &TopologyEntry,
    last_hash: &mut Option<u64>,
    last_announcement: &mut Instant,
    force: bool,
) -> Result<bool> {
    let current_hash = calculate_state_hash(entry);
    let elapsed = last_announcement.elapsed();
    
    // Announce if: forced, changed, or >5min since last
    let should_announce = force 
        || *last_hash != Some(current_hash)
        || elapsed > tokio::time::Duration::from_secs(300);
    
    if should_announce {
        announce(entry).await?;
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
/// Any new fields added to TopologyEntry are automatically hashed.
///
/// Performance: ~6μs (5μs JSON + 1μs hash), acceptable for 30s interval.
fn calculate_state_hash(entry: &TopologyEntry) -> u64 {
    let mut hasher = DefaultHasher::new();
    
    // Serialize to JSON for deterministic, maintainable hashing
    if let Ok(json) = serde_json::to_string(entry) {
        json.hash(&mut hasher);
    } else {
        // Fallback: hash stone_id as unique identifier
        entry.stone_id.hash(&mut hasher);
    }
    
    hasher.finish()
}

/// Send UDP broadcast announcement with current state
///
/// Uses the `UdpAnnouncement` envelope format with `stone_chirp` type.
/// Broadcasts the TopologyEntry directly - chirp IS the topology entry.
async fn send_udp_announcement(entry: &TopologyEntry) -> Result<()> {
    use tokio::net::UdpSocket;

    let socket = UdpSocket::bind("0.0.0.0:0").await?;
    socket.set_broadcast(true)?;

    // Wrap TopologyEntry in UdpAnnouncement envelope
    let announcement = UdpAnnouncement {
        announcement_type: announcement_types::STONE_CHIRP.to_string(),
        data: serde_json::to_value(entry)?,
    };

    let data = serde_json::to_vec(&announcement)?;
    let broadcast_addr = format!("255.255.255.255:{}", ports::DISCOVERY_UDP);

    socket.send_to(&data, &broadcast_addr).await?;

    tracing::trace!(
        endpoint = %entry.endpoint,
        services = entry.services.len(),
        health = %entry.health,
        "UDP chirp broadcast sent"
    );

    Ok(())
}

/// Send a goodbye announcement before shutdown
///
/// Notifies other stones that this stone is going offline gracefully.
/// This allows immediate offline marking instead of waiting for the 90s chirp timeout.
///
/// Called before stone shutdown/reboot operations.
pub async fn send_goodbye(state: &crate::AppState) -> Result<()> {
    use tokio::net::UdpSocket;

    let goodbye = StoneGoodbyePayload {
        stone_id: state.stone_id.clone(),
        stone_name: state.stone_name.clone(),
    };

    tracing::info!(
        stone = %goodbye.stone_name,
        "Sending goodbye announcement before shutdown"
    );

    // Wrap in UdpAnnouncement envelope
    let announcement = UdpAnnouncement {
        announcement_type: announcement_types::STONE_GOODBYE.to_string(),
        data: serde_json::to_value(&goodbye)?,
    };

    let data = serde_json::to_vec(&announcement)?;
    let broadcast_addr = format!("255.255.255.255:{}", ports::DISCOVERY_UDP);

    let socket = UdpSocket::bind("0.0.0.0:0").await?;
    socket.set_broadcast(true)?;
    socket.send_to(&data, &broadcast_addr).await?;

    tracing::info!(
        stone = %goodbye.stone_name,
        "Goodbye announcement sent"
    );

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::domain::TopologyEntry;
    use garden_common::TopologyServiceEntry;

    #[test]
    fn test_hash_stability() {
        let entry1 = TopologyEntry {
            stone_id: "test-id".to_string(),
            stone_name: "test".to_string(),
            endpoint: "http://localhost:7185".to_string(),
            moss_version: "0.1.0".to_string(),
            services: vec![],
            mac: None,
            health: "thriving".to_string(),
            capabilities: None,
            status: crate::domain::StoneStatus::Online,
            discovered_at: chrono::Utc::now(),
            last_seen: chrono::Utc::now(),
        };

        let entry2 = entry1.clone();

        let hash1 = calculate_state_hash(&entry1);
        let hash2 = calculate_state_hash(&entry2);

        assert_eq!(hash1, hash2, "Same entry should produce same hash");
    }

    #[test]
    fn test_hash_detects_changes() {
        let mut entry = TopologyEntry {
            stone_id: "test-id".to_string(),
            stone_name: "test".to_string(),
            endpoint: "http://localhost:7185".to_string(),
            moss_version: "0.1.0".to_string(),
            services: vec![],
            mac: None,
            health: "thriving".to_string(),
            capabilities: None,
            status: crate::domain::StoneStatus::Online,
            discovered_at: chrono::Utc::now(),
            last_seen: chrono::Utc::now(),
        };

        let hash1 = calculate_state_hash(&entry);

        entry.stone_name = "changed".to_string();
        let hash2 = calculate_state_hash(&entry);

        assert_ne!(hash1, hash2, "Changed field should change hash");
    }

    #[test]
    fn test_hash_detects_service_changes() {
        let mut entry = TopologyEntry {
            stone_id: "test-id".to_string(),
            stone_name: "test".to_string(),
            endpoint: "http://localhost:7185".to_string(),
            moss_version: "0.1.0".to_string(),
            services: vec![],
            mac: None,
            health: "thriving".to_string(),
            capabilities: None,
            status: crate::domain::StoneStatus::Online,
            discovered_at: chrono::Utc::now(),
            last_seen: chrono::Utc::now(),
        };

        let hash1 = calculate_state_hash(&entry);

        entry.services.push(TopologyServiceEntry {
            name: "redis".to_string(),
            offering: "redis".to_string(),
            category: "data".to_string(),
            status: "Running".to_string(),
        });
        let hash2 = calculate_state_hash(&entry);

        assert_ne!(hash1, hash2, "Added service should change hash");
    }
}
