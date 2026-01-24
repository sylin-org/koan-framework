# TOPO-0001: Inter-Stone Communication Protocol (P2P Mode)

**Status**: Proposed (Refactoring Required)  
**Date**: 2026-01-23  
**Decision**: Unified chirp-based topology discovery with progressive self-awareness architecture

---

## Context

### Problem Statement

Zen Garden stones operate in P2P mode, discovering and maintaining awareness of peer stones without central coordination. The challenge is keeping topology information current while minimizing network overhead and supporting graceful degradation across diverse network configurations.

### Requirements

**Discovery Requirements:**
1. Stones must discover each other at startup (active + passive)
2. Topology cache must stay current without manual intervention
3. Service/offering changes must propagate to peers
4. Must support tend-less Rake (client selects stone based on discovery)
5. Must work across mDNS-capable and non-capable platforms

**Efficiency Requirements:**
1. Minimize unnecessary network traffic (change detection)
2. Single unified announcement mechanism (DRY principle)
3. Graceful shutdown notification (avoid timeout delays)
4. Fail-safe keep-alive (detect silent failures)

---

## Decision

### Core Concept: The Chirp

A **Chirp** is a UDP broadcast emission of local topology information from a Moss service. Chirps are always sourced from the **Self Topology Entry**, a progressively populated in-memory structure tracking the stone's current state.

**Self Topology Entry Structure:**
```rust
pub struct SelfTopologyEntry {
    pub stone_id: String,
    pub stone_name: String,
    pub endpoint: Option<String>,
    pub moss_version: String,
    pub mac: Option<String>,
    pub health: String,
    pub basic_hardware: Option<BasicHardware>,
    pub capabilities: Option<HardwareCapabilities>,
    pub services: Vec<ChirpServiceInfo>,
    pub last_updated: Instant,
}
```

**Progressive Disclosure Model:**
The self entry is initialized at boot and progressively enriched as data becomes available. At any moment, if an announcement request arrives, Moss chirps the current self entry state—whatever data is available at that instant.

**Health Status Progression:**
- **"starting"** - Boot: stone_id, stone_name, moss_version loaded
- **"initializing"** - Network + basic hardware: endpoint, MAC, CPU/storage detected
- **"thriving"** - Complete inventory: All services healthy, full capabilities available
- **"degraded"** - Complete inventory: Some service errors detected

### Chirp Triggers

Chirps are emitted on five distinct events, always broadcasting the current self topology entry:

| Trigger | Timing | Force | Purpose |
|---------|--------|-------|---------|
| **Self Entry Update** | On any state change | Yes | Auto-chirp when self entry modified (network, hardware, services) |
| **Periodic Heartbeat** | Every 30s | Conditional | Topology freshness + liveness |
| **Offering State Change** | Immediate | Yes | Propagate service updates (updates self entry → auto-chirp) |
| **Announcement Request** | On-demand | Yes | Response to discovery/tend queries (chirps current self entry state) |
| **Graceful Shutdown** | Once at shutdown | Yes | Immediate offline notification |

**Periodic Heartbeat Logic:**
- Every 30 seconds, calculate state hash (JSON serialization)
- If state unchanged AND <5 minutes elapsed: **skip** (traffic optimization)
- If state changed OR ≥5 minutes elapsed: **send** (keep-alive guarantee)
- Result: ~95% reduction in redundant traffic, 5-minute failure detection window

### Dual-Channel Architecture

**Channel 1: UDP Broadcast (Port 7184)**
- **Purpose**: Active topology distribution, all platforms
- **Mechanisms**:
  - Chirp broadcasts (stone → all peers)
  - Discovery requests (Rake/Moss → all stones)
  - Goodbye announcements (stone shutdown)
- **Advantages**: Works everywhere, fast, low overhead
- **Limitations**: Subnet-local only, no guaranteed delivery

**Channel 2: mDNS Service Advertisement (_moss._tcp.local)**
- **Purpose**: Passive discovery hints, name resolution
- **Mechanisms**:
  - Service announcement at startup (Linux/macOS)
  - TXT records: stone_id, MAC, port
  - Continuous advertisement via Avahi/Bonjour
- **Advantages**: Standard protocol, DNS-SD integration
- **Limitations**: Platform-specific, limited payload

### Integration Flow

**Moss Startup Sequence:**
1. **Phase 0**: **Initialize self topology entry** - Create `Arc<RwLock<SelfTopologyEntry>>` with stone_id, stone_name, moss_version, health="starting"
2. **Phase 1**: **Start UDP listener** (all platforms) - Single shared listener with message routing pipeline:
   - Discovery requests → chirps current self entry (always ready)
   - Chirps → updates topology cache
   - Goodbye messages → marks stone offline
3. **Phase 2**: Network configuration → Update self entry (endpoint, MAC, health="initializing") → **auto-chirp**
4. **Phase 3**: Basic hardware detection → Update self entry (basic_hardware) → **auto-chirp**
5. **Phase 4**: Publish mDNS service (Linux/macOS, subset of self entry data)
6. **Phase 11**: Service registry load → Update self entry (services array)
7. **Phase 11.5**: Start mDNS lurk-listener (passive, populates topology from mDNS)
8. **Phase 12**: Active peer discovery (send UDP request, collect chirp responses via listener)
9. **Phase 13**: Complete hardware detection → Update self entry (capabilities, calculate health) → **auto-chirp** ("thriving"/"degraded")
10. **Phase 14**: Start periodic announcer (30s background task, chirps self entry)
11. **Phase 15**: Subscribe to service events (updates self entry → auto-chirp on changes)

**Rake Tend-less Discovery:**
1. Rake sends UDP announcement request (port 7184 broadcast)
2. All reachable stones respond with chirps
3. Rake aggregates responses, presents selection to user
4. User chooses stone, Rake establishes HTTP session

**Rake Observe Command:**
1. Check if Rake has tended stone configured
2. **If NO tend OR connection fails**: Perform UDP discovery + stone selection (same as tend-less flow)
3. **If tend exists AND connects**: HTTP GET to `/api/v1/garden/topology` endpoint
4. Moss responds with complete topology: self entry + peer topology cache
5. Rake displays all stones (local + peers) with current state

**Topology Cache Management:**
- **Chirp received**: Upsert stone with full data (services, version, MAC)
- **mDNS discovered**: Upsert stone with basic data (endpoint, MAC, no services)
- **Goodbye received**: Mark stone offline immediately (preserve MAC for WoL)
- **Timeout (90s no chirp)**: Mark stone offline (automatic cleanup)

**Current Behavior Note:**
mDNS-discovered stones (that don't send chirps) are currently merged directly into topology cache with limited data. Future enhancement: HTTP fallback to query topology endpoint for complete data before caching.

---

## Implementation

### Architecture Overview

**Module Structure:**
```
src/moss/src/
├── announcement.rs          # Core chirp logic (DRY: single source)
├── discovery.rs             # UDP listener + active discovery
├── mdns.rs                  # mDNS advertisement + lurk-listener
├── domain/
│   ├── self_topology.rs     # SelfTopologyEntry + update helpers
│   └── topology.rs          # Peer topology cache
└── tasks/
    ├── announcer.rs         # Periodic 30s background task
    └── coordinator.rs       # Startup orchestration
```

### Core Components

**1. domain/self_topology.rs** (To Implement)
- `SelfTopologyEntry` - Stone's current state (single source of truth for chirps)
- `update_self_topology(field, value)` - Update entry + auto-chirp on changes
- `get_self_entry()` - Read current state (for announcement requests)
- Stored in `AppState` as `Arc<RwLock<SelfTopologyEntry>>`

**2. announcement.rs** (To Update)
- `AnnouncementPayload` - Internal representation of chirp data
- `announce(self_entry)` - Send chirp via UDP broadcast (reads from self entry)
- `announce_if_changed(...)` - Change detection wrapper
- `build_payload(self_entry)` - Serialize self entry to chirp payload
- `send_goodbye(state)` - Graceful shutdown notification
- `calculate_state_hash()` - JSON-based hash for change detection

**Key Decision: JSON-Based Change Detection**
| Approach | Performance | Maintainability | Safety |
|----------|-------------|-----------------|--------|
| Manual field hashing | ~1 μs | Bug-prone, requires updates | Must remember new fields |
| JSON serialization | ~6 μs | Self-maintaining | Automatically includes all fields |

**Chosen: JSON** - 5μs overhead per 30s = 0.0002%, negligible for maintainability gains.

**3. tasks/announcer.rs** (To Update)
- `start_periodic_announcer(state)` - Background task
  - 30-second tokio interval
  - Skips first tick (self entry already chirped at startup)
  - Reads self entry, calls `announce_if_changed()` with state hash tracking
  - Logs decisions (sent/skipped/failed)

**4. discovery.rs** (To Update)
- **Single UDP listener** (port 7184) - Message routing pipeline:
  - `UdpEvent::Request` → Log discovery request, chirp current self entry
  - `UdpEvent::Chirp` → Update topology cache with peer data
  - `UdpEvent::Goodbye` → Mark stone offline immediately
- `discover_peers(stone_id, timeout)` - Active discovery at startup
  - Sends UDP broadcast request
  - Collects chirp responses via shared listener channel
  - Returns Vec<DiscoveryResponse> for topology cache
- **Architecture**: Listener started first (Phase 1), reused by all components

**5. bootstrap/run.rs** (To Update - Orchestration)
- **Phase 0**: Initialize self topology entry (stone_id, stone_name, moss_version, health="starting")
- **Phase 1**: Start UDP listener (can now respond to requests with self entry)
- **Phase 2**: Network configuration → Update self entry (endpoint, MAC, health="initializing") → auto-chirp
- **Phase 3**: Basic hardware → Update self entry (basic_hardware) → auto-chirp
- **Phase 4**: mDNS announcement (Linux/macOS, reads from self entry)
- **Phase 11**: Service registry → Update self entry (services array)
- **Phase 11.5**: Start mDNS lurk-listener
- **Phase 12**: Active peer discovery (`discover_peers(3s timeout)` - uses Phase 1 listener)
- **Phase 13**: Complete hardware → Update self entry (capabilities, health="thriving"/"degraded") → auto-chirp
- **Phase 14**: Start periodic announcer (chirps self entry via Phase 1 listener)
- **Phase 15**: Subscribe to service events → update self entry → auto-chirp (via Phase 1 listener)

### Data Flow Examples

**Example 1: Stone Startup (Progressive Chirp Model)**
```
stone-coral-prairie boots up
  ↓
Phase 0: Self topology entry initialized
  → stone_id="coral-prairie", health="starting"
  ↓
Phase 1: UDP listener starts (port 7184) - ready to receive discovery/chirps/goodbyes
  → Can now respond to announcement requests with current self entry ("starting" state)
  ↓
Phase 2: Network configuration complete
  → Self entry updated: endpoint="192.168.1.141:3000", mac="aa:bb:cc:dd:ee:ff", health="initializing"
  → AUTO-CHIRP #1: Peers now see coral-prairie is initializing
  ↓
Phase 3: Basic hardware detected
  → Self entry updated: basic_hardware={cpu, storage}
  → AUTO-CHIRP #2: Richer data available
  ↓
Phase 4: mDNS announces "_moss._tcp.local" (TXT: stone_id, MAC from self entry)
  ↓
Phase 12: Sends UDP discovery request (port 7184)
  ↓
stone-crystal-forest receives request → chirps its self entry ("thriving")
stone-bronze-canyon receives request → chirps its self entry ("thriving")
  ↓
stone-coral-prairie topology cache: 2 peers discovered
  ↓
Phase 11-13: Service registry → Hardware detection → Catalog build
  → Self entry updated: services=[...], capabilities={...}, health="thriving"
  → AUTO-CHIRP #3: Complete operational state
  ↓
All stones update: coral-prairie is fully operational with complete inventory
  ↓
Phase 14: Periodic announcer starts (30s loop, chirps self entry with change detection)
```

**Example 2: Service Deployment**
```
User deploys Ollama to stone-coral-prairie
  ↓
Moss service registry updated (Ollama: Installing → Running)
  ↓
Service change event emitted
  ↓
Phase 15 handler: Update self entry (services array) → Auto-chirp (bypasses 30s interval)
  ↓
All peer stones receive chirp with updated self entry
  ↓
Topology caches updated: stone-coral-prairie now has Ollama service
```

**Example 3: Graceful Shutdown**
```
User runs: systemctl stop garden-moss
  ↓
Shutdown signal received
  ↓
send_goodbye(state) called
  ↓
UDP broadcast: STONE_GOODBYE message
  ↓
All peer stones receive goodbye
  ↓
Topology caches: stone-coral-prairie marked offline (MAC preserved for WoL)
```

**Example 4: Rake Observe Command**
```
User runs: rake observe
  ↓
Rake checks: Is there a tended stone? → YES: stone-bronze-canyon (192.168.1.111)
  ↓
Rake sends: GET http://192.168.1.111:3000/api/v1/garden/topology
  ↓
Moss reads topology cache + self entry:
  → Self: bronze-canyon (health="thriving", 8 services)
  → Peer: crystal-forest (health="thriving", 6 services)
  → Peer: coral-prairie (health="degraded", 4 services, 1 error)
  ↓
Moss returns JSON with complete topology (3 stones)
  ↓
Rake displays formatted table:
  STONE             STATUS      SERVICES  ENDPOINT
  bronze-canyon*    thriving    8         192.168.1.111:3000
  crystal-forest    thriving    6         192.168.1.101:3000
  coral-prairie     degraded    4         192.168.1.141:3000

*Note: Data from tended stone's cached topology (self entry + peer cache)

Alternative flow (no tend or connection fails):
  → Rake performs UDP discovery broadcast
  → Presents stone selection to user
  → Once selected, displays that stone's topology
```

---

## Benefits

**Operational:**
1. ✅ **Zero-config discovery**: Stones auto-discover peers without manual configuration
2. ✅ **Fresh topology**: 30s updates (with 95% traffic reduction via change detection)
3. ✅ **Immediate propagation**: Service changes visible within seconds
4. ✅ **Graceful degradation**: mDNS failure doesn't break UDP, and vice versa
5. ✅ **Tend-less Rake**: Clients can discover and select stones without predefined endpoints

**Technical:**
1. ✅ **Clean architecture**: SoC (modules), DRY (single announce function), progressive self-awareness
2. ✅ **Always ready**: Can respond to announcement requests from Phase 1 onwards (no coordination needed)
3. ✅ **Maintainable**: JSON-based change detection self-maintains with schema changes
4. ✅ **Extensible**: Easy to add new channels (future: HTTP push, WebSocket)
5. ✅ **Observable**: Structured logs at every decision point

---

## Consequences

### Positive

- Topology cache stays current without external orchestration
- Service discovery (`find` command) benefits from cached service data
- Network traffic minimized (95% reduction) via state hashing
- Wake-on-LAN support via MAC preservation in topology

### Negative

- Additional background task (30s announcer) - minimal CPU/memory overhead
- Periodic UDP broadcasts (mitigated by change detection, ~1 packet/5min stable)
- mDNS TXT updates not dynamic (requires service re-registration)
- Subnet-local only (no cross-VLAN without router support)

### Trade-offs

| Decision | Cost | Benefit |
|----------|------|---------|
| JSON-based hashing | 6μs vs 1μs manual | Self-maintaining, safe, simple |
| 30s interval | Background task overhead | Fresh topology, standard heartbeat |
| 5min keep-alive | Periodic traffic even when stable | Detects silent failures |
| Dual-channel (UDP+mDNS) | Complexity | Platform flexibility, redundancy |

---

## Known Limitations

1. **Subnet-local discovery**: UDP broadcast doesn't cross VLANs (see LANTERN-0001 for multi-subnet solution)
2. **No capabilities in chirps**: Hardware capabilities announced via mDNS TXT only, not UDP payload
3. **mDNS platform-specific**: Windows lacks native mDNS, depends on Bonjour service
4. **No HTTP fallback yet**: mDNS-discovered stones without chirps get partial topology data

---

## Implementation Status

**Required for Completion (Priority Order):**
1. **CRITICAL**: Move UDP listener to Phase 1 (currently Phase 11)
2. **HIGH**: Implement `domain/self_topology.rs` module with SelfTopologyEntry
3. **HIGH**: Add health status field to StoneChirpPayload in common/types.rs
4. **HIGH**: Update announcement.rs to read from self entry instead of gathering state
5. **HIGH**: Update topology API (`api/v1/garden.rs`) to merge self entry + peer topology cache
6. **MEDIUM**: Wire progressive updates in bootstrap/run.rs (Phases 0-15)
7. **MEDIUM**: Update tasks/announcer.rs to read from self entry
8. **LOW**: Update discovery.rs announcement request handler to chirp self entry

**Estimated Effort**: ~9 hours total

## Future Enhancements

**Short-term:**
1. HTTP fallback: Query `/api/v1/stone/info` for mDNS-discovered non-chirping stones
2. Topology pruning task: Auto-remove offline stones after configurable retention period

**Long-term:**
1. Dynamic mDNS TXT updates (requires mdns-sd crate enhancement or alternative)
2. WebSocket topology stream (real-time updates for dashboard/monitoring)
3. Persistent topology cache (SQLite for fast startup with historical data)
4. Cross-subnet discovery via Lantern registry (see LANTERN-0001)

---

## References

**RFCs & Standards:**
- mDNS: RFC 6762 (https://www.rfc-editor.org/rfc/rfc6762)
- DNS-SD: RFC 6763 (https://www.rfc-editor.org/rfc/rfc6763)

**Related ADRs:**
- LANTERN-0001: Central registry for multi-subnet deployments
- API-0002: Admin hierarchy and stone management
- STATE-0001: Stateless Moss design principles

**Implementation Files:**
- Self Entry: `src/moss/src/domain/self_topology.rs` (to create)
- Core: `src/moss/src/announcement.rs` (to update)
- Periodic: `src/moss/src/tasks/announcer.rs` (to update)
- UDP: `src/moss/src/discovery.rs` (to update)
- mDNS: `src/moss/src/mdns.rs`
- Topology: `src/moss/src/domain/topology.rs`
- Topology API: `src/moss/src/api/v1/garden.rs` (returns self entry + peer cache)
- Types: `src/common/src/types.rs` (add health field)
- Orchestration: `src/moss/src/bootstrap/run.rs` (to update)
