# Elasticsearch Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | Elasticsearch |
| **Category** | Search & Analytics Engine |
| **Primary Use** | Full-text search, log analytics, vector search |
| **License** | AGPL v3 / SSPL / Elastic License (triple-licensed since Sept 2024) |
| **Project URL** | https://www.elastic.co/elasticsearch |
| **Docker Registry** | docker.elastic.co/elasticsearch/elasticsearch |
| **Runtime** | JVM (bundled OpenJDK) |

## Licensing History (Important)

### Timeline
- **Pre-2021**: Apache 2.0 (open source)
- **2021**: Changed to SSPL + Elastic License (not OSI-approved, caused OpenSearch fork)
- **Sept 2024**: Added AGPL v3 as third option (OSI-approved open source)

### Current State
Elasticsearch is now available under three licenses:
1. **AGPL v3** - OSI-approved open source (new in 2024)
2. **SSPL** - Server Side Public License (not OSI-approved)
3. **Elastic License** - Proprietary/commercial

**Note**: OpenSearch remains the Apache 2.0 alternative, forked by AWS in 2021.

**Sources**:
- [Elastic Blog: Elasticsearch is Open Source Again](https://www.elastic.co/blog/elasticsearch-is-open-source-again)
- [Elastic Licensing FAQ](https://www.elastic.co/pricing/faq/licensing)

## Docker Image Analysis

### Image Selection
**Selected**: `docker.elastic.co/elasticsearch/elasticsearch:8.17.0`

Using official Elastic registry (faster, more reliable than Docker Hub).

### Version Strategy

| Version | Status | Notes |
|---------|--------|-------|
| 8.17.x | Latest | Current stable, vector search support |
| 8.x | Supported | Major version line |
| 7.x | Maintenance | Legacy, limited support |

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | ✅ | Primary platform |
| arm64v8 | ✅ | Since v7.8.0 (June 2020) |
| arm32 | ❌ | **Not supported** |
| i386 | ❌ | Not supported |

**Critical**: ARM32 devices (Raspberry Pi 2/3, Pi Zero) cannot run official Elasticsearch images. Use OpenSearch or PostgreSQL with full-text search as alternatives.

**Sources**:
- [Elastic Blog: Elasticsearch on ARM](https://www.elastic.co/blog/elasticsearch-on-arm)
- [Docker Hub arm64v8/elasticsearch](https://hub.docker.com/r/arm64v8/elasticsearch/)

## CPU Compatibility

### No AVX/SSE Requirements

The JVM does not require specific CPU instructions like AVX or SSE4.2. Elasticsearch runs on any CPU that supports:
- Java 17+ (bundled in Docker image)
- 64-bit architecture (for official images)

However, **performance scales with CPU cores** for:
- Indexing operations
- Complex queries
- Aggregations

### CPU Recommendations

| Workload | Cores |
|----------|-------|
| Development | 2 |
| Small production | 4 |
| Medium production | 8 |
| Large production | 16+ |

## Resource Requirements

### Memory

**Critical**: Elasticsearch is memory-intensive. The JVM heap and filesystem cache compete for RAM.

| Component | Memory Use | Notes |
|-----------|------------|-------|
| JVM Heap | 50% of RAM | Max ~31GB (compressed oops) |
| Filesystem cache | Remaining | Used by Lucene for index access |
| Off-heap buffers | Small | Network I/O, etc. |

**Sizing Guidelines**:

| Environment | RAM | JVM Heap (ES_JAVA_OPTS) |
|-------------|-----|-------------------------|
| Development | 2GB | `-Xms512m -Xmx512m` |
| Small prod | 4GB | `-Xms2g -Xmx2g` |
| Medium prod | 8GB | `-Xms4g -Xmx4g` |
| Large prod | 32GB | `-Xms16g -Xmx16g` |
| Maximum | 64GB | `-Xms31g -Xmx31g` |

**Important Rules**:
1. Always set `-Xms` and `-Xmx` to the same value
2. Never exceed 50% of available RAM for heap
3. Cap heap at ~31GB for compressed ordinary object pointers (oops)

### Critical Kernel Requirement

**`vm.max_map_count` must be at least 262144**

This is the most common cause of Elasticsearch startup failures in Docker.

```bash
# Linux - temporary
sysctl -w vm.max_map_count=262144

# Linux - permanent
echo "vm.max_map_count=262144" >> /etc/sysctl.conf

# Windows WSL2
wsl -d docker-desktop sysctl -w vm.max_map_count=262144

# macOS Docker Desktop
# Create ~/.wslconfig or use Docker Desktop settings
```

**Sources**:
- [Elasticsearch JVM Settings](https://www.elastic.co/guide/en/elasticsearch/reference/current/advanced-configuration.html)
- [Docker for Win vm.max_map_count issue](https://github.com/docker/for-win/issues/5202)

### Disk

| Requirement | Value |
|-------------|-------|
| Minimum | 1GB (empty) |
| Recommended | SSD for production |
| Volume mount | `/usr/share/elasticsearch/data` |

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 9200 | HTTP | REST API |
| 9300 | TCP | Transport (node-to-node) |

Port 9300 is only needed for clustering but exposed for completeness.

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD-SHELL", "curl -s http://localhost:9200/_cluster/health?local=true | grep -q '\"status\":\"green\"\\|\"status\":\"yellow\"' || exit 1"]
  interval: 30s
  timeout: 10s
  retries: 5
  start_period: 60s
```

**Why this approach**:
- Uses `local=true` to avoid cross-node checks
- Accepts both `green` and `yellow` status (yellow is normal for single-node)
- `start_period` allows JVM warmup time

### Alternative Health Checks

```bash
# Simple connectivity check
curl -f http://localhost:9200

# Cluster health with wait
curl http://localhost:9200/_cluster/health?wait_for_status=yellow&timeout=30s
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `discovery.type` | - | Set to `single-node` for single instance |
| `cluster.name` | `elasticsearch` | Cluster identifier |
| `node.name` | Generated | Node identifier |
| `ES_JAVA_OPTS` | Auto | JVM heap settings |
| `xpack.security.enabled` | `true` (8.x) | Enable/disable security |
| `ELASTIC_PASSWORD` | - | Built-in user password |

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| ARM32 | armv7l, armv6l | Fail | No official images |
| < 2GB RAM | memory_mb_less_than: 2048 | Fail | JVM minimum |
| < 4GB RAM | memory_mb_less_than: 4096 | Warning | Production needs headroom |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `vm.max_map_count.*too low` | Kernel setting | Set sysctl value |
| `OutOfMemoryError\|OOM` | Memory exhaustion | Increase RAM/reduce heap |
| `max file descriptors.*too low` | ulimit issue | Increase file descriptor limit |
| `bootstrap check failure` | Startup checks failed | Check specific failure |
| `missing authentication credentials` | Security misconfigured | Disable or configure auth |
| `master not discovered` | Discovery misconfigured | Check single-node setting |

## Raspberry Pi Compatibility

| Device | Support | Alternative |
|--------|---------|-------------|
| Pi 5 (8GB) | ✅ arm64 | - |
| Pi 4 (4GB+, 64-bit OS) | ✅ arm64 | - |
| Pi 4 (2GB) | ⚠️ Marginal | OpenSearch |
| Pi 3/2 | ❌ No arm32 | OpenSearch, PostgreSQL |
| Pi Zero | ❌ No arm32 | PostgreSQL |

**Recommendation for ARM32**: Use OpenSearch (community ARM32 images exist) or PostgreSQL with `pg_trgm` for full-text search.

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Security disabled by default in snippet | Enable for production |
| Network exposure | Internal network (zen-garden) |
| No authentication | Configure `xpack.security.enabled=true` |
| TLS | Enable for production clusters |

**Production Setup**:
```yaml
environment:
  xpack.security.enabled: "true"
  ELASTIC_PASSWORD: ${ES_PASSWORD}
```

## Comparison with Alternatives

| Feature | Elasticsearch | OpenSearch | PostgreSQL FTS |
|---------|---------------|------------|----------------|
| License | AGPL/SSPL | Apache 2.0 | PostgreSQL |
| Full-text search | ✅ Excellent | ✅ Excellent | ✅ Good |
| Vector search | ✅ | ✅ | ✅ (pgvector) |
| ARM32 support | ❌ | ⚠️ Community | ✅ |
| Memory footprint | High | High | Low |
| Complexity | High | High | Low |
| SQL support | Limited | Limited | ✅ Native |

**Recommendation for Zen Garden**:
- Modern hardware, full Elastic ecosystem → Elasticsearch
- Apache 2.0 requirement → OpenSearch
- Resource-constrained, ARM32, simpler needs → PostgreSQL with pgvector

## Vector Search (kNN)

Elasticsearch 8.x includes native vector search capabilities:

```json
PUT /my-index
{
  "mappings": {
    "properties": {
      "embedding": {
        "type": "dense_vector",
        "dims": 768,
        "index": true,
        "similarity": "cosine"
      }
    }
  }
}
```

Supports:
- HNSW indexing (approximate k-NN)
- Exact k-NN (brute force)
- Hybrid search (vector + keyword)

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (amd64 + arm64 only)
- [x] ARM32 limitation documented and handled
- [x] Memory constraints documented (2GB minimum)
- [x] Kernel requirements documented (vm.max_map_count)
- [x] Health check command verified
- [x] Licensing situation documented (AGPL option now available)
- [x] Security considerations reviewed
- [x] Vector search capability noted

## Files

| File | Status |
|------|--------|
| `elasticsearch.snippet.yaml` | ✅ Updated (new image, ports, healthcheck) |
| `elasticsearch.compatibility.yaml` | ✅ Updated (ARM32 fail, patterns) |
| `elasticsearch.frontmatter.json` | ✅ Updated (ports, sysctl note) |
| `elasticsearch.research.md` | ✅ Created |

## References

1. [Elasticsearch Official Documentation](https://www.elastic.co/guide/en/elasticsearch/reference/current/index.html)
2. [Elasticsearch Docker Installation](https://www.elastic.co/guide/en/elasticsearch/reference/current/docker.html)
3. [Elasticsearch Hardware Requirements](https://opster.com/guides/elasticsearch/capacity-planning/elasticsearch-hardware-requirements/)
4. [Elasticsearch JVM Settings](https://www.elastic.co/guide/en/elasticsearch/reference/current/advanced-configuration.html)
5. [Elasticsearch on ARM Blog](https://www.elastic.co/blog/elasticsearch-on-arm)
6. [Elasticsearch Licensing FAQ](https://www.elastic.co/pricing/faq/licensing)
7. [Elasticsearch is Open Source Again](https://www.elastic.co/blog/elasticsearch-is-open-source-again)
8. [vm.max_map_count Docker Issue](https://github.com/docker/for-win/issues/5202)
