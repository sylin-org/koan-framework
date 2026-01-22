use anyhow::Result;
use std::net::UdpSocket;
use std::time::Duration;
use std::sync::{Arc, Mutex};
use garden_common::{DiscoveryRequest, DiscoveryResponse, ports};

/// Get a LAN-suitable local IP address for binding UDP sockets
/// Prioritizes: 192.168.x.x > 10.x.x.x > 172.16-31.x.x
/// This ensures broadcasts go out the correct interface on multi-homed systems
fn get_lan_bind_address() -> String {
    if let Ok(interfaces) = local_ip_address::list_afinet_netifas() {
        let mut candidates: Vec<(u8, std::net::Ipv4Addr)> = Vec::new();

        for (_, ip) in interfaces {
            if let std::net::IpAddr::V4(ipv4) = ip {
                let octets = ipv4.octets();
                // Skip loopback, link-local
                if ipv4.is_loopback() || octets[0] == 169 {
                    continue;
                }
                // Skip Docker bridge (172.17.x.x) and WSL/Hyper-V ranges
                if octets[0] == 172 && (octets[1] == 17 || octets[1] >= 24) {
                    continue;
                }
                // Prioritize by network type
                let priority = match octets[0] {
                    192 if octets[1] == 168 => 1, // 192.168.x.x - home/small office
                    10 => 2,                       // 10.x.x.x - enterprise
                    172 if (16..=23).contains(&octets[1]) => 3, // 172.16-23.x.x
                    _ => 4,                        // Other
                };
                candidates.push((priority, ipv4));
            }
        }

        candidates.sort_by_key(|(p, _)| *p);
        if let Some((_, ip)) = candidates.first() {
            return format!("{}:0", ip);
        }
    }

    "0.0.0.0:0".to_string()
}

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
    // Bind to LAN interface for reliable broadcast on multi-interface systems
    let bind_addr = get_lan_bind_address();
    let socket = match UdpSocket::bind(&bind_addr) {
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

    tracing::debug!(request_id = %request_id, bind = %bind_addr, "Sent Lantern discovery broadcast");

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
    // Bind to LAN interface for reliable broadcast on multi-interface systems
    let bind_addr = get_lan_bind_address();
    let socket = UdpSocket::bind(&bind_addr)?;
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

    // Bind to LAN interface for reliable broadcast on multi-interface systems
    let bind_addr = get_lan_bind_address();
    let socket = UdpSocket::bind(&bind_addr)?;
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

// ============================================================================
// mDNS Discovery (Linux only)
// ============================================================================

/// Discover Moss instances via mDNS service browse (Linux only)
///
/// Browses for `_moss._tcp.local.` services announced by Moss instances.
/// This is the preferred discovery method on Linux as it's more reliable
/// than UDP broadcast and works better with firewalls.
///
/// # Arguments
/// * `timeout` - Maximum duration to wait for mDNS responses
///
/// # Returns
/// Vector of discovered stone responses
#[cfg(not(target_os = "windows"))]
pub fn discover_moss_mdns(timeout: Duration) -> Result<Vec<DiscoveryResponse>> {
    use mdns_sd::{ServiceDaemon, ServiceEvent};
    use std::time::Instant;

    let mdns = ServiceDaemon::new()
        .map_err(|e| anyhow::anyhow!("Failed to create mDNS daemon: {}", e))?;

    let service_type = "_moss._tcp.local.";
    let receiver = mdns.browse(service_type)
        .map_err(|e| anyhow::anyhow!("Failed to browse mDNS services: {}", e))?;

    tracing::debug!(service_type = %service_type, "Starting mDNS service browse");

    let mut stones = Vec::new();
    let start = Instant::now();

    while start.elapsed() < timeout {
        match receiver.recv_timeout(Duration::from_millis(100)) {
            Ok(ServiceEvent::ServiceResolved(info)) => {
                // Extract stone name from service instance name
                let stone_name = info.get_fullname()
                    .split('.')
                    .next()
                    .unwrap_or("unknown")
                    .to_string();

                // Get the first address (prefer any available)
                if let Some(ip) = info.get_addresses().iter().next() {
                    let endpoint = format!("http://{}:{}", ip, info.get_port());

                    tracing::info!(
                        stone = %stone_name,
                        endpoint = %endpoint,
                        "Discovered Moss via mDNS"
                    );

                    stones.push(DiscoveryResponse {
                        stone_name,
                        stone_endpoint: endpoint,
                        moss_version: String::new(), // Not available via mDNS
                        lantern_endpoint: None,
                    });
                }
            }
            Ok(ServiceEvent::SearchStarted(_)) => {
                tracing::debug!("mDNS search started");
            }
            Ok(_) => {
                // Other events (ServiceFound, ServiceRemoved, etc.)
            }
            Err(flume::RecvTimeoutError::Timeout) => {
                // Continue polling
            }
            Err(e) => {
                tracing::debug!(error = ?e, "mDNS browse error");
                break;
            }
        }
    }

    // Stop the browse
    let _ = mdns.stop_browse(service_type);

    tracing::debug!(count = stones.len(), "mDNS discovery complete");
    Ok(stones)
}

/// Discover Moss instances via mDNS with streaming callback (Linux only)
///
/// Like `discover_moss_mdns` but invokes callback immediately for each discovery.
#[cfg(not(target_os = "windows"))]
pub fn discover_moss_mdns_stream<F>(timeout: Duration, mut on_discovered: F) -> Result<usize>
where
    F: FnMut(DiscoveryResponse, std::time::Instant),
{
    use mdns_sd::{ServiceDaemon, ServiceEvent};
    use std::time::Instant;
    use std::collections::HashSet;

    let mdns = ServiceDaemon::new()
        .map_err(|e| anyhow::anyhow!("Failed to create mDNS daemon: {}", e))?;

    let service_type = "_moss._tcp.local.";
    let receiver = mdns.browse(service_type)
        .map_err(|e| anyhow::anyhow!("Failed to browse mDNS services: {}", e))?;

    tracing::debug!(service_type = %service_type, "Starting mDNS service browse (streaming)");

    let mut discovered_endpoints = HashSet::new();
    let start = Instant::now();

    while start.elapsed() < timeout {
        match receiver.recv_timeout(Duration::from_millis(100)) {
            Ok(ServiceEvent::ServiceResolved(info)) => {
                let stone_name = info.get_fullname()
                    .split('.')
                    .next()
                    .unwrap_or("unknown")
                    .to_string();

                // Get the first address (prefer any available)
                if let Some(ip) = info.get_addresses().iter().next() {
                    let endpoint = format!("http://{}:{}", ip, info.get_port());

                    // Only process unique endpoints
                    if !discovered_endpoints.contains(&endpoint) {
                        discovered_endpoints.insert(endpoint.clone());
                        let discovery_instant = Instant::now();

                        tracing::info!(
                            stone = %stone_name,
                            endpoint = %endpoint,
                            elapsed_ms = discovery_instant.duration_since(start).as_millis(),
                            "Discovered Moss via mDNS (streaming)"
                        );

                        on_discovered(
                            DiscoveryResponse {
                                stone_name,
                                stone_endpoint: endpoint.clone(),
                                moss_version: String::new(), // Not available via mDNS
                                lantern_endpoint: None,
                            },
                            discovery_instant,
                        );
                    }
                }
            }
            Ok(_) => {}
            Err(flume::RecvTimeoutError::Timeout) => {}
            Err(_) => break,
        }
    }

    let _ = mdns.stop_browse(service_type);
    Ok(discovered_endpoints.len())
}

/// Stub for Windows - mDNS discovery not available
#[cfg(target_os = "windows")]
pub fn discover_moss_mdns(_timeout: Duration) -> Result<Vec<DiscoveryResponse>> {
    tracing::debug!("mDNS discovery not available on Windows");
    Ok(Vec::new())
}

/// Stub for Windows - mDNS discovery not available
#[cfg(target_os = "windows")]
pub fn discover_moss_mdns_stream<F>(_timeout: Duration, _on_discovered: F) -> Result<usize>
where
    F: FnMut(DiscoveryResponse, std::time::Instant),
{
    tracing::debug!("mDNS discovery not available on Windows");
    Ok(0)
}

/// Platform-aware discovery that uses the best method for the current OS
///
/// - Linux: Runs mDNS AND UDP broadcast in parallel, merges results
/// - Windows: Uses UDP broadcast only
///
/// Note: Windows Moss services don't announce via mDNS, so we must always do UDP
/// broadcast to discover them, even on Linux.
pub fn discover_moss_auto(timeout: Duration) -> Result<Vec<DiscoveryResponse>> {
    use std::collections::HashSet;
    use std::sync::{Arc, Mutex};

    let results = Arc::new(Mutex::new(Vec::new()));
    let seen_endpoints = Arc::new(Mutex::new(HashSet::new()));

    // On Linux, run mDNS and UDP in parallel
    #[cfg(not(target_os = "windows"))]
    {
        let mdns_results = results.clone();
        let mdns_seen = seen_endpoints.clone();
        let mdns_timeout = timeout;

        // Spawn mDNS discovery in background thread
        let mdns_handle = std::thread::spawn(move || {
            if let Ok(stones) = discover_moss_mdns(mdns_timeout) {
                let mut results = mdns_results.lock().unwrap();
                let mut seen = mdns_seen.lock().unwrap();
                for response in stones {
                    if !seen.contains(&response.stone_endpoint) {
                        seen.insert(response.stone_endpoint.clone());
                        results.push(response);
                    }
                }
            }
        });

        // Run UDP discovery in main thread
        let udp_results = results.clone();
        let udp_seen = seen_endpoints.clone();
        let _ = discover_all_moss_stream(timeout, |response, _instant| {
            let mut results = udp_results.lock().unwrap();
            let mut seen = udp_seen.lock().unwrap();
            if !seen.contains(&response.stone_endpoint) {
                seen.insert(response.stone_endpoint.clone());
                results.push(response);
            }
        });

        // Wait for mDNS to complete
        let _ = mdns_handle.join();
    }

    // Windows: UDP only
    #[cfg(target_os = "windows")]
    {
        let _ = discover_all_moss_stream(timeout, |response, _instant| {
            let mut results = results.lock().unwrap();
            let mut seen = seen_endpoints.lock().unwrap();
            if !seen.contains(&response.stone_endpoint) {
                seen.insert(response.stone_endpoint.clone());
                results.push(response);
            }
        });
    }

    let final_results = match Arc::try_unwrap(results) {
        Ok(mutex) => mutex.into_inner().unwrap(),
        Err(arc) => arc.lock().unwrap().clone(),
    };

    tracing::debug!(total = final_results.len(), "Auto-discovery complete");
    Ok(final_results)
}

/// Platform-aware streaming discovery (parallel mDNS + UDP)
///
/// - Linux: Runs mDNS AND UDP broadcast in parallel, streams results as they arrive
/// - Windows: Uses UDP broadcast only
///
/// Results are deduplicated by endpoint and passed to callback immediately.
/// This provides the fastest possible progressive disclosure.
pub fn discover_moss_auto_stream<F>(timeout: Duration, on_discovered: F) -> Result<usize>
where
    F: FnMut(DiscoveryResponse, std::time::Instant) + Send + 'static,
{
    use std::collections::HashSet;
    use std::sync::{Arc, Mutex};

    let seen_endpoints = Arc::new(Mutex::new(HashSet::new()));
    let callback = Arc::new(Mutex::new(on_discovered));
    let total_count = Arc::new(Mutex::new(0usize));

    // On Linux, run mDNS and UDP in parallel
    #[cfg(not(target_os = "windows"))]
    {
        let mdns_seen = seen_endpoints.clone();
        let mdns_callback = callback.clone();
        let mdns_count = total_count.clone();
        let mdns_timeout = timeout;

        // Spawn mDNS discovery in background thread
        let mdns_handle = std::thread::spawn(move || {
            let _ = discover_moss_mdns_stream(mdns_timeout, |response, instant| {
                let mut seen = mdns_seen.lock().unwrap();
                if !seen.contains(&response.stone_endpoint) {
                    seen.insert(response.stone_endpoint.clone());
                    drop(seen); // Release lock before callback

                    let mut cb = mdns_callback.lock().unwrap();
                    cb(response, instant);

                    let mut count = mdns_count.lock().unwrap();
                    *count += 1;
                }
            });
        });

        // Run UDP discovery in main thread
        let udp_seen = seen_endpoints.clone();
        let udp_callback = callback.clone();
        let udp_count = total_count.clone();
        let _ = discover_all_moss_stream(timeout, |response, instant| {
            let mut seen = udp_seen.lock().unwrap();
            if !seen.contains(&response.stone_endpoint) {
                seen.insert(response.stone_endpoint.clone());
                drop(seen); // Release lock before callback

                let mut cb = udp_callback.lock().unwrap();
                cb(response, instant);

                let mut count = udp_count.lock().unwrap();
                *count += 1;
            }
        });

        // Wait for mDNS to complete
        let _ = mdns_handle.join();
    }

    // Windows: UDP only
    #[cfg(target_os = "windows")]
    {
        let _ = discover_all_moss_stream(timeout, |response, instant| {
            let mut seen = seen_endpoints.lock().unwrap();
            if !seen.contains(&response.stone_endpoint) {
                seen.insert(response.stone_endpoint.clone());
                drop(seen);

                let mut cb = callback.lock().unwrap();
                cb(response, instant);

                let mut count = total_count.lock().unwrap();
                *count += 1;
            }
        });
    }

    let final_count = *total_count.lock().unwrap();
    tracing::debug!(total = final_count, "Auto-discovery complete (parallel)");
    Ok(final_count)
}
