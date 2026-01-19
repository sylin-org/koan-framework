# Release Notes

**Purpose:** Release history, breaking changes, known issues, and deprecation timeline.  
**Audience:** Operators upgrading Zen Garden, developers tracking changes.

---

## Table of Contents

1. [Current Release](#current-release)
2. [Release History](#release-history)
3. [Breaking Changes](#breaking-changes)
4. [Known Issues](#known-issues)
5. [Deprecation Timeline](#deprecation-timeline)

---

## Current Release

### Version 0.1.0 (V1 API) - January 2026

**Status:** ✅ Production Ready  
**Build:** Clean (0 errors, 0 warnings)

**What's New:**

#### Dual-Layer API Architecture

**Offerings API (Human Layer - 90% use case):**
- `GET /api/v1/offerings` - List offerings with state filtering
- `GET /api/v1/offerings/:name` - Simplified offering details
- `GET /api/v1/offerings/:name/manifest` - YAML template
- `POST /api/v1/offerings:heal` - Heal garden (reconcile all services)
- `POST /api/v1/offerings:refresh` - Refresh catalog
- `DELETE /api/v1/offerings/:name` - Take away offering (forwards to services)

**Services API (Technical Layer - 10% power users):**
- `GET /api/v1/services/manifests` - List all manifests
- `GET /api/v1/services/:name/manifest` - Get manifest YAML
- `GET /api/v1/services` - List with container details
- `GET /api/v1/services/:name` - Full technical view
- `GET /api/v1/services/:name/logs` - SSE log streaming
- `POST /api/v1/services` - Install with full control
- `DELETE /api/v1/services/:name` - Uninstall
- `POST /api/v1/services/:name:restart` - Restart operation
- `POST /api/v1/services:reconcile` - Reconcile inventory
- `POST /api/v1/services:refresh` - Refresh manifests

**Stone Operations:**
- `POST /api/v1/stone:upgrade` - Upgrade stone software
- `POST /api/v1/stone:shutdown` - Shutdown daemon

**Universal Endpoints (no v1 namespace):**
- `GET /health` - Health check
- `GET /capabilities` - Stone capabilities
- `GET /metrics` - Prometheus metrics

**Events & Jobs:**
- `GET /api/v1/events` - SSE event stream
- `GET /api/v1/jobs` - List jobs
- `GET /api/v1/jobs/:id` - Job status

#### CLI Updates (garden-rake)

**Zen commands (Offerings API):**
- `explore` → `GET /api/v1/offerings`
- `offer <name>` → `GET /api/v1/offerings/:name`
- `refresh` → `POST /api/v1/offerings:refresh`

**Technical commands (Services API):**
- `observe` → `GET /api/v1/services`
- `templates` → `GET /api/v1/services/manifests`
- `template <name>` → `GET /api/v1/services/:name/manifest`

#### Key Design Decisions

**Custom action format:** Single colon (`:heal`, `:refresh`, `:reconcile`) aligns with Kubernetes/GCP standards

**Progressive disclosure pattern:** Same backend, different presentation layers:
- **Offerings API:** Simplified responses, hide container IDs, human-friendly health
- **Services API:** Full container details, technical metrics, debugging info

**API selection philosophy:**
- 90% of users → Offerings API (explore, offer, observe zen commands)
- 10% power users → Services API (container debugging, technical operations)

### What's Not Implemented

**Placeholders returning `NOT_IMPLEMENTED`:**

1. **`POST /api/v1/offerings` (plant offering):**
   - Reason: Requires full installation logic with environment generation
   - Current behavior: Forwards to services API, returns placeholder response
   - Status: Functional but incomplete

2. **`POST /api/v1/services/:name:cordon` (cordon service):**
   - Reason: Requires ServiceStatus enum extension (add Cordoned state)
   - Status: Returns NOT_IMPLEMENTED placeholder

**Future features (not implemented):**
- Garden topology endpoints (`/api/v1/garden/*`) - present in code but not documented
- Pond security endpoints (`/api/v1/pond/*`) - stubbed for Phase 3

### Known Limitations

1. **Native protocol only:** Agnostic Data API sidecars not yet implemented
2. **Single Stone focus:** Garden-wide operations (`--all` flag) work but limited testing
3. **Manual discovery:** mDNS announcements work on Linux; Windows requires UDP broadcast discovery
4. **No RBAC:** Pond security (mTLS) stubbed for Phase 3
5. **Basic health monitoring:** Advanced health checks (restart loops, resource thresholds) in Phase 2

---

## Release History

### V1 API (0.1.0) - January 19, 2026

**Milestone:** Production-ready dual-layer API

**Delivered:**
- 23 v1 API endpoints (Offerings + Services + Stone operations)
- Universal health/capabilities/metrics endpoints
- SSE events and job tracking
- CLI updated to v1 endpoints
- Custom action format (`:heal`, `:refresh`, `:reconcile`)
- Progressive disclosure pattern (human vs technical layers)

**Documentation:**
- API-V1-DUAL-LAYER-DESIGN.md (367 lines) - Complete API specification
- CLI-V1-MIGRATION.md (87 lines) - CLI migration guide
- API-REFERENCE.md updated with v1 structure

**Build:** Clean (0 errors, 0 warnings)

**Related Commits:**
- Implementation: V1 API endpoints
- Documentation: API-V1-DUAL-LAYER-DESIGN.md
- CLI updates: garden-rake v1 endpoint migration

---

### Initial Development (0.0.x) - December 2025 - January 2026

**Milestone:** Core functionality and architecture

**Phase 0: Foundation (Days 1-2)**
- Rust workspace (moss, rake, common crates)
- Shared types: ServiceInfo, StoneInfo, HealthStatus
- Docker build pipeline
- GitHub Actions CI (Linux + Windows)

**Phase 1: Core Functionality (Days 3-12)**
- **Increment 1:** HTTP API Foundation (Axum server, reqwest client)
- **Increment 2:** Service Registry (in-memory tracking, status management)
- **Increment 3:** Docker Compose Integration (template loading, atomic updates)
- **Increment 4:** UDP Broadcast Discovery (Windows-compatible discovery)
- **Increment 5:** mDNS Announcements (Linux mDNS integration)
- **Increment 6:** Garden-Wide Operations (Moss coordinator pattern)

**Success Criteria Met:**
- ✅ `garden-rake offer mongodb` installs service
- ✅ `garden-rake list` shows services
- ✅ `garden-rake upgrade --all` coordinates across Stones
- ✅ Discovery works without `--at` flag
- ✅ Works on both Linux and Windows

---

## Breaking Changes

### V1 API Migration (0.1.0) - January 2026

**Impact:** All API clients must update endpoint paths

#### Endpoint Changes

**Offerings API:**

| Old Endpoint | New Endpoint | Notes |
|--------------|--------------|-------|
| `GET /api/offerings` | `GET /api/v1/offerings` | Added v1 namespace |
| `GET /api/offerings/{name}` | `GET /api/v1/offerings/{name}` | Added v1 namespace |
| `POST /api/offerings/refresh` | `POST /api/v1/offerings:refresh` | Changed to custom action format (single colon) |

**Services API:**

| Old Endpoint | New Endpoint | Notes |
|--------------|--------------|-------|
| `GET /api/templates` | `GET /api/v1/services/manifests` | Renamed templates → manifests |
| `GET /api/templates/{name}` | `GET /api/v1/services/{name}/manifest` | Nested under services, singular manifest |
| `GET /api/services` | `GET /api/v1/services` | Added v1 namespace |

#### Response Format Changes

**Manifest listing:**

```json
// Old response
{
  "templates": [...]
}

// New response
{
  "manifests": [...]
}
```

**Migration impact:** Update JSON key parsing from `templates` to `manifests`

#### Custom Action Format

**Old format:** `POST /api/offerings/refresh` (REST sub-resource)  
**New format:** `POST /api/v1/offerings:refresh` (custom action with single colon)

**Rationale:** Aligns with industry standards (Kubernetes `:exec`, GCP `:start`)

#### Backwards Compatibility

**Old moss versions (pre-v1):** Return 404 for v1 endpoints  
**CLI behavior:** Shows upgrade message when detecting 404 on v1 endpoints

**Migration path:**
1. Upgrade Moss daemon to 0.1.0
2. Update CLI to 0.1.0
3. Update third-party tools to v1 endpoints

---

## Known Issues

### Cross-Platform Discovery (Windows)

**Issue:** mDNS browse not available on Windows without third-party daemon (Bonjour)

**Workaround:** UDP broadcast discovery on port 3004 (Windows-native)

**Status:** By design - UDP broadcast preferred for Windows, mDNS for Linux/macOS

**Resolution:** No fix planned - UDP broadcast meets Windows requirements

---

### Service Template Validation

**Issue:** Template validation does not catch all Docker Compose syntax errors until runtime

**Impact:** Invalid templates accepted during `refresh`, fail during `offer` installation

**Workaround:** Pre-test templates with `docker compose config --file <template>`

**Status:** Improvement tracked for Phase 2

**Planned fix:** Add Docker Compose syntax validation during template refresh

---

### Resource Monitor Thresholds

**Issue:** Stone capacity thresholds (Mini/Standard/Large) hardcoded in daemon

**Impact:** Cannot customize RAM limits or container count warnings

**Workaround:** Edit `/etc/zen-garden/garden-moss.toml` (requires daemon restart)

**Status:** Configuration option planned for Phase 2

**Planned fix:** Add `[capacity]` section to config file with customizable thresholds

---

### Health Check Restart Loop Detection

**Issue:** Restart loop detection (>3 restarts in 10 min) marks service as Degraded but does not auto-remediate

**Impact:** Operators must manually investigate/restart service

**Workaround:** Use `garden-rake observe` to identify degraded services, then `garden-rake remove <name>` and `garden-rake offer <name>` to reinstall

**Status:** Auto-remediation planned for Phase 2

**Planned fix:** Add `:heal` action to automatically restart degraded services

---

### Port Conflict Resolution Logging

**Issue:** Port conflict resolution (27017 → 27018) logs warning but does not update mDNS TXT record immediately

**Impact:** mDNS announcements show incorrect port until next health check cycle (30s delay)

**Workaround:** Wait 30 seconds for mDNS update, or query `/api/v1/services/:name` for actual port

**Status:** Real-time mDNS update planned for Phase 2

**Planned fix:** Trigger mDNS re-announcement immediately after port conflict resolution

---

## Deprecation Timeline

### Deprecated in 0.1.0 (January 2026)

**Old API endpoints (pre-v1):**

| Deprecated Endpoint | Replacement | Removal Target |
|---------------------|-------------|----------------|
| `GET /api/offerings` | `GET /api/v1/offerings` | V2 (2027 Q1) |
| `GET /api/offerings/{name}` | `GET /api/v1/offerings/{name}` | V2 (2027 Q1) |
| `POST /api/offerings/refresh` | `POST /api/v1/offerings:refresh` | V2 (2027 Q1) |
| `GET /api/templates` | `GET /api/v1/services/manifests` | V2 (2027 Q1) |
| `GET /api/templates/{name}` | `GET /api/v1/services/{name}/manifest` | V2 (2027 Q1) |
| `GET /api/services` | `GET /api/v1/services` | V2 (2027 Q1) |

**Status:** Old endpoints still functional (forward to v1 handlers) but return deprecation warnings in response headers

**Response headers:**

```http
X-Deprecated: true
X-Replacement: /api/v1/offerings
X-Removal-Date: 2027-01-15
```

**Migration deadline:** January 15, 2027 (12 months)

---

### Planned Deprecations (Future)

**No planned deprecations at this time.**

When new deprecations occur:
1. Announce deprecation 12 months before removal
2. Add deprecation headers to old endpoints
3. Update documentation with migration guides
4. Provide CLI warnings for deprecated command usage

---

## Next Steps

**Upcoming releases:**

- **0.2.0 (Phase 2 - Q1 2026):** Production hardening
  - Health monitoring (background task)
  - Resource monitoring (capacity warnings with auto-remediation)
  - Port conflict resolution (real-time mDNS updates)
  - Atomic compose updates with rollback
  - Enhanced CLI (`--all` parallel execution, `--json` output, progress indicators)

- **0.3.0 (Phase 3 - Q2 2026):** Advanced features
  - Lantern UI integration (dashboard, topology visualization)
  - Pond security (mTLS, certificate auto-renewal)
  - Cursor-based polling optimization (delta updates)
  - Lifecycle event broadcasting (moss_online, moss_offline)
  - Client bindings (Python, JavaScript, .NET)
  - Prometheus metrics (extended telemetry)

**Feature requests:** Submit proposals to `/docs/proposals/` with ADR format

**Bug reports:** File issues with reproduction steps, environment details (OS, Moss/Rake versions), and logs

---

## See Also

- **API specification:** [../reference/api.md](../reference/api.md)
- **Upgrading guide:** [../guides/upgrading.md](../guides/upgrading.md) (planned)
- **Changelog:** [../CHANGELOG.md](../CHANGELOG.md) (planned)
