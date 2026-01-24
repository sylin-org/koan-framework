//! Network monitoring background task
//!
//! Monitors local network IP address and broadcasts changes to subscribers.
//! Handles network disconnections with configurable retry intervals.
//!
//! ## Architecture
//! - Background task polls for IP changes
//! - When disconnected (127.0.0.1), retries every N seconds (tunable)
//! - When connected, polls less frequently (every 30s)
//! - Broadcasts NetworkEvent::IpChanged when IP changes
//!
//! ## Future: Netlink Integration (Linux)
//! Phase 2 will add netlink event subscription for instant detection on Linux,
//! with polling as fallback for non-Linux or when netlink unavailable.

use std::sync::Arc;
use std::time::Duration;
use tokio::sync::{broadcast, RwLock};

/// Default retry interval when disconnected (no valid LAN IP)
pub const DEFAULT_DISCONNECT_RETRY_SECS: u64 = 5;

/// Default poll interval when connected (has valid LAN IP)
pub const DEFAULT_CONNECTED_POLL_SECS: u64 = 30;

/// Network events broadcast by the monitor
#[derive(Debug, Clone)]
pub enum NetworkEvent {
    /// IP address changed
    IpChanged {
        old: String,
        new: String,
    },
    /// IP validation failed (couldn't detect valid LAN IP)
    Disconnected {
        /// Current IP (likely 127.0.0.1)
        current: String,
        /// Reason for failure
        reason: String,
    },
    /// Reconnected after being disconnected
    Reconnected {
        /// New valid LAN IP
        new: String,
    },
}

/// Configuration for the network monitor
#[derive(Debug, Clone)]
pub struct NetworkMonitorConfig {
    /// Retry interval when disconnected (seconds)
    pub disconnect_retry_secs: u64,
    /// Poll interval when connected (seconds)
    pub connected_poll_secs: u64,
}

impl Default for NetworkMonitorConfig {
    fn default() -> Self {
        Self {
            disconnect_retry_secs: DEFAULT_DISCONNECT_RETRY_SECS,
            connected_poll_secs: DEFAULT_CONNECTED_POLL_SECS,
        }
    }
}

impl NetworkMonitorConfig {
    /// Create config with custom disconnect retry interval
    pub fn with_disconnect_retry(mut self, secs: u64) -> Self {
        self.disconnect_retry_secs = secs;
        self
    }

    /// Create config with custom connected poll interval
    pub fn with_connected_poll(mut self, secs: u64) -> Self {
        self.connected_poll_secs = secs;
        self
    }
}

/// Network monitor that tracks current IP and broadcasts changes
#[derive(Clone)]
pub struct NetworkMonitor {
    current_ip: Arc<RwLock<String>>,
    tx: broadcast::Sender<NetworkEvent>,
}

impl NetworkMonitor {
    /// Start background network monitoring with default config
    pub async fn start() -> Self {
        Self::start_with_config(NetworkMonitorConfig::default()).await
    }

    /// Start background network monitoring with custom config
    pub async fn start_with_config(config: NetworkMonitorConfig) -> Self {
        let initial_ip = get_current_ip();
        let current_ip = Arc::new(RwLock::new(initial_ip.clone()));
        let (tx, _) = broadcast::channel(100);

        let monitor = Self {
            current_ip: current_ip.clone(),
            tx: tx.clone(),
        };

        // Log initial state
        if is_disconnected(&initial_ip) {
            tracing::warn!(
                ip = %initial_ip,
                retry_secs = config.disconnect_retry_secs,
                "NetworkMonitor started in disconnected state, will retry"
            );
        } else {
            tracing::info!(
                ip = %initial_ip,
                poll_secs = config.connected_poll_secs,
                "NetworkMonitor started with valid LAN IP"
            );
        }

        // Spawn monitor task
        tokio::spawn(network_monitor_task(current_ip, tx, config));

        monitor
    }

    /// Get the current IP address
    pub async fn get_ip(&self) -> String {
        self.current_ip.read().await.clone()
    }

    /// Get the current IP address and MAC address
    ///
    /// Returns (ip, mac) tuple. MAC may be None if detection fails.
    /// Used for announcements to include MAC for Wake-on-LAN support.
    pub async fn get_ip_and_mac(&self) -> (String, Option<String>) {
        let ip = self.current_ip.read().await.clone();
        // Get MAC from network module - this re-detects the interface
        // but MAC lookup is fast (sysfs read on Linux)
        let (_, mac) = crate::infra::network::get_local_ip_and_mac();
        (ip, mac)
    }

    /// Check if currently disconnected (no valid LAN IP)
    pub async fn is_disconnected(&self) -> bool {
        is_disconnected(&self.current_ip.read().await)
    }

    /// Subscribe to network events
    pub fn subscribe(&self) -> broadcast::Receiver<NetworkEvent> {
        self.tx.subscribe()
    }

    /// Get a reference to the shared IP for direct access
    pub fn ip_ref(&self) -> Arc<RwLock<String>> {
        self.current_ip.clone()
    }
}

/// Check if an IP indicates disconnected state
fn is_disconnected(ip: &str) -> bool {
    ip == "127.0.0.1" || ip.is_empty()
}

/// Check if IP is a valid LAN address (not loopback, not Docker bridge)
/// Used by Lantern integration to validate IPs before registration
#[allow(dead_code)]
pub fn is_valid_lan_ip(ip: &str) -> bool {
    if is_disconnected(ip) {
        return false;
    }

    // Parse and validate
    if let Ok(parsed) = ip.parse::<std::net::Ipv4Addr>() {
        let octets = parsed.octets();
        // Valid private ranges (excluding Docker bridge 172.17.x.x)
        matches!(
            octets,
            [192, 168, _, _]
                | [10, _, _, _]
                | [100, 64..=127, _, _]  // Carrier-grade NAT (Tailscale)
                | [172, 16, _, _]
                | [172, 18..=31, _, _]   // 172.16-31 except 172.17
        )
    } else {
        false
    }
}

/// Get current IP from the network module
fn get_current_ip() -> String {
    crate::infra::network::get_local_ip()
}

/// Background task that monitors IP changes
async fn network_monitor_task(
    current_ip: Arc<RwLock<String>>,
    tx: broadcast::Sender<NetworkEvent>,
    config: NetworkMonitorConfig,
) {
    let mut was_disconnected = is_disconnected(&*current_ip.read().await);

    loop {
        // Determine poll interval based on current state
        let interval = if was_disconnected {
            Duration::from_secs(config.disconnect_retry_secs)
        } else {
            Duration::from_secs(config.connected_poll_secs)
        };

        tokio::time::sleep(interval).await;

        // Check for IP changes
        let new_ip = get_current_ip();
        let old_ip = current_ip.read().await.clone();

        if new_ip != old_ip {
            // Update stored IP
            *current_ip.write().await = new_ip.clone();

            let now_disconnected = is_disconnected(&new_ip);

            // Determine event type
            let event = if was_disconnected && !now_disconnected {
                // Reconnected
                tracing::info!(
                    old = %old_ip,
                    new = %new_ip,
                    "Network reconnected with valid LAN IP"
                );
                NetworkEvent::Reconnected { new: new_ip.clone() }
            } else if !was_disconnected && now_disconnected {
                // Disconnected
                tracing::warn!(
                    old = %old_ip,
                    new = %new_ip,
                    retry_secs = config.disconnect_retry_secs,
                    "Network disconnected, will retry"
                );
                NetworkEvent::Disconnected {
                    current: new_ip.clone(),
                    reason: "No valid LAN IP detected".to_string(),
                }
            } else {
                // IP changed but still connected (e.g., DHCP renewal)
                tracing::info!(
                    old = %old_ip,
                    new = %new_ip,
                    "Network IP changed"
                );
                NetworkEvent::IpChanged {
                    old: old_ip.clone(),
                    new: new_ip.clone(),
                }
            };

            // Broadcast event (ignore if no receivers)
            let _ = tx.send(event);

            was_disconnected = now_disconnected;
        } else if was_disconnected {
            // Still disconnected, log at debug level
            tracing::debug!(
                ip = %new_ip,
                retry_secs = config.disconnect_retry_secs,
                "Still disconnected, retrying..."
            );
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_is_disconnected() {
        assert!(is_disconnected("127.0.0.1"));
        assert!(is_disconnected(""));
        assert!(!is_disconnected("192.168.1.100"));
        assert!(!is_disconnected("10.0.0.1"));
    }

    #[test]
    fn test_is_valid_lan_ip() {
        // Valid LAN IPs
        assert!(is_valid_lan_ip("192.168.1.100"));
        assert!(is_valid_lan_ip("10.0.0.1"));
        assert!(is_valid_lan_ip("172.16.0.1"));
        assert!(is_valid_lan_ip("172.31.255.255"));
        assert!(is_valid_lan_ip("100.64.0.1")); // Tailscale

        // Invalid
        assert!(!is_valid_lan_ip("127.0.0.1")); // Loopback
        assert!(!is_valid_lan_ip("")); // Empty
        assert!(!is_valid_lan_ip("172.17.0.1")); // Docker bridge - excluded in is_valid_lan_ip
    }

    #[test]
    fn test_config_defaults() {
        let config = NetworkMonitorConfig::default();
        assert_eq!(config.disconnect_retry_secs, DEFAULT_DISCONNECT_RETRY_SECS);
        assert_eq!(config.connected_poll_secs, DEFAULT_CONNECTED_POLL_SECS);
    }

    #[test]
    fn test_config_builder() {
        let config = NetworkMonitorConfig::default()
            .with_disconnect_retry(10)
            .with_connected_poll(60);
        assert_eq!(config.disconnect_retry_secs, 10);
        assert_eq!(config.connected_poll_secs, 60);
    }
}
