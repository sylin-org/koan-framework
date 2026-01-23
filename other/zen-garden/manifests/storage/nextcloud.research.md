# Nextcloud Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | Nextcloud |
| **Category** | Cloud Storage / Collaboration |
| **Primary Use** | Self-hosted file sync, calendar, contacts, office |
| **License** | AGPL v3 |
| **Governance** | Nextcloud GmbH |
| **Project URL** | https://nextcloud.com/ |
| **Docker Hub** | https://hub.docker.com/_/nextcloud |
| **GitHub** | https://github.com/nextcloud |
| **Runtime** | PHP (Apache or FPM) |

## The Quintessential Self-Hosted Cloud

Nextcloud is the most popular self-hosted cloud solution, offering:
- File sync and share (Dropbox replacement)
- Calendar and contacts (Google replacement)
- Collaborative office documents
- 200+ apps for extended functionality

## Docker Image Analysis

### Image Selection
**Selected**: `nextcloud:30-apache`

Using Apache variant for simplicity. FPM variant available for advanced setups.

### Image Variants

| Variant | Description |
|---------|-------------|
| `30-apache` | Apache web server included |
| `30-fpm` | PHP-FPM only (requires separate web server) |
| `30-fpm-alpine` | Alpine-based FPM (smaller image) |

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | Yes | Primary platform |
| arm64v8 | Yes | Raspberry Pi 4+, Apple Silicon |
| arm32v7 | Yes | Raspberry Pi 2/3 |
| arm32v6 | Yes | Raspberry Pi Zero/1 |
| arm32v5 | Yes | Older ARM devices |
| i386 | Yes | 32-bit x86 |
| ppc64le | Yes | IBM Power |
| riscv64 | Yes | RISC-V |
| s390x | Yes | IBM Z |

**Outstanding multi-architecture support** - one of the best in the ecosystem.

## CPU Compatibility

### No Special Requirements

Nextcloud has **no AVX, SSE, or other SIMD requirements**.

As a PHP application, it runs on any supported architecture.

## Resource Requirements

### Memory

Nextcloud is moderately resource-intensive:

| Deployment | Memory | Notes |
|------------|--------|-------|
| Minimum | 256MB | Very basic operation |
| Recommended | 512MB | Small team, minimal apps |
| Comfortable | 1-2GB | Multiple users, apps |
| Production | 4GB+ | Heavy use, office suite |

**Key Factors**:
- PHP_MEMORY_LIMIT setting
- Number of concurrent users
- Apps enabled (especially Office)
- Preview generation

### PHP Memory Settings

```bash
# In Docker
PHP_MEMORY_LIMIT=512M
PHP_UPLOAD_LIMIT=10G
```

### CPU

| Deployment | Cores |
|------------|-------|
| Minimum | 1 |
| Recommended | 2 |
| Production | 4+ |

CPU usage spikes during:
- File uploads/downloads
- Preview generation
- Full-text search indexing
- Office document editing

### Disk

| Use Case | Storage |
|----------|---------|
| Personal | 50GB-1TB |
| Family | 1-10TB |
| Small Business | 10TB+ |

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 80 | HTTP | Web interface (Apache) |
| 443 | HTTPS | Secure web (with TLS) |
| 9000 | FastCGI | FPM variant |

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "curl", "-f", "http://localhost/status.php"]
```

The `/status.php` endpoint returns JSON with installation status.

## Database Support

| Database | Use Case |
|----------|----------|
| SQLite | Testing only (not recommended) |
| MySQL/MariaDB | Production recommended |
| PostgreSQL | Production alternative |

**Important**: SQLite should only be used for testing. Production deployments need MariaDB or PostgreSQL.

## Environment Variables

| Variable | Description |
|----------|-------------|
| `MYSQL_HOST` | Database hostname |
| `MYSQL_DATABASE` | Database name |
| `MYSQL_USER` | Database user |
| `MYSQL_PASSWORD` | Database password |
| `NEXTCLOUD_ADMIN_USER` | Admin username |
| `NEXTCLOUD_ADMIN_PASSWORD` | Admin password |
| `NEXTCLOUD_TRUSTED_DOMAINS` | Allowed access domains |
| `PHP_MEMORY_LIMIT` | PHP memory limit |
| `PHP_UPLOAD_LIMIT` | Maximum upload size |

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| < 256MB RAM | memory_mb_less_than | Fail | PHP minimum |
| < 512MB RAM | memory_mb_less_than | Warning | Recommended |
| < 1GB RAM | memory_mb_less_than | Warning | For apps/users |

No architecture restrictions - outstanding multi-arch support.

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `OOM` | Memory exhaustion | Increase RAM |
| `database.*error` | DB connection | Check credentials |
| `Trusted domain` | Domain not allowed | Add to config |
| `Permission denied` | File permissions | Fix ownership |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 | Yes | Excellent with SSD |
| Pi 4 (4GB+) | Yes | Good performance |
| Pi 4 (2GB) | Yes | Limited users |
| Pi 3 | Yes | Slow but works |
| Pi Zero 2 W | Yes | Very limited |
| Pi Zero/1 | Yes | Not recommended |

**Note**: Performance depends heavily on storage speed. Use SSD, not SD card.

## Recommended Architecture

### With External Database
```yaml
services:
  nextcloud:
    image: nextcloud:30-apache
    depends_on:
      - mariadb
      - redis

  mariadb:
    image: mariadb:11

  redis:
    image: redis:alpine
```

### Caching

Redis is strongly recommended for caching:
- APCu for local caching
- Redis for distributed file locking

## Comparison with Alternatives

| Feature | Nextcloud | Seafile | ownCloud |
|---------|-----------|---------|----------|
| License | AGPL v3 | GPL v2 | AGPL v3 |
| Apps | 200+ | Limited | 100+ |
| Office Suite | Collabora/OnlyOffice | None | Collabora |
| Calendar/Contacts | Built-in | None | Via app |
| Mobile Apps | Yes | Yes | Yes |
| ARM Support | Excellent | Good | Good |
| Resource Usage | Medium-High | Low | Medium |

**For Zen Garden**: Nextcloud is the most feature-complete option, though it requires more resources.

## Known Issues

### Preview Generation
Preview generation for images and videos can exhaust memory on low-resource devices. Consider:
- Disabling preview generation
- Setting preview size limits
- Using external preview generator

### Database Lock Timeout
With SQLite, file locking issues can occur. Always use MySQL/MariaDB or PostgreSQL for production.

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Default credentials | Change admin password immediately |
| Trusted domains | Configure strictly |
| HTTPS | Use reverse proxy with TLS |
| Brute force | Built-in protection + fail2ban |

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (9+ architectures)
- [x] No CPU feature requirements
- [x] Memory constraints documented (256MB minimum)
- [x] Health check command verified
- [x] AGPL v3 license confirmed

## Files

| File | Status |
|------|--------|
| `nextcloud.snippet.yaml` | Created |
| `nextcloud.compatibility.yaml` | Created |
| `nextcloud.frontmatter.json` | Created |
| `nextcloud.research.md` | Created |

## References

1. [Nextcloud Official Documentation](https://docs.nextcloud.com/)
2. [Nextcloud Docker Hub](https://hub.docker.com/_/nextcloud)
3. [Nextcloud GitHub](https://github.com/nextcloud/server)
4. [System Requirements](https://docs.nextcloud.com/server/stable/admin_manual/installation/system_requirements.html)
5. [Docker Installation Guide](https://docs.nextcloud.com/server/stable/admin_manual/installation/example_containerized.html)
6. [Nextcloud Apps](https://apps.nextcloud.com/)
