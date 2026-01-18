# Zen Garden CLI + API Taxonomy v1 (Zen-Preserving Redesign)

**Status:** Proposal (Revised)  
**Date:** 2026-01-17  
**Scope:** Complete redesign preserving Zen Garden metaphors and philosophy  
**Back-Compat:** Not required — greenfield approach for ideal DX/UX  

---

## Executive Summary

**The Problem with the Generic Proposal:**
The first proposal ("services create", "services stop") stripped away Zen Garden's identity. "garden-rake offer mongodb" is **beautiful and clear**—it reads like poetry, reinforces the philosophy (you're offering a gift to your stone), and has perfect semantic intent.

**The Real Problem:**
- **15 top-level commands** with no organizing principle (status/offer/list/remove/upgrade/rest/wake/place/invite/observe/watch/refresh/reconcile/template/tend)
- **Mixed abstraction levels**: "offer" (lifecycle), "status" (inspection), "tend" (context), "reconcile" (admin)
- **API sprawl**: 30 Moss routes split across overlapping namespaces

**This Proposal:**
Preserve zen verbs and metaphors, but **organize by domain** to bound growth:

1. **Relationship verbs** (tend, explore, observe, watch) — your connection to the garden
2. **Cultivation verbs** (offer, rest, wake, nourish, release) — caring for services
3. **Garden verbs** (garden) — multi-stone operations
4. **System verbs** (reconcile, refresh) — admin/dev operations

**Result:** ~11 top-level commands (down from 15), **zen metaphors intact**, predictable growth pattern.

---

## Zen Garden Philosophy (Preserved)

### Core Metaphors

| **Zen Concept**  | **Meaning**                                          | **Usage**                          |
|------------------|------------------------------------------------------|------------------------------------|
| **Stone**        | A machine/host — solid, foundational                | "Tend to stone-01"                 |
| **Moss**         | Daemon that grows on stones, nurtures services       | Runs on each stone                 |
| **Lantern**      | Provides light to discover stones in the garden      | Registry for stone discovery       |
| **Offering**     | A gift/service you offer to a stone                  | "Offer mongodb to this stone"      |
| **Tend**         | To care for, focus attention on                      | "Tend to stone-02" (set context)   |
| **Rest**         | Natural pause, let something rest                    | "Let mongodb rest" (stop)          |
| **Wake**         | Gently awaken from rest                              | "Wake mongodb" (start)             |
| **Nourish**      | Feed, upgrade, improve                               | "Nourish mongodb" (upgrade)        |
| **Release**      | Let go, return to the earth                          | "Release mongodb" (remove)         |
| **Observe**      | Mindful watching, seeing state                       | "Observe your stone's services"    |
| **Watch**        | Continuous observation, real-time awareness          | "Watch mongodb's logs"             |
| **Explore**      | Discover what's available, wander the catalog        | "Explore offerings"                |
| **Garden**       | The collection of all stones, the ecosystem          | "View the garden" (topology)       |

### Why These Metaphors Matter

**"garden-rake offer mongodb"** is not just a command—it's a statement of intent:
- You're **offering** (giving a gift), not "deploying" (military), not "installing" (mechanical)
- The stone **receives** your offering and nurtures it
- Services **rest** and **wake** (natural cycles), not "stop/start" (power switches)
- You **tend** to stones (care, attention), not "target" (combat), not "select" (transactional)

**The zen metaphors encode the system's philosophy:** infrastructure as living ecosystem, not industrial machinery.

---

## Proposed CLI v1 (Zen-Preserving)

### Design Principles

1. **Zen verbs for all user-facing commands** (offer, rest, wake, tend, observe, explore, nourish, release, watch, garden)
2. **Organize by domain** (relationship, cultivation, observation, garden, system) to bound growth
3. **Natural language patterns**: "I explore offerings, offer mongodb, observe my stone, watch mongodb's logs"
4. **Preserve the poetry**: Every command should reinforce the zen metaphor

---

### Command Structure (Organized by Domain)

#### **Relationship Verbs** — Your connection to stones

```bash
garden-rake tend [stone]           # Tend to a stone (set context)
                                   # Examples:
                                   #   garden-rake tend                  → Show what you're tending
                                   #   garden-rake tend stone-02         → Tend to stone-02
                                   #   garden-rake tend this             → Tend to localhost
                                   #   garden-rake tend auto             → Auto-discover and tend

garden-rake untend                 # Stop tending (clear context)
```

**Philosophy:** "Tending" is your relationship with a stone. When you tend to a stone, your rake commands flow to it naturally.

---

#### **Discovery Verbs** — Exploring the catalog

```bash
garden-rake explore [query]        # Explore offerings
                                   # Examples:
                                   #   garden-rake explore               → Browse all offerings by category
                                   #   garden-rake explore database      → Search for database offerings
                                   #   garden-rake explore vector ssd    → Search with preferences
                                   #   garden-rake explore --anywhere    → Explore across all stones

garden-rake explore <name> --inspect  # Inspect a specific offering
                                      # Example: garden-rake explore mongodb --inspect
```

**Philosophy:** "Explore" captures the discovery process—wandering through the catalog, finding what resonates. More poetic than "list" or "search".

**Flags:**
- `--prefer <ssd|nvme|hdd>` — Express preference (non-blocking)
- `--anywhere` — Explore offerings across all discovered stones
- `--inspect` — Deep inspection of specific offering (compatibility, ports, volumes)

---

#### **Cultivation Verbs** — Caring for services

```bash
garden-rake offer <name>           # Offer a service to your stone
                                   # Examples:
                                   #   garden-rake offer mongodb         → Offer mongodb
                                   #   garden-rake offer mongodb --anywhere-on-fail  → Auto-recommend on failure

garden-rake rest <name>            # Let service rest (stop gracefully)
                                   # Example: garden-rake rest mongodb

garden-rake wake <name>            # Wake service (start)
                                   # Example: garden-rake wake mongodb

garden-rake nourish <name>         # Nourish service (upgrade to latest)
                                   # Examples:
                                   #   garden-rake nourish mongodb       → Upgrade mongodb
                                   #   garden-rake nourish --all         → Nourish all services

garden-rake release <name>         # Release service (remove)
                                   # Example: garden-rake release mongodb
```

**Philosophy:**
- **Offer** — You're giving a gift to your stone
- **Rest** — Natural pause, gentle stop (not harsh "kill")
- **Wake** — Gentle awakening (not "boot", "run")
- **Nourish** — Feed and improve (not "upgrade", "patch")
- **Release** — Let go, return to the earth (not "delete", "destroy")

**Flags:**
- `--anywhere-on-fail` — If offering fails due to compatibility, auto-recommend alternatives across garden
- `--all` — Nourish all services (with nourish command)
- `--at <stone>` — Override context (explicit stone for single command)

---

#### **Observation Verbs** — Seeing state and events

```bash
garden-rake observe [target]       # Observe services or stones
                                   # Examples:
                                   #   garden-rake observe               → Observe services on tended stone
                                   #   garden-rake observe mongodb       → Observe specific service
                                   #   garden-rake observe stone-02      → Observe services on stone-02 (from garden)

garden-rake watch [target]         # Watch events in real-time
                                   # Examples:
                                   #   garden-rake watch                 → Watch all events
                                   #   garden-rake watch mongodb         → Watch mongodb logs
                                   #   garden-rake watch --until 'ready' → Watch until string appears
```

**Philosophy:**
- **Observe** — Mindful inspection, seeing the current state (snapshot)
- **Watch** — Continuous observation, flowing awareness (stream)

**Flags (watch):**
- `--until <string>` — Watch until string appears in stream
- `--follow` — Follow logs continuously (default for watch)
- `--tail <n>` — Show last N lines before streaming

---

#### **Garden Verbs** — Multi-stone operations

```bash
garden-rake garden                 # View garden topology
                                   # Shows all stones, their health, offerings

garden-rake garden watch           # Watch garden-wide events
                                   # Stream events from all stones (Lantern-backed)

garden-rake garden observe <stone> # Observe specific stone from garden view
```

**Philosophy:** The **garden** is your ecosystem—all stones together. Garden commands give you the bird's-eye view.

---

#### **System Verbs** — Admin and development operations

```bash
garden-rake reconcile              # Reconcile containers with moss registry
                                   # Examples:
                                   #   garden-rake reconcile             → Adopt missing containers
                                   #   garden-rake reconcile --drop-invalid  → Also remove invalid containers

garden-rake refresh <component>    # Refresh system components (dev use)
                                   # Examples:
                                   #   garden-rake refresh moss --from ./target/release/garden-moss
                                   #   garden-rake refresh rake --from ./dist/linux-x64/garden-rake

garden-rake refresh offerings      # Refresh offerings index
                                   # Rebuilds compatibility/catalog cache
```

**Philosophy:** System commands are functional, not poetic—they're admin/dev utilities for maintaining the garden's infrastructure.

---

### Removed Commands (Deferred to Future)

```bash
# Phase 3 garden bootstrapping (deferred):
garden-rake place <target>         # Place pebble or invite stone (future)
garden-rake invite <stone>         # Generate invitation code (future)

# Replaced by other commands:
garden-rake status                 # → garden-rake observe
garden-rake list                   # → garden-rake observe
garden-rake remove                 # → garden-rake release
garden-rake upgrade                # → garden-rake nourish
garden-rake template list          # → system/dev utility (not primary workflow)
garden-rake template show          # → system/dev utility (not primary workflow)
```

**Result:** 11 core commands (tend, untend, explore, offer, rest, wake, nourish, release, observe, watch, garden, reconcile, refresh)

---

## API Design Philosophy: Progressive Disclosure Through Dual Layers

### The Core Insight

Zen Garden serves **two distinct audiences** with **different mental models**:

1. **Beginners & scripters (90%)** — Want simple, safe, human-friendly operations ("I want MongoDB")
2. **Operators & troubleshooters (10%)** — Need technical access to container internals for debugging

**Rather than compromise**, we provide **two API layers** that access the same underlying resources but with different presentations:

### Offerings API (Human Layer - 90% Use Case)
- **Purpose:** Hide Docker complexity, present clean abstractions
- **Target:** Beginners, scripts, CI/CD pipelines, simple automation
- **Returns:** High-level state (`available`, `installed`, `starting`), friendly descriptions
- **Safety:** Hard to break things accidentally (no raw container flags)
- **Mental model:** "I'm managing offerings in my garden"

**Example response:**
```json
{
  "name": "mongodb",
  "state": "installed",
  "description": "MongoDB NoSQL database",
  "health": "healthy"
}
```

### Services API (Technical Layer - 10% Use Case)
- **Purpose:** Expose container reality for troubleshooting and advanced operations
- **Target:** Operators, DevOps, debugging scenarios, escape hatches
- **Returns:** Docker-level details (container IDs, port bindings, volume mounts, health checks)
- **Power:** Full control, can inspect/manipulate underlying infrastructure
- **Mental model:** "I'm managing containerized services"

**Example response:**
```json
{
  "name": "mongodb",
  "container_id": "a1b2c3d4e5f6",
  "state": "running",
  "ports": [{"host": 27017, "container": 27017, "protocol": "tcp"}],
  "volumes": [{"host": "/var/lib/zen-garden/mongodb", "container": "/data/db"}],
  "health_check": {"status": "healthy", "last_check": "2026-01-17T10:30:00Z"}
}
```

### Why This Works

**Not duplication** — Same underlying data, **different views**:
- Offerings API simplifies and abstracts
- Services API exposes and details

**Progressive disclosure**:
- Quick start guides use Offerings API only
- Most users never touch Services API
- Troubleshooting guides introduce Services API when needed
- Operators can go deep without cluttering beginner experience

**Documentation strategy**:
- Offerings API → "Getting Started", "How-to Guides"
- Services API → "Troubleshooting", "Operations Manual"

---

## Moss HTTP API v1 (Dual-Layer Architecture)

**Full design documentation:** See [`API-V1-DUAL-LAYER-DESIGN.md`](../API-V1-DUAL-LAYER-DESIGN.md)

### Summary

Zen Garden v1 API provides **two layers** for progressive disclosure:

1. **Offerings API** (`/api/v1/offerings`) — Human-friendly, simplified, safe (90% of users)
2. **Services API** (`/api/v1/services`) — Technical, container-level details, full control (10% power users)

**Key endpoints:**

```http
# Offerings API (Human Layer)
GET  /api/v1/offerings                     # Browse catalog
POST /api/v1/offerings                     # Plant offering (simplified)
GET  /api/v1/offerings/{name}              # Offering details + health
POST /api/v1/offerings:heal                # Heal garden (zen term)
POST /api/v1/offerings:refresh             # Refresh catalog

# Services API (Technical Layer)
GET  /api/v1/services/manifests            # List all manifests
GET  /api/v1/services                      # Container-level details
GET  /api/v1/services/{name}               # Full technical view
GET  /api/v1/services/{name}/logs          # Stream logs (SSE)
POST /api/v1/services                      # Install (full Docker control)
POST /api/v1/services:reconcile            # Reconcile inventory (normative term)
POST /api/v1/services:refresh              # Refresh manifests

# Stone Operations (Universal)
GET  /health                               # Prometheus health check
GET  /capabilities                         # Hardware capabilities
GET  /metrics                              # Prometheus metrics
POST /api/v1/stone:upgrade                 # Upgrade stone software
POST /api/v1/stone:shutdown                # Shutdown daemon

# Events & Jobs
GET  /api/v1/events                        # Stream events (SSE)
GET  /api/v1/jobs/{id}                     # Job status
```

### API Layer Comparison

| Aspect | Offerings API | Services API |
|--------|---------------|--------------|
| **Audience** | Beginners, scripters | Operators, troubleshooters |
| **State names** | `available`, `installed` | `running`, `exited` |
| **Configuration** | Simplified | Full Docker control |
| **Health** | `healthy`, `degraded` | Detailed health checks |
| **Responses** | Human-readable | Container IDs, technical details |
| **Operations** | `:heal`, `:refresh` | `:reconcile`, `:restart`, `:cordon` |

**Philosophy:** Same data, different presentations. Progressive disclosure allows beginners to start simple while experts can access full power when needed.

---

### CLI to API Mapping

**Zen commands → Offerings API:**
```bash
garden-rake explore              # GET /api/v1/offerings
garden-rake offer mongodb        # POST /api/v1/offerings
garden-rake observe              # GET /api/v1/offerings?state=installed
{
  "offering": "mongodb",
  "config": {
    "environment": {"MONGO_INITDB_ROOT_USERNAME": "admin"},
    "volumes": [{"host": "/data/mongodb", "container": "/data/db"}]
  }
}

// Response (201 Created)
{
  "service": "mongodb",
  "job_id": "job_abc123",
  "status": "creating"
}
```

**POST /api/services/:service/rest:**
```json
// Request: empty body or {}
// Response (200 OK)
{
  "service": "mongodb",
  "status": "resting",
  "message": "Service gracefully stopped"
}
```

**POST /api/services/:service/wake:**
```json
// Request: empty body or {}
// Response (200 OK)
{
  "service": "mongodb",
  "status": "awake",
  "message": "Service started successfully"
}
```

**POST /api/services/:service/nourish:**
```json
// Request: empty body or {}
// Response (202 Accepted)
{
  "service": "mongodb",
  "job_id": "job_xyz789",
  "status": "nourishing",
  "message": "Upgrade initiated"
}
```

---

### Jobs (Async Operations)

```http
GET /api/jobs                      → List recent jobs
GET /api/jobs/:job_id              → Job status/result
```

**No changes** (jobs are functional, not poetic)

---

### Events (Observation Streams)

```http
GET /api/events                    → Stream all events (SSE)
                                     Query params: ?service=:name&type=logs|lifecycle|error
```

**Consolidates:** Service logs + lifecycle events + system events into unified stream

**SSE event shapes:**
```
event: service.offered
data: {"service": "mongodb", "offering": "mongodb", "timestamp": "..."}

event: service.resting
data: {"service": "mongodb", "timestamp": "..."}

event: service.awake
data: {"service": "mongodb", "timestamp": "..."}

event: service.log
data: {"service": "mongodb", "line": "MongoDB starting...", "timestamp": "..."}

event: service.released
data: {"service": "mongodb", "timestamp": "..."}
```

**Zen vocabulary in event types:** `service.offered`, `service.resting`, `service.awake`, `service.nourishing`, `service.released`

---

### Peer Discovery

```http
GET /api/peers                     → Discover peer stones (UDP broadcast results)
```

**Response shape:**
```json
{
  "peers": [
    {
      "stone_name": "stone-02",
      "endpoint": "http://192.168.1.102:7185",
      "discovered_at": "2026-01-17T10:30:00Z"
    }
  ]
}
```

---

### System Administration

```http
POST /api/system/reconcile         → Reconcile containers with registry
POST /api/system/refresh           → Refresh moss/rake binaries (dev)
GET  /api/system/templates/:name/sources  → Debug template resolution (dev)
PUT  /api/system/templates/:name/compatibility → Upload compatibility rules (dev)
```

**No changes** (system operations are functional)

---

## Lantern HTTP API v1 (Garden-Centric)

### Garden Topology

```http
GET  /api/garden                   → Garden overview (stone count, health)
GET  /api/garden/stones            → List all stones in garden
POST /api/garden/stones            → Register stone (heartbeat)
GET  /api/garden/stones/:stone     → Stone details from registry
GET  /api/garden/events            → Garden-wide event stream (SSE)
```

**Zen-aligned changes:**
- `/api/topology` → `/api/garden` (garden is the zen metaphor for topology)
- `/api/stones` → `/api/garden/stones` (nested under garden)
- `/api/events/stream` → `/api/garden/events` (consistency)

**Response shape (GET /api/garden):**
```json
{
  "garden": {
    "stone_count": 3,
    "healthy_stones": 3,
    "total_services": 12,
    "last_updated": "2026-01-17T10:30:00Z"
  },
  "stones": [
    {
      "stone_name": "stone-01",
      "endpoint": "http://192.168.1.101:7185",
      "health": "healthy",
      "services_count": 4,
      "last_heartbeat": "2026-01-17T10:29:55Z"
    }
  ]
}
```

---

### Service Resolution

```http
GET /api/resolve?service=:name     → Resolve service to stone endpoint
```

**No changes** (resolution is a query, not a resource)

---

### Lantern Health

```http
GET /api/lantern                   → Lantern health + election state
```

**Zen-aligned change:** `/health` → `/api/lantern` (matches moss pattern of `/api/stone` for identity)

---

## CLI ↔ API Mapping Matrix (Zen Edition)

| **User Intent**                    | **CLI v1 (Zen)**                     | **HTTP API v1**                          | **Was (CLI)**                  | **Was (HTTP)**                        |
|------------------------------------|--------------------------------------|------------------------------------------|--------------------------------|---------------------------------------|
| Explore offerings                  | `garden-rake explore`                | `GET /api/offerings`                     | `garden-rake offer`            | `GET /api/offerings`                  |
| Search for offerings               | `garden-rake explore database`       | `GET /api/offerings` (client-side)       | `garden-rake offer database`   | Client-side ranking                   |
| Inspect offering                   | `garden-rake explore mongodb --inspect` | `GET /api/offerings/mongodb`          | `garden-rake offer mongodb info` | `GET /api/offerings/mongodb`        |
| Offer service to stone             | `garden-rake offer mongodb`          | `POST /api/services`                     | `garden-rake offer mongodb`    | `POST /api/operations/offer/mongodb`  |
| Observe services                   | `garden-rake observe`                | `GET /api/services`                      | `garden-rake list`             | `GET /api/services`                   |
| Observe specific service           | `garden-rake observe mongodb`        | `GET /api/services/mongodb`              | (new)                          | `GET /api/services/mongodb`           |
| Let service rest                   | `garden-rake rest mongodb`           | `POST /api/services/mongodb/rest`        | `garden-rake rest mongodb`     | `POST /api/operations/rest/mongodb`   |
| Wake service                       | `garden-rake wake mongodb`           | `POST /api/services/mongodb/wake`        | `garden-rake wake mongodb`     | `POST /api/operations/wake/mongodb`   |
| Nourish service                    | `garden-rake nourish mongodb`        | `POST /api/services/mongodb/nourish`     | `garden-rake upgrade mongodb`  | `POST /api/operations/upgrade/mongodb`|
| Release service                    | `garden-rake release mongodb`        | `DELETE /api/services/mongodb`           | `garden-rake remove mongodb`   | `POST /api/operations/remove/mongodb` |
| Watch service logs                 | `garden-rake watch mongodb`          | `GET /api/services/mongodb/logs`         | `garden-rake watch offering mongodb logs` | `GET /api/services/mongodb/logs` |
| Watch all events                   | `garden-rake watch`                  | `GET /api/events`                        | `garden-rake watch`            | `GET /api/events`                     |
| View garden topology               | `garden-rake garden`                 | `GET /api/garden`                        | `garden-rake observe`          | Lantern: `GET /api/topology`          |
| Observe stone from garden          | `garden-rake garden observe stone-02`| `GET /api/garden/stones/stone-02`        | `garden-rake observe stone-02` | Client-side filter                    |
| Tend to stone                      | `garden-rake tend stone-02`          | N/A (client-side cache)                  | `garden-rake tend stone-02`    | N/A                                   |
| Reconcile containers               | `garden-rake reconcile`              | `POST /api/system/reconcile`             | `garden-rake reconcile`        | `POST /api/system/reconcile`          |
| Refresh offerings index            | `garden-rake refresh offerings`      | `POST /api/offerings/_refresh`           | (hidden)                       | `POST /api/offerings/refresh`         |

---

## Zen Vocabulary Summary

### Preserved Metaphors (Core Identity)

| **Zen Verb**     | **Standard Equivalent** | **Why Zen is Better**                                           |
|------------------|-------------------------|-----------------------------------------------------------------|
| **offer**        | install, deploy         | "Offering a gift" vs "deploying a resource" — care vs transaction |
| **rest**         | stop                    | "Let it rest" vs "stop it" — natural pause vs harsh command     |
| **wake**         | start                   | "Wake gently" vs "start" — awakening vs power-on                |
| **nourish**      | upgrade                 | "Nourish and improve" vs "upgrade" — care vs maintenance        |
| **release**      | delete, remove          | "Release back to earth" vs "delete" — letting go vs destruction |
| **tend**         | target, select          | "Tend with care" vs "target" — relationship vs transaction      |
| **observe**      | list, status            | "Mindfully observe" vs "list" — awareness vs inventory          |
| **watch**        | stream, tail            | "Continuously watch" vs "tail logs" — meditation vs monitoring  |
| **explore**      | search, browse          | "Explore and discover" vs "search query" — journey vs lookup    |
| **garden**       | cluster, topology       | "Cultivate a garden" vs "manage cluster" — ecosystem vs infrastructure |

---

## Benefits of Zen-Preserving Design

### 1. **Identity Preservation**
"garden-rake offer mongodb" immediately communicates:
- **What** you're doing (offering a service)
- **Why** it matters (you're giving a gift to your stone)
- **How** to think about it (cultivation, not deployment)

### 2. **Natural Language Patterns**
Commands read like sentences:
- "I **explore** offerings, **offer** mongodb, **observe** my stone, **watch** mongodb, then **release** it"
- "I **tend** to stone-02, **nourish** all services, **watch** the garden"

### 3. **Memorable & Distinctive**
"rest/wake" is more memorable than "stop/start" because it's unexpected—the surprise reinforces learning.

### 4. **Philosophy Reinforcement**
Every command reminds you: this is a **garden** you're **cultivating**, not infrastructure you're managing.

### 5. **Bounded Growth**
11 core commands (vs 15) with clear domains:
- Relationship: tend, untend
- Discovery: explore
- Cultivation: offer, rest, wake, nourish, release
- Observation: observe, watch
- Garden: garden
- System: reconcile, refresh

New features map to existing verbs (e.g., "garden watch" for multi-stone events) instead of adding top-level commands.

---

## Migration Path (Greenfield)

### Phase 1: API v1 (Moss + Lantern)
**Goal:** Implement zen-aligned HTTP routes alongside existing routes

**Moss changes:**
1. Add zen sub-resource actions: `POST /api/services/:service/rest`, `/wake`, `/nourish`
2. Keep existing routes for back-compat: `/api/operations/*`
3. Update event types: `service.offered`, `service.resting`, `service.awake`, `service.nourishing`, `service.released`
4. Consolidate: `GET /api/stone` (combines `/health` + `/capabilities` + `/metrics`)

**Lantern changes:**
1. Add: `GET /api/garden`, `GET /api/garden/stones`, `POST /api/garden/stones`, `GET /api/lantern`
2. Keep existing routes: `/api/topology`, `/api/stones`, `/api/register`

**Deliverable:** Dual-support API (v0 + v1 routes coexist)

---

### Phase 2: CLI v1 (garden-rake Zen Edition)
**Goal:** Rewrite CLI to use zen verbs and new API routes

**Implementation:**
1. Organize commands by domain:
   ```rust
   #[derive(Subcommand)]
   enum Commands {
       Tend { target: Option<String>, ... },
       Untend,
       Explore { query: Option<String>, ... },
       Offer { name: String, ... },
       Rest { name: String, ... },
       Wake { name: String, ... },
       Nourish { name: String, ... },
       Release { name: String, ... },
       Observe { target: Option<String>, ... },
       Watch { target: Option<String>, ... },
       Garden { #[command(subcommand)] cmd: GardenCommands },
       Reconcile { ... },
       Refresh { ... },
   }
   ```
2. Update all handlers to call v1 API routes (zen sub-resource actions)
3. Update help text to explain zen metaphors:
   ```
   garden-rake offer <name>
       Offer a service to your stone
       
       This installs a new service from the offerings catalog. Think of it
       as presenting a gift to your stone—the moss daemon will receive your
       offering and nurture it into a running service.
       
       Examples:
         garden-rake offer mongodb
         garden-rake offer mongodb --anywhere-on-fail
   ```

**Deliverable:** CLI using zen verbs + v1 API routes

---

### Phase 3: Deprecation (v0 API Sunset)
**Timeline:**
- Release N: Dual-support, CLI uses v1, docs show zen metaphors
- Release N+1: Deprecation warnings in v0 routes
- Release N+2: Remove v0 routes

---

## Open Questions

1. **Should we add zen aliases for power users?**
   - Proposal: `gr` alias for `garden-rake` (like `kubectl` → `k`)
   - Example: `gr offer mongodb`, `gr rest mongodb`
   
2. **Should error messages use zen language?**
   - Current: "Service not found"
   - Zen: "No such offering has been given to this stone"
   - **Recommendation:** Use clear, functional language in errors (don't sacrifice clarity for metaphor)

3. **Should API event types use past tense or present?**
   - Option A: `service.offered`, `service.resting` (present/continuous)
   - Option B: `service.was_offered`, `service.is_resting` (explicit state)
   - **Recommendation:** Present tense for actions (`service.resting`), past tense for completed events (`service.offered`)

4. **HTTP status codes for zen actions?**
   - `POST /api/services/:service/rest` → 200 OK or 202 Accepted?
   - **Recommendation:** 200 OK for synchronous actions (rest/wake are fast), 202 Accepted for async (offer/nourish)

---

## Appendix: Full CLI Help Text (Zen Edition)

```
garden-rake 0.2.0
Tend to your Zen Garden — offer services, observe stones, cultivate your ecosystem

USAGE:
    garden-rake [OPTIONS] <COMMAND>

COMMANDS:
    tend         Tend to a stone (set your working context)
    untend       Stop tending (clear context)
    explore      Explore the offerings catalog
    offer        Offer a service to your stone
    rest         Let a service rest (stop gracefully)
    wake         Wake a service (start)
    nourish      Nourish a service (upgrade to latest)
    release      Release a service (remove)
    observe      Observe services or stones (see current state)
    watch        Watch events in real-time (logs, lifecycle)
    garden       View garden topology and events
    reconcile    Reconcile containers with moss registry
    refresh      Refresh system components or offerings index
    help         Print this message or the help of the given subcommand(s)

OPTIONS:
    -h, --help       Print help
    -V, --version    Print version

ENVIRONMENT:
    GARDEN_STONE    Default stone endpoint (overrides tending cache)
    RUST_LOG        Log level (trace, debug, info, warn, error)

EXAMPLES:
    # Tend to your local stone
    garden-rake tend this
    
    # Explore what offerings are available
    garden-rake explore
    
    # Search for database offerings
    garden-rake explore database
    
    # Offer mongodb to your stone
    garden-rake offer mongodb
    
    # Observe your stone's services
    garden-rake observe
    
    # Watch mongodb's logs
    garden-rake watch mongodb
    
    # Let mongodb rest
    garden-rake rest mongodb
    
    # Wake mongodb
    garden-rake wake mongodb
    
    # Nourish all services
    garden-rake nourish --all
    
    # View your garden
    garden-rake garden
    
    # Tend to a remote stone for one command
    garden-rake observe --at stone-02

PHILOSOPHY:
    Zen Garden treats infrastructure as a living ecosystem. You tend to stones,
    offer services as gifts, let them rest and wake naturally, and observe your
    garden with mindful awareness. Every command reinforces this philosophy.

    Learn more: https://github.com/your-org/zen-garden/docs/philosophy.md
```

---

**End of Zen-Preserving Proposal**
