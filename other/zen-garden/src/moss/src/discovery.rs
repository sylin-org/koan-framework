use anyhow::Result;
use tokio::sync::{broadcast, OnceCell};
use garden_common::{DiscoveryRequest, DiscoveryResponse, ports};

use crate::network_singletons;

/// Discovery event propagated to consumers
#[derive(Debug, Clone)]
pub struct DiscoveryEvent {
    pub request: DiscoveryRequest,
    pub from_addr: std::net::SocketAddr,
}

/// Singleton holder for UDP discovery listener and broadcast channel
static UDP_LISTENER_CELL: OnceCell<broadcast::Sender<DiscoveryEvent>> = OnceCell::const_new();

/// Start or get reference to singleton UDP discovery listener
/// Returns a receiver for subscribing to discovery events
///
/// IMPORTANT: This binds the UDP socket synchronously before spawning the listener task,
/// ensuring the port is immediately available for incoming requests.
pub async fn ensure_udp_listener(
    stone_id: String,
    stone_name: String,
    api_endpoint: String,
) -> Result<broadcast::Receiver<DiscoveryEvent>> {
    let tx = UDP_LISTENER_CELL
        .get_or_init(|| async {
            // Create broadcast channel with capacity for 100 events
            let (tx, _rx) = broadcast::channel(100);
            let broadcast_tx = tx.clone();

            // Bind socket BEFORE spawning to ensure immediate availability
            let addr = format!("0.0.0.0:{}", ports::DISCOVERY_UDP);
            let socket = match network_singletons::create_reusable_udp_socket(&addr).await {
                Ok(s) => {
                    tracing::info!("UDP discovery socket bound on port {}", ports::DISCOVERY_UDP);
                    s
                }
                Err(e) => {
                    tracing::error!(error = ?e, "Failed to bind UDP discovery socket");
                    // Return sender anyway - subscribers will get no events but won't block startup
                    return tx;
                }
            };

            tokio::spawn(async move {
                if let Err(e) = udp_listener_inner(stone_id, stone_name, api_endpoint, broadcast_tx, socket).await {
                    tracing::error!(error = ?e, "UDP discovery listener failed");
                }
            });

            tx
        })
        .await;

    Ok(tx.subscribe())
}

/// Internal UDP listener implementation with async socket
/// Runs for process lifetime, broadcasting discovery events to subscribers
async fn udp_listener_inner(
    stone_id: String,
    stone_name: String,
    api_endpoint: String,
    broadcast_tx: broadcast::Sender<DiscoveryEvent>,
    socket: tokio::net::UdpSocket,
) -> Result<()> {

    let mut buf = [0u8; 1024];
    loop {
        match socket.recv_from(&mut buf).await {
            Ok((len, addr)) => {
                if let Ok(request) = serde_json::from_slice::<DiscoveryRequest>(&buf[..len]) {
                    tracing::debug!(?addr, request_id = %request.request_id, "Discovery request");

                    // Broadcast event to consumers (ignore if no subscribers)
                    let _ = broadcast_tx.send(DiscoveryEvent {
                        request: request.clone(),
                        from_addr: addr,
                    });

                    // Calculate election delay based on stone name + request ID
                    let delay_ms = calculate_election_delay(&stone_name, &request.request_id);
                    tokio::time::sleep(tokio::time::Duration::from_millis(delay_ms)).await;

                    // Determine which local IP to advertise based on requester's network
                    let response_endpoint = get_reachable_endpoint(&addr.ip(), &api_endpoint);

                    let response = DiscoveryResponse {
                        stone_id: Some(stone_id.clone()),
                        stone_name: stone_name.clone(),
                        stone_endpoint: response_endpoint.clone(),
                        moss_version: format!("{}.{}", env!("CARGO_PKG_VERSION"), env!("BUILD_NUMBER")),
                        lantern_endpoint: None,
                    };

                    if let Ok(response_bytes) = serde_json::to_vec(&response) {
                        if let Err(e) = socket.send_to(&response_bytes, addr).await {
                            tracing::warn!(error = ?e, "Failed to send discovery response");
                        } else {
                            tracing::info!(?addr, endpoint = %response_endpoint, "Sent discovery response");
                        }
                    }
                }
            }
            Err(e) => {
                // Log but continue - UDP listener must not die on transient errors
                tracing::warn!(error = ?e, "UDP recv error, continuing");
                continue;
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
