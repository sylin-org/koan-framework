use anyhow::Result;
use std::net::UdpSocket;
use std::time::Duration;
use garden_common::{DiscoveryRequest, DiscoveryResponse, ports};

pub async fn udp_listener(stone_name: String, api_endpoint: String) -> Result<()> {
    let socket = UdpSocket::bind(format!("0.0.0.0:{}", ports::DISCOVERY_UDP))?;
    socket.set_read_timeout(Some(Duration::from_secs(1)))?;

    tracing::info!("UDP discovery listener started on port {}", ports::DISCOVERY_UDP);

    let mut buf = [0u8; 1024];
    loop {
        match socket.recv_from(&mut buf) {
            Ok((len, addr)) => {
                if let Ok(request) = serde_json::from_slice::<DiscoveryRequest>(&buf[..len]) {
                    tracing::debug!(?addr, request_id = %request.request_id, "Discovery request");

                    // Calculate election delay based on stone name + request ID
                    let delay_ms = calculate_election_delay(&stone_name, &request.request_id);
                    tokio::time::sleep(tokio::time::Duration::from_millis(delay_ms)).await;

                    // Determine which local IP to advertise based on requester's network
                    let response_endpoint = get_reachable_endpoint(&addr.ip(), &api_endpoint);
                    
                    let response = DiscoveryResponse {
                        stone_name: stone_name.clone(),
                        stone_endpoint: response_endpoint.clone(),
                        moss_version: format!("{}.{}", env!("CARGO_PKG_VERSION"), env!("BUILD_NUMBER")),
                        lantern_endpoint: None,
                    };

                    let response_bytes = serde_json::to_vec(&response)?;
                    socket.send_to(&response_bytes, addr)?;

                    tracing::info!(?addr, endpoint = %response_endpoint, "Sent discovery response");
                }
            }
            Err(ref e) if e.kind() == std::io::ErrorKind::WouldBlock || e.kind() == std::io::ErrorKind::TimedOut => {
                // Timeout, continue loop (expected behavior)
                tokio::task::yield_now().await;
            }
            Err(e) => {
                tracing::warn!(error = ?e, "UDP recv error (non-timeout)");
            }
        }
    }
}

fn calculate_election_delay(stone_name: &str, request_id: &str) -> u64 {
    use std::collections::hash_map::DefaultHasher;
    use std::hash::{Hash, Hasher};

    let mut hasher = DefaultHasher::new();
    format!("{}{}", stone_name, request_id).hash(&mut hasher);
    let hash = hasher.finish();

    // 0-2550ms delay based on hash
    (hash % 256) * 10
}

/// Determine the best endpoint to advertise based on requester's IP
/// If requester is on same network, use local network IP
/// If requester is loopback, use loopback
/// Otherwise use configured api_endpoint
fn get_reachable_endpoint(requester_ip: &std::net::IpAddr, api_endpoint: &str) -> String {
    use std::net::IpAddr;
    
    // If request from loopback, respond with loopback
    if requester_ip.is_loopback() {
        return format!("http://127.0.0.1:{}", ports::MOSS_HTTP);
    }
    
    // If request from network, detect our IP on same subnet
    if let Ok(interfaces) = local_ip_address::list_afinet_netifas() {
        for (_, ip) in interfaces {
            if let IpAddr::V4(local_ipv4) = ip {
                if let IpAddr::V4(req_ipv4) = requester_ip {
                    // Check if on same /24 subnet (simple heuristic)
                    if local_ipv4.octets()[0..3] == req_ipv4.octets()[0..3] {
                        return format!("http://{}:{}", local_ipv4, ports::MOSS_HTTP);
                    }
                }
            }
        }
    }
    
    // Fallback to configured endpoint
    api_endpoint.to_string()
}
