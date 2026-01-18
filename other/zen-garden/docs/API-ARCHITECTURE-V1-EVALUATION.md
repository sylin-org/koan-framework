# Zen Garden API Architecture v1 — Final Design

**Status:** Architecture Decision  
**Date:** January 17, 2026  
**Scope:** Dual-layer API with progressive disclosure  
**Decision:** Approved

---

## Executive Summary

**Architecture Decision:** Dual-layer API for progressive disclosure

### Two API Layers

1. **Offerings API** (`/api/v1/offerings`) — Human-friendly, simplified (90% of users)
2. **Services API** (`/api/v1/services`) — Technical, container-level details (10% power users)

**Key Insight:** Same backend resources, different presentation layers optimized for different audiences.

### Migration Summary

- **All unversioned endpoints** → `/api/v1/*` namespace
- **Custom actions** → Single colon format (`:heal`, `:reconcile`, `:refresh`, `:upgrade`, `:shutdown`)
- **Standard endpoints** → `/health`, `/capabilities`, `/metrics` stay at root
- **Semantic organization** → Offerings (human layer) + Services (technical layer) + Stone operations

**Full design documentation:** [`API-V1-DUAL-LAYER-DESIGN.md`](API-V1-DUAL-LAYER-DESIGN.md)

---

## Endpoint Inventory & Analysis

### Category 1: Keep As-Is (Industry Standards)

| Current Path | Rationale | Action |
|-------------|-----------|--------|
| `GET /health` | Industry standard health check | **KEEP** |
| `GET /metrics` | Prometheus standard | **KEEP** |
| `GET /capabilities` | Stone capabilities | **KEEP** (or move to `/api/v1/stone/capabilities`) |

**Decision Point:** Should `/capabilities` move to `/api/v1/stone/capabilities` per taxonomy consolidation, or stay as-is for compatibility with monitoring tools?

**Recommendation:** Keep `/capabilities` at root for now; many monitoring/orchestration tools expect root-level capability endpoints.

---

### Category 2: Services (Already v1 ✅)

All service lifecycle operations correctly versioned:

- `POST /api/v1/services` ✅
- `GET /api/v1/services` ✅
- `GET /api/v1/services/:service` ✅
- `POST /api/v1/services/:service/rest` ✅
- `POST /api/v1/services/:service/wake` ✅
- `POST /api/v1/services/:service/nourish` ✅
- `DELETE /api/v1/services/:service` ✅
- `GET /api/v1/services/:service/logs` ✅

**Status:** ✅ No changes needed

---

### Category 3: Garden Topology (Already v1 ✅)

- `GET /api/v1/garden` ✅
- `GET /api/v1/garden/stones/:stone_name` ✅
- `GET /api/v1/stone` ✅

**Status:** ✅ No changes needed

---

### Category 4: Pond Security (Already v1 ✅)

- `POST /api/v1/pond/init` ✅
- `GET /api/v1/pond/status` ✅
- `POST /api/v1/pond/invite` ✅
- `POST /api/v1/pond/join` ✅
- `DELETE /api/v1/pond` ✅
- `DELETE /api/v1/pond/stones/:stone_name` ✅

**Status:** ✅ No changes needed (Phase 3 stubs)

---

### Category 5: Offerings — NEEDS V1 MIGRATION 🔴

| Current Path | Semantic Analysis | Proposed v1 Path |
|-------------|-------------------|------------------|
| `GET /api/offerings` | Unversioned catalog access | `GET /api/v1/offerings` |
| `GET /api/offerings/:name` | Unversioned offering details | `GET /api/v1/offerings/:name` |
| `POST /api/offerings/refresh` | Unversioned admin operation | `POST /api/v1/offerings/_refresh` |

**Issues:**
- Unversioned breaks API evolution promise
- `refresh` is admin operation but looks like resource manipulation
- Taxonomy proposes `_refresh` prefix for admin actions

**Recommendation:**
```
GET  /api/v1/offerings              → List all offerings
GET  /api/v1/offerings/:name        → Get offering details
POST /api/v1/offerings/_refresh     → Rebuild index (admin)
```

**Rationale:**
- `_refresh` prefix signals non-RESTful admin action
- Clear v1 namespace for future compatibility (v2 might change offering schema)
- Consistent with taxonomy's admin operation convention

---

### Category 6: Templates — NEEDS V1 MIGRATION 🔴

| Current Path | Semantic Analysis | Proposed v1 Path |
|-------------|-------------------|------------------|
| `GET /api/templates` | Unversioned template list | `GET /api/v1/templates` |
| `GET /api/templates/:name` | Unversioned template YAML | `GET /api/v1/templates/:name` |

**Issues:**
- Unversioned raw template access
- No version protection for breaking YAML schema changes

**Recommendation:**
```
GET /api/v1/templates              → List templates
GET /api/v1/templates/:name        → Get template YAML
```

**Rationale:**
- Templates are developer/admin-facing resources that need versioning
- Future template schema changes (v2) won't break existing integrations

---

### Category 7: Events & Jobs — NEEDS V1 MIGRATION 🔴

| Current Path | Semantic Analysis | Proposed v1 Path |
|-------------|-------------------|------------------|
| `GET /api/events` | Unversioned SSE stream | `GET /api/v1/events` |
| `GET /api/jobs` | Unversioned job list | `GET /api/v1/jobs` |
| `GET /api/jobs/:job_id` | Unversioned job status | `GET /api/v1/jobs/:job_id` |

**Issues:**
- Event stream schema unversioned (breaking changes would impact clients)
- Job status format unversioned (could change without warning)

**Recommendation:**
```
GET /api/v1/events                 → Stream all events (SSE)
GET /api/v1/jobs                   → List jobs
GET /api/v1/jobs/:job_id           → Get job status
```

**Rationale:**
- Event schema evolution requires versioning (adding event types, changing payload structure)
- Job status/result format might change (progress tracking, cancellation support)

---

### Category 8: System Operations — NEEDS SEMANTIC REORGANIZATION 🔴🔴

**Current Paths (Problematic):**

| Current Path | Actual Purpose | Semantic Issue |
|-------------|----------------|----------------|
| `POST /api/system/reconcile` | Reconcile containers with registry | "system" is vague; reconciliation is service-level operation |
| `POST /api/system/refresh` | Upload/replace moss binary | "system" doesn't convey "binary update" |
| `GET /api/system/templates/:name/sources` | Debug template image resolution | Why is template metadata under "system"? |
| `PUT /api/system/templates/:name/compatibility` | Override compatibility rules | Template operation masquerading as system operation |

**Critical Problem:** `/api/system/*` is a semantic dumping ground with no coherent meaning.

**Analysis by Operation:**

#### 8.1 Reconcile Operation

**Current:** `POST /api/system/reconcile`  
**Meaning:** Synchronize service registry with actual container state  
**Semantic Issue:** "system reconcile" implies OS-level operation, but this is service-level  

**Option A (Service-Centric):**
```
POST /api/v1/services/_reconcile
```
**Rationale:** Reconciliation operates on services collection; `_reconcile` prefix signals admin action

**Option B (Stone-Centric):**
```
POST /api/v1/stone/_reconcile
```
**Rationale:** Reconciliation is stone-level operation (all services on this stone)

**Recommendation:** **Option B** — Reconciliation is a stone-level admin operation that affects all services. Stone context is clearer.

---

#### 8.2 Refresh Binary Operation

**Current:** `POST /api/system/refresh`  
**Meaning:** Upload new moss/rake binary and replace running daemon  
**Semantic Issue:** "refresh" is too generic; doesn't convey "binary update"

**Option A (Stone Operations):**
```
POST /api/v1/stone/_upgrade
```
**Rationale:** Upgrading the stone's daemon binary; clear intent

**Option B (Admin Operations):**
```
POST /api/v1/admin/binaries
```
**Rationale:** Explicit admin namespace for binary management

**Recommendation:** **Option A** — Stone upgrade is more intuitive than "admin binaries". The `_upgrade` prefix signals non-RESTful admin action.

---

#### 8.3 Template Sources (Debugging)

**Current:** `GET /api/system/templates/:name/sources`  
**Meaning:** Show how template resolved images (multi-arch, fallbacks)  
**Semantic Issue:** Template metadata endpoint buried under "system"

**Option A (Template Subresource):**
```
GET /api/v1/templates/:name/sources
```
**Rationale:** Image sources are template metadata; natural subresource

**Option B (Template Inspection):**
```
GET /api/v1/templates/:name/_sources
```
**Rationale:** `_sources` prefix signals this is debug/inspection endpoint (not part of core template API)

**Recommendation:** **Option A** — Sources are legitimate template subresources; developers need this for debugging. No underscore needed.

---

#### 8.4 Template Compatibility Override

**Current:** `PUT /api/system/templates/:name/compatibility`  
**Meaning:** Override template's compatibility rules (dev/test)  
**Semantic Issue:** Template configuration endpoint under "system"

**Option A (Template Subresource):**
```
PUT /api/v1/templates/:name/compatibility
```
**Rationale:** Compatibility is template configuration; PUT is semantically correct

**Option B (Template Admin Action):**
```
PUT /api/v1/templates/:name/_compatibility
```
**Rationale:** Override is admin action, not normal template operation

**Recommendation:** **Option B** — Compatibility override is admin/dev operation that changes template behavior. `_compatibility` prefix signals this is not part of normal template usage.

---

### Category 9: Discovery — NEEDS V1 MIGRATION & NAMING FIX 🔴

| Current Path | Semantic Analysis | Proposed v1 Path |
|-------------|-------------------|------------------|
| `GET /api/peer-stones` | Unversioned peer discovery | `GET /api/v1/peers` |

**Issues:**
- Unversioned discovery endpoint
- `peer-stones` inconsistent with taxonomy's `/api/peers`
- Hyphenated resource name breaks convention

**Recommendation:**
```
GET /api/v1/peers                  → Discover peer stones via mDNS
```

**Rationale:**
- "peers" is clearer than "peer-stones" (stones is implied context)
- Consistent with taxonomy proposal
- V1 namespace protects discovery protocol changes

---

### Category 10: Administrative — NEEDS SEMANTIC HOME 🔴

| Current Path | Semantic Analysis | Proposed v1 Path |
|-------------|-------------------|------------------|
| `POST /admin/shutdown` | Graceful daemon shutdown | `POST /api/v1/stone/_shutdown` or `POST /api/v1/admin/shutdown` |

**Issues:**
- `/admin/*` namespace exists for single endpoint
- Not versioned

**Option A (Stone Operation):**
```
POST /api/v1/stone/_shutdown
```
**Rationale:** Shutting down the stone's daemon; stone-level operation

**Option B (Admin Namespace):**
```
POST /api/v1/admin/shutdown
```
**Rationale:** Explicit admin namespace for privileged operations

**Recommendation:** **Option A** — Shutdown is stone lifecycle operation. Keeps API surface smaller (no separate admin namespace for one endpoint). `_shutdown` prefix signals destructive admin action.

---

## Proposed v1 API Structure (Complete)

### Stone Identity & Monitoring (Standards)
```
GET  /health                           → Health check (industry standard)
GET  /capabilities                     → Hardware/software capabilities
GET  /metrics                          → Prometheus metrics
```

### Services (Offerings Lifecycle)
```
POST   /api/v1/services                → Offer service
GET    /api/v1/services                → List services
GET    /api/v1/services/:service       → Service details
POST   /api/v1/services/:service/rest  → Stop service
POST   /api/v1/services/:service/wake  → Start service
POST   /api/v1/services/:service/nourish → Upgrade service
DELETE /api/v1/services/:service       → Remove service
GET    /api/v1/services/:service/logs  → Stream logs (SSE)
POST   /api/v1/services/_reconcile     → Reconcile registry with containers
```

### Offerings (Catalog)
```
GET  /api/v1/offerings                 → List offerings
GET  /api/v1/offerings/:name           → Offering details
POST /api/v1/offerings/_refresh        → Rebuild offerings index
```

### Templates (Raw Definitions)
```
GET /api/v1/templates                  → List templates
GET /api/v1/templates/:name            → Get template YAML
GET /api/v1/templates/:name/sources    → Image resolution details
PUT /api/v1/templates/:name/_compatibility → Override compatibility
```

### Garden Topology (Multi-Stone)
```
GET /api/v1/garden                     → Garden overview
GET /api/v1/garden/stones/:stone_name  → Stone details from registry
GET /api/v1/stone                      → Local stone info
POST /api/v1/stone/_reconcile          → Reconcile containers (stone-level)
POST /api/v1/stone/_upgrade            → Upgrade moss binary
POST /api/v1/stone/_shutdown           → Shutdown daemon
```

### Pond Security (Trust Mesh)
```
POST   /api/v1/pond/init               → Initialize pond
GET    /api/v1/pond/status             → Pond status
POST   /api/v1/pond/invite             → Generate invite code
POST   /api/v1/pond/join               → Join pond
DELETE /api/v1/pond                    → Leave pond
DELETE /api/v1/pond/stones/:stone_name → Untrust stone
```

### Discovery & Events
```
GET /api/v1/peers                      → Discover peer stones
GET /api/v1/events                     → Stream all events (SSE)
```

### Jobs (Async Operations)
```
GET /api/v1/jobs                       → List jobs
GET /api/v1/jobs/:job_id               → Job status
```

---

## Semantic Patterns & Conventions

### Admin Action Prefix: `_action`

Non-RESTful admin operations use underscore prefix:
- `POST /api/v1/offerings/_refresh` — Not a resource, triggers rebuild
- `POST /api/v1/services/_reconcile` — Not a resource, syncs state
- `POST /api/v1/stone/_upgrade` — Not a resource, replaces binary
- `POST /api/v1/stone/_shutdown` — Not a resource, lifecycle control
- `PUT /api/v1/templates/:name/_compatibility` — Overrides, not standard PUT

**Rationale:** Underscore signals "this is not a REST resource, it's an action/command"

### Stone-Level Operations

Operations that affect the entire stone (not individual services):
- `/api/v1/stone` — Stone identity/capabilities
- `/api/v1/stone/_reconcile` — Reconcile all services
- `/api/v1/stone/_upgrade` — Upgrade daemon binary
- `/api/v1/stone/_shutdown` — Shutdown daemon

**Rationale:** Clear semantic boundary between service operations and stone-level operations

### Collection-Level Admin Actions

Admin operations on entire collections:
- `/api/v1/services/_reconcile` — Reconcile services collection (alternative to stone-level)
- `/api/v1/offerings/_refresh` — Refresh offerings collection

**Rationale:** Actions that operate on entire collections, not individual resources

---

## Migration Impact Analysis

### Breaking Changes

**All unversioned endpoints move to v1:**
- `GET /api/offerings` → `GET /api/v1/offerings`
- `GET /api/templates` → `GET /api/v1/templates`
- `GET /api/events` → `GET /api/v1/events`
- `GET /api/jobs` → `GET /api/v1/jobs`
- `GET /api/peer-stones` → `GET /api/v1/peers`

**System operations reorganized:**
- `POST /api/system/reconcile` → `POST /api/v1/stone/_reconcile`
- `POST /api/system/refresh` → `POST /api/v1/stone/_upgrade`
- `GET /api/system/templates/:name/sources` → `GET /api/v1/templates/:name/sources`
- `PUT /api/system/templates/:name/compatibility` → `PUT /api/v1/templates/:name/_compatibility`

**Admin endpoint reorganized:**
- `POST /admin/shutdown` → `POST /api/v1/stone/_shutdown`

### Client Impact

**CLI (garden-rake):** Update route constants in one location  
**External clients:** None known (greenfield framework)  
**Monitoring tools:** `/health`, `/capabilities`, `/metrics` unchanged  

### Migration Strategy

**Phase 1:** Implement new v1 routes alongside old routes (dual support)  
**Phase 2:** Update CLI to use new routes  
**Phase 3:** Add deprecation warnings to old routes (log warnings)  
**Phase 4:** Remove old routes (breaking change, but greenfield allows)

**Recommendation:** Skip Phase 1-3; make clean break (greenfield framework with no production deployments)

---

## Implementation Checklist

### Code Changes Required

**src/moss/src/main.rs (route definitions):**
- [ ] Move offerings routes to v1 namespace
- [ ] Move templates routes to v1 namespace
- [ ] Move events route to v1 namespace
- [ ] Move jobs routes to v1 namespace
- [ ] Move peer-stones → peers under v1
- [ ] Reorganize system/* operations to stone/* and templates/*
- [ ] Move /admin/shutdown to /api/v1/stone/_shutdown
- [ ] Add `_` prefix to admin action routes

**src/moss/src/handlers/*.rs (handler implementations):**
- [ ] Update handler paths to match new routes
- [ ] Ensure no hardcoded route strings in responses

**garden-rake CLI (route constants):**
- [ ] Update MOSS_ENDPOINT constants
- [ ] Update error messages referencing old routes

### Documentation Updates

- [ ] Update API-REFERENCE.md with all new paths
- [ ] Update CLI-API-TAXONOMY-V1-ZEN.md implementation status
- [ ] Update any integration examples in README
- [ ] Add migration guide (old paths → new paths)

### Testing Updates

- [ ] Update integration test route URLs
- [ ] Add tests for new v1 routes
- [ ] Verify old routes return 404 after migration

---

## Open Questions for Architecture Team

1. **Reconcile placement:** Should reconciliation be `/api/v1/services/_reconcile` (collection-level) or `/api/v1/stone/_reconcile` (stone-level)?
   - **Recommendation:** Stone-level (affects entire stone state)

2. **Capabilities endpoint:** Keep at `/capabilities` or move to `/api/v1/stone/capabilities`?
   - **Recommendation:** Keep at root (monitoring tool compatibility)

3. **Admin namespace:** Should we reserve `/api/v1/admin/*` for future admin operations, or fold everything into `/api/v1/stone/*`?
   - **Recommendation:** Fold into stone/* (keeps API surface smaller)

4. **Template sources:** Should `sources` be public subresource or admin-prefixed `_sources`?
   - **Recommendation:** Public subresource (developers need for debugging)

5. **Migration timeline:** Clean break or phased deprecation?
   - **Recommendation:** Clean break (greenfield allows it)

---

## Decision Record

**Date:** January 17, 2026  
**Decision:** Adopt proposed v1 API structure with semantic stone operations  
**Rationale:**
- Clear domain boundaries (stone, services, offerings, templates, garden, pond)
- Semantic meaning over generic "system" namespace
- Admin action prefix `_` for non-RESTful operations
- Full v1 namespace coverage (except industry standards)

**Next Steps:**
1. Review and approve this proposal
2. Create implementation tasks for route migration
3. Update CLI route constants
4. Update all documentation
5. Test and deploy

**Approvers:** Architecture Team, Lead Developer

---

## Appendix: Full Route Comparison

### Before (Current — 32 endpoints)

```
Standard:
  GET  /health
  GET  /capabilities
  GET  /metrics

V1 Namespaced (16):
  POST   /api/v1/services
  GET    /api/v1/services
  GET    /api/v1/services/:service
  POST   /api/v1/services/:service/rest
  POST   /api/v1/services/:service/wake
  POST   /api/v1/services/:service/nourish
  DELETE /api/v1/services/:service
  GET    /api/v1/services/:service/logs
  GET    /api/v1/garden
  GET    /api/v1/garden/stones/:stone_name
  GET    /api/v1/stone
  POST   /api/v1/pond/init
  GET    /api/v1/pond/status
  POST   /api/v1/pond/invite
  POST   /api/v1/pond/join
  DELETE /api/v1/pond

Unversioned (13):
  GET  /api/offerings
  GET  /api/offerings/:name
  POST /api/offerings/refresh
  GET  /api/templates
  GET  /api/templates/:name
  GET  /api/events
  GET  /api/jobs
  GET  /api/jobs/:job_id
  POST /api/system/reconcile
  POST /api/system/refresh
  GET  /api/system/templates/:name/sources
  PUT  /api/system/templates/:name/compatibility
  GET  /api/peer-stones
  POST /admin/shutdown
```

### After (Proposed — 32 endpoints, all organized)

```
Standard (3):
  GET  /health
  GET  /capabilities
  GET  /metrics

Services (9):
  POST   /api/v1/services
  GET    /api/v1/services
  GET    /api/v1/services/:service
  POST   /api/v1/services/:service/rest
  POST   /api/v1/services/:service/wake
  POST   /api/v1/services/:service/nourish
  DELETE /api/v1/services/:service
  GET    /api/v1/services/:service/logs
  POST   /api/v1/services/_reconcile

Offerings (3):
  GET  /api/v1/offerings
  GET  /api/v1/offerings/:name
  POST /api/v1/offerings/_refresh

Templates (4):
  GET /api/v1/templates
  GET /api/v1/templates/:name
  GET /api/v1/templates/:name/sources
  PUT /api/v1/templates/:name/_compatibility

Garden & Stone (6):
  GET  /api/v1/garden
  GET  /api/v1/garden/stones/:stone_name
  GET  /api/v1/stone
  POST /api/v1/stone/_reconcile
  POST /api/v1/stone/_upgrade
  POST /api/v1/stone/_shutdown

Pond (6):
  POST   /api/v1/pond/init
  GET    /api/v1/pond/status
  POST   /api/v1/pond/invite
  POST   /api/v1/pond/join
  DELETE /api/v1/pond
  DELETE /api/v1/pond/stones/:stone_name

Events & Jobs (4):
  GET /api/v1/events
  GET /api/v1/jobs
  GET /api/v1/jobs/:job_id

Discovery (1):
  GET /api/v1/peers
```

**Total:** 32 endpoints (3 standard + 29 v1)  
**Result:** Clean semantic organization, zero "system" cruft, full v1 coverage
