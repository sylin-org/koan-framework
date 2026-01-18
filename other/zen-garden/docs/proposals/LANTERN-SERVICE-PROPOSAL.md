# Lantern Service - Architecture Proposal

**Status:** DRAFT - Seeking Feedback  
**Date:** 2026-01-16  
**Author:** Architecture Team  
**Reviewers:** Engineering Team

---

## Executive Summary

**Lantern** is a proposed centralized service registry and observability dashboard for Zen Garden that transforms peer-to-peer stone discovery into a reliable, cross-subnet topology management system.

**Key Value:**
- Replaces unreliable UDP broadcasts with HTTP-based registration
- Provides single source of truth for garden topology
- Enables cross-subnet/VLAN stone discovery (critical for Windows clients)
- Offers web dashboard for real-time garden visualization
- Non-blocking event stream for operational awareness

**Decision Required:** Approve Phase 1 implementation (Core Registry MVP)

---

## Problem Statement

### Current Limitations (P2P Mode)

1. **Network Topology**
   - UDP broadcasts limited to same subnet
   - Windows clients can't discover Linux stones across VLANs
   - No discovery in complex enterprise networks (multiple subnets, firewalls)

2. **State Management**
   - No persistent topology (ephemeral UDP responses)
   - Race conditions on concurrent discovery
   - No failure detection mechanism
   - Can't answer "what stones are in the garden?" without active discovery

3. **Operational Visibility**
   - No centralized view of garden state
   - No historical metrics or event audit trail
   - Debugging requires SSH to individual stones
   - No awareness of which operator performed which action

### Impact

- **Windows operators** rely on unreliable UDP discovery (frequently fails)
- **Multi-subnet deployments** require manual endpoint configuration (`--at` flag on every command)
- **Troubleshooting** is reactive (no proactive health monitoring)
- **Collaboration** is blind (multiple operators can't see each other's actions)

---

## Proposed Solution

### Architecture: Hub-and-Spoke Registry

**Lantern** acts as a **control plane directory service** (not a data plane gateway):

```
┌──────────────────────────────────────────────────────┐
│ DISCOVERY (Lantern as Directory)                     │
└──────────────────────────────────────────────────────┘

garden-rake locate mongodb
    ↓
GET http://lantern:7186/api/resolve?service=mongodb
    ↓
Lantern returns: {"stone_name": "stone-01", "offering": "mongodb", "connection_string": "mongodb://stone-01.local:17371"} // indicates the correct connection string to the service

┌──────────────────────────────────────────────────────┐
│ EXECUTION (Direct to Stone)                          │
└──────────────────────────────────────────────────────┘

Rake uses connection string to execute operation:
POST http://stone-01.local:17371/api/operations/offer/mongodb
    ↓
Stone executes operation, returns result

┌──────────────────────────────────────────────────────┐
│ OBSERVABILITY (Stone → Lantern Events)               │
└──────────────────────────────────────────────────────┘

Stone → POST http://lantern:7186/api/events
  {
    "stone_name": "stone-01",
    "event_type": "operation_started",
    "operation": "offer",
    "service": "mongodb"
  }
```

**Key Principle:** Lantern observes but doesn't mediate. Stones execute work directly.

---

## Technical Design

### Core Data Model

**In-Memory Topology Graph (Primary State):**

```rust
#[derive(Clone, Serialize, Deserialize)]
struct GardenTopology {
    stones: HashMap<String, StoneState>,
    last_updated: DateTime<Utc>,
}

#[derive(Clone, Serialize, Deserialize)]
struct StoneState {
    name: String,
    endpoint: String,
    status: StoneStatus,  // Online, Offline
    capabilities: HardwareCapabilities,
    services: HashMap<String, ServiceState>,
    last_seen: DateTime<Utc>,
    first_seen: DateTime<Utc>,
    offline_since: Option<DateTime<Utc>>,
}
```

**Rationale:** Direct HashMap lookups (microseconds) vs SQL joins (milliseconds). Serialize entire graph to JSON for persistence/debugging.

### SQLite Backend (Durable Storage)

```sql
-- Single table: persist JSON blob
CREATE TABLE topology (
  id INTEGER PRIMARY KEY CHECK (id = 1),  -- Only one row
  state JSON NOT NULL,
  last_updated TIMESTAMP NOT NULL
);

-- Optional: Event log for audit trail
CREATE TABLE events (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  event JSON NOT NULL
);
```

**Rationale:** Simple schema, easy backup/restore, no migration complexity. Load on startup, persist async on updates.

### Communication Patterns

**1. Stone Registration (Heartbeat)**
- Stone → `POST /api/register` every 45s
- Lantern updates in-memory topology, marks stone online
- 60s TTL: If no heartbeat, mark stone offline

**2. Service Discovery**
- Rake → `GET /api/resolve?service=mongodb`
- Lantern returns endpoint from in-memory graph
- Rake caches endpoint (90s TTL, reuses existing hot cache)

**3. Event Push (Non-Blocking)**
- Stone → `POST /api/events` (fire-and-forget tokio::spawn)
- Never fails user operation if Lantern unreachable
- Lantern stores event, pushes to dashboard via SSE

**4. TTL Cleanup (Background Task)**
- Every 10s: Check `last_seen` timestamps
- Mark stones offline if `last_seen > 60s ago`
- Cascade services to `unavailable` status

---

## API Specification

### Stone Registration
```
POST /api/register
Content-Type: application/json

{
  "stone_name": "stone-01",
  "endpoint": "http://192.168.1.50:3001",
  "capabilities": { ... },
  "services": [
    {"name": "mongodb", "type": "database", "status": "running"}
  ]
}

Response: 200 OK
{
  "ttl_seconds": 60,
  "next_heartbeat_seconds": 45
}
```

### Service Resolution
```
GET /api/resolve?service=mongodb

Response: 200 OK
{
  "stone_name": "stone-01",
  "offering": "mongodb",
  "connection_string": "mongodb://stone-01.local:17371"  // Service-specific connection string
}

Response: 404 Not Found (if no online stone offers mongodb)
{
  "error": {
    "code": "SERVICE_NOT_AVAILABLE",
    "message": "No online stone offers service 'mongodb'"
  }
}
```

### Topology Query
```
GET /api/stones

Response: 200 OK
[
  {
    "name": "stone-01",
    "endpoint": "http://192.168.1.50:3001",
    "status": "online",
    "services": ["mongodb", "redis"],
    "last_seen": "2026-01-16T18:30:00Z"
  },
  {
    "name": "stone-02",
    "status": "offline",
    "offline_since": "2026-01-16T17:00:00Z"
  }
]
```

### Topology Sync (Dormant Lanterns)
```
GET /api/topology

Response: 200 OK (Primary Lantern only)
{
  "stones": {
    "stone-01": {
      "name": "stone-01",
      "endpoint": "http://192.168.1.50:3001",
      "status": "online",
      "capabilities": { ... },
      "services": { ... },
      "last_seen": "2026-01-16T18:30:00Z"
    }
  },
  "last_updated": "2026-01-16T18:30:05Z"
}

Response: 503 Service Unavailable (Dormant Lantern)
{
  "error": "Not primary",
  "primary_endpoint": "http://192.168.1.105:7186"
}
```

### Event Stream (Dashboard)
```
GET /api/events/stream
Accept: text/event-stream

Response: 200 OK (SSE)
event: stone_online
data: {"stone": "stone-01", "timestamp": "2026-01-16T18:30:00Z"}

event: operation_started
data: {"stone": "stone-01", "operation": "offer", "service": "mongodb"}

event: operation_completed
data: {"stone": "stone-01", "operation": "offer", "status": "success"}
```

### Health Check
```
GET /api/health

Response: 200 OK
{
  "status": "healthy",
  "stones_online": 3,
  "stones_offline": 1,
  "uptime_seconds": 86400
}
```

---

## Implementation Phases

### Phase 1: Core Registry + High Availability (MVP)
**Goal:** Replace UDP discovery with HTTP registration + distributed election  
**Estimate:** 5-7 days

**Deliverables:**
1. Lantern HTTP server (Rust + Axum, port 7186)
   - `POST /api/register` (60s TTL)
   - `GET /api/resolve?service=<type>`
   - `GET /api/stones`
   - `GET /api/topology` (primary serves, dormant returns 503 with primary endpoint)
   - `GET /api/health`

2. In-memory topology graph with SQLite persistence

3. **Distributed Election Protocol:**
   - UUIDv7-based leader selection (lowest wins)
   - Probe interval: 5s (dormant → primary health check)
   - Election cycle: 5s (candidate → primary promotion)
   - State synchronization: Dormant Lanterns pull topology from primary every 30s (HTTP)
   - UDP listener: Capture stone announcements from "Is anyone there?" broadcasts
   - UDP election messages: `LANTERN_ANNOUNCEMENT` (primary health signal)

4. Moss integration:
   - Auto-register to Lantern on startup (if `LANTERN_ENDPOINT` env var set)
   - Re-register every 45s (background task)
   - Graceful degradation: UDP fallback if Lantern unavailable
   - "Is anyone there?" detection: Moss broadcasts UDP "Is anyone there?" when HTTP requests fail, triggers topology announcements captured by all Lanterns

5. Rake integration:
   - Prefer Lantern for discovery (check cached stone capabilities for `lantern_endpoint`)
   - Fall back to UDP broadcast if Lantern unreachable
   - Cache Lantern-resolved endpoints (90s TTL, reuse existing hot cache)

**CLI Commands:**
- `garden-rake place lantern` (enable local Lantern on this stone)
- `garden-rake place lantern at {stone}` (enable remote Lantern via SSH)
- `garden-rake show lanterns` (list all Lanterns, highlight primary)

**Success Criteria:**
- ✅ Rake discovers stones via Lantern (no UDP needed)
- ✅ Cross-subnet discovery works (Windows → Linux across VLANs)
- ✅ Graceful degradation when Lantern offline
- ✅ Zero breaking changes to existing P2P mode
- ✅ Dormant Lanterns stay passive until needed (announcement timeout OR Moss query)
- ✅ Multi-Lantern election converges in 5-8 seconds on active failure
- ✅ No SPOF: Any stone can run Lantern, auto-failover

---

### Phase 2: Dashboard UI
**Goal:** Visualize garden topology  
**Estimate:** 3-4 days

**Deliverables:**
1. Alpine.js static client embedded in Lantern binary
   - Source folder: `src/lantern/static/` (index.html, js/, css/)
   - Embedded at compile time via `include_dir!("src/lantern/static")`
   - Served with preserved folder structure: `GET / → index.html`, `GET /js/alpine.min.js → js/alpine.min.js`
   - Dashboard home: All stones with status indicators
   - Stone detail view: Capabilities, services, metrics
   - Services view: Filter by type (database, messaging, etc.)
   - No build step, no CDN dependencies, <100KB total embedded assets

2. SSE endpoint for real-time updates (`GET /api/events/stream`)

3. Charts:
   - Stone count over time (line chart)
   - Service distribution (pie chart)
   - CPU/memory trends per stone

4. Actions (delegates to Rake CLI or direct API):
   - View stone capabilities
   - Filter services by type
   - See recent events (last 100)

**Success Criteria:**
- ✅ Operator sees all stones at a glance
- ✅ Real-time updates when stone joins/leaves (< 5s latency)
- ✅ Useful for debugging topology issues

---

### Phase 3: Active Health Monitoring
**Goal:** Proactive failure detection  
**Estimate:** 1-2 days

**Deliverables:**
1. HTTP health polling (`GET /health` on each stone every 30s)
2. Alerting:
   - Stone offline > 60s → Log warning
   - Service degraded → Dashboard shows yellow/red
3. Metrics storage:
   - Time-series metrics table (7-day retention)
   - API endpoint for historical queries: `GET /api/metrics?stone=stone-01&range=24h`

**Success Criteria:**
- ✅ Lantern detects stone failures within 60s
- ✅ Historical metrics available for debugging
- ✅ Dashboard shows health trends

---

## Critical Design Decisions

### 1. High-Availability via Distributed Election

**Design Decision: Multi-Lantern with UUIDv7-Based Election (Phase 1)**

**Deployment Model:**
- Lantern bundled with every stone installation
- Enable/disable via systemd service or config flag
- CLI: `garden-rake place lantern` (local) or `garden-rake place lantern at {stone}` (remote)

**Election Protocol:**
- **Probe Interval**: 5 seconds (dormant Lanterns check active Lantern health)
- **Election Cycle**: 5 seconds (time to converge on active set)
- **Leader Selection**: Lowest UUIDv7 preferred (soft preference, not enforced)
- **Multiple Active Lanterns**: 2-3 concurrent active Lanterns acceptable for resilience
- **No Single-Primary Guarantee**: Election reduces active count but doesn't enforce strict single-leader

**State Synchronization:**
- **Primary Lantern**: Broadcasts `LANTERN_ANNOUNCEMENT` every 10s (announces active status)
- **Dormant Lanterns**: Pull topology JSON from primary every 30s via HTTP `GET /api/topology`
- **Cache in memory**: Dormant Lanterns maintain cached copy, ready to serve if promoted
- **On promotion to active**: New primary broadcasts `LANTERN_DISCOVERY` request
- **Stones respond**: Send current capabilities + services to rebuild topology graph (5-10s convergence)
- **UDP broadcast capture**: All Lanterns (active + dormant) listen to UDP broadcasts, update topology from stone announcements

**Dormant Wait Strategy:**
- Pull topology from primary every 30s (HTTP)
- Listen for LANTERN_ANNOUNCEMENT broadcasts (UDP, passive monitoring)
- Respond to Moss "Is anyone there?" queries (triggers UDP broadcast, captured by all Lanterns)
- Only initiate election when primary is silent (no announcement for 15s)

**Failure Detection Triggers (Dormant → Candidate):**
1. **Passive**: No LANTERN_ANNOUNCEMENT received for 15s (3× 5s probe interval)
2. **Active**: Moss sends "Is anyone there?" UDP broadcast (primary unreachable, triggers stone announcements captured by all Lanterns)

**Dormant Behavior:**
- Pull topology from primary every 30s (HTTP `GET /api/topology`)
- Listen for LANTERN_ANNOUNCEMENT broadcasts (primary health signal)
- Capture UDP stone announcements ("Is anyone there?" responses) to update local topology cache
- Only trigger election when primary is silent AND no stone announcements captured

**Election Flow:**
```
1. Dormant waits for activation trigger:
   - No LANTERN_ANNOUNCEMENT received for 3× probe interval (15s)
   - OR Moss sends "Is anyone there?" query (active Lanterns unreachable)
2. Dormant → Candidate: Generate announcement_id (UUIDv7)
3. Calculate election delay: blake3::hash(lantern_name + lan_ip + announcement_id)[0] * 10 ms (0-2550ms)
4. Wait for calculated delay
5. Listen for LANTERN_ANNOUNCEMENT broadcasts during wait
6. If announcement received → Suppress own announcement, return to Dormant
7. If delay expires without hearing announcement → Broadcast LANTERN_ANNOUNCEMENT
8. Promote to Active, stones add Lantern endpoint to rotation pool
```

**Suppression Window:**
- Active Lanterns broadcast `LANTERN_ANNOUNCEMENT` every 10s
- Candidates hearing announcement during their calculated delay suppress their own
- Result: 2-3 active Lanterns naturally maintained (announcements keep others dormant)

**Election Properties (BLAKE3-based):**
- **Deterministic**: Same lantern + LAN IP + announcement_id always produces same delay
- **Unpredictable**: Different announcement_ids produce different response order
- **Prevents announcement storm**: Staggered delays (0-2550ms) space out responses
- **No coordination needed**: Each Lantern calculates delay independently

**Split-Brain Mitigation:**
- Accept eventual consistency (stones may briefly see different Lanterns)
- Per-network-segment Lanterns deferred to future phase
- UDP reliability sufficient for local networks (HTTP fallback for later)

**Rationale:** Eliminates SPOF from day one, simple election protocol, self-healing topology. Multiple active Lanterns (2-3) provide resilience without coordination overhead.

---

### 2. Timing Parameters

**Detection & Election:**
- Probe interval: 5 seconds (dormant → primary health check)
- Election delay: 0-2550ms (BLAKE3 hash-based, deterministic but unpredictable)
- Total convergence time: 5-8 seconds (probe failure + election delay)

**Rationale:** Balance fast failover (5-8s) vs network overhead. BLAKE3 election algorithm consistent with moss UDP discovery behavior.

### 3. State Synchronization

**Approach: Primary Announces, Dormants Pull**

**Primary Lantern:**
- Broadcasts `LANTERN_ANNOUNCEMENT` every 10s (UDP, primary health signal)
- Serves topology via HTTP `GET /api/topology` for dormant Lanterns
- Accepts stone registrations via `POST /api/register`

**Dormant Lanterns:**
- Pull topology JSON from primary every 30s (HTTP)
- Cache topology in memory (ready to serve if promoted)
- Listen to UDP broadcasts (stone announcements from "Is anyone there?" queries)
- Update local cache from captured UDP stone announcements

**On Promotion:**
- New primary broadcasts `LANTERN_DISCOVERY` to all known stones
- Stones respond with current capabilities + services
- Rebuild topology graph from responses (5-10s convergence)
- Begin announcing every 10s

**Acceptable Data Loss:**
- Primary failure loses last 5-30s of topology updates (not yet synced to dormants)
- Stones re-register after new primary promotion (automatic recovery)
- Dormant Lanterns have 0-30s stale cache (last pull cycle)

**Rationale:** Simple HTTP pull model, no complex gossip protocol. Stones remain source of truth. UDP capture provides real-time updates on "Is anyone there?" events.

---

### 4. Dashboard Real-Time Updates

**Design: SSE from Any Active Lantern**

**Connection Flow:**
- Dashboard connects to `GET /api/events/stream` on any active Lantern
- Dormant Lanterns return `503 Service Unavailable` with active Lantern list in header
- Dashboard automatically connects to first available active Lantern

**Discovery:**
- Dashboard broadcasts UDP `WHO_IS_ACTIVE?` → Active Lanterns respond with endpoints
- Fallback: Iterate known Lantern addresses (configured in stone's `garden.toml`)

**Technology Stack (Static Client):**
- **Recommended: Alpine.js** (15KB, no build step, declarative, CDN-free when bundled)
  - Embed entire `static/` folder into Lantern binary (preserves structure)
  - Folder structure: `static/index.html`, `static/js/alpine.min.js`, `static/css/style.css`
  - Rust: Use `include_dir!()` macro to embed directory tree at compile time
  - Relative URLs work naturally: `<script src="/js/alpine.min.js"></script>`
  - Lantern serves: `GET / → static/index.html`, `GET /js/* → static/js/*`, etc.
- **Alternative: Vanilla JS** (zero dependencies, ~5KB custom code)
  - Pure HTML/CSS/JS, no framework
  - EventSource for SSE, Fetch API for REST
- **Alternative: Petite-Vue** (Vue 3 lite, 10KB, no build step)
  - Similar to Alpine but Vue-compatible syntax

**Decision:** Alpine.js for Phase 2, embed `static/` folder tree in Lantern binary.

**Rationale:** No compilation, no CDN dependency, minimal size, folder structure preservation enables easy maintenance and natural relative URLs. Multiple active Lanterns provide redundancy for dashboard connections.

---

### 5. Authentication & Authorization

**Phase 1 Deployment Model:**
- Lantern bundled with stone installation (not standalone)
- Binds to localhost/internal network only (port 7186)
- Network segmentation via firewall rules (block external access)

**Future Considerations (Phase 2+):**
- Add token-based auth for dashboard remote access
- Add mTLS for cross-site topology federation

**Decision:** Defer auth; prioritize bundled deployment model.

**Rationale:** Lantern is infrastructure, not public API. Deployed as part of stone, internal-network-only binding provides security baseline.

---

### 6. Multi-Segment & Multi-Tenant Support

**Current Scope: Single Global Namespace**
- All stones visible to all rakes within same broadcast domain
- Lanterns see only stones on same LAN/subnet (UDP reach)

**Future Architecture:**
- **Per-Segment Lanterns**: One Lantern per subnet/datacenter
- **Federation Protocol**: Cross-segment discovery via HTTP (gossip or hub-and-spoke)
- **Hierarchical Topology**: Segment-local view → Global aggregated view

**Decision:** Defer multi-segment and multi-tenant support to future phase.

**Rationale:** Single-site deployment covers MVP. Cross-segment requires complex federation protocol without clear demand yet.

---

## Integration with Existing Code

### Already Implemented (From Prior Work)
- ✅ Moss lifecycle broadcasts (`moss_online`, `moss_offline` on port 3003)
- ✅ UDP listener on port 3002 (for LANTERN_GATHER, currently unused)
- ✅ `lantern_endpoint` field in `HardwareCapabilities` struct
- ✅ Stone registry structure in Moss
- ✅ Hot caching in garden-rake (90s TTL)

### Required Changes

**1. Moss: Add Auto-Registration Task**
```rust
// In moss main.rs startup
if let Ok(lantern) = env::var("LANTERN_ENDPOINT") {
    tokio::spawn(lantern_registration_loop(lantern, capabilities.clone()));
}

async fn lantern_registration_loop(endpoint: String, caps: HardwareCapabilities) {
    let client = reqwest::Client::new();
    loop {
        match client.post(format!("{}/api/register", endpoint))
            .json(&RegisterRequest { /* ... */ })
            .send()
            .await {
            Ok(_) => tracing::debug!("Registered with Lantern"),
            Err(e) => tracing::warn!("Lantern registration failed: {}", e),
        }
        tokio::time::sleep(Duration::from_secs(45)).await;
    }
}
```

**2. Moss: Event Push on Operations**
```rust
async fn push_event_to_lantern(event: GardenEvent) {
    if let Ok(lantern_endpoint) = env::var("LANTERN_ENDPOINT") {
        tokio::spawn(async move {
            let _ = reqwest::Client::new()
                .post(format!("{}/api/events", lantern_endpoint))
                .json(&event)
                .send()
                .await;
        });
    }
}

// In offer handler
async fn handle_offer(...) -> Result<Response> {
    push_event_to_lantern(GardenEvent {
        event_type: "operation_started",
        operation: "offer",
        service: offering.clone(),
        // ...
    }).await;
    
    // ... perform operation ...
}
```

**3. Rake: Lantern-First Discovery**
```rust
// In discovery.rs
pub async fn discover_via_lantern(client: &reqwest::Client, service: &str) -> Result<String> {
    // Check if any cached stone has lantern_endpoint
    if let Some(lantern) = get_lantern_endpoint_from_cache() {
        let url = format!("{}/api/resolve?service={}", lantern, service);
        if let Ok(resp) = client.get(&url).send().await {
            if resp.status().is_success() {
                let resolved: ResolveResponse = resp.json().await?;
                return Ok(resolved.connection_string);  // Service-specific connection string
            }
        }
    }
    
    // Fallback: UDP broadcast
    discover_moss()
}
```

**4. New Crate: `src/lantern/`**
- Clean separation from moss/rake
- Can run standalone: `cargo run --bin garden-lantern`
- Shares `zen-common` for types

---

## Risk Assessment

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| **SQLite corruption** | Medium | Low | WAL mode, regular backups, JSON export |
| **Registration storms** | Medium | Medium | Rate limiting (1 reg/stone/min), exponential backoff |
| **Network partition** | Medium | High | Expected; stones continue working, Lantern marks offline |
| **Dashboard stale data** | Low | Medium | SSE for real-time updates from primary only |
| **Memory growth** | Medium | Low | Limit event log (7 days), prune offline stones (30 days) |
| **Split-brain topology** | Low | Medium | Multiple active Lanterns tolerated (2-3 is resilience, not failure) |

---

## Open Questions for Engineering Team

### Technical
1. **Cross-platform support**: Should Lantern run on Windows or Linux-only initially?
2. **Dashboard assets**: Embed entire `static/` folder tree (preserves structure) or flatten to single HTML file?
3. **Authentication timing**: Phase 2 or Phase 3? What auth method (API keys, JWT, mTLS)?
4. **Event retention**: 7 days sufficient? Need archival to external system?
5. **Scaling limits**: What's max expected stone count? (Impacts in-memory topology size)

### Operational
6. **Deployment model**: Docker container, systemd service, or both?
7. **Port allocation**: Port 7186 acceptable or conflicts with other services?
8. **Backup strategy**: Automatic SQLite backups or manual operator responsibility?
9. **Monitoring integration**: Prometheus metrics? Structured logging format?

### Product
10. **Phase priority**: Is Phase 2 (Dashboard) higher priority than Phase 3 (Active Monitoring)?
11. **API stability**: Lock API contract after Phase 1 or allow evolution?
12. **CLI integration**: Should `garden-rake` have Lantern-specific commands? (e.g., `rake lantern status`, `rake show lanterns`)

---

## Success Metrics

### Phase 1 (Core Registry)
- **Discovery Success Rate**: >99% via Lantern (vs ~85% UDP)
- **Cross-Subnet Discovery**: 100% success (vs 0% UDP)
- **Latency**: <50ms for `/api/resolve` (p95)
- **Reliability**: Graceful degradation verified (Lantern offline → UDP works)

### Phase 2 (Dashboard)
- **Time to Insight**: Operator identifies offline stone in <5s
- **Update Latency**: SSE events appear within 3s of occurrence
- **Adoption**: 80% of operators use dashboard at least once per day

### Phase 3 (Active Monitoring)
- **Failure Detection**: Stone failures detected within 60s (vs manual discovery)
- **False Positives**: <5% (network blips don't trigger alerts)
- **Historical Query Performance**: <100ms for 24h metrics (p95)

---

## Recommendation

**Approve Phase 1 implementation with the following conditions:**

1. **Mandatory**: Distributed election (UUIDv7-based, soft preference for 2-3 active Lanterns) with UDP fallback
2. **Mandatory**: Zero breaking changes to existing moss/rake functionality
3. **Mandatory**: Comprehensive documentation (ADR, API docs, deployment guide, CLI reference)
4. **Mandatory**: Alpine.js static client bundled in binary (Phase 2, no CDN dependencies)
5. **Recommended**: Prototype Phase 1 in 5-7 days, validate with real multi-subnet network + simulated failures
6. **Recommended**: Address "Technical" open questions before Phase 2 starts

**Go/No-Go Decision Point:** After Phase 1 prototype, evaluate:
- Does Lantern solve cross-subnet discovery?
- Does BLAKE3 election protocol maintain 2-3 active Lanterns reliably?
- Performance acceptable (<50ms resolve latency)?
- Can stones failover between active Lanterns seamlessly?
- If YES → Proceed to Phase 2
- If NO → Re-evaluate architecture or scope

---

## Next Steps

**If Approved:**
1. Create ADR: `docs/decisions/LANTERN-0001-service-registry-architecture.md`
2. Scaffold project: `src/lantern/` with Cargo.toml
3. Define API contracts: Finalize request/response schemas in `zen-common`
4. Implement Phase 1 core: Registration + Resolution + TTL cleanup
5. Integration test: Docker Compose with multiple subnets
6. Integrate with Moss: Auto-registration task
7. Integrate with Rake: Lantern-first discovery
8. Documentation: API reference, deployment guide, troubleshooting

**Estimated Timeline:**
- Phase 1: 5-7 days (core registry + HA election + integration)
- Phase 2: 3-4 days (dashboard UI + SSE from primary)
- Phase 3: 1-2 days (health monitoring + metrics)
- **Total MVP: ~2.5 weeks**

**Election Implementation Breakdown (Phase 1):**
- Election state machine: 2 days
- UDP messaging (probe, announcement): 1 day
- BLAKE3 delay calculation: 0.5 days
- Dormant topology caching + promotion rebuild: 1 day
- Integration with Moss "Is anyone there?" detection: 0.5 days
- Testing (simulated failures, network partitions): 1 day

---

## Feedback Requested

Please provide feedback on:

1. **Architecture**: Any fundamental concerns with hub-and-spoke design?
2. **Scope**: Is Phase 1 sufficient for initial rollout or require Phase 2 (Dashboard)?
3. **Risks**: Missing critical risks? Additional mitigations needed?
4. **Priorities**: Should we re-order phases based on operational needs?
5. **Technical Decisions**: Agreement on in-memory topology vs SQL-first approach?
6. **Integration**: Concerns with proposed moss/rake changes?
7. **Alternative Approaches**: Better solutions we haven't considered?

**Response Format:**
- ✅ **Approve**: Proceed with Phase 1 implementation
- 🔶 **Approve with Changes**: Specific modifications required (list below)
- ❌ **Reject**: Fundamental concerns (explain reasoning)

---

**Document History:**
- 2026-01-16: Initial proposal (DRAFT)
