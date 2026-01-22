# CLI & API Surface Area Analysis

**Purpose**: Comprehensive mapping of Zen Garden functionality to establish the foundation for dual-ergonomics CLI design (Zen + Normative).

**Generated**: 2026-01-21
**Status**: Phase 1 - Surface Area Mapping

---

## Executive Summary

This document maps:
1. **Implemented API endpoints** (41 v1 endpoints)
2. **Implemented CLI commands** (20 commands)
3. **Planned functionality** (from proposals)
4. **Gaps and missing mappings**
5. **'Tend' scope binding implications**

The goal is to establish a complete picture before designing comprehensive dual-syntax ergonomics.

---

## 1. Functional Scope Groups

### 1.1 Service Lifecycle Management

**Domain**: Creating, starting, stopping, removing, and managing services/offerings.

| Functionality | API Endpoint | CLI Command (Zen) | CLI Command (Normative) | Tend Scope | Status |
|---------------|--------------|-------------------|-------------------------|------------|--------|
| List services | `GET /api/v1/services` | `list` | ❌ Missing | Stone-scoped | ✅ Implemented |
| Get service details | `GET /api/v1/services/:name` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |
| Create service | `POST /api/v1/services` | `offer <name>` | ❌ Missing | Stone-scoped | ✅ Implemented |
| Stop service | `POST /api/v1/services/:name:rest` | `rest <name>` | ❌ Missing | Stone-scoped | ✅ Implemented |
| Start service | `POST /api/v1/services/:name:wake` | `wake <name>` | ❌ Missing | Stone-scoped | ✅ Implemented |
| Restart service | `POST /api/v1/services/:name:restart` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |
| Update service | `POST /api/v1/services/:name:nourish` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |
| Delete service | `DELETE /api/v1/services/:name` | `remove <name>` | ❌ Missing | Stone-scoped | ✅ Implemented |
| Upgrade service | ❌ Missing | `upgrade <name>` | ❌ Missing | Stone-scoped | ✅ CLI only |
| Cordon service | `POST /api/v1/services/:name:cordon` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |
| Stream logs | `GET /api/v1/services/:name/logs` | `watch offering <name> logs` | ❌ Missing | Stone-scoped | ✅ Implemented |

**Tend Binding**: All service lifecycle operations are **stone-scoped** - they target the currently tended stone (or --at override).

---

### 1.2 Offering Discovery & Installation

**Domain**: Browsing catalog, checking compatibility, installing offerings.

| Functionality | API Endpoint | CLI Command (Zen) | CLI Command (Normative) | Tend Scope | Status |
|---------------|--------------|-------------------|-------------------------|------------|--------|
| List offerings | `GET /api/v1/offerings` | `offer` (no args) | ❌ Missing | Stone-scoped | ✅ Implemented |
| Get offering details | `GET /api/v1/offerings/:name` | `offer <name> info` | ❌ Missing | Stone-scoped | ✅ Implemented |
| Get offering manifest | `GET /api/v1/offerings/:name/manifest` | `template show <name>` | ❌ Missing | Stone-scoped | ✅ Implemented |
| Install offering (simplified) | `POST /api/v1/offerings` | ❌ Missing | ❌ Missing | Stone-scoped | 🔶 API stub |
| Uninstall offering | `DELETE /api/v1/offerings/:name` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ Implemented |
| Refresh catalog | `POST /api/v1/offerings:refresh` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |
| Heal garden | `POST /api/v1/offerings:heal` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |

**Tend Binding**: Offering operations are **stone-scoped** - catalog is per-stone, installations happen on tended stone.

**Note**: The offerings API is the "human layer" (simplified) while services API is the "power layer" (full control).

---

### 1.3 Service Adoption & External Services

**Domain**: Managing Adopted (existing containers) and Borrowed (external network services) offerings.

| Functionality | API Endpoint | CLI Command (Zen) | CLI Command (Normative) | Tend Scope | Status |
|---------------|--------------|-------------------|-------------------------|------------|--------|
| List adoptable containers | `GET /api/v1/adoption/adoptable` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |
| Adopt container | `POST /api/v1/adoption/adopt` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |
| List adopted services | `GET /api/v1/adoption/adopted` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |
| List borrowed services | `GET /api/v1/adoption/borrowed` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |
| Unadopt service | `DELETE /api/v1/adoption/adopted/:name` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |

**Tend Binding**: Adoption operations are **stone-scoped** - manage containers/services on tended stone.

**Gap**: Entire adoption/borrowed functionality has NO CLI exposure yet.

---

### 1.4 Garden Observation & Monitoring

**Domain**: Observing garden state, stone details, metrics, health.

| Functionality | API Endpoint | CLI Command (Zen) | CLI Command (Normative) | Tend Scope | Status |
|---------------|--------------|-------------------|-------------------------|------------|--------|
| Get garden overview | `GET /api/v1/garden` | `observe` (no args) | ❌ Missing | Garden-wide | ✅ Implemented |
| Get specific stone | `GET /api/v1/garden/stones/:name` | `observe <stone>` | ❌ Missing | Garden-wide | ✅ Implemented |
| Get local stone | `GET /api/v1/stone` | `status` | ❌ Missing | Stone-scoped | ✅ Implemented |
| Get stone capabilities | `GET /capabilities` | (implicit in status) | ❌ Missing | Stone-scoped | ✅ Implemented |
| Get stone metrics | `GET /metrics` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |
| Get health | `GET /health` | (implicit in status) | ❌ Missing | Stone-scoped | ✅ Implemented |
| Watch events | `GET /api/v1/events` | `watch` (no target) | ❌ Missing | Stone-scoped | ✅ Implemented |

**Tend Binding**:
- `observe` is **garden-wide** - can query multiple stones
- `status`, `watch`, `metrics` are **stone-scoped** - target tended stone

---

### 1.5 Manifests & Templates

**Domain**: Managing service manifests and offering templates.

| Functionality | API Endpoint | CLI Command (Zen) | CLI Command (Normative) | Tend Scope | Status |
|---------------|--------------|-------------------|-------------------------|------------|--------|
| List service manifests | `GET /api/v1/services/manifests` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |
| Get specific manifest | `GET /api/v1/services/manifests/:name` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |
| Refresh manifests | `POST /api/v1/services/manifests:refresh` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |
| List templates | ❌ Missing | `template list` | ❌ Missing | Stone-scoped | ✅ CLI only |
| Show template content | ❌ Missing | `template show <name>` | ❌ Missing | Stone-scoped | ✅ CLI only |

**Tend Binding**: All manifest/template operations are **stone-scoped**.

**Note**: Templates are stored on disk (per-stone), manifests are runtime representations.

---

### 1.6 Inventory Reconciliation

**Domain**: Syncing moss registry with actual container state.

| Functionality | API Endpoint | CLI Command (Zen) | CLI Command (Normative) | Tend Scope | Status |
|---------------|--------------|-------------------|-------------------------|------------|--------|
| Reconcile inventory | `POST /api/v1/services:reconcile` | `reconcile` | ❌ Missing | Stone-scoped | ✅ Implemented |
| Heal garden (zen) | `POST /api/v1/offerings:heal` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |

**Tend Binding**: Reconciliation is **stone-scoped** - syncs tended stone's registry.

**Note**: `reconcile` (normative) and `heal` (zen) are semantically equivalent operations.

---

### 1.7 Pond Security (Multi-Stone Trust)

**Domain**: Establishing trust between stones via pond security model.

| Functionality | API Endpoint | CLI Command (Zen) | CLI Command (Normative) | Tend Scope | Status |
|---------------|--------------|-------------------|-------------------------|------------|--------|
| Initialize pond | `POST /api/v1/pond/init` | `place keystone` | `pond init` | Stone-scoped | 🔶 Stub (Phase 3b) |
| Get pond status | `GET /api/v1/pond/status` | ❌ Missing | `pond status` | Stone-scoped | ✅ Implemented |
| Generate invitation | `POST /api/v1/pond/invite` | `invite` | `pond invite` | Stone-scoped | 🔶 Stub (Phase 3b) |
| Join pond | `POST /api/v1/pond/join` | `place stone --code <code>` | `pond join <code>` | Stone-scoped | 🔶 Stub (Phase 3b) |
| Remove pond | `DELETE /api/v1/pond` | ❌ Missing | `pond remove` | Stone-scoped | 🔶 Stub (Phase 3b) |
| Untrust stone | `DELETE /api/v1/pond/stones/:name` | `lift stone <name>` | `pond untrust <name>` | Stone-scoped | 🔶 Stub (Phase 3b) |

**Tend Binding**: Pond operations are **stone-scoped** but affect garden-wide trust relationships.

**Note**: This is Phase 3b functionality - API stubs exist, implementation pending.

---

### 1.8 Stone System Operations

**Domain**: Stone-level system operations (upgrade, shutdown, service installation).

| Functionality | API Endpoint | CLI Command (Zen) | CLI Command (Normative) | Tend Scope | Status |
|---------------|--------------|-------------------|-------------------------|------------|--------|
| Upgrade stone binaries | `POST /api/v1/stone:upgrade` | `refresh <component> --from <path>` | ❌ Missing | Stone-scoped | ✅ Implemented |
| Shutdown stone | `POST /api/v1/stone:shutdown` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |
| Install as service | `POST /admin/take-root` | `take-root` | `install-service` | Stone-scoped | ✅ Implemented |
| Admin shutdown | `POST /admin/shutdown` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |

**Tend Binding**: Stone operations are **stone-scoped** - affect tended stone only.

**Note**: Both zen (`take-root`) and normative (`install-service`) syntax exist for service installation.

---

### 1.9 Console & Output Control

**Domain**: Controlling stone console verbosity and output modes.

| Functionality | API Endpoint | CLI Command (Zen) | CLI Command (Normative) | Tend Scope | Status |
|---------------|--------------|-------------------|-------------------------|------------|--------|
| Set console mode | `POST /api/v1/console/mode` | `make stone sing/quiet/silent/minimal` | ❌ Missing | Stone-scoped | ✅ Implemented |
| Get console mode | `GET /api/v1/console/mode` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |

**Tend Binding**: Console operations are **stone-scoped**.

**Console Modes**:
- `silent` - No console output (systemd/service use)
- `minimal` - Critical events only
- `informative` - Major lifecycle events (default)
- `verbose` - Full debug output (sing mode)

---

### 1.10 Tending State Management

**Domain**: Managing which stone rake commands target (context management).

| Functionality | API Endpoint | CLI Command (Zen) | CLI Command (Normative) | Tend Scope | Status |
|---------------|--------------|-------------------|-------------------------|------------|--------|
| Show tending state | N/A (local cache) | `tend` (no args) | ❌ Missing | Local cache | ✅ Implemented |
| Set tending | N/A (local cache) | `tend <target>` | ❌ Missing | Local cache | ✅ Implemented |
| Clear tending | N/A (local cache) | `tend --clear` | ❌ Missing | Local cache | ✅ Implemented |

**Tend Binding**: This is the **mechanism** for scope binding itself.

**Tending Targets**:
- `this` / `local` - Localhost (http://localhost:7185)
- `auto` - Auto-discover via UDP broadcast
- `<stone-name>` - Named stone (resolved via cache or discovery)
- `<endpoint>` - Explicit endpoint URL

**Cache TTL**: 90 seconds

**Priority Chain**:
1. `--at` flag (explicit override, highest priority)
2. `GARDEN_STONE` env var
3. Tending cache (90s TTL)
4. Auto-discovery via UDP broadcast

---

### 1.11 Jobs & Background Tasks

**Domain**: Tracking background operations (future feature).

| Functionality | API Endpoint | CLI Command (Zen) | CLI Command (Normative) | Tend Scope | Status |
|---------------|--------------|-------------------|-------------------------|------------|--------|
| List jobs | `GET /api/v1/jobs` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |
| Get job status | `GET /api/v1/jobs/:id` | ❌ Missing | ❌ Missing | Stone-scoped | ✅ API only |

**Tend Binding**: Jobs are **stone-scoped**.

**Note**: Jobs API exists but not yet exposed via CLI.

---

## 2. Planned Functionality (From Proposals)

### 2.1 From cli-taxonomy.md (Ongoing - 60-70% implemented)

**Missing Zen Verbs** (proposed but not implemented):

| Proposed Verb | Semantic | Normative Equivalent | Scope | Status |
|---------------|----------|----------------------|-------|--------|
| `explore` | Scan network for stones | `stones discover` | Network-wide | ❌ Not implemented |
| `nourish` | Update service config/image | `services update` | Stone-scoped | ✅ API exists, no CLI |
| `release` | Export stone config | `config export` | Stone-scoped | ❌ Not implemented |
| `touch` | Health check / wake-up test | `health check` | Stone-scoped | ❌ Not implemented |
| `garden` | Multi-stone orchestration | `orchestrate` | Garden-wide | ❌ Not implemented |

**Dual Syntax** (cli-taxonomy proposal):
- **Proposed**: Full 1:1 mirroring between zen and normative
- **Current Reality**: Only zen syntax exists for most commands
- **Gap**: Normative equivalents missing for nearly all commands

---

### 2.2 From stone-profiles.md (Proposed - Not Implemented)

**Stone Profiles** - Hardware capability categories:

| Profile | Description | Detection | Status |
|---------|-------------|-----------|--------|
| Pebble | Minimal (phones, Raspberry Pi Zero) | Auto-detect | ❌ Proposed |
| River Stone | Standard (Pi 4, NUC) | Auto-detect | ❌ Proposed |
| Boulder | High-performance (workstations, GPUs) | Auto-detect | ❌ Proposed |

**Implications**:
- Would affect offering compatibility decisions
- Would enable profile-based filtering in `offer` command
- Would add profile field to stone metadata

---

### 2.3 From bridges.md (Proposed - Not Implemented)

**Bridges** - Cross-stone service discovery:

| Functionality | Description | CLI Syntax | Status |
|---------------|-------------|------------|--------|
| Bridge creation | Expose service across stones | `garden-rake bridge create <service> to <stone>` | ❌ Proposed |
| Bridge listing | List active bridges | `garden-rake bridge list` | ❌ Proposed |
| Bridge removal | Remove bridge | `garden-rake bridge remove <service>` | ❌ Proposed |

**Implications**: Would enable cross-stone service access without pond security.

---

### 2.4 From ceremonies.md (Proposed - Not Implemented)

**Ceremonies** - Multi-step guided workflows:

| Ceremony | Purpose | Status |
|----------|---------|--------|
| First Stone | Guided initial stone setup | ❌ Proposed |
| Keystone Placement | Guided pond initialization | ❌ Proposed |
| Stone Addition | Guided stone joining | ❌ Proposed |

**Implications**: Would add `ceremony` command group.

---

### 2.5 From firefly.md (Proposed - Not Implemented)

**Firefly** - Physical LED status indicators:

| Functionality | Purpose | Status |
|---------------|---------|--------|
| LED control API | Set stone LED color/pattern | ❌ Proposed |
| Status mapping | Map vitality → LED color | ❌ Proposed |

**Implications**: Would add hardware integration layer.

---

## 3. Major Gaps & Missing Mappings

### 3.1 API-Only Features (No CLI Exposure)

| Feature | API Endpoint | Business Value | Priority |
|---------|--------------|----------------|----------|
| Adoption management | `/api/v1/adoption/*` | High - manage existing containers | **High** |
| Service nourishment | `POST /api/v1/services/:name:nourish` | Medium - update configs | Medium |
| Service cordoning | `POST /api/v1/services/:name:cordon` | Low - advanced orchestration | Low |
| Manifest management | `GET /api/v1/services/manifests` | Medium - debugging | Medium |
| Jobs tracking | `GET /api/v1/jobs/*` | High - async operation visibility | **High** |
| Console mode get | `GET /api/v1/console/mode` | Low - mostly automated | Low |
| Catalog refresh | `POST /api/v1/offerings:refresh` | Medium - catalog management | Medium |
| Stone metrics | `GET /metrics` | Medium - monitoring | Medium |

**Recommendation**: Prioritize adoption management and jobs tracking for CLI exposure.

---

### 3.2 CLI-Only Features (No API Backend)

| Feature | CLI Command | Implementation | Gap Impact |
|---------|-------------|----------------|------------|
| Template listing | `template list` | Direct file system access | Medium - could benefit from API |
| Template showing | `template show <name>` | Direct file system access | Medium - could benefit from API |
| Browse commands | `commands` | Local reference data | Low - intentionally local |

**Recommendation**: Consider adding template API for consistency, but low priority.

---

### 3.3 Dual Syntax Gaps

**Current State**: Only 2 commands have dual syntax (zen + normative):
1. `take-root` ↔ `install-service`
2. `place keystone` ↔ `pond init`

**Missing Normative Equivalents**:
- ❌ `offer` → `services create` / `offerings install`
- ❌ `rest` → `services stop`
- ❌ `wake` → `services start`
- ❌ `observe` → `garden list` / `stones list`
- ❌ `watch` → `events stream` / `logs stream`
- ❌ `tend` → `context set`
- ❌ `reconcile` → `inventory sync`
- ❌ `make stone sing` → `console set-mode verbose`
- ❌ `lift stone` → `pond untrust`
- ❌ `invite` → `pond invite`

**Recommendation**: Design comprehensive normative syntax for ALL zen commands.

---

### 3.4 Scope Binding Ambiguities

**Current Behavior**:
- Most commands are **stone-scoped** (target tended stone or --at override)
- `observe` is **garden-wide** (but currently only shows local stone - multi-stone discovery is Phase 3)

**Ambiguous Cases**:
1. **Template operations**: Are templates per-stone or garden-wide?
   - Current: Per-stone (stored in `/opt/zen-garden/templates/`)
   - Future: Could be garden-wide catalog?

2. **Offering catalog**: Per-stone or garden-wide?
   - Current: Per-stone (each stone builds own index)
   - Future: Could be shared/replicated?

3. **Adoption scanning**: Local stone only or cross-stone?
   - Current: Stone-scoped (scans local Docker)
   - Future: Could scan remote stones?

**Recommendation**: Formalize scope rules explicitly in CLI design.

---

## 4. Tend Scope Binding Model

### 4.1 Scope Types

| Scope | Description | Resolution | Example Commands |
|-------|-------------|------------|------------------|
| **Stone-scoped** | Targets single stone | Via tending chain | `offer`, `rest`, `wake`, `list` |
| **Garden-wide** | Queries multiple stones | Discovery + aggregation | `observe`, `explore` (proposed) |
| **Local-only** | No network calls | Local state/cache | `tend`, `commands` |

### 4.2 Tending Resolution Chain

```
Command Execution
    ↓
1. Check --at flag?
    YES → Use explicit endpoint
    NO  ↓
2. Check GARDEN_STONE env?
    YES → Use env endpoint
    NO  ↓
3. Check tending cache (90s TTL)?
    YES → Use cached endpoint
    NO  ↓
4. Auto-discover via UDP broadcast
    SUCCESS → Cache result, use endpoint
    FAIL    → Error: No stone found
```

### 4.3 Scope Override Patterns

| Pattern | Syntax | Effect | Use Case |
|---------|--------|--------|----------|
| Explicit endpoint | `--at http://stone-01:7185` | Override tending | Script targeting specific stone |
| Named stone | `--at stone-01` | Resolve name → endpoint | Human-friendly targeting |
| Environment | `GARDEN_STONE=http://...` | Session-wide override | Docker/CI environments |
| Tending cache | `tend stone-01` | Set default for 90s | Interactive sessions |

---

## 5. API Versioning & Stability

### 5.1 Current API Structure

```
/api/v1/               ← Versioned, stable
  ├── garden           ← Garden observation
  ├── services         ← Service lifecycle (power layer)
  ├── offerings        ← Offering management (human layer)
  ├── adoption         ← Adoption/borrowed management
  ├── pond             ← Pond security
  ├── console          ← Console control
  ├── events           ← Event streaming
  ├── stone            ← Stone operations
  └── jobs             ← Jobs tracking

/health                ← Root level, stable
/metrics               ← Root level, stable
/capabilities          ← Root level, stable

/admin/                ← Legacy, should migrate to /api/v1/stone
  ├── shutdown
  └── take-root
```

### 5.2 API Design Patterns

**Custom Actions** (single colon):
- `POST /api/v1/services/:name:rest` - Stop service
- `POST /api/v1/services/:name:wake` - Start service
- `POST /api/v1/services/:name:nourish` - Update service
- `POST /api/v1/services/:name:restart` - Restart service
- `POST /api/v1/services/:name:cordon` - Cordon service
- `POST /api/v1/services:reconcile` - Reconcile all services
- `POST /api/v1/offerings:heal` - Heal garden
- `POST /api/v1/offerings:refresh` - Refresh catalog
- `POST /api/v1/stone:upgrade` - Upgrade stone
- `POST /api/v1/stone:shutdown` - Shutdown stone

**Standard REST**:
- `GET /api/v1/services` - List
- `POST /api/v1/services` - Create
- `GET /api/v1/services/:name` - Read
- `DELETE /api/v1/services/:name` - Delete

---

## 6. Key Insights for CLI Design

### 6.1 Semantic Consistency

**Zen Philosophy**:
- `offer` - giving/providing (installation)
- `rest` - peaceful stopping
- `wake` - gentle starting
- `observe` - mindful watching
- `watch` - active monitoring
- `tend` - caring attention
- `place` - careful positioning
- `invite` - welcoming
- `lift` - removing
- `make` - transformation

**Normative Clarity**:
- Should use standard industry terms
- Should be self-documenting
- Should follow CRUD/REST patterns where applicable

### 6.2 Positional vs Flag Syntax

**Current Patterns**:
- Zen: Prefers positional (`garden-rake offer mongodb at stone-01`)
- Normative: Uses flags (`garden-rake services create --name mongodb --at stone-01`)

**Recommendation**:
- Zen: Keep positional for human ergonomics
- Normative: Use flags for script clarity and IDE autocomplete

### 6.3 Verb Hierarchies

**Proposed Normative Structure**:
```
services/           ← Resource noun
  ├── create        ← CRUD verb
  ├── list
  ├── show
  ├── update
  ├── delete
  ├── start         ← Lifecycle verb
  ├── stop
  ├── restart
  └── logs          ← Observability verb

offerings/          ← Resource noun
  ├── list
  ├── show
  ├── install
  └── uninstall

stones/             ← Resource noun
  ├── list
  ├── show
  ├── discover
  └── upgrade

pond/               ← Security domain
  ├── init
  ├── status
  ├── invite
  ├── join
  ├── remove
  └── untrust

context/            ← Session management
  ├── show
  ├── set
  └── clear
```

---

## 7. Priority Recommendations

### Phase 1: Foundation (Immediate)
1. ✅ **Complete this surface area analysis** (DONE)
2. **Design normative syntax** for ALL implemented commands
3. **Design zen refinements** for consistency
4. **Establish dual-syntax conventions**
5. **Create CLI syntax proposal document**

### Phase 2: Gaps (Next)
1. **Expose adoption API** via CLI
2. **Expose jobs API** via CLI
3. **Add missing normative commands**
4. **Standardize help/documentation**

### Phase 3: Enhancement (Future)
1. **Implement missing zen verbs** (explore, touch, garden, etc.)
2. **Stone profiles integration**
3. **Bridges implementation**
4. **Ceremonies implementation**

---

## 8. Next Steps

**Immediate Action Items**:
1. Assemble specialist team (semiotics, semantics, devops, UX, CLI design)
2. Design comprehensive normative syntax
3. Refine zen syntax for consistency
4. Ensure 1:1 mirroring between zen and normative
5. Create detailed CLI syntax proposal
6. Prototype and validate with users

**Success Criteria**:
- ✅ Every API endpoint has CLI exposure (where appropriate)
- ✅ Every CLI command has both zen and normative syntax
- ✅ Scope binding is clear and documented
- ✅ Help system covers both syntaxes
- ✅ Scripts can use normative syntax exclusively
- ✅ Humans can use zen syntax exclusively
- ✅ Conversion between syntaxes is trivial

---

## Appendix A: Complete API Inventory

### A.1 API v1 Endpoints (41 total)

**Garden (3)**:
- `GET /api/v1/garden` - Get garden overview
- `GET /api/v1/garden/stones/:stone_name` - Get specific stone
- `GET /api/v1/stone` - Get local stone

**Services (13)**:
- `GET /api/v1/services` - List services
- `GET /api/v1/services/:name` - Get service details
- `POST /api/v1/services` - Create service
- `DELETE /api/v1/services/:name` - Delete service
- `POST /api/v1/services/:name:rest` - Stop service
- `POST /api/v1/services/:name:wake` - Start service
- `POST /api/v1/services/:name:restart` - Restart service
- `POST /api/v1/services/:name:nourish` - Update service
- `POST /api/v1/services/:name:cordon` - Cordon service
- `POST /api/v1/services:reconcile` - Reconcile inventory
- `GET /api/v1/services/manifests` - List manifests
- `GET /api/v1/services/manifests/:name` - Get manifest
- `POST /api/v1/services/manifests:refresh` - Refresh manifests
- `GET /api/v1/services/:name/logs` - Stream logs

**Offerings (7)**:
- `GET /api/v1/offerings` - List offerings
- `GET /api/v1/offerings/:name` - Get offering details
- `GET /api/v1/offerings/:name/manifest` - Get offering manifest
- `POST /api/v1/offerings` - Plant offering (stub)
- `DELETE /api/v1/offerings/:name` - Take away offering
- `POST /api/v1/offerings:heal` - Heal garden
- `POST /api/v1/offerings:refresh` - Refresh catalog

**Adoption (5)**:
- `GET /api/v1/adoption/adoptable` - List adoptable containers
- `POST /api/v1/adoption/adopt` - Adopt container
- `GET /api/v1/adoption/adopted` - List adopted services
- `GET /api/v1/adoption/borrowed` - List borrowed services
- `DELETE /api/v1/adoption/adopted/:name` - Unadopt service

**Pond (6)**:
- `POST /api/v1/pond/init` - Initialize pond (stub)
- `DELETE /api/v1/pond` - Remove pond (stub)
- `POST /api/v1/pond/invite` - Generate invitation (stub)
- `POST /api/v1/pond/join` - Join pond (stub)
- `DELETE /api/v1/pond/stones/:stone_name` - Untrust stone (stub)
- `GET /api/v1/pond/status` - Get pond status

**Console (2)**:
- `POST /api/v1/console/mode` - Set console mode
- `GET /api/v1/console/mode` - Get console mode

**Events (1)**:
- `GET /api/v1/events` - Stream events

**Stone (2)**:
- `POST /api/v1/stone:upgrade` - Upgrade stone
- `POST /api/v1/stone:shutdown` - Shutdown stone

**Jobs (2)**:
- `GET /api/v1/jobs` - List jobs
- `GET /api/v1/jobs/:id` - Get job status

### A.2 Root-Level Endpoints (3)

- `GET /health` - Health check
- `GET /metrics` - Prometheus metrics
- `GET /capabilities` - Hardware capabilities

### A.3 Legacy Admin Endpoints (2)

- `POST /admin/shutdown` - Shutdown (duplicates /api/v1/stone:shutdown)
- `POST /admin/take-root` - Install as service

---

## Appendix B: Complete CLI Inventory

### B.1 CLI Commands (20 total)

**Service Lifecycle**:
- `status` - Show stone status
- `offer [name] [info]` - List/install/inspect offerings
- `list` - List services
- `remove <service>` - Remove service
- `upgrade [service]` - Upgrade service(s)
- `rest <service>` - Stop service
- `wake <service>` - Start service
- `reconcile` - Reconcile inventory

**Observation**:
- `observe [stone]` - Observe garden state
- `watch [target]` - Watch real-time events/logs

**Templates**:
- `template list` - List templates
- `template show <name>` - Show template content

**Tending**:
- `tend [target]` - Manage tending state

**Pond Security**:
- `place <keystone|stone>` - Initialize/join pond
- `invite` - Generate invitation
- `lift <stone>` - Remove stone from pond
- `pond <action>` - Pond management (normative)

**Stone Operations**:
- `refresh <component>` - Upgrade moss/rake binary
- `take-root` - Install as service (zen)
- `install-service` - Install as service (normative)

**Console**:
- `make stone <sing|quiet|silent|minimal>` - Control console output

**Meta**:
- `commands [name]` - Browse command directory

---

**End of Surface Analysis**
