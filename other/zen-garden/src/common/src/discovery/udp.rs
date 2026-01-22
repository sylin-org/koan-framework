//! UDP broadcast discovery for stones
//!
//! Client-side discovery: Send broadcast, collect responses
//! Server-side: Handled by moss/src/discovery.rs (server-specific logic)

use crate::traits::discovery::{DiscoveryResult, DiscoveryError};
use crate::types::{DiscoveryRequest, DiscoveryResponse};
use std::net::{IpAddr, Ipv4Addr, SocketAddr};
use std::time::Duration;
use tokio::net::UdpSocket;
use tokio::time::timeout;

/// UDP broadcast discovery client
pub struct UdpDiscovery {
    port: u16,
}

impl UdpDiscovery {
    /// Create a new UDP discovery client
    pub fn new(port: u16) -> Self {
        Self { port }
    }

    /// Create with default port (3999)
    pub fn default() -> Self {
        Self::new(3999)
    }

    /// Discover all stones via broadcast
    ///
    /// Sends broadcast packet, waits for responses within timeout.
    /// Returns all discovered stones.
    pub async fn discover_all(
        &self,
        discovery_timeout: Duration,
    ) -> Result<Vec<DiscoveryResult>, DiscoveryError> {
        let socket = Self::create_broadcast_socket()?;
        let request = DiscoveryRequest {
            discover: "moss".into(),
            request_id: format!("req-{}", chrono::Utc::now().timestamp_millis()),
            requester: "rake".into(),
        };

        // Send broadcast
        self.send_broadcast(&socket, &request).await?;

        // Collect responses
        let mut results = Vec::new();
        let deadline = tokio::time::Instant::now() + discovery_timeout;

        loop {
            let remaining = deadline.saturating_duration_since(tokio::time::Instant::now());
            if remaining.is_zero() {
                break;
            }

            match timeout(remaining, self.recv_response(&socket)).await {
                Ok(Ok(result)) => results.push(result),
                Ok(Err(_)) => continue, // Invalid response, keep waiting
                Err(_) => break,        // Timeout
            }
        }

        if results.is_empty() {
            return Err(DiscoveryError::NoStonesFound(discovery_timeout));
        }

        Ok(results)
    }

    /// Find a specific stone by name
    pub async fn find_stone(
        &self,
        stone_name: &str,
        discovery_timeout: Duration,
    ) -> Result<DiscoveryResult, DiscoveryError> {
        let all_stones = self.discover_all(discovery_timeout).await?;

        all_stones
            .into_iter()
            .find(|s| s.stone_name == stone_name)
            .ok_or_else(|| DiscoveryError::StoneNotFound(stone_name.to_string()))
    }

    /// Create a UDP socket configured for broadcasting
    fn create_broadcast_socket() -> Result<UdpSocket, DiscoveryError> {
        let socket = std::net::UdpSocket::bind("0.0.0.0:0")?;
        socket.set_broadcast(true)?;

        // Convert to tokio socket
        socket.set_nonblocking(true)?;
        UdpSocket::from_std(socket).map_err(|e| DiscoveryError::NetworkError(e))
    }

    /// Send broadcast discovery request
    async fn send_broadcast(
        &self,
        socket: &UdpSocket,
        request: &DiscoveryRequest,
    ) -> Result<(), DiscoveryError> {
        let broadcast_addr = SocketAddr::new(
            IpAddr::V4(Ipv4Addr::BROADCAST),
            self.port,
        );

        let request_bytes = serde_json::to_vec(request)
            .map_err(|e| DiscoveryError::InvalidResponse(e.to_string()))?;

        socket
            .send_to(&request_bytes, broadcast_addr)
            .await
            .map_err(|e| DiscoveryError::BroadcastFailed(e.to_string()))?;

        Ok(())
    }

    /// Receive a single discovery response
    async fn recv_response(&self, socket: &UdpSocket) -> Result<DiscoveryResult, DiscoveryError> {
        let mut buf = [0u8; 1024];
        let (len, _addr) = socket.recv_from(&mut buf).await?;

        let response: DiscoveryResponse = serde_json::from_slice(&buf[..len])
            .map_err(|e| DiscoveryError::InvalidResponse(e.to_string()))?;

        Ok(DiscoveryResult {
            stone_name: response.stone_name,
            endpoint: response.stone_endpoint,
            moss_version: response.moss_version,
            lantern_endpoint: response.lantern_endpoint,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_udp_discovery_creation() {
        let discovery = UdpDiscovery::new(3999);
        assert_eq!(discovery.port, 3999);

        let default_discovery = UdpDiscovery::default();
        assert_eq!(default_discovery.port, 3999);
    }

    // Integration tests require running moss instance
    // Skip in unit tests
    #[tokio::test]
    #[ignore]
    async fn test_discover_all() {
        let discovery = UdpDiscovery::default();
        let result = discovery.discover_all(Duration::from_secs(2)).await;

        // This will fail without running moss, but demonstrates usage
        assert!(result.is_ok() || matches!(result, Err(DiscoveryError::NoStonesFound(_))));
    }
}
