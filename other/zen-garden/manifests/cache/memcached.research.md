# Memcached Offering Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | Memcached |
| **Category** | Cache |
| **Primary Use** | Distributed memory object caching |
| **License** | BSD-3-Clause |
| **Project URL** | https://memcached.org/ |
| **Docker Hub** | https://hub.docker.com/_/memcached |

## Docker Image Analysis

### Official Image
- **Repository**: `memcached` (Docker Official Image)
- **Maintained by**: Docker Community
- **Current Version**: 1.6.40 (as of research date)
- **Base Image**: Debian (default), Alpine variant available

### Version Selection Decision
**Selected**: `memcached:1.6` (version-pinned major.minor)

**Rationale**:
- Latest stable branch with active maintenance
- Avoids automatic major version jumps
- 1.6.x has been stable since 2020
- Patch versions (1.6.x) are backward compatible

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | ✅ | Primary platform |
| arm64v8 | ✅ | Raspberry Pi 4+, Apple Silicon |
| arm32v7 | ✅ | Raspberry Pi 2/3 |
| arm32v6 | ✅ | Raspberry Pi Zero/1 |
| arm32v5 | ✅ | Older ARM devices |
| i386 | ✅ | Legacy 32-bit x86 |
| ppc64le | ✅ | IBM Power |
| riscv64 | ✅ | RISC-V |
| s390x | ✅ | IBM Z |

**Conclusion**: Excellent cross-platform support. No architecture-specific fallbacks needed.

## Resource Requirements

### Memory
| Requirement | Value | Notes |
|-------------|-------|-------|
| Minimum | ~10MB | Process overhead only |
| Recommended Minimum | 64MB | Practical for small caches |
| Default Allocation | 64MB | `-m 64` default |
| Maximum | System RAM | Limited by available memory |

**Decision**: Warning for systems with < 128MB total RAM (makes caching impractical)

### CPU
| Requirement | Value |
|-------------|-------|
| CPU Features | None required |
| AVX/SSE | Not required |
| Minimum Cores | 1 |
| Default Threads | 4 (`-t 4`) |

**Conclusion**: No CPU feature requirements. Runs on any processor.

### Storage
| Requirement | Value |
|-------------|-------|
| Persistent Volume | Not needed |
| Disk Space | ~50MB for image |

**Decision**: No volumes defined. Memcached is ephemeral by design.

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 11211 | TCP | Memcached protocol |
| 11211 | UDP | Memcached protocol (optional) |

**Decision**: Expose TCP 11211 only. UDP disabled by default for security.

## Healthcheck Strategy

### Options Evaluated

1. **TCP Socket Check**
   - Simple but doesn't verify protocol
   - `nc -z localhost 11211`

2. **Stats Command** ✅ Selected
   - Verifies protocol functionality
   - `echo stats | nc localhost 11211 | grep -q STAT`
   - Returns version, uptime, memory stats

3. **Version Command**
   - `echo version | nc localhost 11211`
   - Lightweight but less comprehensive

**Decision**: Stats command provides functional verification with minimal overhead.

## Compatibility Constraints

### CPU Features
**None required**. Memcached is written in C and uses portable code without SIMD dependencies.

Unlike MongoDB 5+ (requires AVX) or some AI workloads, memcached has no CPU feature gates.

### Memory Constraints
| Condition | Action |
|-----------|--------|
| < 128MB total RAM | Warning (suggestion to reduce `-m` allocation) |
| < 64MB total RAM | Likely unusable for practical caching |

### Known Issues

1. **Memory Allocation Failures**
   - Pattern: `failed to allocate`, `Cannot allocate memory`
   - Cause: Requested more memory than available
   - Solution: Reduce `-m` parameter

2. **UDP Amplification (DDoS)**
   - Default: UDP disabled in recent versions
   - Historical: CVE-2018-1000115
   - Our config: TCP only, no UDP exposure

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| No authentication | Network isolation via `zen-garden` network |
| Unencrypted protocol | Internal network only |
| UDP amplification | UDP not exposed |

**Future Enhancement**: SASL authentication support in manifest configuration.

## Configuration Options

### Commonly Tuned Parameters
| Flag | Default | Description |
|------|---------|-------------|
| `-m <mb>` | 64 | Memory limit in MB |
| `-c <conn>` | 1024 | Max connections |
| `-t <threads>` | 4 | Worker threads |
| `-I <size>` | 1MB | Max item size |

**Decision**: Use defaults in snippet. Users can override via environment customization.

## Comparison with Alternatives

| Feature | Memcached | Redis |
|---------|-----------|-------|
| Data Structures | Key-value only | Rich (lists, sets, sorted sets, streams) |
| Persistence | None | RDB/AOF |
| Replication | Client-side | Built-in |
| Memory Efficiency | Higher (no persistence overhead) | Lower |
| Complexity | Simple | Feature-rich |
| Use Case | Pure caching | Caching + data store |

**Conclusion**: Memcached is the right choice for pure, simple caching needs. Redis for more complex requirements.

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified
- [x] No CPU feature requirements (no AVX, etc.)
- [x] Memory constraints documented
- [x] Healthcheck command tested
- [x] Port configuration standard
- [x] Security considerations reviewed
- [x] Compatibility rules match implementation capabilities

## Files Created

| File | Purpose |
|------|---------|
| `memcached.snippet.yaml` | Docker Compose service definition |
| `memcached.compatibility.yaml` | Hardware compatibility rules |
| `memcached.frontmatter.json` | Offering metadata |
| `memcached.research.md` | This research document |

## References

1. Memcached Official Documentation: https://memcached.org/
2. Docker Hub memcached: https://hub.docker.com/_/memcached
3. Memcached Wiki: https://github.com/memcached/memcached/wiki
4. Memcached Protocol: https://github.com/memcached/memcached/blob/master/doc/protocol.txt
