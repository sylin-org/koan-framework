# MinIO Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | MinIO |
| **Category** | Object Storage |
| **Primary Use** | S3-compatible object storage, backups, artifacts |
| **License** | GNU AGPL v3 (Server), Apache 2.0 (SDKs) |
| **Governance** | MinIO Inc. |
| **Project URL** | https://min.io/ |
| **Docker Hub** | https://hub.docker.com/r/minio/minio |
| **GitHub** | https://github.com/minio/minio |
| **Runtime** | Go binary |

## License Notes

MinIO Server is AGPL v3, which requires derivative works to be open source. For proprietary use, commercial licenses are available. The S3 API clients (SDKs) are Apache 2.0 licensed.

## Docker Image Analysis

### Image Selection
**Selected**: `minio/minio:RELEASE.2024-12-18T13-15-44Z`

Using date-stamped release for reproducibility. MinIO uses release dates as version tags.

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | Yes | Primary platform |
| arm64 | Yes | Apple Silicon, Graviton, Pi 4+ |
| arm/v7 | No | Not officially supported |
| arm/v6 | No | Not supported |

**Note**: Community multi-arch builds exist (jessestuart/minio-multiarch, carlosedp/minio-multiarch) but official images only support amd64 and arm64.

## CPU Compatibility

### No Special Requirements

MinIO has **no AVX, SSE, or other SIMD requirements**.

As a Go binary, it runs on any supported architecture without special CPU features.

### Production Recommendations

| Deployment | CPU Cores |
|------------|-----------|
| Minimum | 2 |
| Recommended | 4-8 |
| Enterprise | 8+ |

## Resource Requirements

### Memory

MinIO memory usage depends on:
- Number of concurrent connections
- Object size distribution
- Caching behavior
- Number of buckets

| Deployment | Memory | Notes |
|------------|--------|-------|
| Minimum | 256MB | With CI_CD=true env var |
| Development | 512MB-1GB | Single user/evaluation |
| Small | 2-4GB | Small team usage |
| Production | 8-32GB | Heavy workloads |
| Enterprise | 128GB+ | High concurrency |

**Note**: As of RELEASE.2024-01-28, MinIO preallocates 2GB per node in distributed mode, 1GB in single-node. Set `CI_CD=true` environment variable to reduce to 256MB for testing.

### Disk

MinIO is designed for high-capacity storage:

| Use Case | Recommended |
|----------|-------------|
| Testing | 10GB+ |
| Homelab | 100GB-1TB |
| Production | Multi-TB |

**Storage path**: `/data`

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 9000 | HTTP/S3 | S3 API endpoint |
| 9001 | HTTP | Admin Console UI |

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "mc", "ready", "local"]
```

**Note**: `curl` is not included in the MinIO Docker image. The `mc` (MinIO Client) tool is bundled and provides the `ready` command for health checks.

### Health Endpoints

| Endpoint | Purpose |
|----------|---------|
| `/minio/health/live` | Liveness probe (server running) |
| `/minio/health/ready` | Readiness probe (accepting requests) |
| `/minio/health/cluster` | Cluster write quorum |
| `/minio/health/cluster/read` | Cluster read quorum |

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `MINIO_ROOT_USER` | Admin username | minioadmin |
| `MINIO_ROOT_PASSWORD` | Admin password | minioadmin |
| `MINIO_BROWSER` | Enable console UI | on |
| `CI_CD` | Reduce memory preallocation | false |

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| ARM32 | architectures | Fail | No official ARM32 images |
| < 256MB RAM | memory_mb_less_than | Fail | Minimum for operation |
| < 512MB RAM | memory_mb_less_than | Warning | Better performance needs more |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `OOM` | Memory exhaustion | Increase RAM |
| `disk.*error` | Storage issue | Check volume/permissions |
| `address.*in use` | Port conflict | Change ports |
| `Access Denied` | Auth error | Check credentials |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 | Yes | Excellent performance |
| Pi 4 (4GB+) | Yes | Good for homelab storage |
| Pi 4 (2GB) | Yes | Limited, use CI_CD=true |
| Pi 3 | No | ARM32, not supported |
| Pi Zero 2 W | No | ARM32, not supported |

## Use Cases in Zen Garden

1. **Backup Target**: S3-compatible target for Duplicati, Restic, etc.
2. **Artifact Storage**: Store build artifacts, container images
3. **Media Storage**: Backend for media applications
4. **Log Archival**: Long-term log storage
5. **Data Lake**: Foundation for analytics pipelines

## MinIO Client (mc)

The bundled `mc` tool provides CLI access:

```bash
# Inside container
mc alias set local http://localhost:9000 minioadmin minioadmin
mc mb local/mybucket
mc cp file.txt local/mybucket/
```

## S3 Compatibility

MinIO implements the S3 API and is compatible with:
- AWS SDKs (all languages)
- s3cmd
- rclone
- Cyberduck
- Any S3-compatible tool

## Comparison with Alternatives

| Feature | MinIO | SeaweedFS | Ceph RGW |
|---------|-------|-----------|----------|
| License | AGPL v3 | Apache 2.0 | LGPL |
| S3 Compatibility | Excellent | Good | Excellent |
| Setup Complexity | Low | Medium | High |
| Resource Usage | Medium | Low | High |
| ARM64 Support | Yes | Yes | Limited |
| Single Binary | Yes | Yes | No |

**For Zen Garden**: MinIO is the best choice for S3-compatible storage due to its simplicity, excellent S3 compatibility, and good ARM64 support.

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Default credentials | Change MINIO_ROOT_USER/PASSWORD immediately |
| Network exposure | Use reverse proxy with TLS |
| Bucket policies | Configure proper IAM policies |
| Console access | Consider disabling MINIO_BROWSER for headless |

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (amd64, arm64)
- [x] No CPU feature requirements
- [x] Memory constraints documented (256MB minimum with CI_CD=true)
- [x] Health check command verified (mc ready local)
- [x] AGPL v3 license noted
- [x] ARM32 limitation documented

## Files

| File | Status |
|------|--------|
| `minio.snippet.yaml` | Created |
| `minio.compatibility.yaml` | Created |
| `minio.frontmatter.json` | Created |
| `minio.research.md` | Created |

## References

1. [MinIO Official Documentation](https://min.io/docs/minio/container/index.html)
2. [MinIO Docker Hub](https://hub.docker.com/r/minio/minio)
3. [MinIO GitHub](https://github.com/minio/minio)
4. [MinIO Hardware Checklist](https://min.io/docs/minio/kubernetes/upstream/operations/checklists/hardware.html)
5. [MinIO Health Check API](https://min.io/docs/minio/linux/operations/monitoring/healthcheck-probe.html)
6. [MinIO Memory Discussion](https://github.com/minio/minio/discussions/19133)
