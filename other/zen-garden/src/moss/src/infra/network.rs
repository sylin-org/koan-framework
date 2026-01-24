//! Network utilities
//!
//! Provides network-related helper functions:
//! - Local IP address detection with priority ordering
//! - MAC address discovery for Wake-on-LAN support
//! - Network interface enumeration

/// Get the local IP address for network communication
///
/// Returns the best non-loopback IPv4 address, preferring LAN addresses over
/// container/virtual network addresses.
///
/// Priority order:
/// 1. Private LAN (192.168.x.x, 10.x.x.x)
/// 2. Carrier-grade NAT (100.64-127.x.x) - for Tailscale, etc.
/// 3. Other private (172.16-31.x.x) but NOT Docker bridge (172.17.x.x)
/// 4. Any other non-loopback, non-link-local
///
/// Falls back to `local_ip_address::local_ip()` if interface enumeration fails,
/// and finally to 127.0.0.1 if all else fails.
///
/// This is used for:
/// - Displaying the management URL during first boot
/// - Registering with Lantern service discovery
/// - Building service endpoints
pub fn get_local_ip() -> String {
    use std::net::IpAddr;

    // Try priority-based selection first
    if let Some(ip) = get_local_ip_with_priority() {
        return ip;
    }

    // Fallback: use local_ip_address crate's simpler method
    // This works on more systems but doesn't give us priority control
    if let Ok(ip) = local_ip_address::local_ip() {
        if let IpAddr::V4(ipv4) = ip {
            // Still skip Docker bridge if we can detect it
            let octets = ipv4.octets();
            if !(octets[0] == 172 && octets[1] == 17) {
                return ipv4.to_string();
            }
        }
    }

    // Last resort fallback
    "127.0.0.1".to_string()
}

/// Try to get local IP with priority-based selection
/// Returns None if interface enumeration fails or no valid candidates found
fn get_local_ip_with_priority() -> Option<String> {
    use std::net::IpAddr;

    let addrs = local_ip_address::list_afinet_netifas().ok()?;

    let mut candidates: Vec<(u8, std::net::Ipv4Addr)> = Vec::new();

    for (iface_name, ip) in addrs {
        if let IpAddr::V4(ipv4) = ip {
            // Skip loopback and link-local
            if ipv4.is_loopback() || ipv4.is_link_local() {
                continue;
            }

            let octets = ipv4.octets();
            let priority = get_ip_priority(octets, &iface_name);

            // Skip addresses with priority 0 (Docker bridge, etc.)
            if priority == 0 {
                continue;
            }

            candidates.push((priority, ipv4));
        }
    }

    if candidates.is_empty() {
        return None;
    }

    // Sort by priority (higher is better)
    candidates.sort_by(|a, b| b.0.cmp(&a.0));

    candidates.first().map(|(_, ip)| ip.to_string())
}

/// Get priority score for an IP address (higher = better)
/// Returns 0 for addresses that should be skipped
fn get_ip_priority(octets: [u8; 4], iface_name: &str) -> u8 {
    // Skip Docker/container interfaces by name
    let iface_lower = iface_name.to_lowercase();
    if iface_lower.starts_with("docker")
        || iface_lower.starts_with("br-")
        || iface_lower.starts_with("veth")
        || iface_lower == "docker0"
    {
        return 0;
    }

    match octets {
        // 192.168.x.x - typical home/office LAN (highest priority)
        [192, 168, _, _] => 100,

        // 10.x.x.x - corporate/large private networks
        [10, _, _, _] => 90,

        // 100.64-127.x.x - Carrier-grade NAT (Tailscale, etc.)
        [100, second, _, _] if (64..=127).contains(&second) => 80,

        // 172.16-31.x.x - private range, but check for Docker bridge
        [172, second, _, _] if (16..=31).contains(&second) => {
            // Docker bridge is typically 172.17.x.x - deprioritize heavily
            if second == 17 {
                0 // Skip Docker bridge entirely
            } else {
                70
            }
        }

        // Any other routable address
        _ => 50,
    }
}

/// Get the local IP and MAC address for network communication
///
/// Returns (ip_address, mac_address) tuple using the same interface selection
/// logic as `get_local_ip()`. MAC address may be None if detection fails.
///
/// Used for:
/// - Announcing stone presence with MAC for Wake-on-LAN support
/// - Network identity in topology cache
pub fn get_local_ip_and_mac() -> (String, Option<String>) {
    // Try priority-based selection first
    if let Some((ip, mac)) = get_local_ip_and_mac_with_priority() {
        return (ip, mac);
    }

    // Fallback: use simple IP detection, no MAC available
    (get_local_ip(), None)
}

/// Try to get local IP and MAC with priority-based selection
fn get_local_ip_and_mac_with_priority() -> Option<(String, Option<String>)> {
    use std::net::IpAddr;

    let addrs = local_ip_address::list_afinet_netifas().ok()?;

    let mut candidates: Vec<(u8, std::net::Ipv4Addr, String)> = Vec::new();

    for (iface_name, ip) in addrs {
        if let IpAddr::V4(ipv4) = ip {
            // Skip loopback and link-local
            if ipv4.is_loopback() || ipv4.is_link_local() {
                continue;
            }

            let octets = ipv4.octets();
            let priority = get_ip_priority(octets, &iface_name);

            // Skip addresses with priority 0 (Docker bridge, etc.)
            if priority == 0 {
                continue;
            }

            candidates.push((priority, ipv4, iface_name));
        }
    }

    if candidates.is_empty() {
        return None;
    }

    // Sort by priority (higher is better)
    candidates.sort_by(|a, b| b.0.cmp(&a.0));

    let (_, ip, iface_name) = candidates.first()?;
    let mac = get_mac_for_interface(&iface_name);

    Some((ip.to_string(), mac))
}

/// Get MAC address for a given interface name
///
/// Platform-specific implementation:
/// - Linux: Reads from /sys/class/net/{interface}/address
/// - Windows: Uses GetAdaptersAddresses API (via local_ip_address crate)
/// - Other: Returns None
fn get_mac_for_interface(interface_name: &str) -> Option<String> {
    #[cfg(target_os = "linux")]
    {
        let path = format!("/sys/class/net/{}/address", interface_name);
        if let Ok(mac) = std::fs::read_to_string(&path) {
            let mac = mac.trim().to_uppercase();
            // Validate MAC format (xx:xx:xx:xx:xx:xx)
            if mac.len() == 17 && mac.chars().filter(|c| *c == ':').count() == 5 {
                return Some(mac);
            }
        }
        None
    }

    #[cfg(target_os = "windows")]
    {
        // Windows: Use ipconfig /all output parsing or WMI
        // For now, return None - WoL is primarily a Linux stone feature
        // Future: Could use windows-rs to call GetAdaptersAddresses
        let _ = interface_name;
        None
    }

    #[cfg(not(any(target_os = "linux", target_os = "windows")))]
    {
        let _ = interface_name;
        None
    }
}

/// Send a Wake-on-LAN magic packet
///
/// The magic packet consists of:
/// - 6 bytes of 0xFF (synchronization stream)
/// - MAC address repeated 16 times
///
/// Sent as UDP broadcast to port 9 (or 7).
pub async fn send_wol_packet(mac: &str) -> anyhow::Result<()> {
    use tokio::net::UdpSocket;

    // Parse MAC address (accepts xx:xx:xx:xx:xx:xx or xx-xx-xx-xx-xx-xx)
    let mac_bytes = parse_mac_address(mac)?;

    // Build magic packet: 6 bytes of 0xFF + MAC repeated 16 times
    let mut packet = vec![0xFFu8; 6];
    for _ in 0..16 {
        packet.extend_from_slice(&mac_bytes);
    }

    // Send to broadcast address on port 9 (standard WoL port)
    let socket = UdpSocket::bind("0.0.0.0:0").await?;
    socket.set_broadcast(true)?;

    // Try multiple broadcast addresses for better coverage
    let broadcast_addrs = ["255.255.255.255:9", "255.255.255.255:7"];

    for addr in broadcast_addrs {
        if let Err(e) = socket.send_to(&packet, addr).await {
            tracing::warn!(addr = %addr, error = ?e, "WoL broadcast failed to one address");
        }
    }

    tracing::info!(mac = %mac, "Wake-on-LAN magic packet sent");

    Ok(())
}

/// Parse MAC address string to bytes
///
/// Accepts formats:
/// - AA:BB:CC:DD:EE:FF (colon-separated)
/// - AA-BB-CC-DD-EE-FF (dash-separated)
fn parse_mac_address(mac: &str) -> anyhow::Result<[u8; 6]> {
    let mac = mac.replace('-', ":");
    let parts: Vec<&str> = mac.split(':').collect();

    if parts.len() != 6 {
        anyhow::bail!("Invalid MAC address format: {}", mac);
    }

    let mut bytes = [0u8; 6];
    for (i, part) in parts.iter().enumerate() {
        bytes[i] = u8::from_str_radix(part, 16)
            .map_err(|_| anyhow::anyhow!("Invalid hex in MAC address: {}", part))?;
    }

    Ok(bytes)
}
