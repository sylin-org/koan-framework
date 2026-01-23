# InfluxDB Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | InfluxDB |
| **Category** | Time Series Database |
| **Primary Use** | Metrics, IoT data, monitoring |
| **License** | MIT (OSS) / Commercial (Enterprise) |
| **Governance** | InfluxData |
| **Project URL** | https://www.influxdata.com/ |
| **Docker Hub** | https://hub.docker.com/_/influxdb |
| **GitHub** | https://github.com/influxdata/influxdb |
| **Runtime** | Go binary |

## Version Considerations

| Version | Description |
|---------|-------------|
| InfluxDB 3 | Latest generation, columnar storage |
| InfluxDB 2.x | Current stable, built-in UI, Flux |
| InfluxDB 1.x | Legacy, InfluxQL |

**Selected**: InfluxDB 2.7 for balance of stability and features.

**Note**: As of February 2026, the `latest` tag will point to InfluxDB 3. Use specific version tags.

## Docker Image Analysis

### Image Selection
**Selected**: `influxdb:2.7-alpine`

Using Alpine variant for smaller footprint. Full variant available for compatibility.

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | Yes | Primary platform |
| arm64v8 | Yes | Raspberry Pi 4+, Apple Silicon |
| arm32v7 | No | Not officially supported |
| arm32v6 | No | Not supported |

**Note**: ARM32 users can use community-maintained images but these are not officially supported.

## CPU Compatibility

### No Special Requirements

InfluxDB has **no AVX, SSE, or other SIMD requirements**.

As a Go binary, it runs on any supported architecture.

### Performance Tuning

InfluxDB 3 allows configuration of:
- IO threads (`--num-io-threads`)
- DataFusion threads (`--datafusion-num-threads`)

## Resource Requirements

### Memory

Memory is the primary consideration for InfluxDB:

| Deployment | Memory | Notes |
|------------|--------|-------|
| Minimum | 256MB | Very limited |
| Homelab | 512MB-1GB | Light workloads |
| Production | 2-8GB | Higher cardinality |
| Enterprise | 16GB+ | High cardinality |

**Key Factor**: Series cardinality determines memory needs. ~3KB per series.

### Series Cardinality

Series cardinality = unique combinations of measurement + tags

- 10K series: ~30MB
- 100K series: ~300MB
- 1M series: ~3GB
- 10M+ series: Can cause OOM

### CPU

| Deployment | Cores |
|------------|-------|
| Minimum | 1 |
| Homelab | 2 |
| Production | 4+ |

### Disk

| Deployment | Storage | Type |
|------------|---------|------|
| Minimum | 10GB | SSD preferred |
| Homelab | 50-100GB | SSD recommended |
| Production | 500GB+ | SSD required |

**Important**: InfluxDB requires 1000+ IOPS. SSD strongly recommended.

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 8086 | HTTP | API and Web UI |

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "influx", "ping"]
```

The `influx ping` command checks if the server is responsive.

### API Endpoints

| Endpoint | Purpose |
|----------|---------|
| `/health` | Health check |
| `/ready` | Readiness check |
| `/ping` | Simple ping |

## Environment Variables (Initial Setup)

| Variable | Description |
|----------|-------------|
| `DOCKER_INFLUXDB_INIT_MODE` | Setup mode |
| `DOCKER_INFLUXDB_INIT_USERNAME` | Admin username |
| `DOCKER_INFLUXDB_INIT_PASSWORD` | Admin password |
| `DOCKER_INFLUXDB_INIT_ORG` | Initial organization |
| `DOCKER_INFLUXDB_INIT_BUCKET` | Initial bucket |
| `DOCKER_INFLUXDB_INIT_RETENTION` | Data retention period |

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| ARM32 | architectures | Fail | Not supported |
| < 256MB RAM | memory_mb_less_than | Fail | Minimum for Go + DB |
| < 512MB RAM | memory_mb_less_than | Warning | Better performance |
| < 2GB RAM | memory_mb_less_than | Warning | Production needs |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `OOM` | Memory exhaustion | Increase RAM |
| `disk.*full` | Storage exhausted | Add storage |
| `database.*corrupted` | Data corruption | Restore backup |
| `cardinality.*exceeded` | Too many series | Reduce tags |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 (8GB) | Yes | Excellent |
| Pi 5 (4GB) | Yes | Good |
| Pi 4 (4GB+) | Yes | Works well |
| Pi 4 (2GB) | Yes | Limited cardinality |
| Pi Zero 2 W | Marginal | ARM64 but limited RAM |
| Pi 3/2/Zero | No | ARM32 not supported |

## Data Model

```
measurement
  |-- tag set (indexed)
  |-- field set (values)
  |-- timestamp
```

### Example

```
temperature,location=room1,sensor=dht22 value=23.5 1620000000000000000
```

## Query Languages

| Version | Language |
|---------|----------|
| InfluxDB 2.x | Flux (primary), InfluxQL (compatibility) |
| InfluxDB 3.x | SQL, InfluxQL |
| InfluxDB 1.x | InfluxQL |

## Integrations

| Tool | Integration |
|------|-------------|
| Grafana | Native data source |
| Telegraf | Official collector agent |
| Prometheus | Remote write support |
| Home Assistant | Native integration |

## Comparison with Alternatives

| Feature | InfluxDB | Prometheus | VictoriaMetrics | TimescaleDB |
|---------|----------|------------|-----------------|-------------|
| License | MIT | Apache 2.0 | Apache 2.0 | Timescale |
| Query Language | Flux/SQL | PromQL | MetricsQL | SQL |
| Storage | Custom | Custom | Custom | PostgreSQL |
| Push/Pull | Push | Pull | Both | Push |
| ARM64 Support | Yes | Yes | Yes | Yes |
| ARM32 Support | No | Yes | No | Limited |
| Memory Usage | Medium | Medium | Low | Medium |

**For Zen Garden**: InfluxDB is ideal for IoT/sensor data with its flexible schema. VictoriaMetrics for Prometheus-compatible with lower memory.

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Default credentials | Change immediately after setup |
| Token management | Use environment variables |
| API exposure | Use reverse proxy with TLS |
| Backup | Regular backups of /var/lib/influxdb2 |

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (amd64, arm64)
- [x] No CPU feature requirements
- [x] Memory constraints documented (256MB minimum)
- [x] Health check command verified
- [x] MIT license confirmed
- [x] ARM32 limitation documented

## Files

| File | Status |
|------|--------|
| `influxdb.snippet.yaml` | Created |
| `influxdb.compatibility.yaml` | Created |
| `influxdb.frontmatter.json` | Created |
| `influxdb.research.md` | Created |

## References

1. [InfluxDB Official Documentation](https://docs.influxdata.com/)
2. [InfluxDB Docker Hub](https://hub.docker.com/_/influxdb)
3. [InfluxDB GitHub](https://github.com/influxdata/influxdb)
4. [Hardware Sizing Guidelines](https://docs.influxdata.com/influxdb/v1/guides/hardware_sizing/)
5. [Docker ARM Support Blog](https://www.influxdata.com/blog/influxdata-docker-arm/)
6. [InfluxDB v2 Upgrade](https://docs.influxdata.com/influxdb/v2/upgrade/)
