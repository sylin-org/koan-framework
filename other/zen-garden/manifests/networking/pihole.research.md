# Pi-hole Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | Pi-hole |
| **Category** | DNS / Ad Blocking |
| **Primary Use** | Network-wide ad blocking, DNS sinkhole |
| **License** | EUPL v1.2 |
| **Governance** | Pi-hole LLC |
| **Project URL** | https://pi-hole.net/ |
| **Docker Hub** | https://hub.docker.com/r/pihole/pihole |
| **GitHub** | https://github.com/pi-hole/docker-pi-hole |
| **Runtime** | FTL (pihole-FTL daemon) |

## The "Killer Homelab App"

Pi-hole is widely considered one of the most essential homelab services. It blocks ads and trackers at the DNS level for all devices on the network without requiring per-device configuration.

## Docker Image Analysis

### Image Selection
**Selected**: `pihole/pihole:2024.07.0`

Using last v5 stable release. Note: v6 images have breaking configuration changes.

### Version Considerations

| Version | Notes |
|---------|-------|
| v5.x (2024.07.0) | Last v5 stable, configuration compatible |
| v6.x (2024.08.0+) | Breaking changes, new config format |

**Important**: Upgrading from v5 to v6 results in irreversible configuration file changes.

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | Yes | Primary platform |
| arm64 | Yes | Raspberry Pi 4+, Apple Silicon |
| arm32v7 | Yes | Raspberry Pi 2/3 |
| arm32v6 | No | Pi Zero/1 not supported |

## CPU Compatibility

### No Special Requirements

Pi-hole has **no AVX, SSE, or other SIMD requirements**.

The FTL daemon is optimized but doesn't require special CPU features.

## Resource Requirements

### Memory

Pi-hole is designed to run on minimal hardware:

| Deployment | Memory | Notes |
|------------|--------|-------|
| Minimum | 128MB | Very basic operation |
| Recommended | 256MB | With query logging |
| Comfortable | 512MB | Multiple blocklists, long-term data |

**Docker Shared Memory**: The default Docker shared memory (64MB) may be insufficient. Set `shm_size: 256m` to avoid crashes.

### Shared Memory Issue

A common problem in Docker deployments:
- Pi-hole uses `/dev/shm` for inter-process communication
- Default Docker shm_size is 64MB
- Heavy query logging can exhaust shared memory
- Symptoms: "Lost connection to API", periodic crashes

**Solution**: Always set `shm_size: 256m` or higher in Docker configuration.

### CPU

| Deployment | Cores |
|------------|-------|
| Minimum | 1 |
| Recommended | 1-2 |

CPU usage is minimal for DNS operations.

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 53 | TCP/UDP | DNS queries |
| 80 | HTTP | Web admin interface |
| 443 | HTTPS | Secure web interface (optional) |
| 67 | UDP | DHCP server (optional) |

### Port 53 Conflict

Many Linux distributions run `systemd-resolved` which binds to port 53. Solutions:
1. Stop systemd-resolved: `systemctl disable systemd-resolved`
2. Use a different host port: `5353:53`
3. Configure stub listener: Edit `/etc/systemd/resolved.conf`

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "dig", "+short", "+norecurse", "+retry=0", "@127.0.0.1", "pi.hole"]
```

This verifies DNS resolution is working by querying the special `pi.hole` domain.

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `TZ` | Timezone | UTC |
| `WEBPASSWORD` | Admin password | Random |
| `DNSMASQ_LISTENING` | Listen mode | local |
| `PIHOLE_DNS_` | Upstream DNS | 8.8.8.8;8.8.4.4 |
| `DNSSEC` | Enable DNSSEC | false |

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| ARM32v6 | architectures | Fail | Not supported |
| < 128MB RAM | memory_mb_less_than | Fail | Minimum requirement |
| < 256MB RAM | memory_mb_less_than | Warning | Better with more |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `OOM` | Memory exhaustion | Increase RAM |
| `shm.*shortage` | Shared memory full | Increase shm_size |
| `port 53.*in use` | DNS port conflict | Stop systemd-resolved |
| `gravity.*failed` | Blocklist update failed | Check internet |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 | Yes | Excellent |
| Pi 4 | Yes | Primary target |
| Pi 3 | Yes | Works well |
| Pi 2 | Yes | ARM32v7 supported |
| Pi Zero 2 W | Yes | ARM64 supported |
| Pi Zero/1 | No | ARM32v6 not supported |

## Capabilities Required

```yaml
cap_add:
  - NET_ADMIN  # Required for DHCP functionality
```

**Note**: Avoid using `--privileged` flag. Use explicit capabilities instead.

## Blocklists

Pi-hole uses "gravity" to manage blocklists:

- Default lists block ~100k domains
- Additional community lists available
- Updates can be scheduled via cron

## Comparison with Alternatives

| Feature | Pi-hole | AdGuard Home | Blocky |
|---------|---------|--------------|--------|
| License | EUPL | GPL v3 | Apache 2.0 |
| Web UI | Yes | Yes | No |
| DoH/DoT | Via Unbound | Native | Native |
| DHCP | Yes | Yes | No |
| ARM32 Support | v7+ | v7+ | v7+ |
| Resource Usage | Low | Low | Very Low |

**For Zen Garden**: Pi-hole is the most popular choice with excellent community support and documentation.

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Default password | Set WEBPASSWORD environment variable |
| DNS amplification | Limit to internal network |
| Web exposure | Keep behind reverse proxy |
| Query logging | Consider privacy implications |

## Integration with Other Services

Pi-hole integrates well with:
- **Unbound**: Local recursive DNS resolver
- **WireGuard/VPN**: DNS for remote clients
- **Traefik**: Reverse proxy for web interface
- **Home Assistant**: Automation integration

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (amd64, arm64, arm32v7)
- [x] No CPU feature requirements
- [x] Memory constraints documented (128MB minimum)
- [x] Health check command verified
- [x] EUPL v1.2 license noted
- [x] ARM32v6 limitation documented
- [x] Shared memory issue documented

## Files

| File | Status |
|------|--------|
| `pihole.snippet.yaml` | Created |
| `pihole.compatibility.yaml` | Created |
| `pihole.frontmatter.json` | Created |
| `pihole.research.md` | Created |

## References

1. [Pi-hole Official Documentation](https://docs.pi-hole.net/)
2. [Pi-hole Docker Hub](https://hub.docker.com/r/pihole/pihole)
3. [Pi-hole GitHub](https://github.com/pi-hole/docker-pi-hole)
4. [Pi-hole Prerequisites](https://docs.pi-hole.net/main/prerequisites/)
5. [Shared Memory Issue](https://github.com/pi-hole/docker-pi-hole/issues/571)
6. [Pi-hole Community](https://discourse.pi-hole.net/)
