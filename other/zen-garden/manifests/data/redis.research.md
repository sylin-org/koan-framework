# Redis Stack Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | Redis Stack |
| **Category** | In-Memory Data Store / Cache |
| **Primary Use** | Caching, real-time data, pub/sub, vector search |
| **License** | Redis Source Available License (RSALv2) / SSPL |
| **Project URL** | https://redis.io/ |
| **Docker Hub (Stack)** | https://hub.docker.com/r/redis/redis-stack |
| **Docker Hub (OSS)** | https://hub.docker.com/_/redis |

## Image Selection Strategy

### Redis Stack vs Standard Redis

| Feature | Redis Stack | Standard Redis |
|---------|-------------|----------------|
| Core Redis | ✅ | ✅ |
| RediSearch | ✅ | ❌ |
| RedisJSON | ✅ | ❌ |
| RedisGraph | ✅ | ❌ |
| RedisTimeSeries | ✅ | ❌ |
| RedisBloom | ✅ | ❌ |
| Vector Search | ✅ | ❌ |
| Multi-arch support | ⚠️ Limited | ✅ Excellent |
| ARM32 support | ❌ | ✅ |

### Selected Image
**Primary**: `redis/redis-stack:latest` (amd64 only)
**ARM64 Fallback**: `redis/redis-stack-server:7.4.0-v0-arm64`
**ARM32 Fallback**: `redis:7-alpine`

## Critical: Multi-Architecture Support

### The Problem

**`redis/redis-stack:latest` does NOT have multi-arch manifest support.**

This means:
- On amd64: Works correctly
- On ARM64: Fails silently or pulls wrong architecture
- On ARM32: No builds available at all

### Architecture-Specific Tags

| Architecture | Recommended Image |
|--------------|-------------------|
| amd64 (x86_64) | `redis/redis-stack:latest` |
| arm64 (aarch64) | `redis/redis-stack-server:7.4.0-v0-arm64` |
| arm32v7 (armv7l) | `redis:7-alpine` (no Stack) |
| arm32v6 (armv6l) | `redis:7-alpine` (no Stack) |

### Standard Redis Multi-Arch

The official `redis` image (without Stack) has excellent support:
- amd64, arm64v8, arm32v7, arm32v6, arm32v5
- i386, ppc64le, s390x, riscv64, mips64le

**Sources**:
- [Docker Hub redis](https://hub.docker.com/_/redis)
- [Docker Hub redis-stack](https://hub.docker.com/r/redis/redis-stack)

## Important: Redis Stack Deprecation Notice

**Starting with Redis 8** (expected 2025):
- Redis Stack modules will be merged into mainline Redis
- RediSearch, RedisJSON, etc. will be part of standard Redis
- Redis Stack maintenance releases end December 2025

**Recommendation**: Plan to migrate to `redis:8` when available for unified experience.

**Source**: [Redis Stack Documentation](https://redis.io/docs/latest/operate/oss_and_stack/install/install-stack/)

## CPU Requirements

### No AVX/SSE Requirements

Unlike MongoDB, **Redis does not require AVX or special CPU instructions**.

Redis is written in C and compiled for broad compatibility:
- No SIMD optimizations that would cause SIGILL
- Runs on Celeron J4105 and similar low-end CPUs
- Runs on Raspberry Pi (all models)

### CPU Considerations

| Factor | Notes |
|--------|-------|
| Single-threaded | Most operations are single-threaded |
| CPU bottleneck | Rare - usually memory or network bound |
| Recommended cores | 1-2 for small deployments |
| I/O threads | Optional multi-threading for I/O (Redis 6+) |

## Resource Requirements

### Memory

| Workload | Memory | Notes |
|----------|--------|-------|
| Minimum | 64MB | Bare Redis, no data |
| With Stack modules | 256MB | Recommended minimum |
| Production | Depends on data | ~1 byte overhead per key |
| Guideline | Keep 30% free | For operations and replication |

**Memory Formula**: Total keys × (key size + value size + ~50 bytes overhead)

### Disk

| Requirement | Value |
|-------------|-------|
| Persistence | Optional (RDB/AOF) |
| Volume mount | `/data` |
| Disk usage | ~1x memory for RDB snapshots |

## Network Configuration

| Port | Protocol | Purpose | Image |
|------|----------|---------|-------|
| 6379 | TCP | Redis protocol | All |
| 8001 | HTTP | RedisInsight UI | redis-stack (not -server) |

**Note**: `redis-stack-server` excludes RedisInsight (port 8001). Use `redis-stack` for full UI.

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "redis-cli", "ping"]
  interval: 10s
  timeout: 3s
  retries: 5
```

**Why `redis-cli ping`**:
- Built into Redis image
- Returns `PONG` if server is ready
- Fast and lightweight
- Works with and without authentication

### Alternative Health Checks
```bash
# With authentication
redis-cli -a ${REDIS_PASSWORD} ping

# Check cluster status
redis-cli cluster info

# Check memory
redis-cli info memory
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| None required | - | Redis works without env vars |
| `REDIS_ARGS` | - | Additional command-line args |

**For authentication** (production):
```yaml
command: redis-server --requirepass ${REDIS_PASSWORD}
```

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| ARM64 | aarch64 | Fallback to -arm64 tag | :latest is amd64-only |
| ARM32v7 | armv7l | Fallback to redis:7 | No Stack ARM32 builds |
| ARM32v6 | armv6l | Fallback to redis:7 | No Stack ARM32 builds |
| < 256MB RAM | memory | Fail | Stack modules need memory |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `Cannot allocate memory` | OOM | Increase RAM |
| `OOM` | Out of memory | Increase RAM |

## Raspberry Pi Considerations

| Device | Recommended Image | Notes |
|--------|-------------------|-------|
| Pi 5 | redis-stack-server (arm64) | Full Stack support |
| Pi 4 (64-bit OS) | redis-stack-server (arm64) | Full Stack support |
| Pi 4 (32-bit OS) | redis:7-alpine | No Stack modules |
| Pi 3 | redis:7-alpine | No Stack modules |
| Pi Zero 2 W | redis:7-alpine | Limited resources |
| Pi Zero/1 | redis:7-alpine | ARMv6, limited |

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| No auth by default | Set `requirepass` in production |
| Network exposure | Internal network (zen-garden) |
| Dangerous commands | Use `rename-command` to disable |
| Protected mode | Enabled by default in recent versions |

**Production Security**:
```yaml
command: >
  redis-server
  --requirepass ${REDIS_PASSWORD}
  --rename-command FLUSHALL ""
  --rename-command FLUSHDB ""
  --rename-command DEBUG ""
```

## Comparison with Alternatives

| Feature | Redis Stack | Memcached | KeyDB |
|---------|-------------|-----------|-------|
| Data structures | Rich | Key-value only | Rich |
| Persistence | RDB/AOF | None | RDB/AOF |
| Clustering | Built-in | Client-side | Built-in |
| Search | ✅ RediSearch | ❌ | ❌ |
| JSON | ✅ RedisJSON | ❌ | ❌ |
| Vector | ✅ | ❌ | ❌ |
| Multi-threaded | I/O threads | Yes | Yes |
| ARM support | Limited | Excellent | Good |

**Recommendation for ARM/low-resource**:
- Need search/vector: Use pgvector instead
- Pure caching: Memcached is more lightweight

## Validation Checklist

- [x] Docker image exists
- [x] Multi-architecture issues identified and handled
- [x] ARM64 fallback configured
- [x] ARM32 fallback configured
- [x] No CPU feature requirements (no AVX)
- [x] Memory constraints documented
- [x] Health check command verified
- [x] Redis Stack deprecation noted
- [x] Security considerations reviewed

## Files

| File | Status |
|------|--------|
| `redis.snippet.yaml` | ✅ Validated |
| `redis.compatibility.yaml` | ✅ **Updated** (added ARM fallbacks) |
| `redis.frontmatter.json` | ✅ Updated (added vector/JSON tags) |
| `redis.research.md` | ✅ Created |

## References

1. [Redis Documentation](https://redis.io/docs/)
2. [Docker Hub redis](https://hub.docker.com/_/redis)
3. [Docker Hub redis-stack](https://hub.docker.com/r/redis/redis-stack)
4. [Redis Stack GitHub](https://github.com/redis-stack/redis-stack)
5. [Redis Hardware Requirements](https://redis.io/docs/latest/operate/rs/installing-upgrading/install/plan-deployment/hardware-requirements/)
6. [Redis FAQ](https://redis.io/docs/latest/develop/get-started/faq/)
