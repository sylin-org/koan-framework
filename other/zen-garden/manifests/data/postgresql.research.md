# PostgreSQL (with pgvector) Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | PostgreSQL |
| **Extension** | pgvector (vector similarity search) |
| **Category** | Relational Database |
| **Primary Use** | ACID-compliant relational database with vector search |
| **License** | PostgreSQL License (permissive) |
| **Project URL** | https://www.postgresql.org/ |
| **pgvector URL** | https://github.com/pgvector/pgvector |
| **Docker Hub** | https://hub.docker.com/r/pgvector/pgvector |

## Docker Image Analysis

### Image Selection
**Selected**: `pgvector/pgvector:pg16`

This image provides PostgreSQL 16 with the pgvector extension pre-installed, enabling:
- Traditional relational database features
- Vector similarity search for AI/ML applications
- Hybrid queries combining SQL and vector search

### Version Information
| Component | Version | Notes |
|-----------|---------|-------|
| PostgreSQL | 16.x | LTS, stable |
| pgvector | 0.8.x | Latest stable |
| Base Image | Debian | Official postgres base |

**Alternative Tags Available**:
- `pgvector/pgvector:pg17` - PostgreSQL 17 (newest)
- `pgvector/pgvector:pg15` - PostgreSQL 15
- `pgvector/pgvector:pg14` - PostgreSQL 14
- `pgvector/pgvector:pg13` - PostgreSQL 13 (minimum supported)

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | ✅ | Primary platform |
| arm64v8 | ✅ | Apple Silicon, Raspberry Pi 4+, AWS Graviton |
| arm32v7 | ✅ | Raspberry Pi 2/3 |
| arm32v6 | ✅ | Raspberry Pi Zero/1 |
| arm32v5 | ✅ | Older ARM devices |
| i386 | ✅ | Legacy 32-bit x86 |
| ppc64le | ✅ | IBM Power |
| s390x | ✅ | IBM Z |
| riscv64 | ✅ | RISC-V |
| mips64le | ✅ | MIPS |

**Conclusion**: Excellent cross-platform support inherited from official PostgreSQL image.

**Sources**:
- [Docker Hub postgres](https://hub.docker.com/_/postgres)
- [pgvector Docker](https://hub.docker.com/r/pgvector/pgvector)

## CPU Compatibility

### pgvector SIMD Handling

pgvector uses SIMD optimizations for vector distance calculations. Critical compatibility details:

| Build Type | `-march=native` | Compatibility |
|------------|-----------------|---------------|
| Official Docker | **Disabled** | Broad (safe) |
| Source build | **Enabled** | Machine-specific |
| Azure/Cloud | Varies | Check provider |

**Key Finding**: Official pgvector Docker images use `OPTFLAGS=""` to disable `-march=native`, ensuring the compiled binary runs on any compatible CPU architecture without requiring specific SIMD instructions.

### Known Issue: pgvector 0.8.0 on Azure

In late 2024, pgvector 0.8.0 caused crashes on Azure Database for PostgreSQL Flexible Server in certain regions due to aggressive CPU optimizations in Azure's build. This was a **provider-specific build issue**, not affecting official Docker images.

**Workaround**: Use official Docker images or roll back to 0.7.x if issues occur.

### No AVX/SSE Requirements

Unlike MongoDB (requires AVX for 5+) or some AI tools, PostgreSQL and pgvector's official Docker images:
- Do **NOT** require AVX or AVX2
- Do **NOT** require specific SSE versions beyond baseline
- Will run on Celeron J4105 and similar low-end CPUs

**Sources**:
- [pgvector GitHub](https://github.com/pgvector/pgvector)
- [Azure pgvector issue](https://learn.microsoft.com/en-us/answers/questions/2284930/azure-database-for-postgresql-flexible-server-cras)
- [pgvector Docker deployment](https://deepwiki.com/pgvector/pgvector/8.3-docker-deployment)

## Resource Requirements

### Memory

| Workload | Memory | Notes |
|----------|--------|-------|
| Minimum | 256MB | Bare startup, not practical |
| Practical Minimum | 512MB | Small databases, light load |
| Recommended | 1-2GB | General workloads |
| Vector workloads | 2-4GB+ | Depends on index size |

**pgvector-specific**: HNSW index building benefits from `maintenance_work_mem`. A notice appears if the graph exceeds available memory during construction.

**Formula for vector indexes**: Memory ≈ vectors × (dimensions × 4 bytes + `m` × 8 bytes)

### CPU

| Requirement | Value |
|-------------|-------|
| Minimum Cores | 1 |
| Recommended | 2-4 |
| Vector operations | More cores = faster index builds |

### Disk

| Requirement | Value |
|-------------|-------|
| Minimum | 100MB (empty) |
| Typical | Depends on data |
| Volume Mount | `/var/lib/postgresql/data` |

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 5432 | TCP | PostgreSQL wire protocol |

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD-SHELL", "pg_isready -U postgres"]
  interval: 10s
  timeout: 5s
  retries: 5
```

**Why `pg_isready`**:
- Official PostgreSQL utility
- Checks if server accepts connections
- Does not require database query
- Fast and reliable

### Alternative Health Checks
```bash
# More thorough (but slower)
psql -U postgres -c "SELECT 1"

# Check specific database
pg_isready -U postgres -d mydb
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `POSTGRES_PASSWORD` | Required | Superuser password |
| `POSTGRES_USER` | `postgres` | Superuser name |
| `POSTGRES_DB` | `postgres` | Default database |
| `PGDATA` | `/var/lib/postgresql/data` | Data directory |

**Security Note**: Default password `postgres` in snippet is for development. Production should use secrets management.

## Compatibility Rules Analysis

### Current Rules (validated)

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| insufficient-memory | < 512MB | Fail | PostgreSQL needs this minimum |

### Recommendations

The current compatibility rules are appropriate:
- 512MB minimum is validated by real-world usage (Pi Zero can run with 512MB)
- No CPU feature restrictions needed (unlike MongoDB)
- No architecture restrictions (wide support)

### Post-install Healthcheck Patterns

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `FATAL:.*out of memory` | OOM | Increase RAM |
| `Cannot allocate memory` | OOM | Increase RAM |
| `OOM` | Out of memory | Increase RAM |

## Comparison: pgvector vs Dedicated Vector DBs

| Feature | PostgreSQL + pgvector | Weaviate | Milvus |
|---------|----------------------|----------|--------|
| SQL Support | ✅ Full | ❌ | ❌ |
| Vector Search | ✅ Good | ✅ Excellent | ✅ Excellent |
| Hybrid Queries | ✅ Native | Limited | Limited |
| Memory Footprint | Low | Medium | High |
| ARM Support | ✅ Excellent | ✅ | Partial |
| Low-end CPU | ✅ Safe | ⚠️ Risk | ⚠️ Risk |
| Learning Curve | Low (SQL) | Medium | Medium |
| Scalability | Vertical | Horizontal | Horizontal |

**Recommendation**: For Zen Garden homelab users with:
- Existing PostgreSQL skills → pgvector
- Low-end hardware (Celeron, Pi) → pgvector (safest choice)
- Pure vector workloads at scale → Weaviate or Milvus

## Raspberry Pi Performance

Based on community benchmarks:

| Device | Memory | Performance |
|--------|--------|-------------|
| Pi Zero W | 512MB | Works, limited |
| Pi 3B | 1GB | ~200 TPS (TPC-B) |
| Pi 4 (4GB) | 4GB | Good for small DBs |
| Pi 4 (8GB) | 8GB | Recommended |

**Sources**:
- [PostgreSQL on Raspberry Pi](https://blog.rustprooflabs.com/2019/04/postgresql-pgbench-raspberry-pi)
- [Pi PostgreSQL setup](https://pimylifeup.com/raspberry-pi-postgresql/)

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Default password | Use secrets management in production |
| Network exposure | Internal network only (zen-garden) |
| Encryption | Configure SSL for sensitive data |
| Authentication | Use pg_hba.conf for access control |

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (10+ architectures)
- [x] CPU feature requirements researched (none for Docker)
- [x] Memory constraints validated (512MB minimum)
- [x] Health check command verified
- [x] Port configuration standard
- [x] Security considerations reviewed
- [x] pgvector compatibility confirmed
- [x] Raspberry Pi compatibility confirmed

## Files

| File | Status |
|------|--------|
| `postgresql.snippet.yaml` | ✅ Validated |
| `postgresql.compatibility.yaml` | ✅ Validated |
| `postgresql.frontmatter.json` | ✅ Updated (added vector tags) |
| `postgresql.research.md` | ✅ Created |

## References

1. [PostgreSQL Official Documentation](https://www.postgresql.org/docs/)
2. [pgvector GitHub](https://github.com/pgvector/pgvector)
3. [pgvector Docker Hub](https://hub.docker.com/r/pgvector/pgvector)
4. [PostgreSQL Docker Hub](https://hub.docker.com/_/postgres)
5. [pgvector 0.8.0 Release](https://www.postgresql.org/about/news/pgvector-080-released-2952/)
6. [pgvector Docker Deployment Guide](https://deepwiki.com/pgvector/pgvector/8.3-docker-deployment)
7. [PostgreSQL on Raspberry Pi Benchmarks](https://blog.rustprooflabs.com/2019/04/postgresql-pgbench-raspberry-pi)
