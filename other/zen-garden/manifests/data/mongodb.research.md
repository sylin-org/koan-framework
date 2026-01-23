# MongoDB Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | MongoDB |
| **Category** | Document Database (NoSQL) |
| **Primary Use** | JSON-like document storage, flexible schemas |
| **License** | Server Side Public License (SSPL) |
| **Project URL** | https://www.mongodb.com/ |
| **Docker Hub** | https://hub.docker.com/_/mongo |

## Docker Image Analysis

### Image Selection
**Selected**: `mongo:7`

MongoDB 7 is the latest major version with improved performance and features. However, it has strict hardware requirements that require compatibility fallbacks.

### Version Strategy

| Version | AVX Required | ARM Requirement | SSE4.2 Required | Use Case |
|---------|--------------|-----------------|-----------------|----------|
| mongo:7 | ✅ Yes | ARMv8.2-A+ | ✅ Yes | Modern hardware |
| mongo:6 | ✅ Yes | ARMv8.2-A+ | ✅ Yes | Modern hardware |
| mongo:5 | ✅ Yes | ARMv8.2-A+ | ✅ Yes | Modern hardware |
| **mongo:4.4** | ❌ No | ARMv8.0-A+ | ✅ Yes | **Fallback for non-AVX** |
| mongo:4.2 | ❌ No | ARMv7+ | ❌ No | Legacy fallback |

### Architecture Support

| Architecture | mongo:7 | mongo:4.4 | Notes |
|--------------|---------|-----------|-------|
| amd64 (with AVX) | ✅ | ✅ | Full support |
| amd64 (no AVX) | ❌ | ✅ | Celeron J-series, older Atoms |
| arm64 (ARMv8.2-A+) | ✅ | ✅ | AWS Graviton2+, Pi 5 |
| arm64 (ARMv8.0-A) | ❌ | ✅ | Raspberry Pi 4, older ARM |
| arm32v7 | ❌ | ✅ | Raspberry Pi 2/3 |
| arm32v6 | ❌ | ❌ | Not supported |

**Sources**:
- [MongoDB Production Notes](https://www.mongodb.com/docs/manual/administration/production-notes/)
- [MongoDB ARM64 Discussion](https://www.mongodb.com/community/forums/t/mongodb-and-the-pi-4-on-ubuntu-64-bit-aka-armv8-0-a-support/220635)
- [Docker Hub mongo](https://hub.docker.com/_/mongo)

## Critical CPU Requirements

### AVX Requirement (x86/AMD64)

**MongoDB 5.0+ requires AVX CPU instruction support.**

This is a **hard requirement** with no workaround except:
1. Using MongoDB 4.4 (fallback)
2. Recompiling MongoDB from source without AVX (not practical)
3. Using community builds without AVX (unsupported)

**Why AVX?**: MongoDB 5.0's WiredTiger storage engine uses AVX-optimized memcpy operations, providing up to 63% read throughput improvement. This optimization cannot be disabled at runtime.

**Affected Processors**:

| Processor | AVX Support | MongoDB 7 | Fallback |
|-----------|-------------|-----------|----------|
| Intel Core i3/i5/i7 (Sandy Bridge+) | ✅ | ✅ | - |
| Intel Core (Nehalem) | ❌ | ❌ | mongo:4.4 |
| **Intel Celeron J4105** | ❌ | ❌ | mongo:4.4 |
| **Intel Celeron J3455** | ❌ | ❌ | mongo:4.4 |
| Intel Atom C3758 | ❌ | ❌ | mongo:4.4 |
| AMD Ryzen | ✅ | ✅ | - |
| AMD Bulldozer+ | ✅ | ✅ | - |

**Error Message**: `MongoDB 5.0+ requires a CPU with AVX support` or `Illegal instruction`

**How to Check**:
```bash
cat /proc/cpuinfo | grep avx
```

**Sources**:
- [MongoDB AVX Discussion](https://github.com/turnkeylinux/tracker/issues/1724)
- [Proxmox MongoDB Issue](https://forum.proxmox.com/threads/mongo-db-5-0-not-install.95857/)

### ARM64 Microarchitecture Requirement

**MongoDB 5.0+ requires ARMv8.2-A or later microarchitecture.**

| Device | ARM Version | MongoDB 7 | Fallback |
|--------|-------------|-----------|----------|
| Raspberry Pi 5 | ARMv8.2-A | ✅ | - |
| AWS Graviton2/3 | ARMv8.2-A+ | ✅ | - |
| Apple M1/M2/M3 | ARMv8.4-A+ | ✅ | - |
| **Raspberry Pi 4** | ARMv8.0-A | ❌ | mongo:4.4 |
| Raspberry Pi 3 | ARMv8.0-A | ❌ | mongo:4.4 |

**Error Message**: `MongoDB 5.0+ requires ARMv8.2-A or higher, and your current system does not appear to implement any of the common features for that!`

**Workarounds for Raspberry Pi 4**:
1. Use `mongo:4.4` (recommended, official)
2. Unofficial binaries: [themattman/mongodb-raspberrypi-binaries](https://github.com/themattman/mongodb-raspberrypi-binaries)
3. Compile from source (complex, unsupported)

**Sources**:
- [MongoDB Pi 4 Discussion](https://www.mongodb.com/community/forums/t/mongodb-and-the-pi-4-on-ubuntu-64-bit-aka-armv8-0-a-support/220635)
- [Raspberry Pi MongoDB Binaries](https://github.com/themattman/mongodb-raspberrypi-binaries)

### SSE4.2 Requirement

**MongoDB 4.4+ requires SSE4.2 CPU support.**

This affects very old processors (pre-2008):
- Intel: Requires Nehalem (2008) or later
- AMD: Requires Bulldozer (2011) or later

Fallback: `mongo:4.2`

## Resource Requirements

### Memory

| Workload | Memory | Notes |
|----------|--------|-------|
| Minimum | 512MB | Bare operation |
| Recommended | 1-2GB | Small databases |
| WiredTiger cache | 50% of RAM - 1GB | Default cache size |
| Large datasets | 4GB+ | Adjust cache settings |

**WiredTiger Cache**: By default, WiredTiger uses 50% of RAM minus 1GB (minimum 256MB). For a 2GB system, this means ~512MB cache.

### CPU

| Requirement | Value |
|-------------|-------|
| Minimum Cores | 1 |
| Recommended | 2+ |
| For indexes | More cores help |

### Disk

| Requirement | Value |
|-------------|-------|
| Minimum | 200MB (empty) |
| Journal | ~500MB pre-allocated |
| Data path | `/data/db` |

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 27017 | TCP | MongoDB wire protocol |

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "mongosh", "--eval", "db.adminCommand('ping')"]
  interval: 10s
  timeout: 5s
  retries: 5
```

**Why `mongosh`**: The modern MongoDB shell (`mongosh`) replaced the legacy `mongo` shell. It's included in recent MongoDB images.

### Alternative Health Checks
```bash
# Legacy shell (older images)
mongo --eval "db.adminCommand('ping')"

# Using mongosh with auth
mongosh -u admin -p password --eval "db.adminCommand('ping')"
```

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| Celeron J-series | processor_patterns | Fallback to 4.4 | Known no-AVX CPUs |
| Missing AVX | cpu_features_missing | Fallback to 4.4 | MongoDB 5+ requirement |
| Missing SSE4.2 | cpu_features_missing | Fallback to 4.2 | MongoDB 4.4+ requirement |
| ARMv6 | architectures | Fail | No support at all |
| < 512MB RAM | memory_mb_less_than | Fail | Minimum requirement |

### Post-install Checks

| Pattern | Issue | Action |
|---------|-------|--------|
| `MongoDB 5.0+ requires a CPU with AVX` | x86 AVX missing | Fallback to 4.4 |
| `MongoDB 5.0+ requires ARMv8.2-A` | ARM too old | Fallback to 4.4 |
| `Illegal instruction` | CPU mismatch | Fallback to 4.2 |
| `ENOSPC|No space left` | Disk full | Suggestion |
| `Cannot allocate memory|OOM` | OOM | Suggestion |

## Virtualization Considerations

### Proxmox/KVM

By default, Proxmox uses `kvm64` CPU type which doesn't expose AVX to the guest.

**Solution**: Change CPU type to `host` or `x86-64-v3`:
```
CPU Type: host (or x86-64-v3)
```

### VMware

VMware vSphere typically passes through AVX if the host supports it. Check VM compatibility level.

### VirtualBox

Enable "Nested VT-x/AMD-V" and ensure host has AVX support.

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| No auth by default | Enable authentication in production |
| Network exposure | Internal network only (zen-garden) |
| Default ports | Firewall or network isolation |

**Production Auth Setup**:
```yaml
environment:
  MONGO_INITDB_ROOT_USERNAME: admin
  MONGO_INITDB_ROOT_PASSWORD: ${MONGO_PASSWORD}
```

## Comparison with Alternatives

| Feature | MongoDB | PostgreSQL | CouchDB |
|---------|---------|------------|---------|
| Schema | Flexible | Fixed | Flexible |
| Query Language | MQL | SQL | MapReduce |
| Joins | $lookup | Native | Limited |
| CPU Requirements | **AVX (5+)** | None | None |
| ARM Pi 4 | 4.4 only | ✅ Full | ✅ Full |
| Homelab Friendly | ⚠️ | ✅ | ✅ |

**Recommendation**: For homelabs with older hardware, PostgreSQL (with JSON support) may be a better choice than MongoDB.

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support documented
- [x] AVX requirement documented and handled
- [x] ARM microarchitecture requirement documented and handled
- [x] SSE4.2 requirement documented and handled
- [x] Memory constraints validated
- [x] Fallback versions defined
- [x] Health check command verified
- [x] Post-install healthcheck patterns comprehensive

## Files

| File | Status |
|------|--------|
| `mongodb.snippet.yaml` | ✅ Validated |
| `mongodb.compatibility.yaml` | ✅ Updated (added ARM pattern) |
| `mongodb.frontmatter.json` | ✅ Validated |
| `mongodb.research.md` | ✅ Created |

## References

1. [MongoDB Production Notes](https://www.mongodb.com/docs/manual/administration/production-notes/)
2. [MongoDB Docker Hub](https://hub.docker.com/_/mongo)
3. [MongoDB AVX Requirement Discussion](https://github.com/turnkeylinux/tracker/issues/1724)
4. [MongoDB ARM64 Forum Discussion](https://www.mongodb.com/community/forums/t/mongodb-and-the-pi-4-on-ubuntu-64-bit-aka-armv8-0-a-support/220635)
5. [Raspberry Pi MongoDB Binaries](https://github.com/themattman/mongodb-raspberrypi-binaries)
6. [Proxmox MongoDB Issue](https://forum.proxmox.com/threads/mongo-db-5-0-not-install.95857/)
7. [MongoDB 5.0 Illegal Instruction Solution](https://inf.news/en/news/ce3a5224410b4f023f5d20fb9124e859.html)
