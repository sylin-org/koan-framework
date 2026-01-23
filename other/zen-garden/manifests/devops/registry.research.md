# Docker Registry Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | Docker Registry / Distribution |
| **Category** | Container Registry |
| **Primary Use** | Private Docker image hosting |
| **License** | Apache 2.0 |
| **Governance** | CNCF (distribution/distribution) |
| **Project URL** | https://distribution.github.io/distribution/ |
| **Docker Hub** | https://hub.docker.com/_/registry |
| **GitHub** | https://github.com/distribution/distribution |
| **Runtime** | Go binary |

## Why a Private Registry?

A private Docker registry allows you to:
- Host custom images without Docker Hub limits
- Keep proprietary images internal
- Reduce bandwidth by caching public images
- Control image distribution across your infrastructure

## Docker Image Analysis

### Image Selection
**Selected**: `registry:2.8`

Using stable v2 branch. Version 3.x is available but v2.8 is widely deployed and stable.

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | Yes | Primary platform |
| arm64v8 | Yes | Raspberry Pi 4+, Apple Silicon |
| arm32v7 | Yes | Raspberry Pi 2/3 |
| arm32v6 | Yes | Raspberry Pi Zero/1 |
| ppc64le | Yes | IBM Power |
| riscv64 | Yes | RISC-V |
| s390x | Yes | IBM Z |

**Outstanding multi-architecture support** - runs on virtually any platform.

## CPU Compatibility

### No Special Requirements

Docker Registry has **no AVX, SSE, or other SIMD requirements**.

As a Go binary, it runs on any supported architecture.

## Resource Requirements

### Memory

Docker Registry is extremely lightweight:

| Deployment | Memory | Notes |
|------------|--------|-------|
| Minimum | 64MB | Basic operation |
| Recommended | 128-256MB | Concurrent operations |
| Production | 512MB+ | High throughput |

Memory usage scales with concurrent pull/push operations.

### CPU

| Deployment | Cores |
|------------|-------|
| Minimum | 0.5 |
| Recommended | 1 |
| Production | 2+ |

CPU usage is minimal for serving images.

### Disk

| Use Case | Storage |
|----------|---------|
| Small | 10-50GB |
| Medium | 100GB-1TB |
| Large | Multi-TB |

Storage is the primary resource consideration.

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 5000 | HTTP | Registry API |

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:5000/v2/"]
```

The `/v2/` endpoint returns 200 OK when the registry is accessible.

## Environment Variables

| Variable | Description |
|----------|-------------|
| `REGISTRY_HTTP_ADDR` | Listen address |
| `REGISTRY_STORAGE_DELETE_ENABLED` | Allow image deletion |
| `REGISTRY_STORAGE_FILESYSTEM_ROOTDIRECTORY` | Storage path |
| `REGISTRY_AUTH_HTPASSWD_*` | Basic auth config |

## Configuration

Registry uses YAML configuration (`config.yml`):

```yaml
version: 0.1
log:
  level: info
storage:
  filesystem:
    rootdirectory: /var/lib/registry
  delete:
    enabled: true
http:
  addr: :5000
  headers:
    X-Content-Type-Options: [nosniff]
```

## Storage Backends

| Backend | Use Case |
|---------|----------|
| Filesystem | Default, local storage |
| S3 | AWS or MinIO |
| Azure | Azure Blob Storage |
| GCS | Google Cloud Storage |

### MinIO Integration
```yaml
storage:
  s3:
    accesskey: minioadmin
    secretkey: minioadmin
    region: us-east-1
    bucket: registry
    regionendpoint: http://minio:9000
```

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| < 64MB RAM | memory_mb_less_than | Fail | Minimum for Go runtime |
| < 128MB RAM | memory_mb_less_than | Warning | Better with concurrent ops |

No architecture restrictions - excellent multi-arch support including ARM32v6.

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `OOM` | Memory exhaustion | Increase RAM |
| `address.*in use` | Port conflict | Change port |
| `storage.*error` | Storage issue | Check volume |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 | Yes | Excellent |
| Pi 4 | Yes | Great |
| Pi 3 | Yes | Good |
| Pi 2 | Yes | ARM32v7 |
| Pi Zero 2 W | Yes | ARM64 |
| Pi Zero/1 | Yes | ARM32v6 supported |

## Authentication Options

### No Authentication (Development)
Default - not recommended for production.

### Basic Auth (htpasswd)
```yaml
auth:
  htpasswd:
    realm: basic-realm
    path: /auth/htpasswd
```

### Token-Based (Production)
Integration with external auth services.

## Usage

### Push Image
```bash
docker tag myimage localhost:5000/myimage
docker push localhost:5000/myimage
```

### Pull Image
```bash
docker pull localhost:5000/myimage
```

### List Repositories
```bash
curl http://localhost:5000/v2/_catalog
```

## Garbage Collection

Images aren't automatically deleted. Run garbage collection periodically:

```bash
docker exec registry registry garbage-collect /etc/docker/registry/config.yml
```

## Comparison with Alternatives

| Feature | Registry | Harbor | Gitea | Nexus |
|---------|----------|--------|-------|-------|
| License | Apache 2.0 | Apache 2.0 | MIT | EPL |
| Complexity | Low | High | Medium | High |
| Memory Usage | Very Low | High | Medium | High |
| UI | None | Yes | Yes | Yes |
| Scanning | No | Yes | No | Yes |
| ARM32 Support | Yes | No | Yes | Limited |

**For Zen Garden**: Plain Registry is ideal for lightweight deployments. Harbor or Gitea for more features.

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| No auth by default | Configure htpasswd or token auth |
| HTTP (insecure) | Use reverse proxy with TLS |
| Image deletion | Set REGISTRY_STORAGE_DELETE_ENABLED carefully |

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (7 architectures)
- [x] No CPU feature requirements
- [x] Memory constraints documented (64MB minimum)
- [x] Health check endpoint verified (/v2/)
- [x] Apache 2.0 license confirmed

## Files

| File | Status |
|------|--------|
| `registry.snippet.yaml` | Created |
| `registry.compatibility.yaml` | Created |
| `registry.frontmatter.json` | Created |
| `registry.research.md` | Created |

## References

1. [Docker Registry Documentation](https://distribution.github.io/distribution/)
2. [Docker Registry Docker Hub](https://hub.docker.com/_/registry)
3. [Distribution GitHub](https://github.com/distribution/distribution)
4. [Registry API](https://distribution.github.io/distribution/spec/api/)
5. [Storage Backends](https://distribution.github.io/distribution/storage-drivers/)
6. [Authentication](https://distribution.github.io/distribution/spec/auth/)
