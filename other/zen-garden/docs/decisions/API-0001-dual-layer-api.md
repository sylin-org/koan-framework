---
status: Accepted
date: 2026-01-17
---

# API-0001: Dual-Layer API Design

## Status

**Accepted** - Implemented in v1 API

## Context

Zen Garden serves two distinct user populations with different needs:

1. **Beginners/operators (90%)**: Want simple service deployment, safety rails, hide Docker complexity
2. **Power users/DevOps (10%)**: Need full container control, debugging capabilities, technical details

**Problem:** Single API cannot optimize for both audiences without compromising UX for one group.

**Constraints:**
- Same underlying data (services, offerings, Stone registry)
- Must maintain consistency (no divergent state between layers)
- Backward compatibility required (API v1 stable)

**Source:** [API-V1-DUAL-LAYER-DESIGN.md](../API-V1-DUAL-LAYER-DESIGN.md)

## Decision

Provide **two API layers** for the same resources:

### Offerings API (Human Layer)
- **Prefix:** `/api/v1/offerings`
- **Target:** 90% of users
- **Philosophy:** Hide complexity, provide safety, optimize for common case
- **Response format:** Simplified (hide container IDs, image SHAs)
- **Operations:** `plant` (install), `take-away` (uninstall), semantic actions

### Services API (Technical Layer)
- **Prefix:** `/api/v1/services`
- **Target:** 10% power users
- **Philosophy:** Expose reality, provide control, enable debugging
- **Response format:** Full technical details (container_id, image_id, SHA256)
- **Operations:** Direct Docker control (restart policies, health checks, log streaming)

### Progressive Disclosure Pattern

Users transition from Offerings → Services as expertise grows:
- Beginners start with Offerings API (safe, simple)
- As they encounter limitations, discover Services API (powerful, complex)
- Both APIs access identical backend (no state divergence)

## Consequences

### Positive

✅ **UX optimization:** Each audience gets API tailored to their needs  
✅ **Safety by default:** Beginners protected from dangerous operations  
✅ **Power when needed:** Advanced users not artificially limited  
✅ **Clear migration path:** Natural progression as users gain expertise  
✅ **Backward compatibility:** Can evolve each layer independently

### Negative

❌ **Maintenance burden:** Must maintain two API surfaces  
❌ **Documentation complexity:** Two sets of endpoints to document  
❌ **Testing overhead:** Must test both layers for consistency  
❌ **Potential confusion:** Users may not understand layer difference

### Risks

**Risk:** Users bypass Offerings API, use Services API exclusively (defeats purpose)  
**Mitigation:** Make Offerings API the default in all documentation, tutorials, code samples

**Risk:** State divergence between layers (e.g., Offerings cache stale)  
**Mitigation:** Both layers query same backend, no separate caching

**Risk:** Feature parity mismatch (Services API gains features not exposed in Offerings)  
**Mitigation:** Explicit policy: all common operations available in Offerings, advanced-only in Services

## Alternatives Considered

### Alternative 1: Single API with Verbosity Parameter

**Approach:** `/api/v1/services?verbose=true` (full details) vs `?verbose=false` (simplified)

**Why not:**
- Unclear which level of verbosity is "default"
- Single endpoint serves two masters, compromises both
- Response format inconsistent (sometimes array, sometimes object)

### Alternative 2: Separate Tools (Rake vs Moss-Admin)

**Approach:** `garden-rake` (simple) vs `garden-moss-admin` (advanced) with different CLIs

**Why not:**
- Forces artificial tool separation
- Users must switch tools as they learn (friction)
- Duplicate code, inconsistent behavior

### Alternative 3: Feature Flags in Requests

**Approach:** `POST /api/v1/offerings {"advanced": true}` toggles behavior

**Why not:**
- Single endpoint becomes complex (if advanced { ... } else { ... })
- Testing explosion (2^N combinations of flags)
- Unclear which flags safe to combine

## Implementation Notes

**Endpoint structure:**
```
/api/v1/offerings              # Catalog view (available + installed)
/api/v1/offerings/{name}       # Offering details + compatibility
POST /api/v1/offerings         # Plant offering (simplified install)
DELETE /api/v1/offerings/{name} # Take away (uninstall)

/api/v1/services               # Runtime view (container-level)
/api/v1/services/{name}        # Service technical details
POST /api/v1/services          # Install with full Docker control
/api/v1/services/{name}:restart # Direct container action
/api/v1/services/{name}/logs   # Stream logs (SSE)
```

**Response format example:**
```json
// Offerings API (simplified)
{"name": "mongodb", "state": "installed", "health": "healthy"}

// Services API (detailed)
{
  "name": "mongodb",
  "container_id": "abc123",
  "image_id": "sha256:def456",
  "state": "running",
  "health": {"status": "healthy", "checks_passing": 3},
  "resources": {"cpu": "12%", "memory": "203MB"}
}
```

## References

- **Canonical spec:** [API-V1-DUAL-LAYER-DESIGN.md](../API-V1-DUAL-LAYER-DESIGN.md)
- **Related:** [V1-IMPLEMENTATION-COMPLETE.md](../V1-IMPLEMENTATION-COMPLETE.md) (v1 completion summary)
- **Migration guide:** [CLI-V1-MIGRATION.md](../CLI-V1-MIGRATION.md)
- **Async operations:** [ASYNC-JOB-API.md](../ASYNC-JOB-API.md)

## Versioning

**API v1 stability:** Both layers committed to backward compatibility for v1.x series.

**Deprecation policy:**
- Endpoints marked deprecated for 1 major version (v1.x)
- Removed in v2.0 with migration guide

**Evolution:**
- Offerings API: May gain simplified operations (batch plant, smart recommendations)
- Services API: May expose more Docker internals (network config, volume inspection)
