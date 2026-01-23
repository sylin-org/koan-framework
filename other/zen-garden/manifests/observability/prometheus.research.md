# Prometheus Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | Prometheus |
| **Category** | Monitoring / Metrics |
| **Primary Use** | Metrics collection, time series database, alerting |
| **License** | Apache License 2.0 |
| **Governance** | CNCF (graduated project) |
| **Project URL** | https://prometheus.io/ |
| **Docker Hub** | https://hub.docker.com/r/prom/prometheus |
| **GitHub** | https://github.com/prometheus/prometheus |
| **Runtime** | Go binary |

## CNCF Status

Prometheus was the **second project to graduate from CNCF** (after Kubernetes), highlighting its importance in the cloud-native ecosystem.

## Docker Image Analysis

### Image Selection
**Selected**: `prom/prometheus:v2.54.0`

Using pinned version for stability.

### Version Strategy

| Version | Status | Notes |
|---------|--------|-------|
| 2.54.x | Latest | Current stable |
| 2.53.x | Supported | Previous stable |

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | ✅ | Primary platform |
| arm64 | ✅ | Apple Silicon, Graviton, Pi 4+ |
| arm/v7 | ✅ | Raspberry Pi 2/3 |
| arm/v6 | ✅ | Raspberry Pi Zero/1 |

**Good multi-architecture support** - runs on all common ARM platforms.

## CPU Compatibility

### No Special Requirements

Prometheus has **no AVX, SSE, or other SIMD requirements**.

As a Go binary, it runs on any supported architecture.

## Resource Requirements

### Memory

Prometheus memory usage depends on:
- Number of time series
- Scrape frequency
- Retention period
- Query complexity

| Workload | Memory | Notes |
|----------|--------|-------|
| Minimum | 256MB | ~10K series |
| Small | 512MB-1GB | ~100K series |
| Medium | 2-4GB | ~500K series |
| Large | 8GB+ | 1M+ series |

**Formula**: ~3KB per active time series (approximate)

### CPU

| Workload | Cores |
|----------|-------|
| Small | 1 |
| Medium | 2 |
| Large | 4+ |

CPU usage scales with:
- Scrape frequency
- Number of targets
- Query load

### Disk

| Workload | Storage |
|----------|---------|
| Small | 1-5GB |
| Medium | 10-50GB |
| Large | 100GB+ |

**Formula**: ~1-2 bytes per sample × samples per second × retention seconds

**Storage path**: `/prometheus`

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 9090 | HTTP | Web UI and API |

## Command Line Options

Key options used in snippet:

| Option | Description |
|--------|-------------|
| `--config.file` | Configuration file path |
| `--storage.tsdb.path` | TSDB data directory |
| `--web.enable-lifecycle` | Enable /-/reload and /-/quit |
| `--storage.tsdb.retention.time` | Data retention period |

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:9090/-/healthy"]
```

### Health Endpoints

| Endpoint | Purpose |
|----------|---------|
| `/-/healthy` | Basic health check |
| `/-/ready` | Readiness (TSDB ready) |
| `/-/reload` | Reload configuration (POST) |
| `/-/quit` | Shutdown (POST) |

## Configuration

Prometheus uses `prometheus.yml` for configuration:

```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: 'prometheus'
    static_configs:
      - targets: ['localhost:9090']
```

**Note**: The default image includes a basic configuration. For Zen Garden, a custom config mount may be needed.

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| < 256MB RAM | memory_mb_less_than | Fail | TSDB minimum |
| < 512MB RAM | memory_mb_less_than | Warning | Limited capacity |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `OOM` | Memory exhaustion | Increase RAM or reduce retention |
| `storage.*corrupted` | TSDB corruption | Check permissions/rebuild |
| `config.*error` | Bad config | Check YAML syntax |
| `scrape.*failed` | Target unreachable | Check connectivity |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 | ✅ | Excellent |
| Pi 4 (4GB+) | ✅ | Good for homelab monitoring |
| Pi 4 (2GB) | ✅ | Limited series count |
| Pi 3 | ✅ | ARM32 supported, limited |
| Pi Zero 2 W | ⚠️ | Very limited |

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| No auth by default | Configure reverse proxy with auth |
| Network exposure | Internal network (zen-garden) |
| API access | Consider --web.enable-admin-api carefully |

## Prometheus Ecosystem

| Component | Purpose |
|-----------|---------|
| Prometheus | Core metrics server |
| Alertmanager | Alert routing and deduplication |
| Grafana | Visualization (separate service) |
| Pushgateway | Push metrics for batch jobs |
| Node Exporter | Host metrics |
| Various exporters | Application-specific metrics |

## Comparison with Alternatives

| Feature | Prometheus | InfluxDB | VictoriaMetrics |
|---------|------------|----------|-----------------|
| License | Apache 2.0 | MIT/Commercial | Apache 2.0 |
| Storage | Local TSDB | Local/Cloud | Local |
| Query Language | PromQL | InfluxQL/Flux | MetricsQL (PromQL-compatible) |
| Memory footprint | Medium | Medium | Low |
| ARM support | ✅ | ✅ | ✅ |
| Push model | Via Pushgateway | Native | Native |
| Long-term storage | Remote write | Native | Native |

**For Zen Garden**:
- **Prometheus**: Standard metrics collection, wide ecosystem
- **VictoriaMetrics**: Lower resource usage, PromQL compatible
- **InfluxDB**: If time-series analytics is primary use case

## Remote Write/Read

Prometheus supports remote storage for long-term retention:
- VictoriaMetrics
- Thanos
- Cortex
- Grafana Mimir
- InfluxDB

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (4 architectures)
- [x] No CPU feature requirements
- [x] Memory constraints documented (256MB minimum)
- [x] Health check command verified
- [x] Apache 2.0 license confirmed
- [x] CNCF graduation noted

## Files

| File | Status |
|------|--------|
| `prometheus.snippet.yaml` | ✅ Created |
| `prometheus.compatibility.yaml` | ✅ Created |
| `prometheus.frontmatter.json` | ✅ Created |
| `prometheus.research.md` | ✅ Created |

## References

1. [Prometheus Official Documentation](https://prometheus.io/docs/)
2. [Prometheus Docker Hub](https://hub.docker.com/r/prom/prometheus)
3. [Prometheus GitHub](https://github.com/prometheus/prometheus)
4. [PromQL Documentation](https://prometheus.io/docs/prometheus/latest/querying/basics/)
5. [CNCF Prometheus Page](https://www.cncf.io/projects/prometheus/)
6. [Prometheus Storage](https://prometheus.io/docs/prometheus/latest/storage/)
