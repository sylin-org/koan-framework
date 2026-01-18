use anyhow::Result;
use std::net::UdpSocket;
use std::time::Duration;
use garden_common::{DiscoveryRequest, DiscoveryResponse, ports};

/// Attempt to discover a Lantern service registry via UDP broadcast
/// Returns the Lantern HTTP endpoint if found, None if only Moss stones are discovered
pub fn discover_lantern() -> Option<String> {
    let socket = match UdpSocket::bind("0.0.0.0:0") {
        Ok(s) => s,
        Err(_) => return None,
    };
    socket.set_broadcast(true).ok()?;
    socket.set_read_timeout(Some(Duration::from_secs(2))).ok()?;

    let request_id = uuid::Uuid::now_v7().to_string();
    let request = DiscoveryRequest {
        discover: "lantern".into(),
        request_id: request_id.clone(),
        requester: "rake-cli".into(),
    };

    let request_bytes = serde_json::to_vec(&request).ok()?;
    socket.send_to(&request_bytes, "255.255.255.255:7187").ok()?;

    tracing::debug!(request_id = %request_id, "Sent Lantern discovery broadcast");

    // Wait for Lantern response (shorter timeout than Moss discovery)
    let mut buf = [0u8; 1024];
    if let Ok((len, addr)) = socket.recv_from(&mut buf) {
        if let Ok(response) = serde_json::from_slice::<DiscoveryResponse>(&buf[..len]) {
            tracing::info!(?addr, endpoint = %response.stone_endpoint, "Discovered Lantern registry");
            return Some(response.stone_endpoint);
        }
    }

    None
}

pub fn discover_moss() -> Result<String> {
    let socket = UdpSocket::bind("0.0.0.0:0")?;
    let local = socket.local_addr()?;
    socket.set_broadcast(true)?;
    socket.set_read_timeout(Some(Duration::from_secs(3)))?;

    let request_id = uuid::Uuid::now_v7().to_string();
    let request = DiscoveryRequest {
        discover: "moss".into(),
        request_id: request_id.clone(),
        requester: "rake-cli".into(),
    };

    let request_bytes = serde_json::to_vec(&request)?;
    let sent = socket.send_to(&request_bytes, format!("255.255.255.255:{}", ports::DISCOVERY_UDP))?;

    tracing::debug!(?local, bytes = sent, request_id = %request_id, "Sent UDP discovery broadcast");

    // Wait for first response
    let mut buf = [0u8; 1024];
    let recv_result = socket.recv_from(&mut buf);

    match recv_result {
        Ok((len, addr)) => {
            tracing::debug!(bytes = len, %request_id, ?addr, "Received UDP discovery response");
            let response: DiscoveryResponse = serde_json::from_slice(&buf[..len])?;
            tracing::info!(?addr, stone = %response.stone_name, endpoint = %response.stone_endpoint, %request_id, "Discovered Moss");
            Ok(response.stone_endpoint)
        }
        Err(e) => {
            tracing::warn!(error = ?e, %request_id, "UDP discovery recv failed");
            Err(e.into())
        }
    }
}

/// Discover all Moss instances on the network
pub fn discover_all_moss(timeout: Duration) -> Result<Vec<String>> {
    let socket = UdpSocket::bind("0.0.0.0:0")?;
    let local = socket.local_addr()?;
    socket.set_broadcast(true)?;
    socket.set_read_timeout(Some(timeout))?;

    let request_id = uuid::Uuid::now_v7().to_string();
    let request = DiscoveryRequest {
        discover: "moss".into(),
        request_id: request_id.clone(),
        requester: "rake-cli".into(),
    };

    let request_bytes = serde_json::to_vec(&request)?;
    let sent = socket.send_to(&request_bytes, format!("255.255.255.255:{}", ports::DISCOVERY_UDP))?;

    tracing::debug!(?local, bytes = sent, request_id = %request_id, "Sent UDP discovery broadcast (all)");

    // Collect all responses within timeout
    let mut endpoints = Vec::new();
    let mut buf = [0u8; 1024];

    loop {
        match socket.recv_from(&mut buf) {
            Ok((len, addr)) => {
                if let Ok(response) = serde_json::from_slice::<DiscoveryResponse>(&buf[..len]) {
                    tracing::info!(?addr, stone = %response.stone_name, "Discovered Moss");
                    if !endpoints.contains(&response.stone_endpoint) {
                        endpoints.push(response.stone_endpoint);
                    }
                }
            }
            Err(e) => {
                // Timeout or error - return what we have
                tracing::debug!(error = ?e, count = endpoints.len(), "Discovery collection ended");
                break;
            }
        }
    }

    if endpoints.is_empty() {
        Err(anyhow::anyhow!("No Moss instances discovered"))
    } else {
        Ok(endpoints)
    }
}
