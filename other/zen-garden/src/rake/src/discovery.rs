use anyhow::Result;
use std::net::UdpSocket;
use std::time::Duration;
use std::sync::{Arc, Mutex};
use garden_common::{DiscoveryRequest, DiscoveryResponse, ports};

/// Cached Lantern discovery result
static LANTERN_CACHE: once_cell::sync::Lazy<Arc<Mutex<Option<Option<String>>>>> =
    once_cell::sync::Lazy::new(|| Arc::new(Mutex::new(None)));

/// Start background Lantern discovery (non-blocking)
/// Returns immediately, result will be cached for future use
pub fn discover_lantern_background() {
    std::thread::spawn(|| {
        let result = discover_lantern_sync();
        if let Ok(mut cache) = LANTERN_CACHE.lock() {
            *cache = Some(result);
        }
    });
}

/// Get cached Lantern endpoint (non-blocking)
/// Returns None if discovery is still in progress or no Lantern found
pub fn get_cached_lantern() -> Option<String> {
    LANTERN_CACHE.lock().ok()?.as_ref()?.clone()
}

/// Synchronous Lantern discovery (blocks for up to 2 seconds)
fn discover_lantern_sync() -> Option<String> {
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

/// Attempt to discover a Lantern service registry via UDP broadcast (deprecated - use background version)
/// Returns the Lantern HTTP endpoint if found, None if only Moss stones are discovered
#[deprecated(note = "Use discover_lantern_background() and get_cached_lantern() instead")]
pub fn discover_lantern() -> Option<String> {
    discover_lantern_sync()
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
/// Discover all Moss instances on the network with progressive disclosure
/// 
/// Streams discovered stones via callback as they respond, rather than batching.
/// This exposes network physics and provides immediate feedback to users.
/// 
/// # Arguments
/// * `timeout` - Maximum duration to wait for responses
/// * `on_discovered` - Callback invoked for each unique stone discovered
///   - Receives: (DiscoveryResponse, discovery_instant)
///   - Called immediately when stone responds
/// 
/// # Returns
/// Total count of unique stones discovered
pub fn discover_all_moss_stream<F>(
    timeout: Duration,
    mut on_discovered: F,
) -> Result<usize>
where
    F: FnMut(DiscoveryResponse, std::time::Instant) -> (),
{
    use std::collections::HashSet;
    use std::time::Instant;

    let socket = UdpSocket::bind("0.0.0.0:0")?;
    let local = socket.local_addr()?;
    socket.set_broadcast(true)?;
    // Short read timeout for polling (not the full discovery timeout)
    socket.set_read_timeout(Some(Duration::from_millis(100)))?;

    let request_id = uuid::Uuid::now_v7().to_string();
    let request = DiscoveryRequest {
        discover: "moss".into(),
        request_id: request_id.clone(),
        requester: "rake-cli".into(),
    };

    let request_bytes = serde_json::to_vec(&request)?;
    let sent = socket.send_to(&request_bytes, format!("255.255.255.255:{}", ports::DISCOVERY_UDP))?;

    tracing::debug!(?local, bytes = sent, request_id = %request_id, "Sent UDP discovery broadcast (streaming mode)");

    let start = Instant::now();
    let mut discovered_endpoints = HashSet::new();
    let mut buf = [0u8; 1024];

    loop {
        // Check if we've exceeded the discovery timeout
        if start.elapsed() >= timeout {
            tracing::debug!(count = discovered_endpoints.len(), "Discovery timeout reached");
            break;
        }

        match socket.recv_from(&mut buf) {
            Ok((len, addr)) => {
                if let Ok(response) = serde_json::from_slice::<DiscoveryResponse>(&buf[..len]) {
                    // Only process unique endpoints
                    if !discovered_endpoints.contains(&response.stone_endpoint) {
                        discovered_endpoints.insert(response.stone_endpoint.clone());
                        let discovery_instant = Instant::now();
                        
                        tracing::info!(
                            ?addr, 
                            stone = %response.stone_name,
                            elapsed_ms = discovery_instant.duration_since(start).as_millis(),
                            "Discovered Moss (streaming)"
                        );
                        
                        // ✅ IMMEDIATE CALLBACK - Progressive disclosure
                        on_discovered(response, discovery_instant);
                    }
                }
            }
            Err(e) if e.kind() == std::io::ErrorKind::WouldBlock || e.kind() == std::io::ErrorKind::TimedOut => {
                // Timeout on this recv, continue polling
                continue;
            }
            Err(e) => {
                tracing::debug!(error = ?e, count = discovered_endpoints.len(), "Discovery ended with error");
                break;
            }
        }
    }

    Ok(discovered_endpoints.len())
}
