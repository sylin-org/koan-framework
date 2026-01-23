use anyhow::Result;
use garden_common::{
    announcement_types, ports, DiscoveryRequest, DiscoveryResponse, StoneChirpPayload,
    UdpAnnouncement,
};
use std::net::SocketAddr;
use tokio::sync::{broadcast, OnceCell};

use crate::network_singletons;

/// UDP event propagated to consumers
///
/// Consumers subscribe to these events and filter by variant.
/// - `Request`: Another stone is looking for peers (respond with our info)
/// - `Chirp`: Another stone is announcing its presence (update topology cache)
#[derive(Debug, Clone)]
pub enum UdpEvent {
    /// Discovery request from a stone looking for peers
    Request {
        request: DiscoveryRequest,
        from_addr: SocketAddr,
    },
    /// Stone chirp with full state (services, etc.)
    Chirp {
        chirp: StoneChirpPayload,
        from_addr: SocketAddr,
    },
}

/// Singleton holder for UDP listener and broadcast channel
static UDP_LISTENER_CELL: OnceCell<broadcast::Sender<UdpEvent>> = OnceCell::const_new();

/// Start or get reference to singleton UDP listener
/// Returns a receiver for subscribing to UDP events (requests and chirps)
///
/// IMPORTANT: This binds the UDP socket synchronously before spawning the listener task,
/// ensuring the port is immediately available for incoming requests.
pub async fn ensure_udp_listener(
    stone_id: String,
    stone_name: String,
    api_endpoint: String,
) -> Result<broadcast::Receiver<UdpEvent>> {
    let tx = UDP_LISTENER_CELL
        .get_or_init(|| async {
            // Create broadcast channel with capacity for 100 events
            let (tx, _rx) = broadcast::channel(100);
            let broadcast_tx = tx.clone();

            // Bind socket BEFORE spawning to ensure immediate availability
            let addr = format!("0.0.0.0:{}", ports::DISCOVERY_UDP);
            let socket = match network_singletons::create_reusable_udp_socket(&addr).await {
                Ok(s) => {
                    tracing::info!("UDP listener socket bound on port {}", ports::DISCOVERY_UDP);
                    s
                }
                Err(e) => {
                    tracing::error!(error = ?e, "Failed to bind UDP listener socket");
                    // Return sender anyway - subscribers will get no events but won't block startup
                    return tx;
                }
            };

            tokio::spawn(async move {
                if let Err(e) =
                    udp_listener_inner(stone_id, stone_name, api_endpoint, broadcast_tx, socket)
                        .await
                {
                    tracing::error!(error = ?e, "UDP listener failed");
                }
            });

            tx
        })
        .await;

    Ok(tx.subscribe())
}

/// Internal UDP listener implementation with async socket
/// Runs for process lifetime, parsing UdpAnnouncement envelopes and broadcasting events
async fn udp_listener_inner(
    stone_id: String,
    stone_name: String,
    api_endpoint: String,
    broadcast_tx: broadcast::Sender<UdpEvent>,
    socket: tokio::net::UdpSocket,
) -> Result<()> {
    let mut buf = [0u8; 4096]; // Larger buffer for chirps with service data

    loop {
        match socket.recv_from(&mut buf).await {
            Ok((len, addr)) => {
                // Try parsing as new UdpAnnouncement envelope format
                if let Ok(announcement) = serde_json::from_slice::<UdpAnnouncement>(&buf[..len]) {
                    handle_announcement(
                        &announcement,
                        addr,
                        &stone_id,
                        &stone_name,
                        &api_endpoint,
                        &broadcast_tx,
                        &socket,
                    )
                    .await;
                }
                // Legacy: Try parsing as raw DiscoveryRequest (backwards compat during migration)
                else if let Ok(request) = serde_json::from_slice::<DiscoveryRequest>(&buf[..len]) {
                    handle_legacy_request(
                        &request,
                        addr,
                        &stone_id,
                        &stone_name,
                        &api_endpoint,
                        &broadcast_tx,
                        &socket,
                    )
                    .await;
                } else {
                    tracing::trace!(?addr, "Received unrecognized UDP packet, ignoring");
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

/// Handle a UdpAnnouncement envelope by dispatching based on type
async fn handle_announcement(
    announcement: &UdpAnnouncement,
    addr: SocketAddr,
    stone_id: &str,
    stone_name: &str,
    api_endpoint: &str,
    broadcast_tx: &broadcast::Sender<UdpEvent>,
    socket: &tokio::net::UdpSocket,
) {
    match announcement.announcement_type.as_str() {
        announcement_types::DISCOVERY_REQUEST => {
            // Parse the data as DiscoveryRequest
            if let Ok(request) = serde_json::from_value::<DiscoveryRequest>(announcement.data.clone())
            {
                tracing::debug!(?addr, request_id = %request.request_id, "Discovery request (envelope)");

                // Broadcast to consumers
                let _ = broadcast_tx.send(UdpEvent::Request {
                    request: request.clone(),
                    from_addr: addr,
                });

                // Respond to discovery request
                respond_to_discovery(
                    &request,
                    addr,
                    stone_id,
                    stone_name,
                    api_endpoint,
                    socket,
                )
                .await;
            }
        }
        announcement_types::STONE_CHIRP => {
            // Parse the data as StoneChirpPayload
            if let Ok(chirp) = serde_json::from_value::<StoneChirpPayload>(announcement.data.clone())
            {
                // Ignore our own chirps
                if chirp.stone_id == stone_id {
                    tracing::trace!("Ignoring own chirp");
                    return;
                }

                tracing::debug!(
                    stone = %chirp.stone_name,
                    services = chirp.services.len(),
                    from = ?addr,
                    "Received stone chirp"
                );

                // Broadcast chirp event to consumers (for topology cache update)
                let _ = broadcast_tx.send(UdpEvent::Chirp {
                    chirp,
                    from_addr: addr,
                });
            }
        }
        _ => {
            tracing::trace!(
                announcement_type = %announcement.announcement_type,
                "Unknown announcement type, ignoring"
            );
        }
    }
}

/// Handle legacy DiscoveryRequest (not wrapped in envelope)
async fn handle_legacy_request(
    request: &DiscoveryRequest,
    addr: SocketAddr,
    stone_id: &str,
    stone_name: &str,
    api_endpoint: &str,
    broadcast_tx: &broadcast::Sender<UdpEvent>,
    socket: &tokio::net::UdpSocket,
) {
    tracing::debug!(?addr, request_id = %request.request_id, "Discovery request (legacy)");

    // Broadcast to consumers
    let _ = broadcast_tx.send(UdpEvent::Request {
        request: request.clone(),
        from_addr: addr,
    });

    // Respond to discovery request
    respond_to_discovery(request, addr, stone_id, stone_name, api_endpoint, socket).await;
}

/// Respond to a discovery request with our stone info
async fn respond_to_discovery(
    request: &DiscoveryRequest,
    addr: SocketAddr,
    stone_id: &str,
    stone_name: &str,
    api_endpoint: &str,
    socket: &tokio::net::UdpSocket,
) {
    // Calculate election delay based on stone name + request ID
    let delay_ms = calculate_election_delay(stone_name, &request.request_id);
    tokio::time::sleep(tokio::time::Duration::from_millis(delay_ms)).await;

    // Determine which local IP to advertise based on requester's network
    let response_endpoint = get_reachable_endpoint(&addr.ip(), api_endpoint);

    let response = DiscoveryResponse {
        stone_id: Some(stone_id.to_string()),
        stone_name: stone_name.to_string(),
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

/// Send discovery request and collect peer responses
///
/// Called at startup to proactively discover neighbor stones.
/// Broadcasts UDP discovery request and collects responses.
///
/// Returns discovered stones for topology cache population.
pub async fn discover_peers(
    stone_id: &str,
    timeout_secs: u64,
) -> Vec<DiscoveryResponse> {
    use tokio::net::UdpSocket;

    let mut discovered = Vec::new();

    let socket = match UdpSocket::bind("0.0.0.0:0").await {
        Ok(s) => s,
        Err(e) => {
            tracing::warn!(error = ?e, "Failed to bind socket for peer discovery");
            return discovered;
        }
    };

    if let Err(e) = socket.set_broadcast(true) {
        tracing::warn!(error = ?e, "Failed to set broadcast for peer discovery");
        return discovered;
    }

    // Send discovery request
    let request = DiscoveryRequest {
        discover: "moss".to_string(),
        request_id: uuid::Uuid::new_v7(uuid::Timestamp::now(uuid::NoContext)).to_string(),
        requester: stone_id.to_string(),
    };

    let data = match serde_json::to_vec(&request) {
        Ok(d) => d,
        Err(e) => {
            tracing::warn!(error = ?e, "Failed to serialize discovery request");
            return discovered;
        }
    };

    let broadcast_addr = format!("255.255.255.255:{}", ports::DISCOVERY_UDP);
    if let Err(e) = socket.send_to(&data, &broadcast_addr).await {
        tracing::warn!(error = ?e, "Failed to send discovery broadcast");
        return discovered;
    }

    tracing::info!(request_id = %request.request_id, "Sent peer discovery request");

    // Collect responses for timeout duration
    let deadline = tokio::time::Instant::now() + tokio::time::Duration::from_secs(timeout_secs);
    let mut buf = [0u8; 2048];

    while tokio::time::Instant::now() < deadline {
        let remaining = deadline - tokio::time::Instant::now();
        match tokio::time::timeout(remaining, socket.recv_from(&mut buf)).await {
            Ok(Ok((len, addr))) => {
                if let Ok(response) = serde_json::from_slice::<DiscoveryResponse>(&buf[..len]) {
                    tracing::info!(
                        stone = %response.stone_name,
                        endpoint = %response.stone_endpoint,
                        from = %addr,
                        "Discovered peer stone"
                    );
                    discovered.push(response);
                }
            }
            Ok(Err(_)) | Err(_) => break,
        }
    }

    tracing::info!(count = discovered.len(), "Peer discovery complete");
    discovered
}
