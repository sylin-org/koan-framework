# Grafana Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | Grafana |
| **Category** | Visualization / Dashboards |
| **Primary Use** | Metrics visualization, dashboards, alerting |
| **License** | AGPL v3 (OSS) / Commercial (Enterprise) |
| **Project URL** | https://grafana.com/ |
| **Docker Hub** | https://hub.docker.com/r/grafana/grafana-oss |
| **GitHub** | https://github.com/grafana/grafana |
| **Runtime** | Go (backend) + Node.js (frontend) |

## Image Variants

| Image | License | Notes |
|-------|---------|-------|
| `grafana/grafana-oss` | AGPL v3 | Open source, recommended |
| `grafana/grafana` | AGPL v3 | Same as OSS |
| `grafana/grafana-enterprise` | Commercial | Enterprise features |

**For Zen Garden**: Use `grafana-oss` for fully open source stack.

## Docker Image Analysis

### Image Selection
**Selected**: `grafana/grafana-oss:11.4.0`

Using pinned version with OSS variant.

### Version Strategy

| Version | Status | Notes |
|---------|--------|-------|
| 11.x | Latest | Current LTS |
| 10.x | Supported | Previous LTS |

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | ✅ | Primary platform |
| arm64 | ✅ | Apple Silicon, Graviton, Pi 4+ |
| arm/v7 | ✅ | Raspberry Pi 2/3 |
| arm/v6 | ❌ | Not supported |

**Note**: Pi Zero (ARMv6) is NOT supported by official Grafana images.

## CPU Compatibility

### No Special Requirements

Grafana has **no AVX, SSE, or other SIMD requirements**.

## Resource Requirements

### Memory

| Workload | Memory | Notes |
|----------|--------|-------|
| Minimum | 256MB | Basic dashboards |
| Recommended | 512MB-1GB | Multiple dashboards |
| Heavy use | 2GB+ | Many panels, users |

Memory usage depends on:
- Number of active dashboards
- Panel complexity
- Concurrent users
- Plugin count

### CPU

| Workload | Cores |
|----------|-------|
| Small | 1 |
| Medium | 2 |
| Large | 4+ |

### Disk

| Component | Storage |
|-----------|---------|
| Application | ~500MB |
| SQLite database | Varies |
| Plugins | ~50-200MB each |

**Data path**: `/var/lib/grafana`

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 3000 | HTTP | Web UI and API |

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:3000/api/health"]
```

### Health Endpoints

| Endpoint | Purpose |
|----------|---------|
| `/api/health` | Basic health check |
| `/api/org` | Organization info (requires auth) |
| `/metrics` | Prometheus metrics |

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `GF_SECURITY_ADMIN_USER` | `admin` | Admin username |
| `GF_SECURITY_ADMIN_PASSWORD` | `admin` | Admin password |
| `GF_USERS_ALLOW_SIGN_UP` | `false` | Allow user registration |
| `GF_SERVER_ROOT_URL` | - | Public URL |
| `GF_AUTH_ANONYMOUS_ENABLED` | `false` | Anonymous access |

**Environment variable format**: `GF_<SECTION>_<KEY>` maps to INI `[section] key`.

## Data Sources

Grafana supports many data sources out of the box:

| Data Source | Type |
|-------------|------|
| Prometheus | Metrics |
| Loki | Logs |
| Elasticsearch/OpenSearch | Logs/Metrics |
| InfluxDB | Metrics |
| PostgreSQL/MySQL | SQL |
| Alertmanager | Alerts |
| Jaeger/Tempo | Traces |
| CloudWatch, Azure Monitor | Cloud |

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| ARMv6 | armv6l | Fail | No images available |
| < 256MB RAM | memory_mb_less_than | Fail | Minimum for operation |
| < 512MB RAM | memory_mb_less_than | Warning | Heavy dashboards need more |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `OOM` | Memory exhaustion | Increase RAM |
| `database is locked` | SQLite lock | Check permissions |
| `plugin.*failed` | Plugin error | Check compatibility |
| `address already in use` | Port conflict | Change port |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 | ✅ | Excellent |
| Pi 4 (4GB+) | ✅ | Good performance |
| Pi 4 (2GB) | ✅ | Works well |
| Pi 3 | ✅ | ARM32v7 supported |
| Pi 2 | ✅ | ARM32v7 supported |
| Pi Zero 2 W | ✅ | ARM64 |
| Pi Zero/1 | ❌ | ARMv6 not supported |

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Default admin password | Change immediately in production |
| Network exposure | Internal network or reverse proxy |
| Anonymous access | Disable in production |

**Production Setup**:
```yaml
environment:
  GF_SECURITY_ADMIN_PASSWORD: ${GRAFANA_ADMIN_PASSWORD}
  GF_USERS_ALLOW_SIGN_UP: "false"
  GF_AUTH_ANONYMOUS_ENABLED: "false"
```

## Plugin System

Grafana has a rich plugin ecosystem:

| Type | Examples |
|------|----------|
| Data Sources | Zabbix, Oracle, MongoDB |
| Panels | Pie chart, Worldmap, Clock |
| Apps | Kubernetes, GitLab, Oncall |

Install plugins via:
- UI (Settings > Plugins)
- CLI: `grafana-cli plugins install <plugin-id>`
- Environment: `GF_INSTALL_PLUGINS=plugin1,plugin2`

## Alerting

Grafana 9+ includes unified alerting:
- Alert rules evaluated in Grafana
- Integration with Alertmanager
- Contact points (email, Slack, PagerDuty, etc.)
- Silences and notification policies

## Comparison with Alternatives

| Feature | Grafana | Kibana | Chronograf |
|---------|---------|--------|------------|
| License | AGPL v3 | SSPL/Elastic | MIT |
| Primary focus | Multi-source | Elasticsearch | InfluxDB |
| Data sources | 100+ | Elastic only | InfluxDB only |
| ARM support | ✅ | ❌ (arm32) | ✅ |
| Alerting | Built-in | Watcher | Kapacitor |

**For Zen Garden**: Grafana is the standard choice for multi-source visualization.

## Grafana + Prometheus Stack

Common pairing:
- **Prometheus**: Collect and store metrics
- **Grafana**: Visualize and alert
- **Alertmanager**: Route and deduplicate alerts

Pre-built dashboards available for many services.

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (amd64, arm64, armv7)
- [x] ARMv6 limitation documented
- [x] No CPU feature requirements
- [x] Memory constraints documented (256MB minimum)
- [x] Health check command verified
- [x] AGPL v3 license noted

## Files

| File | Status |
|------|--------|
| `grafana.snippet.yaml` | ✅ Created |
| `grafana.compatibility.yaml` | ✅ Created |
| `grafana.frontmatter.json` | ✅ Created |
| `grafana.research.md` | ✅ Created |

## References

1. [Grafana Official Documentation](https://grafana.com/docs/grafana/latest/)
2. [Grafana Docker Hub](https://hub.docker.com/r/grafana/grafana-oss)
3. [Grafana GitHub](https://github.com/grafana/grafana)
4. [Grafana Data Sources](https://grafana.com/docs/grafana/latest/datasources/)
5. [Grafana Plugins](https://grafana.com/grafana/plugins/)
6. [Grafana Alerting](https://grafana.com/docs/grafana/latest/alerting/)
