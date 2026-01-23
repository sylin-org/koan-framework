# WireGuard Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | WireGuard |
| **Category** | VPN / Networking |
| **Primary Use** | Fast, secure VPN tunnel |
| **License** | GPL v2 |
| **Governance** | WireGuard Project |
| **Project URL** | https://www.wireguard.com/ |
| **Docker Hub** | https://hub.docker.com/r/linuxserver/wireguard |
| **GitHub** | https://github.com/linuxserver/docker-wireguard |
| **Runtime** | Linux kernel module + userspace tools |

## Why WireGuard?

WireGuard is a modern VPN that aims to be:
- **Faster** than OpenVPN and IPSec
- **Simpler** with ~4,000 lines of code vs 100,000+ for OpenVPN
- **More secure** with modern cryptography
- **Cross-platform** (Linux, Windows, macOS, iOS, Android)

## Docker Image Analysis

### Image Selection
**Selected**: `lscr.io/linuxserver/wireguard:1.0.20210914`

Using LinuxServer.io image for excellent documentation and multi-arch support.

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | Yes | Primary platform |
| arm64v8 | Yes | Raspberry Pi 4+, Apple Silicon |
| arm32v7 | Yes | Raspberry Pi 2/3 |
| arm32v6 | No | Not supported |

### Image Variants

| Tag | Description |
|-----|-------------|
| `latest` | Full image with module compilation support |
| `alpine` | Smaller image, no module compilation |

## Kernel Requirements

WireGuard requires:
- Linux kernel 5.6+ (module built-in), OR
- WireGuard kernel module installed on older kernels

**Container can compile modules** if kernel headers are available (mount `/usr/src:/usr/src`).

## CPU Compatibility

### No Special Requirements

WireGuard has **no AVX, SSE, or other SIMD requirements**.

The crypto is implemented in kernel space and optimized for each architecture.

### Performance

WireGuard achieves excellent throughput:
- ~1 Gbps on modest hardware
- Very low CPU overhead
- Minimal latency

## Resource Requirements

### Memory

WireGuard is extremely lightweight:

| Deployment | Memory | Notes |
|------------|--------|-------|
| Minimum | 64MB | Basic operation |
| Recommended | 128MB | Multiple peers |
| Comfortable | 256MB | High throughput |

The base container is only ~15MB.

### CPU

| Deployment | Cores |
|------------|-------|
| Minimum | 0.5 |
| Recommended | 1 |
| Production | 2+ |

CPU usage scales with throughput and number of peers.

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 51820 | UDP | WireGuard tunnel |

## Required Capabilities

```yaml
cap_add:
  - NET_ADMIN     # Network interface management
  - SYS_MODULE    # Load WireGuard kernel module
```

### Sysctls

```yaml
sysctls:
  - net.ipv4.conf.all.src_valid_mark=1  # Required for routing
```

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD-SHELL", "wg show | grep -q 'interface' || exit 1"]
```

WireGuard doesn't have an HTTP endpoint, so we check if the interface exists.

**Note**: LinuxServer image doesn't include built-in health check.

## Environment Variables

| Variable | Description |
|----------|-------------|
| `PUID` / `PGID` | User/Group ID |
| `TZ` | Timezone |
| `SERVERURL` | Public URL/IP (auto-detected if "auto") |
| `SERVERPORT` | WireGuard port (51820) |
| `PEERS` | Number of peer configs to generate |
| `PEERDNS` | DNS server for peers |
| `INTERNAL_SUBNET` | VPN subnet (10.13.13.0) |
| `ALLOWEDIPS` | Allowed IP ranges (0.0.0.0/0 for full tunnel) |

## Peer Configuration

The container generates peer configs automatically:

```bash
/config/peer1/peer1.conf
/config/peer1/peer1.png  # QR code for mobile
```

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| ARM32v6 | architectures | Fail | Not supported |
| < 64MB RAM | memory_mb_less_than | Fail | Minimum for operation |
| < 128MB RAM | memory_mb_less_than | Warning | Better with peers |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `module.*not found` | No kernel module | Upgrade kernel 5.6+ |
| `permission denied` | Missing caps | Add NET_ADMIN, SYS_MODULE |
| `iptables.*error` | Firewall issue | Check iptables |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 | Yes | Excellent |
| Pi 4 | Yes | Great performance |
| Pi 3 | Yes | Good (armv7) |
| Pi 2 | Yes | Works (armv7) |
| Pi Zero 2 W | Yes | ARM64 |
| Pi Zero/1 | No | ARM32v6 not supported |

**Tested**: Ubuntu and Raspbian Buster on Pi 2-4.

## Use Cases for Zen Garden

1. **Remote Access**: Access your homelab from anywhere
2. **Site-to-Site**: Connect multiple locations
3. **Pi-hole Integration**: Use Pi-hole DNS over VPN
4. **Secure Tunneling**: Route all traffic through home
5. **Mesh Networking**: Connect containers/services

## Comparison with Alternatives

| Feature | WireGuard | OpenVPN | Tailscale | Headscale |
|---------|-----------|---------|-----------|-----------|
| License | GPL v2 | GPL v2 | BSD-3 | BSD-3 |
| Performance | Excellent | Good | Excellent | Excellent |
| Setup Complexity | Medium | High | Very Low | Low |
| Self-hosted | Yes | Yes | No | Yes |
| ARM32 Support | v7+ | Yes | No | No |
| Memory Usage | Very Low | Medium | Low | Low |
| Protocol | UDP only | UDP/TCP | UDP | UDP |

**For Zen Garden**: WireGuard is the best choice for performance and control. Tailscale/Headscale for easier setup if ARM32 not needed.

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Key management | Keys stored in /config volume |
| Port exposure | Only UDP 51820 needed |
| DNS leaks | Set PEERDNS appropriately |
| Kill switch | Configure AllowedIPs carefully |

## Integration Tips

### With Pi-hole
```yaml
environment:
  PEERDNS: 10.13.13.1  # Pi-hole's WireGuard IP
```

### With Traefik
WireGuard typically runs independently; use for accessing services behind Traefik.

## Validation Checklist

- [x] Docker image exists (LinuxServer)
- [x] Multi-architecture support verified (amd64, arm64, arm32v7)
- [x] No CPU feature requirements
- [x] Memory constraints documented (64MB minimum)
- [x] Health check command provided
- [x] GPL v2 license confirmed
- [x] ARM32v6 limitation documented
- [x] Kernel requirements documented

## Files

| File | Status |
|------|--------|
| `wireguard.snippet.yaml` | Created |
| `wireguard.compatibility.yaml` | Created |
| `wireguard.frontmatter.json` | Created |
| `wireguard.research.md` | Created |

## References

1. [WireGuard Official](https://www.wireguard.com/)
2. [LinuxServer WireGuard Docs](https://docs.linuxserver.io/images/docker-wireguard/)
3. [LinuxServer WireGuard Docker Hub](https://hub.docker.com/r/linuxserver/wireguard)
4. [LinuxServer WireGuard GitHub](https://github.com/linuxserver/docker-wireguard)
5. [WireGuard Performance](https://www.wireguard.com/performance/)
6. [WireGuard Quickstart](https://www.wireguard.com/quickstart/)
