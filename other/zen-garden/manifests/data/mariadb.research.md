# MariaDB Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | MariaDB |
| **Category** | Relational Database |
| **Primary Use** | MySQL-compatible ACID-compliant database |
| **License** | GPL v2 |
| **Project URL** | https://mariadb.org/ |
| **Docker Hub** | https://hub.docker.com/_/mariadb |
| **GitHub** | https://github.com/MariaDB/server |

## Why MariaDB over MySQL?

MariaDB is a community-developed fork of MySQL, created by MySQL's original developers after Oracle acquired Sun Microsystems (and thus MySQL).

| Aspect | MariaDB | MySQL |
|--------|---------|-------|
| License | GPL v2 (open source) | GPL with proprietary Enterprise |
| Governance | MariaDB Foundation | Oracle |
| ARM support | Excellent | Good |
| Storage engines | More options | Standard |
| Performance | Generally equivalent | Generally equivalent |
| Compatibility | Drop-in MySQL replacement | - |

**For Zen Garden**: MariaDB recommended for fully open-source stack.

## Docker Image Analysis

### Image Selection
**Selected**: `mariadb:11`

MariaDB 11 is the current LTS version with improved performance.

### Version Strategy

| Version | Status | Notes |
|---------|--------|-------|
| 11.x | LTS | Current, recommended |
| 10.11.x | LTS | Previous LTS |
| 10.6.x | LTS | Extended support |

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | ✅ | Primary platform |
| arm64v8 | ✅ | Apple Silicon, Graviton, Pi 4+ |
| arm32v7 | ✅ | Raspberry Pi 2/3 |
| i386 | ✅ | 32-bit x86 |
| ppc64le | ✅ | IBM Power |
| s390x | ✅ | IBM Z |

**Excellent multi-architecture support** - one of the best in the database category.

## CPU Compatibility

### No Special Requirements

MariaDB has **no AVX, SSE, or other SIMD requirements**.

Runs on:
- Any x86/x86_64 processor
- Any ARM processor (32-bit or 64-bit)
- Raspberry Pi (all models)
- Low-end Celeron/Atom processors

## Resource Requirements

### Memory

| Workload | RAM | Notes |
|----------|-----|-------|
| Minimum | 512MB | Basic operation |
| Small production | 1-2GB | InnoDB buffer pool ~50% |
| Medium production | 4-8GB | Good buffer pool sizing |
| Large production | 16GB+ | Depends on data size |

**InnoDB Buffer Pool**: Set to 50-70% of available RAM for optimal performance.

### CPU

| Workload | Cores |
|----------|-------|
| Development | 1 |
| Small production | 2 |
| Medium production | 4-8 |

### Disk

| Requirement | Value |
|-------------|-------|
| Minimum | 200MB |
| Data directory | `/var/lib/mysql` |
| Recommended | SSD for production |

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 3306 | TCP | MySQL wire protocol |

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "healthcheck.sh", "--connect", "--innodb_initialized"]
  interval: 10s
  timeout: 5s
  retries: 5
  start_period: 30s
```

**Why `healthcheck.sh`**:
- Built into MariaDB Docker image
- Checks connectivity and InnoDB readiness
- More reliable than `mysqladmin ping`

### Alternative Health Checks

```bash
# Simple ping
mysqladmin ping -h localhost

# Connection test
mysql -e "SELECT 1"
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `MARIADB_ROOT_PASSWORD` | Required | Root user password |
| `MARIADB_DATABASE` | - | Create database on startup |
| `MARIADB_USER` | - | Create user on startup |
| `MARIADB_PASSWORD` | - | Password for created user |
| `MARIADB_ALLOW_EMPTY_ROOT_PASSWORD` | - | Allow empty root password |
| `MARIADB_RANDOM_ROOT_PASSWORD` | - | Generate random root password |

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| < 512MB RAM | memory_mb_less_than | Fail | Minimum for InnoDB |
| < 1GB RAM | memory_mb_less_than | Warning | Production needs more |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `OOM\|Cannot allocate memory` | Memory exhaustion | Increase RAM |
| `Can't connect.*socket` | Socket not ready | Wait or check config |
| `Access denied` | Auth failed | Check password |
| `Unable to lock` | Data directory conflict | Single instance only |
| `marked as crashed` | Table corruption | Repair or restore |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 | ✅ | Excellent performance |
| Pi 4 (4GB+) | ✅ | Good for small databases |
| Pi 4 (2GB) | ✅ | Works, limited buffer pool |
| Pi 3 | ✅ | ARM32 supported |
| Pi 2 | ✅ | ARM32 supported |
| Pi Zero 2 W | ⚠️ | Very limited |

MariaDB is one of the most Pi-friendly databases.

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Root password | Use strong password in production |
| Network exposure | Internal network (zen-garden) |
| Default password in snippet | Change for production |

**Production Setup**:
```yaml
environment:
  MARIADB_ROOT_PASSWORD: ${MARIADB_ROOT_PASSWORD}
  MARIADB_ROOT_HOST: "%"  # Or restrict to specific hosts
```

## Comparison with Alternatives

| Feature | MariaDB | MySQL | PostgreSQL |
|---------|---------|-------|------------|
| License | GPL v2 | GPL/Proprietary | PostgreSQL |
| ARM32 support | ✅ | ✅ | ✅ |
| Memory footprint | Medium | Medium | Medium |
| JSON support | ✅ | ✅ | ✅ Native |
| Full-text search | ✅ | ✅ | ✅ Better |
| Replication | ✅ | ✅ | ✅ |
| WordPress/Drupal | ✅ Native | ✅ Native | Plugin |

**Use MariaDB when**:
- MySQL compatibility required
- WordPress, Drupal, other MySQL-native apps
- Simple replication needs
- Fully open-source requirement

**Use PostgreSQL when**:
- Advanced SQL features needed
- JSON document workloads
- Full-text search important
- PostGIS for geospatial

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (6 architectures)
- [x] No CPU feature requirements
- [x] Memory constraints documented (512MB minimum)
- [x] Health check command verified
- [x] GPL license confirmed
- [x] MySQL compatibility noted

## Files

| File | Status |
|------|--------|
| `mariadb.snippet.yaml` | ✅ Created |
| `mariadb.compatibility.yaml` | ✅ Created |
| `mariadb.frontmatter.json` | ✅ Created |
| `mariadb.research.md` | ✅ Created |

## References

1. [MariaDB Official Documentation](https://mariadb.com/kb/en/)
2. [MariaDB Docker Hub](https://hub.docker.com/_/mariadb)
3. [MariaDB Server GitHub](https://github.com/MariaDB/server)
4. [MariaDB Foundation](https://mariadb.org/)
5. [MariaDB vs MySQL Comparison](https://mariadb.com/kb/en/mariadb-vs-mysql-compatibility/)
