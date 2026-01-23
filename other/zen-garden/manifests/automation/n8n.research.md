# n8n Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | n8n (pronounced "nodemation") |
| **Category** | Workflow Automation |
| **Primary Use** | Automation workflows, API integrations |
| **License** | Sustainable Use License (source-available) |
| **Governance** | n8n GmbH |
| **Project URL** | https://n8n.io/ |
| **Docker Hub** | https://hub.docker.com/r/n8nio/n8n |
| **GitHub** | https://github.com/n8n-io/n8n |
| **Runtime** | Node.js |

## License Note

n8n uses a "Sustainable Use License" which allows self-hosting for personal and internal business use. Commercial redistribution or offering as a service requires a commercial license.

## Why n8n?

n8n is often called the "self-hosted Zapier/Make alternative":
- 400+ integrations (APIs, databases, services)
- Visual workflow builder
- Code nodes for custom logic
- Self-hosted with no execution limits
- Fair-code model

## Docker Image Analysis

### Image Selection
**Selected**: `n8nio/n8n:1.72.1`

Using specific version for stability. Latest available on both Docker Hub and GitHub Container Registry.

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | Yes | Primary platform |
| arm64 | Yes | Raspberry Pi 4+, Apple Silicon |
| arm32v7 | No | Not officially supported |
| arm32v6 | No | Not supported |

**ARM64 support is official** but ARM32 is not provided in official builds.

## CPU Compatibility

### No Special Requirements

n8n has **no AVX, SSE, or other SIMD requirements**.

As a Node.js application, it runs on any supported architecture.

### CPU Usage

n8n isn't CPU intensive, so even small instances should be enough for most use cases. Memory requirements typically supersede CPU requirements.

## Resource Requirements

### Memory

n8n at idle uses ~100MB but requires headroom for workflows:

| Deployment | Memory | Notes |
|------------|--------|-------|
| Minimum | 512MB | Very basic workflows |
| Recommended | 1-2GB | Avoid memory errors |
| Production | 4GB+ | Complex data processing |

**Warning**: Running on 1GB RAM can cause memory errors even with simple workflows. 2GB is the safe minimum.

### Workflow Memory Considerations

Memory usage depends on:
- Data volume processed
- Number of concurrent executions
- Complexity of nodes
- Binary data (files, images)

### CPU

| Deployment | Cores |
|------------|-------|
| Minimum | 1 |
| Recommended | 2+ |
| Production | 4+ |

### Disk

| Use Case | Storage |
|----------|---------|
| Basic | 20GB |
| Production | 50GB+ |

SSD recommended for database performance.

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 5678 | HTTP | Web interface and API |

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:5678/healthz"]
```

### Health Endpoints

| Endpoint | Purpose |
|----------|---------|
| `/healthz` | Basic reachability check |
| `/healthz/readiness` | Ready to accept requests |
| `/metrics` | Prometheus metrics |

**Note**: The `/healthz` endpoint must be enabled for health checks to work.

## Environment Variables

| Variable | Description |
|----------|-------------|
| `N8N_HOST` | Listen address |
| `N8N_PORT` | Listen port (5678) |
| `N8N_PROTOCOL` | http or https |
| `WEBHOOK_URL` | Public webhook URL |
| `N8N_METRICS` | Enable metrics endpoint |
| `GENERIC_TIMEZONE` | Default timezone |
| `DB_TYPE` | sqlite, postgresdb, mysqldb |

## Database Support

| Database | Use Case |
|----------|----------|
| SQLite | Default, single-user |
| PostgreSQL | Production, scaling |
| MySQL/MariaDB | Production alternative |

**Recommendation**: Use PostgreSQL for production to avoid SQLite locking issues with concurrent workflows.

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| ARM32 | architectures | Fail | Not supported |
| < 512MB RAM | memory_mb_less_than | Fail | Minimum for Node.js |
| < 1GB RAM | memory_mb_less_than | Warning | Memory errors likely |
| < 2GB RAM | memory_mb_less_than | Warning | Recommended minimum |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `OOM/heap` | Memory exhaustion | Increase RAM |
| `SQLITE_BUSY` | Database lock | Use PostgreSQL |
| `ECONNREFUSED` | Service connection | Check network |
| `timeout` | Execution timeout | Optimize workflow |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 (8GB) | Yes | Excellent |
| Pi 5 (4GB) | Yes | Good |
| Pi 4 (4GB+) | Yes | Works well |
| Pi 4 (2GB) | Yes | Limited workflows |
| Pi 4 (1GB) | Marginal | Not recommended |
| Pi 3 | No | ARM32 not supported |
| Pi Zero 2 W | Marginal | ARM64 but limited RAM |

**Note**: RAM is the limiting factor on Raspberry Pi devices.

## Scaling Options

### Worker Mode
n8n supports queue-based execution with workers:
- Main instance handles UI/triggers
- Workers execute workflows
- Requires Redis for queue

### External Database
For production, use PostgreSQL:
```yaml
environment:
  DB_TYPE: postgresdb
  DB_POSTGRESDB_HOST: postgres
  DB_POSTGRESDB_DATABASE: n8n
  DB_POSTGRESDB_USER: n8n
  DB_POSTGRESDB_PASSWORD: n8n
```

## Comparison with Alternatives

| Feature | n8n | Node-RED | Huginn |
|---------|-----|----------|--------|
| License | Sustainable Use | Apache 2.0 | MIT |
| Integrations | 400+ | Via nodes | Agents |
| Visual Editor | Yes | Yes | No |
| Code Support | Yes | Yes | Ruby |
| ARM64 Support | Yes | Yes | Yes |
| ARM32 Support | No | Yes | Yes |
| Memory Usage | Medium | Low | Medium |

**For Zen Garden**: n8n offers the best balance of usability and integrations, though it requires ARM64 and adequate memory.

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Authentication | Built-in user auth |
| Credentials storage | Encrypted in database |
| Webhook exposure | Use reverse proxy |
| Execution isolation | Consider worker mode |

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (amd64, arm64)
- [x] No CPU feature requirements
- [x] Memory constraints documented (2GB recommended)
- [x] Health check endpoint verified (/healthz)
- [x] Sustainable Use License noted
- [x] ARM32 limitation documented

## Files

| File | Status |
|------|--------|
| `n8n.snippet.yaml` | Created |
| `n8n.compatibility.yaml` | Created |
| `n8n.frontmatter.json` | Created |
| `n8n.research.md` | Created |

## References

1. [n8n Official Documentation](https://docs.n8n.io/)
2. [n8n Docker Hub](https://hub.docker.com/r/n8nio/n8n)
3. [n8n GitHub](https://github.com/n8n-io/n8n)
4. [Prerequisites](https://docs.n8n.io/hosting/installation/server-setups/)
5. [Monitoring Endpoints](https://docs.n8n.io/hosting/logging-monitoring/monitoring/)
6. [Self-Hosting Requirements](https://docs.n8n.io/hosting/installation/server-setups/)
