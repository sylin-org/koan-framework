# Weaviate Vector Database Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | Weaviate |
| **Category** | Vector Database |
| **Primary Use** | Vector search, semantic search, AI applications |
| **License** | BSD-3-Clause |
| **Project URL** | https://weaviate.io/ |
| **GitHub** | https://github.com/weaviate/weaviate |
| **Docker Registry** | cr.weaviate.io/semitechnologies/weaviate |

## Docker Image Analysis

### Official Image
- **Repository**: `semitechnologies/weaviate` (Docker Hub) or `cr.weaviate.io/semitechnologies/weaviate` (Official Registry)
- **Maintained by**: Weaviate B.V.
- **Current Version**: v1.35.3 (January 2025)
- **Image Size**: ~52MB (compressed)
- **Base**: Built with Go for portability

### Version Selection Decision
**Selected**: `cr.weaviate.io/semitechnologies/weaviate:1.35`

**Rationale**:
- Uses official Weaviate registry (faster, more reliable)
- Version-pinned to 1.35 major.minor for stability
- Avoids breaking changes from major version upgrades
- Allows patch updates for security fixes

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | ✅ | Primary platform, full SIMD optimizations |
| arm64 | ✅ | Apple Silicon (M1/M2/M3), Raspberry Pi 4+ |
| arm32 | ❌ | Not supported |
| i386 | ❌ | Not supported |

**Conclusion**: Two-architecture support (amd64 + arm64). No fallback images needed for architecture.

## CPU Feature Requirements

### SIMD Optimizations

Weaviate uses SIMD instructions for vector distance calculations:
- **AVX-256**: Used for most vector operations
- **AVX-512**: Used on newer Intel CPUs (Sapphire/Emerald Rapids)
- **SSE4.2**: Baseline requirement

**Critical Finding**: 40-60% of CPU time in HNSW indexing is spent on vector distance calculations using SIMD.

### Feature Detection

Weaviate uses Go's `sys/cpu` package for runtime feature detection:
```go
// Simplified - actual code in weaviate/usecases/distancer
if cpu.X86.HasAVX2 {
    // Use AVX2 optimized distance function
} else {
    // Fallback to scalar implementation
}
```

### CPUs Without AVX - Risk Assessment

| CPU Series | AVX Support | Risk Level | Notes |
|------------|-------------|------------|-------|
| Intel Core i3/i5/i7 (Haswell+) | ✅ AVX2 | Low | Full support |
| Intel Core (Sandy Bridge+) | ✅ AVX | Low | Good support |
| AMD Ryzen | ✅ AVX2 | Low | Full support |
| Apple M1/M2/M3 | N/A (ARM) | Low | Uses ARM NEON |
| Intel Celeron J4105 | ❌ No AVX | **Medium** | Fallback code exists |
| Intel Celeron J3455 | ❌ No AVX | **Medium** | Fallback code exists |
| Intel Atom N-series | ❌ No AVX | **High** | Very old, may lack SSE4.2 |

**Decision**: Added compatibility warning for Celeron J/N-series. No hard fail since Weaviate claims fallback support, but users should monitor for SIGILL errors.

**Sources**:
- [Weaviate Forum: AVX Instruction Sets](https://forum.weaviate.io/t/is-it-possible-to-turn-off-the-use-of-any-avx-instruction-set-s-for-weaviate/1401)
- [Weaviate Blog: Intel Emerald Rapids](https://weaviate.io/blog/intel)

## Resource Requirements

### Memory

| Workload | Memory Required |
|----------|-----------------|
| Minimal (< 100K vectors) | 1-2 GB |
| Small (100K - 1M vectors) | 2-4 GB |
| Medium (1M - 10M vectors) | 4-16 GB |
| Large (10M+ vectors) | 16+ GB |

**Formula**: Memory ≈ 2 × (vectors × (dimensions × 4 bytes + connections × 10 bytes))

**Example**: 1M vectors × 768 dimensions ≈ 3GB RAM

**Decision**: Set minimum at 2GB (hard requirement for practical use)

### CPU

| Requirement | Value |
|-------------|-------|
| Minimum Cores | 1 |
| Recommended | 4+ |
| Scaling | Linear with query load |

Weaviate CPU usage scales with:
- Import speed (HNSW graph building)
- Query throughput
- Vector dimensionality

### GPU

**Weaviate core does NOT require GPU.**

Optional modules that can use GPU:
- `text2vec-transformers` - ML inference
- `img2vec-neural` - Image vectorization
- `multi2vec-*` - Multimodal models

These run in separate containers with CUDA support.

**Sources**:
- [Weaviate Resource Planning](https://docs.weaviate.io/weaviate/concepts/resources)

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 8080 | HTTP | REST API |
| 50051 | gRPC | gRPC API (faster for bulk operations) |

### gRPC Support
Added in Weaviate 1.19+. Provides:
- Faster batch imports
- Streaming responses
- Better performance for high-throughput scenarios

**Decision**: Expose both ports in snippet.

## Healthcheck Strategy

### Endpoints Available

| Endpoint | Purpose | Response |
|----------|---------|----------|
| `/v1/.well-known/ready` | Readiness check | 200 when ready |
| `/v1/.well-known/live` | Liveness check | 200 when alive |
| `/v1/meta` | Full metadata | Version, modules, etc. |

**Decision**: Use `/v1/.well-known/ready` for healthcheck (lighter weight than `/v1/meta`)

### Healthcheck Command
Changed from `curl` to `wget` for better container compatibility:
```yaml
healthcheck:
  test: ["CMD", "wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:8080/v1/.well-known/ready"]
```

## Compatibility Constraints Summary

### Pre-flight Checks (compatibility_rules)

| Condition | Action | `warn_only` | Rationale |
|-----------|--------|-------------|-----------|
| memory < 2GB | Fail | `false` | Insufficient for vector operations |
| Celeron J/N-series | Warning | `true` | No AVX, may have issues but fallback exists |
| Missing SSE4.2 | Warning | `true` | May still work with degraded performance |

**Note on Warnings**: Rules with `warn_only: true` produce warnings that:
1. Are displayed to the user before installation proceeds
2. Apply a **-50 point penalty** to the stone's fitness score (vs -999 for Fail)
3. Allow installation to proceed with user acknowledgment

This ensures low-end CPUs aren't blocked but are deprioritized in "offer wishfully" scenarios.

### Post-install Checks (post_install_healthcheck)

| Pattern | Issue |
|---------|-------|
| `OOM`, `out of memory` | Memory exhaustion |
| `SIGILL`, `illegal instruction` | CPU compatibility |
| `failed to load index` | Data corruption |
| `failed to bind.*50051` | Port conflict |

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Anonymous access | Enabled by default (development mode) |
| No encryption | Network isolation via zen-garden network |
| API exposure | Internal network only |

**Production Note**: For production, configure:
- `AUTHENTICATION_APIKEY_ENABLED: true`
- `AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED: false`
- TLS termination at reverse proxy

## Comparison with Alternatives

| Feature | Weaviate | Milvus | pgvector |
|---------|----------|--------|----------|
| Architecture | Standalone | Distributed | PostgreSQL extension |
| Memory footprint | Medium | High | Low |
| ARM support | ✅ | Partial | ✅ |
| Low-end CPU | Risky | Risky | ✅ |
| Complexity | Medium | High | Low |
| Maturity | Good | Good | Good |
| Use case | AI-native apps | Large scale | Existing Postgres |

**Recommendation for low-resource environments**: pgvector (PostgreSQL extension) is safer for Celeron/Atom CPUs.

## Module Support on ARM64

| Module | amd64 | arm64 | Notes |
|--------|-------|-------|-------|
| Core Weaviate | ✅ | ✅ | Full support |
| text2vec-contextionary | ✅ | ✅ | Works |
| text2vec-transformers | ✅ | ✅ | Works, slower on ARM |
| img2vec-neural (keras) | ✅ | ❌ | amd64 only |
| img2vec-neural (pytorch) | ✅ | ✅ | Works |
| text2vec-gpt4all | ✅ | ❌ | amd64 only |

**Sources**:
- [Weaviate Docker Installation](https://docs.weaviate.io/weaviate/installation/docker-compose)
- [Docker Blog: Weaviate](https://www.docker.com/blog/how-to-get-started-weaviate-vector-database-on-docker/)

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (amd64, arm64)
- [x] CPU feature requirements researched (AVX optional but beneficial)
- [x] Memory constraints documented
- [x] Healthcheck endpoint validated
- [x] Port configuration verified (HTTP + gRPC)
- [x] Security considerations reviewed
- [x] Compatibility rules match research findings
- [x] Post-install healthcheck patterns comprehensive

## Files Modified

| File | Changes |
|------|---------|
| `weaviate.snippet.yaml` | Updated image registry, added gRPC port, improved healthcheck |
| `weaviate.compatibility.yaml` | Added Celeron warning, SSE4.2 check, improved healthcheck patterns |
| `weaviate.frontmatter.json` | Added gRPC port, improved description and tags |
| `weaviate.research.md` | This research document |

## References

1. [Weaviate Official Documentation](https://docs.weaviate.io/)
2. [Weaviate GitHub Repository](https://github.com/weaviate/weaviate)
3. [Weaviate Docker Hub](https://hub.docker.com/r/semitechnologies/weaviate)
4. [Weaviate Resource Planning Guide](https://docs.weaviate.io/weaviate/concepts/resources)
5. [Weaviate Community Forum - AVX Discussion](https://forum.weaviate.io/t/is-it-possible-to-turn-off-the-use-of-any-avx-instruction-set-s-for-weaviate/1401)
6. [Intel Emerald Rapids Optimization](https://weaviate.io/blog/intel)
7. [Weaviate v1.35.3 Release](https://github.com/weaviate/weaviate/releases/tag/v1.35.3)
