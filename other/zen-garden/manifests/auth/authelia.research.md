# Authelia Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | Authelia |
| **Category** | Authentication / Authorization |
| **Primary Use** | SSO, 2FA, access control for web applications |
| **License** | Apache 2.0 |
| **Governance** | Authelia Project |
| **Project URL** | https://www.authelia.com/ |
| **Docker Hub** | https://hub.docker.com/r/authelia/authelia |
| **GitHub** | https://github.com/authelia/authelia |
| **Runtime** | Go binary |

## Why Authelia?

Authelia provides single sign-on (SSO) and two-factor authentication (2FA) for web applications. It acts as a companion for reverse proxies like Traefik, Nginx, and Caddy, adding authentication to services that don't have their own.

## Docker Image Analysis

### Image Selection
**Selected**: `authelia/authelia:4.38`

Using stable v4.38 branch for production stability.

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | Yes | Primary platform |
| arm64 | Yes | Raspberry Pi 4+, Apple Silicon |
| arm32v7 | Yes | Raspberry Pi 2/3 |
| arm32v6 | No | Pi Zero/1 not supported |

### Image Security

Authelia images are:
- Signed with Sigstore/Cosign
- Include SBOM (Software Bill of Materials)
- Built with supply chain verification

## CPU Compatibility

### No Special Requirements

Authelia has **no AVX, SSE, or other SIMD requirements**.

As a Go binary, it runs on any supported architecture.

### CPU Usage

- Idle: Near 0% (unmeasurable)
- Active: < 1% for small deployments
- Exception: Password hashing operations (configurable)

## Resource Requirements

### Memory

Authelia is exceptionally lightweight:

| Deployment | Memory | Notes |
|------------|--------|-------|
| Minimum | 20-30MB | Normal operation |
| With Argon2id | 64-128MB+ | Depends on hash parameters |
| Container size | < 20MB | Compressed image |

**Password Hashing Caveat**: If using `argon2id` with the file user provider, memory usage increases during authentication. The `$m=` parameter in the hash specifies memory in KB.

### CPU

CPU usage is minimal outside of password hashing operations, which are highly tunable.

### Disk

- Configuration: < 1MB
- SQLite database: Variable based on session count
- Logs: Configurable

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 9091 | HTTP | Main service |

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:9091/api/health"]
```

Authelia exposes `/api/health` endpoint for health checks.

## Configuration Requirements

Authelia requires a `configuration.yml` file with:
- JWT secret
- Default 2FA method
- Session settings
- Storage backend
- Authentication backend
- Access control rules

### Minimal Configuration

```yaml
# configuration.yml
jwt_secret: a_very_secret_key_change_me
default_2fa_method: totp

server:
  host: 0.0.0.0
  port: 9091

authentication_backend:
  file:
    path: /config/users_database.yml

access_control:
  default_policy: deny
  rules:
    - domain: "*.example.com"
      policy: one_factor

session:
  name: authelia_session
  secret: another_secret_change_me
  expiration: 3600

storage:
  local:
    path: /config/db.sqlite3
```

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| ARM32v6 | architectures | Fail | Not supported |
| < 64MB RAM | memory_mb_less_than | Fail | Minimum for Go runtime |
| < 128MB RAM | memory_mb_less_than | Warning | Argon2id may need more |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `OOM` | Memory exhaustion | Adjust argon2id params |
| `config.*error` | Config syntax | Check YAML |
| `storage.*error` | Database issue | Check connection |
| `secret.*error` | Missing secrets | Configure jwt_secret |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 | Yes | Excellent |
| Pi 4 | Yes | Great performance |
| Pi 3 | Yes | Works well |
| Pi 2 | Yes | ARM32v7 supported |
| Pi Zero 2 W | Yes | ARM64 supported |
| Pi Zero/1 | No | ARM32v6 not supported |

## Integration with Reverse Proxies

### Traefik Integration

```yaml
labels:
  - "traefik.http.middlewares.authelia.forwardauth.address=http://authelia:9091/api/verify?rd=https://auth.example.com/"
  - "traefik.http.middlewares.authelia.forwardauth.trustForwardHeader=true"
  - "traefik.http.middlewares.authelia.forwardauth.authResponseHeaders=Remote-User,Remote-Groups"
```

### Nginx Integration

```nginx
location /authelia {
    internal;
    proxy_pass http://authelia:9091/api/verify;
}
```

## Authentication Methods

| Method | Description |
|--------|-------------|
| TOTP | Time-based one-time passwords (Google Authenticator) |
| WebAuthn | Hardware keys (YubiKey, fingerprint) |
| Duo | Duo Security push notifications |
| One-factor | Password only |
| Two-factor | Password + second factor |

## Storage Backends

| Backend | Use Case |
|---------|----------|
| SQLite | Single instance, homelab |
| PostgreSQL | Production, clustering |
| MySQL/MariaDB | Production, clustering |

## Comparison with Alternatives

| Feature | Authelia | Authentik | Keycloak |
|---------|----------|-----------|----------|
| License | Apache 2.0 | MIT | Apache 2.0 |
| Memory Usage | ~30MB | ~500MB | ~1GB+ |
| Container Size | < 20MB | ~300MB | ~500MB |
| Setup Complexity | Medium | Medium | High |
| OIDC Provider | Yes | Yes | Yes |
| ARM32 Support | v7+ | No | Limited |
| WebAuthn | Yes | Yes | Yes |

**For Zen Garden**: Authelia is ideal due to its extremely low resource usage, making it perfect for edge devices and Raspberry Pis.

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Secrets exposure | Use environment variables or secrets files |
| Session hijacking | Configure secure cookies, HTTPS |
| Brute force | Built-in rate limiting, ban policies |
| JWT security | Use strong, unique jwt_secret |

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (amd64, arm64, arm32v7)
- [x] No CPU feature requirements
- [x] Memory constraints documented (30MB base, more for Argon2id)
- [x] Health check endpoint verified
- [x] Apache 2.0 license confirmed
- [x] ARM32v6 limitation documented

## Files

| File | Status |
|------|--------|
| `authelia.snippet.yaml` | Created |
| `authelia.compatibility.yaml` | Created |
| `authelia.frontmatter.json` | Created |
| `authelia.research.md` | Created |

## References

1. [Authelia Official Documentation](https://www.authelia.com/docs/)
2. [Authelia Docker Hub](https://hub.docker.com/r/authelia/authelia)
3. [Authelia GitHub](https://github.com/authelia/authelia)
4. [Hardware Requirements Discussion](https://github.com/authelia/authelia/discussions/6048)
5. [Memory Usage Discussion](https://github.com/authelia/authelia/discussions/5939)
6. [Configuration Reference](https://www.authelia.com/configuration/)
