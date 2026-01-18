# LANTERN-0001: Service Registry Architecture

**Status:** Accepted  
**Date:** 2026-01-16  
**Context:** Zen Garden stones rely on UDP broadcast for peer discovery, which fails across subnets/VLANs and limits operational visibility

---

## Problem

Current P2P discovery model has critical limitations:

### Network Topology Issues
- **UDP broadcasts limited to same subnet** - Windows clients can't discover Linux stones across VLANs
- **No cross-subnet discovery** - Multi-VLAN deployments require manual endpoint configuration
- **Complex enterprise networks** - Firewalls and multiple subnets block broadcasts

### State Management Issues
- **No persistent topology** - Ephemeral UDP responses, no "what's in the garden?" query
- **Race conditions** - Concurrent discovery requests create chaos
- **No failure detection** - Can't distinguish offline vs. unreachable stones
- **Blind collaboration** - Multiple operators can't see each other's actions

### Operational Impact
- Windows operators rely on unreliable UDP (frequently fails)
- Multi-subnet deployments require `--at` flag on every command
- Troubleshooting is reactive (no proactive health monitoring)
- No centralized view of garden state

---

## Decision

Implement **Lantern** - a centralized HTTP-based service registry with distributed high availability.

### Core Design Principles

1. **Control Plane Directory, Not Data Plane Gateway**
   - Lantern resolves "where is mongodb?" → returns connection string
   - Operations execute stone-to-stone (direct HTTP/native protocols)
   - Lantern observes but doesn't mediate

2. **Multi-Active for Resilience**
   - 2-3 concurrent active Lanterns acceptable
   - No strict single-primary enforcement
   - BLAKE3 hash-based election reduces active count naturally
   - Suppression-based coordination (no complex consensus)

3. **Eventual Consistency over Strong Consistency**
   - Accept brief windows where stones see different Lanterns
   - Local network convergence sufficient (seconds, not milliseconds)
   - Stones remain source of truth (re-register after Lantern failure)

4. **Graceful Degradation**
   - UDP fallback if Lantern unreachable
   - Zero breaking changes to existing P2P mode
   - Optional opt-in (set `LANTERN_ENDPOINT` env var)

---

## Architecture

### Components

**Lantern Server:**
- Rust + Axum HTTP server
- Port 7186 (HTTP API)
- Port 7187 (UDP election messages)
- SQLite persistence (single-row JSON topology blob)
- In-memory HashMap for microsecond lookups

**Election Protocol:**
- States: Dormant, Candidate, Active
- Activation triggers:
  1. No LANTERN_ANNOUNCEMENT heard for 15s (passive timeout)
  2. Moss "Is anyone there?" UDP broadcast (active detection)
- Delay calculation: `blake3::hash(lantern_name + lan_ip + announcement_id)[0] * 10ms`
- Suppression: Candidates hearing LANTERN_ANNOUNCEMENT suppress their own
- Result: 2-3 active Lanterns self-regulate without coordination

**State Synchronization:**
- Active Lanterns: Broadcast LANTERN_ANNOUNCEMENT every 10s (UDP port 7187)
- Dormant Lanterns: Pull topology JSON every 30s via HTTP `GET /api/topology`
- UDP listener: All Lanterns capture stone announcements from "Is anyone there?" broadcasts
- On promotion: New active broadcasts LANTERN_DISCOVERY, stones re-register

**Timing:**
- Probe interval: 5s (dormant → active health check)
- Election delay: 0-2550ms (BLAKE3-based)
- Total failover: 5-8s
- TTL: 60s (stone registration timeout)
- Heartbeat: 45s (stone re-registration interval)

### API Specification

**Stone Registration:**
```http
POST /api/register
Authorization: Bearer <JWT>

{
  "stone_name": "stone-01",
  "endpoint": "http://192.168.1.50:7185",
  "capabilities": {...},
  "services": [
    {"name": "mongodb", "type": "database", "status": "running"}
  ]
}

Response 200: {"ttl_seconds": 60, "next_heartbeat_seconds": 45}
```

**Service Resolution:**
```http
GET /api/resolve?service=mongodb

Response 200:
{
  "stone_name": "stone-01",
  "offering": "mongodb",
  "connection_string": "mongodb://stone-01.local:17371"
}

Response 404: {"error": {"code": "SERVICE_NOT_AVAILABLE", ...}}
```

**Topology Query:**
```http
GET /api/stones

Response 200:
[
  {
    "name": "stone-01",
    "endpoint": "http://192.168.1.50:7185",
    "status": "online",
    "services": ["mongodb", "redis"],
    "last_seen": "2026-01-16T18:30:00Z"
  }
]
```

**Topology Sync (Dormant Lanterns):**
```http
GET /api/topology

Response 200 (Active):
{
  "stones": {...},
  "last_updated": "2026-01-16T18:30:05Z"
}

Response 503 (Dormant):
{
  "error": "Not primary",
  "primary_endpoint": "http://192.168.1.105:7186"
}
```

**Event Stream (Dashboard):**
```http
GET /api/events/stream
Accept: text/event-stream

Response 200 (SSE):
event: stone_online
data: {"stone": "stone-01", "timestamp": "..."}
```

**Health Check:**
```http
GET /api/health

Response 200:
{
  "status": "healthy",
  "role": "active",  // or "dormant"
  "stones_online": 3,
  "uptime_seconds": 86400
}
```

### Authentication

**Bearer Tokens (JWT per SECURITY-SPEC.md):**
- Format: `{stone_name, operation, timestamp, nonce, expires_at}`
- Signature: `HMAC-SHA256(payload, stone_private_key)`
- TTL: 5 minutes (short mode default)
- Validation: Axum middleware on `/api/*` endpoints
- Nonce tracking: Prevent replay attacks in long mode

**Security Model:**
- Internal network only (bind 0.0.0.0:7186, no external exposure)
- Network segmentation via firewall rules
- Stone private key signs requests (Ed25519 from SECURITY-SPEC.md)
- Future: mTLS for cross-site federation (deferred to Phase 2+)

---

## Election Protocol Details

### BLAKE3-Based Suppression

**Properties:**
- **Deterministic:** Same inputs always produce same delay
- **Unpredictable:** Different announcement_id produces different order
- **Self-regulating:** Announcements prevent storms, 2-3 active maintained
- **No coordination:** Each Lantern calculates independently

**Algorithm:**
```rust
let hash = blake3::hash(format!("{}{}{}", 
    lantern_name, 
    lan_ip, 
    announcement_id
));
let delay_ms = (hash.as_bytes()[0] as u64) * 10; // 0-2550ms
```

**Election Flow:**
1. Dormant waits for trigger (announcement timeout OR "Is anyone there?")
2. Dormant → Candidate: Generate UUIDv7 announcement_id
3. Calculate BLAKE3 delay (0-2550ms)
4. Listen for LANTERN_ANNOUNCEMENT during wait
5. If announcement heard → Suppress own, return to Dormant
6. If delay expires → Broadcast LANTERN_ANNOUNCEMENT, promote to Active
7. Active announces every 10s, keeps others dormant

**Rationale:**
- Consistent with existing moss UDP election behavior
- Simple, no complex consensus (Raft/Paxos overhead avoided)
- Tested pattern in production (moss discovery uses similar logic)

---

## Data Model

**In-Memory Topology:**
```rust
struct GardenTopology {
    stones: HashMap<String, StoneState>,
    last_updated: DateTime<Utc>,
}

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

**SQLite Persistence:**
```sql
-- Single table: persist JSON blob
CREATE TABLE topology (
  id INTEGER PRIMARY KEY CHECK (id = 1),  -- Only one row
  state JSON NOT NULL,
  last_updated TIMESTAMP NOT NULL
);

-- Event log for audit trail
CREATE TABLE events (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  event JSON NOT NULL
);
```

**Rationale:**
- HashMap lookups: microseconds vs SQL joins: milliseconds
- Serialize entire graph for easy backup/debugging
- Simple schema, no migration complexity

---

## Integration Points

### Moss Auto-Registration

**Implementation:**
```rust
// In moss main.rs startup
if let Ok(lantern) = env::var("LANTERN_ENDPOINT") {
    tokio::spawn(lantern_registration_loop(lantern, capabilities.clone()));
}

async fn lantern_registration_loop(endpoint: String, caps: HardwareCapabilities) {
    let client = reqwest::Client::new();
    loop {
        let token = generate_bearer_token(&stone_name, "register").await;
        match client.post(format!("{}/api/register", endpoint))
            .header("Authorization", format!("Bearer {}", token))
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

**Behavior:**
- Non-blocking: Fire-and-forget tokio::spawn
- Graceful degradation: Logs warning if Lantern unreachable
- "Is anyone there?" detection: Moss UDP broadcast triggers topology announcements captured by all Lanterns

### Rake Lantern-First Discovery

**Implementation:**
```rust
// In discovery.rs
pub async fn discover_via_lantern(client: &reqwest::Client, service: &str) -> Result<String> {
    // Check cached stone for lantern_endpoint
    if let Some(lantern) = get_lantern_endpoint_from_cache() {
        let token = generate_bearer_token(&stone_name, "resolve").await;
        let url = format!("{}/api/resolve?service={}", lantern, service);
        if let Ok(resp) = client.get(&url)
            .header("Authorization", format!("Bearer {}", token))
            .send()
            .await {
            if resp.status().is_success() {
                let resolved: ResolveResponse = resp.json().await?;
                return Ok(resolved.connection_string);
            }
        }
    }
    
    // Fallback: UDP broadcast
    discover_moss()
}
```

**Cache Strategy:**
- 90s TTL (reuse existing hot cache)
- Lantern endpoint discovered from stone capabilities
- Transparent fallback to UDP if Lantern unavailable

---

## Deployment Model

**Bundled with Stone Installation:**
- Lantern binary installed alongside moss/rake
- Systemd service: `lantern.service`
- Enable/disable via CLI: `garden-rake place lantern`
- Default: Disabled (opt-in via `LANTERN_ENDPOINT` env var)

**Configuration:**
```toml
# /etc/zen-garden/lantern.toml
lantern_name = "lantern-01"
http_port = 7186
udp_port = 7187
log_level = "info"
```

**CLI Commands:**
- `garden-rake place lantern` - Enable Lantern on local stone
- `garden-rake place lantern at {stone}` - Enable remote via SSH
- `garden-rake show lanterns` - List all Lanterns, highlight active

---

## Alternatives Considered

### 1. Single Primary with Raft Consensus
**Rejected:** Complexity overkill for home lab scale. Raft requires 3-5 nodes minimum, adds significant implementation burden. Multi-active with eventual consistency sufficient for local networks.

### 2. Federated Multi-Lantern with Gossip Protocol
**Deferred:** Future consideration for multi-site deployments. Current scope: single-site, same broadcast domain.

### 3. Keep UDP-Only Discovery
**Rejected:** Doesn't solve cross-subnet problem. Windows reliability issues persist. No centralized topology view.

### 4. Consul/etcd Integration
**Rejected:** External dependencies violate "batteries included" philosophy. Adds operational complexity (separate installation, version management).

---

## Migration Path

**Phase 1: Parallel Operation (2 weeks)**
- Lantern deployed alongside UDP discovery
- UDP remains primary discovery method
- Lantern opt-in via `LANTERN_ENDPOINT` env var
- Zero breaking changes

**Phase 2: Lantern Preferred (4 weeks)**
- Rake defaults to Lantern-first discovery
- UDP automatic fallback
- Dashboard UI released (Alpine.js)

**Phase 3: UDP Deprecation (8+ weeks)**
- Lantern required for cross-subnet deployments
- UDP retained for simple single-subnet gardens
- Documentation recommends Lantern for production

---

## Success Metrics

**Phase 1 (Core Registry):**
- ✅ Discovery success rate >99% via Lantern (vs ~85% UDP)
- ✅ Cross-subnet discovery 100% success (vs 0% UDP)
- ✅ Latency <50ms for `/api/resolve` (p95)
- ✅ Graceful degradation verified (Lantern offline → UDP works)
- ✅ Election convergence <8s (probe + delay)
- ✅ 2-3 active Lanterns maintained without coordination

**Phase 2 (Dashboard):**
- ✅ Time to insight <5s (operator identifies offline stone)
- ✅ SSE update latency <3s (event → dashboard)
- ✅ 80% operator adoption (dashboard used daily)

**Phase 3 (Active Monitoring):**
- ✅ Failure detection <60s (stone offline detected)
- ✅ False positive rate <5% (network blips don't trigger alerts)
- ✅ Historical query performance <100ms (24h metrics, p95)

---

## Risks and Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| **SQLite corruption** | Medium | Low | WAL mode, regular backups, JSON export |
| **Registration storms** | Medium | Medium | Rate limiting (1/stone/min), exponential backoff |
| **Network partition** | Medium | High | Expected; stones continue, Lantern marks offline |
| **Split-brain topology** | Low | Medium | Multiple active Lanterns tolerated (2-3 is resilience) |
| **Memory growth** | Medium | Low | Event log 7-day retention, prune offline stones >30d |
| **Election storms** | Low | Low | BLAKE3 staggered delays prevent simultaneous announcements |

---

## Future Considerations

**Multi-Segment Support (Phase 3+):**
- Per-segment Lanterns (one per subnet/datacenter)
- Federation protocol: Cross-segment discovery via HTTP
- Hierarchical topology: Segment-local → Global aggregated view

**Multi-Tenant Support (Phase 3+):**
- Namespace isolation per tenant
- RBAC for cross-tenant visibility
- Separate topology views per organization

**Dashboard Phase 2:**
- Alpine.js static client (15KB, no build step)
- Embedded via `include_dir!("src/lantern/static")`
- SSE real-time updates from any active Lantern
- Charts: Stone count, service distribution, CPU/memory trends

---

## References

- **Proposal:** [docs/proposals/LANTERN-SERVICE-PROPOSAL.md](../proposals/LANTERN-SERVICE-PROPOSAL.md)
- **Security:** [docs/SECURITY-SPEC.md](../SECURITY-SPEC.md) - Bearer token implementation
- **Ports:** [docs/PORT-ALLOCATION.md](../PORT-ALLOCATION.md) - Port 7186 (HTTP), 7187 (UDP)
- **Technical Spec:** [docs/TECHNICAL-SPEC.md](../TECHNICAL-SPEC.md) - Moss integration points

---

## Decision Outcome

**Accepted** - Proceed with Phase 1 implementation (Core Registry + HA Election)

**Rationale:**
- Solves critical cross-subnet discovery problem
- Provides operational visibility (centralized topology)
- Maintains backward compatibility (UDP fallback)
- Simple enough for home lab (no Raft/etcd complexity)
- Multi-active design eliminates SPOF without coordination overhead
- BLAKE3 election consistent with existing moss behavior

**Next Steps:**
1. Scaffold `src/lantern/` crate structure
2. Define shared types in `zen-common`
3. Implement core registry (in-memory + SQLite)
4. Implement BLAKE3 election state machine
5. Integrate moss auto-registration
6. Integrate rake Lantern-first discovery
7. Docker Compose integration tests (multi-subnet)
8. Documentation updates
