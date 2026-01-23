# HashiCorp Vault Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | HashiCorp Vault |
| **Category** | Secrets Management |
| **Primary Use** | Secrets storage, encryption, identity management, PKI |
| **License** | Business Source License (BSL) v1.1 (since Aug 2023) |
| **Project URL** | https://www.vaultproject.io/ |
| **Docker Hub** | https://hub.docker.com/r/hashicorp/vault |
| **GitHub** | https://github.com/hashicorp/vault |
| **Runtime** | Go binary (statically compiled) |

## Licensing (Important)

### BSL License Change (August 2023)

HashiCorp changed Vault's license from Mozilla Public License v2.0 (MPL 2.0) to Business Source License (BSL) v1.1 in August 2023.

**Key Points**:
- BSL is "source available" but **not OSI-approved open source**
- End users can use Vault freely for internal infrastructure
- Cannot offer Vault as a competing commercial service
- License converts to MPL 2.0 after 4 years from release

**For Zen Garden users**: Using Vault for homelab/internal infrastructure is permitted under BSL.

**Alternatives**: OpenBao (community fork) maintains MPL 2.0 licensing.

**Sources**:
- [HashiCorp BSL Announcement](https://www.hashicorp.com/en/blog/hashicorp-adopts-business-source-license)
- [HashiCorp Licensing FAQ](https://www.hashicorp.com/en/license-faq)

## Docker Image Analysis

### Image Selection
**Selected**: `hashicorp/vault:1.18`

Using pinned major.minor version for stability.

### Version Strategy

| Version | Status | Notes |
|---------|--------|-------|
| 1.18.x | Latest | Current stable |
| 1.17.x | Supported | Previous stable |
| 1.15+ | BSL | New license applies |

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | ✅ | Primary platform |
| arm64 | ✅ | Apple Silicon, Graviton, Pi 4+ |
| arm | ✅ | 32-bit ARM (Pi 2/3) |
| 386 | ✅ | 32-bit x86 |

**Excellent multi-architecture support** - Go's cross-compilation makes Vault highly portable.

## CPU Compatibility

### No Special Requirements

Vault is a statically compiled Go binary with **no AVX, SSE, or other SIMD requirements**.

Runs on:
- Any x86/x86_64 processor
- Any ARM processor (32-bit or 64-bit)
- Raspberry Pi (all models)
- Low-end Celeron/Atom processors

## Resource Requirements

### Memory

Vault is lightweight:

| Mode | Memory | Notes |
|------|--------|-------|
| Dev mode | 64-128MB | In-memory storage |
| Production (idle) | 128-256MB | With backend |
| Production (active) | 256-512MB | Depends on secrets volume |

**Formula**: Base ~100MB + ~1KB per secret + caching overhead

### CPU

| Workload | Cores |
|----------|-------|
| Development | 1 |
| Small production | 1-2 |
| High availability | 2-4 |

Vault is not CPU-intensive except during:
- Initial unseal operations
- High-frequency secret access
- PKI certificate generation

### Disk

| Mode | Storage |
|------|---------|
| Dev mode | None (in-memory) |
| Production | Depends on backend |

**Production storage backends**:
- Raft (integrated) - local disk
- Consul - distributed
- PostgreSQL, MySQL - database
- S3, GCS - cloud storage

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 8200 | HTTP/HTTPS | API and UI |
| 8201 | TCP | Cluster communication (HA) |

## Deployment Modes

### Dev Mode (Current Snippet)

```yaml
command: server -dev -dev-listen-address=0.0.0.0:8200
```

**Characteristics**:
- In-memory storage (data lost on restart)
- Automatically unsealed
- Root token preset (`root`)
- TLS disabled
- **For development only**

### Production Mode

Requires:
- Persistent storage backend
- Manual unsealing (or auto-unseal)
- TLS configuration
- Proper seal configuration

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "sh", "-c", "VAULT_ADDR=http://127.0.0.1:8200 vault status"]
  interval: 10s
  timeout: 5s
  retries: 5
  start_period: 10s
```

**Why this approach**:
- `vault status` checks if server is responding
- Returns 0 for unsealed, 1 for sealed, 2 for error
- VAULT_ADDR must be set for CLI to work

### Health Status Codes

| Exit Code | Meaning |
|-----------|---------|
| 0 | Unsealed and active |
| 1 | Sealed |
| 2 | Error |

For dev mode, Vault auto-unseals, so exit code 0 is expected.

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `VAULT_DEV_ROOT_TOKEN_ID` | Generated | Root token for dev mode |
| `VAULT_ADDR` | - | Vault server address (required for CLI) |
| `VAULT_TOKEN` | - | Authentication token |
| `VAULT_SKIP_VERIFY` | `false` | Skip TLS verification |

## Capability Requirements

### IPC_LOCK Capability

```yaml
cap_add:
  - IPC_LOCK
```

Vault uses `mlock()` to prevent sensitive data from being swapped to disk. Without IPC_LOCK:
- Vault will warn about memory locking
- Can set `disable_mlock = true` in config (less secure)

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| < 256MB RAM | memory_mb_less_than: 256 | Fail | Minimum for stable operation |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `Failed to lock memory` | mlock permission | Add IPC_LOCK capability |
| `OOM\|Cannot allocate memory` | Memory exhaustion | Increase RAM |
| `address already in use` | Port conflict | Change port or stop conflict |
| `seal configuration missing` | Production config | Use dev mode or configure seal |
| `permission denied.*storage` | Volume permissions | Fix permissions |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 | ✅ | Excellent performance |
| Pi 4 | ✅ | Works well |
| Pi 3 | ✅ | ARM32 supported |
| Pi 2 | ✅ | ARM32 supported |
| Pi Zero 2 W | ✅ | Works, limited RAM |
| Pi Zero/1 | ⚠️ | ARMv6, limited RAM |

Vault's Go binary is highly portable and runs on all Pi models.

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Dev mode insecure | Only for development |
| Root token exposure | Use proper auth methods in production |
| No TLS in dev | Configure TLS for production |
| Memory not locked | Add IPC_LOCK capability |

**Production Recommendations**:
- Never use dev mode in production
- Enable TLS
- Use proper authentication (AppRole, Kubernetes, etc.)
- Configure auto-unseal for HA
- Enable audit logging

## Comparison with Alternatives

| Feature | Vault | Infisical | Doppler |
|---------|-------|-----------|---------|
| License | BSL | MIT | Proprietary |
| Self-hosted | ✅ | ✅ | ❌ |
| Dynamic secrets | ✅ | Limited | ❌ |
| PKI | ✅ | ❌ | ❌ |
| ARM support | ✅ | ✅ | N/A |
| Complexity | High | Low | Low |

**Alternatives for Zen Garden**:
- **Infisical**: OSS, simpler, web UI focused
- **OpenBao**: Vault fork, MPL 2.0 licensed
- **SOPS**: File-based encryption, simpler

## Use Cases

1. **Application Secrets**: Database credentials, API keys
2. **Dynamic Secrets**: Generate temporary credentials
3. **Encryption as a Service**: Transit secrets engine
4. **PKI**: Certificate authority for internal TLS
5. **Identity**: OIDC provider, auth methods

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (amd64, arm64, arm, 386)
- [x] No CPU feature requirements
- [x] Memory constraints documented (256MB minimum)
- [x] Health check command verified
- [x] Licensing documented (BSL)
- [x] Dev vs production modes documented
- [x] IPC_LOCK capability documented

## Files

| File | Status |
|------|--------|
| `vault.snippet.yaml` | ✅ Updated (version, VAULT_ADDR, healthcheck) |
| `vault.compatibility.yaml` | ✅ Updated (additional patterns) |
| `vault.frontmatter.json` | ✅ Updated (tags, note) |
| `vault.research.md` | ✅ Created |

## References

1. [Vault Official Documentation](https://developer.hashicorp.com/vault/docs)
2. [Vault Docker Hub](https://hub.docker.com/r/hashicorp/vault)
3. [HashiCorp BSL Announcement](https://www.hashicorp.com/en/blog/hashicorp-adopts-business-source-license)
4. [HashiCorp Licensing FAQ](https://www.hashicorp.com/en/license-faq)
5. [Vault GitHub Repository](https://github.com/hashicorp/vault)
6. [Vault Production Hardening](https://developer.hashicorp.com/vault/docs/concepts/production-hardening)
