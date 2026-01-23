# OpenSearch Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | OpenSearch |
| **Category** | Search & Analytics Engine |
| **Primary Use** | Full-text search, log analytics, observability, vector search |
| **License** | Apache License 2.0 |
| **Governance** | OpenSearch Software Foundation (Linux Foundation since Sept 2024) |
| **Project URL** | https://opensearch.org/ |
| **Docker Hub** | https://hub.docker.com/r/opensearchproject/opensearch |
| **Runtime** | JVM (bundled OpenJDK) |

## Project History

### Fork from Elasticsearch
OpenSearch was created in April 2021 by Amazon Web Services as a fork of Elasticsearch 7.10.2 and Kibana 7.10.2 after Elastic NV changed their license from Apache 2.0 to SSPL.

### Linux Foundation Governance (2024)
In September 2024, OpenSearch transitioned to the Linux Foundation under the newly created **OpenSearch Software Foundation**:
- Vendor-neutral governance
- Founding members: AWS, SAP, Uber
- General members: Aiven, Aryn, Atlassian, Canonical, DigitalOcean, Graylog, NetApp, and others
- 700+ million downloads, thousands of contributors, 200+ maintainers

**Sources**:
- [OpenSearch joins Linux Foundation](https://www.linuxfoundation.org/blog/how-the-opensearch-software-foundation-will-ensure-long-term-sustainability-of-the-opensearch-project)
- [AWS Blog: OpenSearch Foundation](https://aws.amazon.com/blogs/opensource/aws-welcomes-the-opensearch-foundation/)

## Docker Image Analysis

### Image Selection
**Selected**: `opensearchproject/opensearch:2.18.0`

### Version Strategy

| Version | Status | Notes |
|---------|--------|-------|
| 2.18.x | Latest | Current stable |
| 2.x | Supported | Major version line |
| 1.x | EOL | Based on Amazon Linux 2 |

**Base Image**:
- OpenSearch 1.x and 2.0-2.9: Amazon Linux 2
- OpenSearch 2.10+: Amazon Linux 2023

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | ✅ | Primary platform |
| arm64 | ✅ | Since ~v2.3.0 |
| arm32 | ❌ | Not supported |

**ARM64 Support**: Full support for AWS Graviton, Apple Silicon M1/M2/M3, and Raspberry Pi 4/5 (64-bit OS).

**Sources**:
- [OpenSearch Forum: ARM64 Support](https://forum.opensearch.org/t/does-opensearch-support-arm64-architecture/6709)
- [OpenSearch Docker ARM64 Artifacts](https://opensearch.org/artifacts/opensearch/opensearch-2-5-0-docker-arm64.html)

## CPU Compatibility

### No AVX/SSE Requirements

Like Elasticsearch, OpenSearch's JVM does not require specific CPU instructions. It runs on any CPU supporting:
- Java 17+ (bundled)
- 64-bit architecture

### CPU Recommendations

| Workload | Cores |
|----------|-------|
| Development | 2 |
| Small production | 4 |
| Medium production | 8 |
| Large production | 16+ |

## Resource Requirements

### Memory

OpenSearch follows the same memory model as Elasticsearch:

| Component | Memory Use | Notes |
|-----------|------------|-------|
| JVM Heap | 50% of RAM | Max ~32GB |
| Filesystem cache | Remaining | Lucene index access |
| Off-heap buffers | Small | Network I/O |

**Sizing Guidelines**:

| Environment | RAM | JVM Heap (OPENSEARCH_JAVA_OPTS) |
|-------------|-----|--------------------------------|
| Development | 2GB | `-Xms512m -Xmx512m` |
| Small prod | 4GB | `-Xms2g -Xmx2g` |
| Medium prod | 8GB | `-Xms4g -Xmx4g` |
| Large prod | 32GB | `-Xms16g -Xmx16g` |
| Maximum | 64GB | `-Xms32g -Xmx32g` |

**Important**: AWS OpenSearch Service enforces a hard 32GB heap limit.

### Critical Kernel Requirement

**`vm.max_map_count` must be at least 262144**

Same requirement as Elasticsearch. Set on host:

```bash
# Linux - permanent
echo "vm.max_map_count=262144" >> /etc/sysctl.conf
sysctl -p

# Windows WSL2
wsl -d docker-desktop sysctl -w vm.max_map_count=262144
```

### Disk

| Requirement | Value |
|-------------|-------|
| Minimum | 1GB (empty) |
| Recommended | SSD for production |
| Volume mount | `/usr/share/opensearch/data` |

**Sources**:
- [OpenSearch Important Settings](https://opensearch.org/docs/1.0/opensearch/install/important-settings/)
- [OpenSearch Memory Usage Guide](https://opster.com/guides/opensearch/opensearch-capacity-planning/memory-usage/)

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 9200 | HTTP | REST API |
| 9300 | TCP | Transport (node-to-node) |
| 9600 | HTTP | Performance Analyzer |

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
- Uses `local=true` for single-node efficiency
- Accepts `green` or `yellow` (yellow normal for single shard)
- `start_period` allows JVM warmup

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `discovery.type` | - | Set to `single-node` for single instance |
| `cluster.name` | `opensearch` | Cluster identifier |
| `node.name` | Generated | Node identifier |
| `OPENSEARCH_JAVA_OPTS` | `-Xms1g -Xmx1g` | JVM heap settings |
| `DISABLE_SECURITY_PLUGIN` | `false` | Disable security for development |
| `bootstrap.memory_lock` | `false` | Lock memory (recommended) |

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| ARM32 | armv7l, armv6l | Fail | No images available |
| < 2GB RAM | memory_mb_less_than: 2048 | Fail | JVM minimum |
| < 4GB RAM | memory_mb_less_than: 4096 | Warning | Production needs headroom |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `vm.max_map_count.*too low` | Kernel setting | Set sysctl value |
| `OutOfMemoryError\|OOM` | Memory exhaustion | Increase RAM |
| `max file descriptors.*too low` | ulimit issue | Increase to 65536 |
| `memory locking.*not locked` | memlock failed | Set ulimit or disable |
| `master not discovered` | Discovery issue | Check single-node setting |
| `OpenSearchSecurityException` | Security config | Disable or configure |

## Comparison: OpenSearch vs Elasticsearch

| Feature | OpenSearch | Elasticsearch |
|---------|------------|---------------|
| License | Apache 2.0 | AGPL/SSPL/Elastic |
| Governance | Linux Foundation | Elastic NV |
| Base version | ES 7.10.2 fork | Continuous |
| Vector search | ✅ k-NN | ✅ Dense vectors |
| ARM64 support | ✅ | ✅ |
| ARM32 support | ❌ | ❌ |
| Dashboards | OpenSearch Dashboards | Kibana |
| Observability | ✅ Built-in | ✅ Elastic Stack |

**When to Choose OpenSearch**:
- Apache 2.0 license requirement
- AWS integration (Managed OpenSearch Service)
- Vendor-neutral preference
- Community-first development

**When to Choose Elasticsearch**:
- Elastic ecosystem integration
- Latest Elastic-specific features
- Existing Elastic investment

## Vector Search (k-NN)

OpenSearch has native k-NN (k-Nearest Neighbor) support:

```json
PUT /my-index
{
  "settings": {
    "index.knn": true
  },
  "mappings": {
    "properties": {
      "embedding": {
        "type": "knn_vector",
        "dimension": 768,
        "method": {
          "name": "hnsw",
          "space_type": "cosinesimil",
          "engine": "nmslib"
        }
      }
    }
  }
}
```

Supports multiple engines:
- **nmslib** - Non-Metric Space Library (default)
- **faiss** - Facebook AI Similarity Search
- **lucene** - Native Lucene

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 (8GB) | ✅ | Recommended |
| Pi 4 (4GB+, 64-bit OS) | ✅ | Works well |
| Pi 4 (2GB) | ⚠️ | Marginal |
| Pi 3/2/Zero | ❌ | No ARM32 support |

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Security disabled in snippet | Enable for production |
| Network exposure | Internal network (zen-garden) |
| No authentication | Configure security plugin |
| TLS | Enable for production |

**Production Setup**:
```yaml
environment:
  DISABLE_SECURITY_PLUGIN: "false"
  OPENSEARCH_INITIAL_ADMIN_PASSWORD: ${OPENSEARCH_PASSWORD}
```

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (amd64 + arm64)
- [x] ARM32 limitation documented
- [x] Memory constraints documented (2GB minimum)
- [x] Kernel requirements documented (vm.max_map_count)
- [x] Health check command verified
- [x] Licensing documented (Apache 2.0)
- [x] Linux Foundation governance noted
- [x] Vector search capability documented

## Files

| File | Status |
|------|--------|
| `opensearch.snippet.yaml` | ✅ Updated (version, ulimits, healthcheck) |
| `opensearch.compatibility.yaml` | ✅ Updated (ARM32 fail, patterns) |
| `opensearch.frontmatter.json` | ✅ Updated (ports, sysctl note) |
| `opensearch.research.md` | ✅ Created |

## References

1. [OpenSearch Official Documentation](https://opensearch.org/docs/latest/)
2. [OpenSearch Docker Hub](https://hub.docker.com/r/opensearchproject/opensearch)
3. [OpenSearch Important Settings](https://opensearch.org/docs/1.0/opensearch/install/important-settings/)
4. [OpenSearch Memory Guide](https://opster.com/guides/opensearch/opensearch-capacity-planning/memory-usage/)
5. [OpenSearch JVM Guide](https://opster.com/guides/opensearch/opensearch-basics/opensearch-heap-size-usage-and-jvm-garbage-collection/)
6. [Linux Foundation OpenSearch Announcement](https://www.linuxfoundation.org/blog/how-the-opensearch-software-foundation-will-ensure-long-term-sustainability-of-the-opensearch-project)
7. [OpenSearch k-NN Plugin](https://opensearch.org/docs/latest/search-plugins/knn/index/)
8. [OpenSearch Wikipedia](https://en.wikipedia.org/wiki/OpenSearch_(software))
