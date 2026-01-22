//! Network utilities
//!
//! Provides network-related helper functions:
//! - Local IP address detection with priority ordering
//! - Network interface enumeration (future)
//! - Port availability checking (future)

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
