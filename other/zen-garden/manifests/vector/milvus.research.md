# Milvus Vector Database Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | Milvus |
| **Category** | Vector Database |
| **Primary Use** | Vector similarity search, AI/ML applications |
| **License** | Apache License 2.0 |
| **Governance** | LF AI & Data Foundation |
| **Project URL** | https://milvus.io/ |
| **Docker Hub** | https://hub.docker.com/r/milvusdb/milvus |
| **GitHub** | https://github.com/milvus-io/milvus |

## Deployment Modes

Milvus offers three deployment modes:

| Mode | Scale | Use Case |
|------|-------|----------|
| **Milvus Lite** | < 1M vectors | Prototyping, edge devices, Python-embedded |
| **Milvus Standalone** | < 100M vectors | Small to medium deployments |
| **Milvus Distributed** | Billions | Production at scale |

**Zen Garden uses Standalone** mode with embedded etcd for simplicity.

## Docker Image Analysis

### Image Selection
**Selected**: `milvusdb/milvus:v2.5.4`

Using pinned version for stability. The `latest` tag is not recommended for production.

### Version Strategy

| Version | Status | Notes |
|---------|--------|-------|
| 2.5.x | Latest | ARM64 GPU support added |
| 2.4.x | Stable | Production ready |
| 2.3.x | Legacy | First ARM64 support |

### Architecture Support

| Architecture | Supported | Since | Notes |
|--------------|-----------|-------|-------|
| amd64 | ✅ | v1.0 | Primary platform |
| arm64 | ✅ | v2.3 (Aug 2023) | Apple Silicon, Graviton |
| arm32 | ❌ | - | Not supported |

**ARM64 Limitations**:
- Cluster mode may have issues (Pulsar v2.8 dependency)
- Some early versions had jemalloc page size issues
- Standalone mode works well

**Sources**:
- [Milvus 2.3 ARM64 Announcement](https://milvus.io/blog/unveiling-milvus-2-3-milestone-release-offering-support-for-gpu-arm64-cdc-and-other-features.md)

## CPU Requirements

### CRITICAL: SIMD Instruction Requirement

**Milvus REQUIRES at least one SIMD instruction set:**
- SSE4.2
- AVX
- AVX2
- AVX512

This is a **hard requirement**. Without SIMD support, Milvus will crash with "Illegal instruction" error.

### Why SIMD is Required

Milvus's vector search engine (Knowhere) uses SIMD for:
- Index building
- Distance calculations (L2, IP, Cosine)
- Vector operations

SIMD provides 20-30% performance improvement (AVX512 vs AVX2).

### CPU Compatibility Check

```bash
# Check SIMD support
lscpu | grep -e sse4_2 -e avx -e avx2 -e avx512

# Or
cat /proc/cpuinfo | grep -E 'sse4_2|avx|avx2|avx512'
```

### Problematic CPUs

| CPU | SIMD Support | Milvus Compatible |
|-----|--------------|-------------------|
| Intel Core i3/i5/i7 (Haswell+) | AVX2 | ✅ |
| Intel Core (Sandy Bridge+) | AVX | ✅ |
| Intel Core (Nehalem) | SSE4.2 only | ✅ |
| **Intel Celeron J4105** | SSE4.2 only | ⚠️ Works but verify |
| Intel Atom N-series (old) | May lack SSE4.2 | ❌ |
| AMD Ryzen | AVX2 | ✅ |
| Apple M1/M2/M3 | ARM NEON | ✅ |
| Raspberry Pi 4+ (64-bit) | ARM NEON | ✅ |

**Sources**:
- [Milvus Operational FAQ](https://milvus.io/docs/operational_faq.md)
- [Knowhere Documentation](https://milvus.io/docs/knowhere.md)

## Resource Requirements

### Memory

**Milvus is resource-intensive.** Memory requirements depend on:
- Number of vectors
- Vector dimensions
- Index type

| Environment | RAM | Notes |
|-------------|-----|-------|
| Docker minimum | 8GB | MacOS Docker VM setting |
| Small production | 16GB | < 10M vectors |
| Medium production | 32GB | 10-50M vectors |
| Large production | 64GB+ | 50M+ vectors |

**Index Memory Formulas** (approximate):

| Index Type | Memory Formula |
|------------|----------------|
| IVF_FLAT | vectors × dim × 4 bytes |
| IVF_SQ8 | vectors × dim × 1 byte |
| HNSW | vectors × (dim × 4 + M × 8 × 2) |
| DISKANN | vectors × dim × 1 byte (in-memory part) |

### CPU

| Workload | Cores |
|----------|-------|
| Development | 2 |
| Small production | 4-8 |
| Medium production | 8-16 |
| Large production | 16+ |

### Disk

| Requirement | Value |
|-------------|-------|
| Minimum | 10GB |
| Production | SSD recommended |
| Volume mount | `/var/lib/milvus` |

**Sources**:
- [Milvus Prerequisites](https://milvus.io/docs/prerequisite-docker.md)
- [Milvus Resource Allocation](https://milvus.io/docs/allocate.md)

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 19530 | gRPC | Primary API |
| 9091 | HTTP | Health check, metrics |

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "curl", "-f", "http://localhost:9091/healthz"]
  interval: 30s
  timeout: 10s
  retries: 5
  start_period: 90s
```

**Why this approach**:
- `/healthz` endpoint is purpose-built for health checks
- 90-second start period allows index loading
- curl is available in the container

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ETCD_USE_EMBED` | `false` | Use embedded etcd (standalone) |
| `ETCD_DATA_DIR` | - | Embedded etcd data directory |
| `COMMON_STORAGETYPE` | `minio` | Storage backend (local for standalone) |

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| ARM32 | armv7l, armv6l | Fail | No images available |
| Missing all SIMD | x86_64, no SSE4.2/AVX | Fail | Hard requirement |
| Celeron J/N | processor_patterns | Warning | May work with SSE4.2 |
| < 8GB RAM | memory_mb_less_than: 8192 | Fail | Minimum for stability |
| < 16GB RAM | memory_mb_less_than: 16384 | Warning | Production needs more |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `Illegal instruction\|SIGILL` | Missing SIMD | Different CPU or pgvector |
| `OOM\|out of memory` | Memory exhaustion | Increase to 8GB+ |
| `etcd.*failed` | Embedded etcd issue | Check disk/permissions |
| `Unsupported system page size` | ARM64 jemalloc | Update Milvus version |

## Milvus Lite Alternative

For resource-constrained environments, consider **Milvus Lite**:

```python
# pip install pymilvus
from pymilvus import MilvusClient

# Embedded SQLite-like usage
client = MilvusClient("./milvus_demo.db")
```

**Characteristics**:
- No separate server needed
- Persists to SQLite file
- < 1M vectors recommended
- Same API as full Milvus
- Integrates with LangChain, LlamaIndex

**Sources**:
- [Milvus Lite Blog](https://milvus.io/blog/introducing-milvus-lite.md)
- [Milvus Lite GitHub](https://github.com/milvus-io/milvus-lite)

## Comparison with Alternatives

| Feature | Milvus | Weaviate | pgvector |
|---------|--------|----------|----------|
| Scale | Billions | Millions | Millions |
| Architecture | Distributed | Standalone | Extension |
| ARM64 | ✅ | ✅ | ✅ |
| ARM32 | ❌ | ❌ | ✅ |
| Memory footprint | High | Medium | Low |
| SIMD required | Yes | Optional | No |
| Setup complexity | High | Medium | Low |
| SQL integration | No | No | ✅ Native |

**Recommendations for Zen Garden**:
- Large-scale AI workloads → Milvus
- General vector search → Weaviate
- Resource-constrained, ARM32, SQL integration → pgvector

## Index Types

Milvus supports multiple index types:

| Index | Type | Use Case |
|-------|------|----------|
| FLAT | Exact | Small datasets, high accuracy |
| IVF_FLAT | Approximate | Balanced accuracy/speed |
| IVF_SQ8 | Approximate | Memory-efficient |
| IVF_PQ | Approximate | Very large datasets |
| HNSW | Graph-based | Fast queries, more memory |
| DISKANN | On-disk | Large datasets, limited RAM |
| GPU_IVF_FLAT | GPU | Fast indexing with CUDA |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 (8GB) | ⚠️ Marginal | May work but resource-constrained |
| Pi 4 (8GB, 64-bit) | ⚠️ Marginal | Standalone barely fits |
| Pi 4 (4GB) | ❌ | Insufficient RAM |
| Pi 3/2/Zero | ❌ | No ARM32, insufficient RAM |

**Recommendation**: Use pgvector or Weaviate on Raspberry Pi instead of Milvus.

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| No auth by default | Enable authentication for production |
| Network exposure | Internal network (zen-garden) |
| TLS | Configure for production |

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (amd64 + arm64)
- [x] ARM32 limitation documented
- [x] SIMD requirements documented (SSE4.2/AVX/AVX2/AVX512)
- [x] Memory constraints documented (8GB minimum)
- [x] Health check command verified
- [x] Milvus Lite alternative documented
- [x] Index types documented

## Files

| File | Status |
|------|--------|
| `milvus.snippet.yaml` | ✅ Updated (version, env vars, start_period) |
| `milvus.compatibility.yaml` | ✅ Updated (SIMD rules, ARM32 fail) |
| `milvus.frontmatter.json` | ✅ Updated (ports, memory note) |
| `milvus.research.md` | ✅ Created |

## References

1. [Milvus Official Documentation](https://milvus.io/docs/)
2. [Milvus Docker Hub](https://hub.docker.com/r/milvusdb/milvus)
3. [Milvus Prerequisites](https://milvus.io/docs/prerequisite-docker.md)
4. [Milvus Operational FAQ](https://milvus.io/docs/operational_faq.md)
5. [Milvus 2.3 ARM64 Announcement](https://milvus.io/blog/unveiling-milvus-2-3-milestone-release-offering-support-for-gpu-arm64-cdc-and-other-features.md)
6. [Knowhere Vector Engine](https://milvus.io/docs/knowhere.md)
7. [Milvus Lite Introduction](https://milvus.io/blog/introducing-milvus-lite.md)
8. [Milvus Resource Allocation](https://milvus.io/docs/allocate.md)
