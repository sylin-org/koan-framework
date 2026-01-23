# Homepage Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | Homepage |
| **Category** | Dashboard / Start Page |
| **Primary Use** | Application dashboard with service API integrations |
| **License** | GPL-3.0 |
| **Governance** | gethomepage Community |
| **Project URL** | https://gethomepage.dev/ |
| **Docker Hub** | https://hub.docker.com/r/gethomepage/homepage |
| **GitHub** | https://github.com/gethomepage/homepage |
| **Runtime** | Node.js |

## Why Homepage?

Homepage is one of the most popular dashboard solutions for homelabs. It offers:
- 100+ service integrations with real-time data
- Docker integration with auto-discovery
- Beautiful, customizable UI
- Lightweight resource usage

## Docker Image Analysis

### Image Selection
**Selected**: `ghcr.io/gethomepage/homepage:v0.9.13`

Using GitHub Container Registry (ghcr.io) for official releases.

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | Yes | Primary platform |
| arm64 | Yes | Raspberry Pi 4+, Apple Silicon |
| arm32v7 | Yes | Raspberry Pi 2/3 |
| arm32v6 | Yes | Raspberry Pi Zero/1 |

**Excellent multi-architecture support** - runs on all common platforms including older Raspberry Pis.

## CPU Compatibility

### No Special Requirements

Homepage has **no AVX, SSE, or other SIMD requirements**.

As a Node.js application, it runs on any supported architecture.

## Resource Requirements

### Memory

Homepage is lightweight:

| Deployment | Memory | Notes |
|------------|--------|-------|
| Minimum | 64MB | Basic dashboard |
| Recommended | 128-256MB | Multiple widgets |
| Comfortable | 512MB | Many services, Docker integration |

User reports confirm it runs well on Raspberry Pi Zero 2 W (512MB RAM).

### CPU

| Deployment | Cores |
|------------|-------|
| Minimum | 1 |
| Recommended | 1-2 |

CPU usage is minimal for a dashboard application.

### Disk

Configuration files only - minimal disk requirements.

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 3000 | HTTP | Web interface |

**Note**: Using port 3001 externally to avoid conflict with Grafana (which also uses 3000).

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:3000"]
```

Simple HTTP check to verify the web server is responding.

## Configuration

Homepage uses YAML files for configuration:

### services.yaml
```yaml
- My Services:
    - Portainer:
        href: http://portainer.local
        description: Container management
        icon: portainer
```

### settings.yaml
```yaml
title: My Homepage
background: /images/background.png
theme: dark
color: slate
```

### widgets.yaml
```yaml
- resources:
    cpu: true
    memory: true
- datetime:
    format:
      dateStyle: short
```

## Docker Integration

Homepage can auto-discover services via Docker labels:

```yaml
labels:
  - homepage.group=Media
  - homepage.name=Jellyfin
  - homepage.icon=jellyfin
  - homepage.href=http://jellyfin.local
  - homepage.description=Media server
```

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| < 64MB RAM | memory_mb_less_than | Fail | Minimum for Node.js |
| < 128MB RAM | memory_mb_less_than | Warning | Better with more widgets |

No architecture restrictions - excellent multi-arch support including ARM32v6.

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `OOM` | Memory exhaustion | Increase RAM |
| `address.*in use` | Port conflict | Change port |
| `docker.sock` | Socket access | Fix permissions |
| `YAML.*error` | Config error | Check syntax |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 | Yes | Excellent |
| Pi 4 | Yes | Great performance |
| Pi 3 | Yes | Good |
| Pi 2 | Yes | ARM32v7 supported |
| Pi Zero 2 W | Yes | ARM64 supported |
| Pi Zero/1 | Yes | ARM32v6 supported |

## Service Integrations

Homepage supports 100+ service integrations:

| Category | Services |
|----------|----------|
| Media | Jellyfin, Plex, Radarr, Sonarr, etc. |
| Monitoring | Prometheus, Grafana, Portainer |
| Storage | Nextcloud, MinIO, Synology |
| Network | Pi-hole, AdGuard, Traefik |
| Home | Home Assistant, Node-RED |
| And more... | Full list at gethomepage.dev |

## Comparison with Alternatives

| Feature | Homepage | Homarr | Dashy | Homer |
|---------|----------|--------|-------|-------|
| License | GPL-3.0 | MIT | MIT | Apache 2.0 |
| Service Integrations | 100+ | Good | Good | Basic |
| Docker Integration | Excellent | Good | Limited | None |
| Resource Usage | Low | Medium | Medium | Very Low |
| ARM32v6 Support | Yes | No | Yes | Yes |
| Setup Complexity | Easy | Easy | Medium | Easy |

**For Zen Garden**: Homepage is ideal due to its extensive service integrations, Docker auto-discovery, and excellent ARM support.

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Docker socket access | Mount read-only, consider socket proxy |
| No authentication | Use reverse proxy with Authelia |
| Service credentials | Store in environment variables |

## Environment Variables

| Variable | Description |
|----------|-------------|
| `PUID` | User ID for file ownership |
| `PGID` | Group ID for file ownership |
| `HOMEPAGE_ALLOWED_HOSTS` | Allowed hostnames |

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (4 architectures including ARM32v6)
- [x] No CPU feature requirements
- [x] Memory constraints documented (64MB minimum)
- [x] Health check command verified
- [x] GPL-3.0 license confirmed

## Files

| File | Status |
|------|--------|
| `homepage.snippet.yaml` | Created |
| `homepage.compatibility.yaml` | Created |
| `homepage.frontmatter.json` | Created |
| `homepage.research.md` | Created |

## References

1. [Homepage Official Documentation](https://gethomepage.dev/)
2. [Homepage Docker Hub](https://hub.docker.com/r/gethomepage/homepage)
3. [Homepage GitHub](https://github.com/gethomepage/homepage)
4. [Hardware Requirements Discussion](https://github.com/gethomepage/homepage/discussions/3540)
5. [Service Widgets](https://gethomepage.dev/widgets/)
6. [Docker Integration](https://gethomepage.dev/installation/docker/)
