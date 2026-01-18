# Zen Garden CLI + API Taxonomy v1 (Greenfield Redesign)

**Status:** Proposal  
**Date:** 2026-01-17  
**Scope:** Complete redesign of `garden-rake` CLI and Moss/Lantern HTTP APIs  
**Back-Compat:** Not required — greenfield approach for ideal DX/UX  

---

## Executive Summary

The current Zen Garden CLI and API surface has organically grown into a sprawling, inconsistent interface with:
- **14 top-level CLI commands** using mixed metaphors (offer/rest/wake/tend/observe/place/invite)
- **30 Moss HTTP routes** split across 6 namespaces with unclear resource boundaries
- **Verb inconsistency**: "offer" (noun+verb), "reconcile" (admin), "refresh" (update), "rest/wake" (zen)
- **Scope blur**: `/api/operations/*` vs `/api/system/*` vs `/api/offerings/refresh`
- **Missing REST affordances**: No DELETE for services, no idempotent PUT/PATCH, POST-heavy operations

This proposal defines a **unified Zen Garden language** with:
1. **Canonical resource hierarchy**: stone → offering → service → job/event
2. **Scope-first CLI pattern**: `garden-rake <scope> <verb> [target]`
3. **RESTful API design**: Standard HTTP verbs, predictable URL structure
4. **Zen metaphors preserved** at the *noun* level (stone/offering/garden), standard verbs for actions

**Key Benefits:**
- **Predictability**: Users can infer commands/routes from patterns
- **Discoverability**: `garden-rake offerings list` reads like English
- **Consistency**: CLI mirrors API structure (same nouns, same hierarchy)
- **Elegance**: Zen philosophy encoded in *what* things are, not *how* you interact with them

---

## Current State Inventory

### Rake CLI Commands (Phase 0 → Current)
```
garden-rake status [--at]
garden-rake offer [offering] [info] [--at] [--prefer] [--anywhere-on-fail]
garden-rake list [--at]
garden-rake remove <service> [--at]
garden-rake upgrade [service] [--all] [--at]
garden-rake rest <service> [--at]
garden-rake wake <service> [--at]
garden-rake place <target> [--code] [--at]
garden-rake invite <stone_name> [--at]
garden-rake observe [stone] [--offering]
garden-rake watch [target] [--until] [--at]
garden-rake refresh <component> --from <path> [--at]
garden-rake reconcile [--drop-invalid] [--at]
garden-rake template <list|show> <name> [--at]
garden-rake tend [target] [--clear] [--verbose]
```

### Moss HTTP API (Phase 0 → Current)
```
GET  /health
GET  /capabilities
GET  /metrics
GET  /api/services
GET  /api/services/:service
GET  /api/services/:service/logs
GET  /api/templates
GET  /api/templates/:name
GET  /api/offerings
POST /api/offerings/refresh
GET  /api/offerings/:name
POST /api/operations/offer/:offering
POST /api/operations/offer
POST /api/operations/remove/:target
POST /api/operations/upgrade
POST /api/operations/upgrade/:service
POST /api/operations/rest/:service
POST /api/operations/wake/:service
POST /api/operations/place/:target
POST /api/operations/invite/:stone_name
POST /api/system/refresh
POST /api/system/reconcile
GET  /api/system/templates/:name/sources
PUT  /api/system/templates/:name/compatibility
GET  /api/peer-stones
GET  /api/jobs/:job_id
GET  /api/jobs
GET  /api/events
POST /admin/shutdown
```

### Lantern HTTP API (Phase 0 → Current)
```
GET  /health
POST /api/register
GET  /api/resolve
GET  /api/stones
GET  /api/topology
GET  /api/events/stream
```

---

## Pain Points Analysis

### 1. Verb Inconsistency
- **CLI**: "offer" (noun+verb fusion), "rest/wake" (zen metaphors), "tend" (unique to rake), "observe" (topology), "reconcile" (admin)
- **API**: `/api/operations/offer` (install), `/api/operations/rest` (stop), `/api/offerings/refresh` (rebuild index)
- **Impact**: Users must memorize each command; no composable mental model

### 2. Resource Boundary Blur
- **Templates vs Offerings vs Services**: Three views of the same lifecycle (catalog → validated → running), but API treats them as separate silos
- **Example**: `/api/templates/:name` (raw YAML) vs `/api/offerings/:name` (compiled with compat) vs `/api/services/:service` (running instance)
- **Impact**: Unclear when to use which endpoint; overlapping concerns

### 3. Scope Sprawl
- **Operations namespace**: `/api/operations/*` holds lifecycle verbs (offer/remove/upgrade/rest/wake/place/invite)
- **System namespace**: `/api/system/*` holds admin verbs (refresh/reconcile) *and* dev utilities (templates sources/compatibility)
- **Orphan routes**: `/api/offerings/refresh` (should be under `/api/system`?)
- **Impact**: No clear rule for where new endpoints belong

### 4. CLI Command Grab-Bag
- **Status** (stone health), **observe** (garden topology), **tend** (client cache), **watch** (event stream), **template** (dev/debug), **place/invite** (future)
- **No hierarchy**: `garden-rake status` vs `garden-rake stone status` — which scope is implied?
- **Impact**: Top-level commands grow without bound; `--help` becomes a wall of text

### 5. Missing REST Affordances
- **No DELETE**: Services removed via `POST /api/operations/remove/:target` (should be `DELETE /api/services/:service`)
- **No idempotency**: POST-heavy; can't safely retry operations
- **No PATCH**: Can't update service config in-place (must remove + re-offer)
- **Impact**: API doesn't follow HTTP semantics; harder to build robust clients

### 6. Event Streams Split
- **Lantern**: `/api/events/stream` (garden-wide)
- **Moss**: `/api/events` (stone-local) + `/api/services/:service/logs` (per-service)
- **CLI**: `garden-rake watch` (events) vs `garden-rake watch offering <name> logs` (logs)
- **Impact**: Three different shapes for "show me what's happening"; no unified observability API

### 7. "Tending" is CLI-Only State
- **Concept**: "Which stone am I working with?" → cached for 90s, set via `garden-rake tend`
- **Reality**: Server has no session/context API; client-side cache with TTL
- **Impact**: Useful UX feature, but implemented as a rake-specific workaround instead of a general "context" pattern

### 8. Zen Metaphors are Charming but Leaky
- **Effective when learned**: "rest/wake" (stop/start), "offer" (install), "stone/moss/lantern" (machine/daemon/registry)
- **Opaque to new users**: "How do I stop a service?" → "Use `garden-rake rest <service>`" (not guessable)
- **Doesn't compose**: Metaphors work for top-level nouns, break down as verbs
- **Impact**: Beautiful philosophy, poor discoverability; high learning curve

---

## Proposed Taxonomy v1

### Design Principles

1. **Zen metaphors for nouns** (stone, offering, garden, service)  
   → Preserve the philosophy and identity of Zen Garden
   
2. **Standard verbs for actions** (list, get, create, delete, start, stop, logs)  
   → Maximize discoverability and composability
   
3. **Resource hierarchy maps to URL structure**  
   → `/api/{collection}/{id}/{sub-resource}` follows REST conventions
   
4. **CLI follows `garden-rake <scope> <verb> [target]` pattern**  
   → Scope-first (stone/offerings/services/garden/context) makes top-level commands bounded
   
5. **Idempotent operations use PUT/PATCH, mutations use POST, reads use GET, removals use DELETE**  
   → HTTP semantics for retry safety and caching
   
6. **Client-side concerns separate from server contracts**  
   → "Tending" becomes "context management" (explicit, not magical)

---

### Canonical Resources

| **Zen Noun**     | **Plural**       | **Definition**                                          | **Scope**           |
|------------------|------------------|---------------------------------------------------------|---------------------|
| **Stone**        | `stones`         | Physical/VM host running `moss` daemon                  | Garden (multi-node) |
| **Offering**     | `offerings`      | Validated service template with compatibility rules     | Stone (local catalog)|
| **Service**      | `services`       | Running container instance of an offering               | Stone (runtime)     |
| **Job**          | `jobs`           | Async operation handle (create/upgrade/remove)          | Stone (ephemeral)   |
| **Event**        | `events`         | Real-time stream (lifecycle changes, logs, errors)      | Stone or Garden     |
| **Garden**       | `gardens`        | Logical grouping of stones (topology + registry)        | Global (Lantern)    |
| **Context**      | N/A              | Client-side selected stone (for CLI convenience)        | Local (rake cache)  |

---

### Moss HTTP API v1

#### Stone Metadata & Health
```
GET /api/stone                              → Stone identity, capabilities, health
GET /api/stone/capabilities                 → Hardware capabilities (fine-grained)
GET /api/stone/metrics                      → Current metrics snapshot
```
**Consolidates:** `/health` + `/capabilities` + `/metrics` into unified stone resource

---

#### Offerings (Catalog)
```
GET  /api/offerings                         → List all offerings (by category, with compat decisions)
GET  /api/offerings/:name                   → Offering details + compatibility + ports/volumes
POST /api/offerings/_refresh                → Rebuild offerings index (admin, underscore-prefixed)
```
**Changes:**
- `POST /api/offerings/refresh` → `POST /api/offerings/_refresh` (signals non-RESTful action)
- Offerings are read-only catalog; mutations happen via templates (dev workflow)

---

#### Services (Runtime)
```
GET    /api/services                        → List running services
GET    /api/services/:service               → Service status/info
POST   /api/services                        → Create service from offering
                                               Body: {offering, config}
DELETE /api/services/:service               → Remove service
POST   /api/services/:service/start         → Start (wake) service
POST   /api/services/:service/stop          → Stop (rest) service
POST   /api/services/:service/restart       → Restart service (new affordance)
POST   /api/services/:service/upgrade       → Upgrade service to latest image
GET    /api/services/:service/logs          → Stream logs (SSE, query params: ?follow=true&tail=100)
```
**Changes:**
- `POST /api/operations/offer/:offering` → `POST /api/services` (RESTful collection endpoint)
- `POST /api/operations/remove/:target` → `DELETE /api/services/:service` (proper HTTP verb)
- `POST /api/operations/rest/:service` → `POST /api/services/:service/stop` (standard verb)
- `POST /api/operations/wake/:service` → `POST /api/services/:service/start` (standard verb)
- `POST /api/operations/upgrade/:service` → `POST /api/services/:service/upgrade` (sub-resource action)
- `GET /api/services/:service/logs` consolidates with `/api/events?service=:name` filtering

---

#### Jobs (Async Operations)
```
GET /api/jobs                               → List recent jobs (paginated, filterable)
GET /api/jobs/:job_id                       → Job status/result/logs
```
**No changes** (existing routes are good)

---

#### Events (Observability)
```
GET /api/events                             → Real-time event stream (SSE)
                                               Query params: ?service=:name&type=logs|lifecycle|error
```
**Consolidates:**
- Service lifecycle events (create/start/stop/upgrade)
- Service logs (replaces separate `/api/services/:service/logs` for unified stream)
- System events (reconcile, compatibility decisions, image pulls)

**Usage patterns:**
- `GET /api/events` → All events (garden-rake events stream)
- `GET /api/events?service=mongodb` → Filter by service (garden-rake services logs mongodb)
- `GET /api/events?type=logs` → Only logs (garden-rake events stream --type logs)

---

#### System (Daemon Management)
```
POST /api/system/reconcile                  → Reconcile containers with registry
                                               Body: {drop_invalid: bool}
POST /api/system/refresh                    → Update moss/rake binaries (dev use)
                                               Body: {component, binary_base64}
GET  /api/system/templates/:name/sources    → Debug template resolution (dev)
PUT  /api/system/templates/:name/compatibility → Upload compatibility rules (dev)
```
**No major changes** (system namespace is correct for admin/dev operations)

---

#### Peer Discovery
```
GET /api/peers                              → Discover peer stones on network (UDP broadcast results)
```
**Changes:**
- `/api/peer-stones` → `/api/peers` (shorter, clearer)

---

#### Admin
```
POST /api/admin/shutdown                    → Graceful shutdown
```
**No changes**

---

#### Removed/Deferred
```
POST /api/operations/place/:target          → Move to Phase 3 garden bootstrapping API
POST /api/operations/invite/:stone_name     → Move to Phase 3 garden bootstrapping API
```
**Rationale:** `place` and `invite` are placeholders for future multi-stone onboarding; keep them out of v1 API until Phase 3 design is real.

---

### Lantern HTTP API v1

#### Garden Topology
```
GET  /api/garden                            → Garden topology summary (stone count, health)
GET  /api/garden/stones                     → List registered stones (with last heartbeat)
POST /api/garden/stones                     → Register stone heartbeat
                                               Body: {stone_name, endpoint, capabilities}
GET  /api/garden/stones/:stone              → Stone details from registry
GET  /api/garden/events                     → Garden-wide event stream (SSE)
```
**Changes:**
- `/health` → `/api/lantern` (new endpoint for lantern daemon health)
- `/api/topology` → `/api/garden` (clearer resource name)
- `/api/stones` → `/api/garden/stones` (nested under garden resource)
- `/api/register` → `POST /api/garden/stones` (RESTful collection POST)
- `/api/events/stream` → `/api/garden/events` (consistent with Moss `/api/events`)

---

#### Service Resolution
```
GET /api/resolve?service=:name              → Resolve service to stone endpoint (DNS-like)
```
**No changes** (resolution is a query operation, not a resource)

---

#### Lantern Health
```
GET /api/lantern                            → Lantern daemon health + election state
```
**Changes:**
- `/health` → `/api/lantern` (matches moss pattern of `/api/stone` for identity)

---

### garden-rake CLI v1

**New pattern: `garden-rake <scope> <verb> [target] [flags]`**

Scopes:
- `stone` → Current stone operations (local or remote via context)
- `offerings` → Catalog discovery
- `services` → Service lifecycle
- `jobs` → Async operation tracking
- `events` → Observability
- `garden` → Multi-stone topology (Lantern-backed)
- `context` → Client-side stone selection
- `templates` → Dev utilities
- `system` → Admin operations

---

#### Stone Management
```bash
garden-rake stone info                      # Show stone capabilities, health, version
garden-rake stone metrics                   # Show current metrics snapshot
garden-rake stone reconcile                 # Reconcile containers with registry

# Flags (apply to all stone commands):
  --at <endpoint|name>                      # Override context (explicit target)
```
**Was:** `garden-rake status`, `garden-rake reconcile`

---

#### Offerings (Catalog)
```bash
garden-rake offerings list                  # List offerings by category
garden-rake offerings search <query>        # Recommend offerings by query (with --prefer, --anywhere)
garden-rake offerings info <name>           # Show offering details + compatibility decision
garden-rake offerings refresh               # Rebuild offerings index (admin)

# Flags:
  --prefer <ssd|nvme|hdd>                   # Bias recommendations (non-blocking)
  --anywhere                                # Search across all discovered stones
  --at <endpoint|name>                      # Target stone (default: current context)
```
**Was:** `garden-rake offer`, `garden-rake offer <query>`, `garden-rake offer <name> info`

---

#### Services (Runtime)
```bash
garden-rake services list                   # List running services
garden-rake services create <offering>      # Install service from offering
garden-rake services delete <service>       # Remove service
garden-rake services start <service>        # Start service (was: wake)
garden-rake services stop <service>         # Stop service (was: rest)
garden-rake services restart <service>      # Restart service (new)
garden-rake services upgrade [service]      # Upgrade service(s) to latest image
garden-rake services logs <service>         # Stream logs (SSE, with --follow, --tail)
garden-rake services info <service>         # Show service status + ports + volumes

# Flags:
  --all                                     # Upgrade all services (with upgrade command)
  --follow                                  # Follow logs in real-time (with logs command)
  --tail <n>                                # Show last N lines (with logs command)
  --at <endpoint|name>                      # Target stone
```
**Was:** `garden-rake list`, `garden-rake offer <name>`, `garden-rake remove`, `garden-rake wake`, `garden-rake rest`, `garden-rake upgrade`, `garden-rake watch offering <name> logs`

---

#### Jobs
```bash
garden-rake jobs list                       # List recent async jobs
garden-rake jobs get <job_id>               # Show job status/result

# Flags:
  --at <endpoint|name>                      # Target stone
```
**New** (explicit job tracking, was implicit in create/upgrade output)

---

#### Events (Observability)
```bash
garden-rake events stream                   # Stream all events from stone
garden-rake events stream --service <name>  # Filter events by service
garden-rake events stream --type logs       # Filter by event type

# Flags:
  --service <name>                          # Filter by service
  --type <logs|lifecycle|error>             # Filter by event type
  --until <string>                          # Exit when string appears
  --at <endpoint|name>                      # Target stone
```
**Was:** `garden-rake watch`, `garden-rake watch offering <name> logs`

---

#### Garden (Multi-Stone Topology)
```bash
garden-rake garden list                     # List all stones in garden (Lantern registry)
garden-rake garden info <stone>             # Show stone details from registry
garden-rake garden events                   # Garden-wide event stream (Lantern SSE)

# Flags:
  --offering <name>                         # Filter by offering (with list command)
```
**Was:** `garden-rake observe`, `garden-rake observe <stone>`, `garden-rake observe --offering`

---

#### Context (Client-Side Stone Selection)
```bash
garden-rake context show                    # Show current context (stone, endpoint, age)
garden-rake context set <endpoint|name>     # Set target stone (cached for 90s)
garden-rake context set this                # Tend to localhost
garden-rake context set auto                # Auto-discover and set
garden-rake context clear                   # Clear context (forces auto-discovery)

# Flags:
  --verbose                                 # Show detailed cache info
```
**Was:** `garden-rake tend`, `garden-rake tend <target>`, `garden-rake tend --clear`

---

#### Templates (Dev Utilities)
```bash
garden-rake templates list                  # List available templates
garden-rake templates show <name>           # Show template YAML content

# Flags:
  --at <endpoint|name>                      # Target stone
```
**Was:** `garden-rake template list`, `garden-rake template show <name>`

---

#### System (Admin Operations)
```bash
garden-rake system refresh <component>      # Update moss/rake binary on remote stone
  --from <path>                             # Binary file path

# Flags:
  --at <endpoint|name>                      # Target stone
```
**Was:** `garden-rake refresh <component> --from <path>`

---

#### Removed/Deferred Commands
```bash
# Phase 3 garden bootstrapping (deferred until design is real):
garden-rake place <target> [--code]         # Move to future "garden bootstrap" family
garden-rake invite <stone_name>             # Move to future "garden bootstrap" family
```

---

## CLI/API Mapping Matrix

| **User Intent**                        | **CLI v1**                               | **HTTP v1**                              | **Was (CLI)**                          | **Was (HTTP)**                         |
|----------------------------------------|------------------------------------------|------------------------------------------|----------------------------------------|----------------------------------------|
| Show stone capabilities                | `garden-rake stone info`                 | `GET /api/stone`                         | `garden-rake status`                   | `GET /health`, `/capabilities`         |
| List available offerings               | `garden-rake offerings list`             | `GET /api/offerings`                     | `garden-rake offer`                    | `GET /api/offerings`                   |
| Search for offering by query           | `garden-rake offerings search <query>`   | `GET /api/offerings` (client-side)       | `garden-rake offer <query>`            | N/A (client-side ranking)              |
| Show offering details                  | `garden-rake offerings info <name>`      | `GET /api/offerings/:name`               | `garden-rake offer <name> info`        | `GET /api/offerings/:name`             |
| Install service                        | `garden-rake services create <offering>` | `POST /api/services`                     | `garden-rake offer <name>`             | `POST /api/operations/offer/:offering` |
| List running services                  | `garden-rake services list`              | `GET /api/services`                      | `garden-rake list`                     | `GET /api/services`                    |
| Remove service                         | `garden-rake services delete <service>`  | `DELETE /api/services/:service`          | `garden-rake remove <service>`         | `POST /api/operations/remove/:target`  |
| Start service                          | `garden-rake services start <service>`   | `POST /api/services/:service/start`      | `garden-rake wake <service>`           | `POST /api/operations/wake/:service`   |
| Stop service                           | `garden-rake services stop <service>`    | `POST /api/services/:service/stop`       | `garden-rake rest <service>`           | `POST /api/operations/rest/:service`   |
| Upgrade service                        | `garden-rake services upgrade <service>` | `POST /api/services/:service/upgrade`    | `garden-rake upgrade <service>`        | `POST /api/operations/upgrade/:service`|
| Stream service logs                    | `garden-rake services logs <service>`    | `GET /api/services/:service/logs`        | `garden-rake watch offering <name> logs`| `GET /api/services/:service/logs`      |
| Stream all events                      | `garden-rake events stream`              | `GET /api/events`                        | `garden-rake watch`                    | `GET /api/events`                      |
| List stones in garden                  | `garden-rake garden list`                | `GET /api/garden/stones`                 | `garden-rake observe`                  | Lantern: `GET /api/stones`             |
| Show stone details (from registry)     | `garden-rake garden info <stone>`        | `GET /api/garden/stones/:stone`          | `garden-rake observe <stone>`          | N/A (was client-side filter)           |
| Set target stone (context)             | `garden-rake context set <endpoint>`     | N/A (client-side cache)                  | `garden-rake tend <endpoint>`          | N/A                                    |
| Reconcile containers                   | `garden-rake stone reconcile`            | `POST /api/system/reconcile`             | `garden-rake reconcile`                | `POST /api/system/reconcile`           |
| Refresh offerings index                | `garden-rake offerings refresh`          | `POST /api/offerings/_refresh`           | (hidden/implicit)                      | `POST /api/offerings/refresh`          |

---

## Migration Notes (Greenfield Implementation)

### Phase 1: API v1 (Moss + Lantern)
**Goal:** Implement new HTTP routes alongside existing routes (dual-support during transition)

**Moss changes:**
1. Add new routes: `GET /api/stone`, `POST /api/services`, `DELETE /api/services/:service`, `/api/services/:service/{start,stop,restart}`, `GET /api/peers`
2. Keep existing routes for backward compat: `/api/operations/*`, `/health`, `/capabilities`, `/metrics`, `/api/peer-stones`
3. Update OpenAPI spec to reflect v1 (mark old routes as deprecated)

**Lantern changes:**
1. Add new routes: `GET /api/garden`, `GET /api/garden/stones`, `POST /api/garden/stones`, `GET /api/lantern`, `GET /api/garden/events`
2. Keep existing routes: `/api/stones`, `/api/register`, `/api/topology`, `/health`, `/api/events/stream`
3. Update OpenAPI spec

**Deliverable:** Dual-support API (v0 + v1 routes coexist)

---

### Phase 2: CLI v1 (garden-rake)
**Goal:** Rewrite CLI to use new command structure and new API routes

**Implementation:**
1. Rewrite clap derive structure to use scope-based subcommands:
   ```rust
   #[derive(Subcommand)]
   enum Commands {
       Stone { #[command(subcommand)] cmd: StoneCommands },
       Offerings { #[command(subcommand)] cmd: OfferingsCommands },
       Services { #[command(subcommand)] cmd: ServicesCommands },
       // ...
   }
   ```
2. Update all handlers to call v1 API routes
3. Keep backward-compat shims for 1-2 releases (e.g., `garden-rake offer` → warn + delegate to `garden-rake offerings list`)
4. Update help text, examples, and error messages to reflect v1 patterns

**Deliverable:** CLI using scope-first pattern + v1 API routes

---

### Phase 3: Deprecation (v0 API Sunset)
**Goal:** Remove old API routes after 2-3 release cycles

**Timeline:**
- Release N: Dual-support (v0 + v1), CLI uses v1, docs updated
- Release N+1: Deprecation warnings in v0 API responses (HTTP header: `Deprecated: true`, body includes migration hint)
- Release N+2: Remove v0 routes, keep only v1

**Communication:**
- ADR documenting v0→v1 migration path
- Changelog entries in each release
- Migration guide with side-by-side command/route comparisons

---

### Phase 4: Phase 3 Integration (Garden Bootstrapping)
**Goal:** Add `place`/`invite` functionality to v1 API once Phase 3 design is finalized

**Deferred routes:**
- `POST /api/garden/bootstrap/pebble` (place pebble on stone)
- `POST /api/garden/bootstrap/stones` (invite stone to garden)
- `GET /api/garden/invitations/:code` (resolve invitation)

**CLI commands:**
- `garden-rake garden bootstrap pebble`
- `garden-rake garden bootstrap stone --code <invitation>`
- `garden-rake garden invitations create <stone_name>`

**Rationale:** Keep these out of v1 until Phase 3 architecture is real (avoid designing API for unimplemented features)

---

## Rationale & Philosophy

### Why Scope-First CLI?
**Problem:** Top-level verbs (`offer`, `remove`, `wake`, `rest`, `observe`, `watch`, `tend`) don't compose — each is a unique snowflake.

**Solution:** Scope-first pattern groups related operations:
- `garden-rake services <verb>` → All service lifecycle operations
- `garden-rake offerings <verb>` → All catalog operations
- `garden-rake garden <verb>` → All topology operations

**Benefit:** Bounded growth (new verbs go under existing scopes), predictable help structure (`garden-rake services --help` shows all service operations).

---

### Why Standard Verbs Over Zen Metaphors?
**Problem:** "rest/wake" are charming but opaque; new users don't know "offer" means "install".

**Solution:** Use standard verbs (`start`, `stop`, `create`, `delete`) for actions, keep zen metaphors for *nouns* (stone, offering, garden).

**Benefit:** Discoverability for new users, beauty preserved in the domain model (not the interface).

**Counterargument:** "But 'rest/wake' is core to Zen Garden's identity!"  
**Response:** Identity is in the *philosophy* (tending stones, offering services, cultivating a garden), not in CLI verb choice. We can document the zen metaphors in help text and docs while using predictable verbs for commands.

---

### Why RESTful API Over RPC-Style?
**Problem:** `POST /api/operations/offer/:offering` treats the API as a command bus (RPC), not a resource model (REST).

**Solution:** Model services as a resource collection (`/api/services`) with standard HTTP verbs:
- `POST /api/services` → Create resource
- `DELETE /api/services/:service` → Remove resource
- `POST /api/services/:service/start` → Perform action on resource

**Benefit:** HTTP semantics (idempotency, caching, status codes) work naturally; clients can use standard REST libraries.

---

### Why Consolidate Event Streams?
**Problem:** Three different event APIs (Moss `/api/events`, Moss `/api/services/:service/logs`, Lantern `/api/events/stream`) with different shapes.

**Solution:** Unified SSE endpoint (`/api/events`) with query filtering:
- `GET /api/events` → All events
- `GET /api/events?service=mongodb` → Service-specific
- `GET /api/events?type=logs` → Logs only

**Benefit:** Single observability contract; clients learn one SSE pattern.

---

### Why "Context" Instead of "Tending"?
**Problem:** "Tending" is a zen metaphor that implies care/maintenance, but the implementation is just a client-side cache for "which stone am I targeting."

**Solution:** Rename to "context" (standard term in CLI tools: `kubectl config`, `docker context`, `az account set`).

**Benefit:** Clearer intent (this is a client-side convenience), aligns with industry patterns.

**Preserve Zen:** Docs can still say "tend to your stones by setting a context" — metaphor lives in documentation, not command names.

---

## Success Criteria

1. **Predictability**: Users can infer 80% of commands/routes without consulting docs
2. **Consistency**: CLI mirrors API structure (same resource names, same hierarchy)
3. **Discoverability**: `garden-rake --help` shows bounded scopes, each scope has focused help
4. **Standards Compliance**: HTTP API follows REST conventions (GET/POST/PUT/DELETE, status codes, resource URLs)
5. **Migration Path**: Dual-support for 2 releases, then clean cutover (no breaking changes for users who update regularly)

---

## Open Questions

1. **Backward compat timeline**: Should we keep v0 routes for 2 releases or 3?  
   **Recommendation:** 2 releases (6 months) is sufficient given greenfield philosophy + early adopter audience.

2. **CLI alias support**: Should we add short aliases for common commands? (e.g., `garden-rake svc ls` for `garden-rake services list`)  
   **Recommendation:** Yes, but as opt-in (not documented in primary help). Precedent: `kubectl` has `k`, `docker` has `d`.

3. **OpenAPI spec generation**: Should we auto-generate OpenAPI from Axum routes or hand-write it?  
   **Recommendation:** Auto-generate (use `utoipa` or similar) to keep docs in sync with code.

4. **Client libraries**: Should we provide official Rust/Python/JavaScript clients for v1 API?  
   **Recommendation:** Deferred to Phase 2; focus on CLI + HTTP API first.

---

## Next Steps

1. **Review & approve** this proposal (stakeholder sign-off)
2. **Create ADR** (ARCH-00XX) documenting the v0→v1 transition
3. **Implement Phase 1** (API v1 routes in Moss + Lantern, dual-support)
4. **Implement Phase 2** (CLI v1 using scope-first pattern)
5. **Update documentation** (API reference, CLI guide, migration guide)
6. **Release N** with dual-support + deprecation warnings
7. **Release N+1** with v0 sunset

---

## Appendix: Full CLI Help Text (Proposed)

```
garden-rake 0.2.0
Zen Garden management CLI

USAGE:
    garden-rake [OPTIONS] <COMMAND>

COMMANDS:
    stone        Stone operations (info, metrics, reconcile)
    offerings    Offering catalog (list, search, info, refresh)
    services     Service lifecycle (list, create, delete, start, stop, logs, upgrade)
    jobs         Async job tracking (list, get)
    events       Event streams (stream)
    garden       Garden topology (list, info, events)
    context      Context management (show, set, clear)
    templates    Template utilities (list, show)
    system       System administration (refresh)
    help         Print this message or the help of the given subcommand(s)

OPTIONS:
    -h, --help       Print help
    -V, --version    Print version

ENVIRONMENT:
    GARDEN_STONE    Default stone endpoint (overrides context cache)
    RUST_LOG        Log level (trace, debug, info, warn, error)

EXAMPLES:
    # Set context to local stone
    garden-rake context set this
    
    # List available offerings
    garden-rake offerings list
    
    # Install mongodb service
    garden-rake services create mongodb
    
    # Stream logs from mongodb
    garden-rake services logs mongodb --follow
    
    # List all stones in garden
    garden-rake garden list
    
    # Override context for single command
    garden-rake services list --at stone-02
```

---

**End of Proposal**
