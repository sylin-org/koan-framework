# Zen Garden Topology Caching Specification

**Status**: Proposal
**Author**: Architecture Team
**Date**: 2026-01-22
**Relates to**: `zen-garden-spec-discovery.md`

## Overview

This specification defines the topology caching architecture for Zen Garden, enabling all components (Moss, Rake, Lantern) to share a consistent view of the garden's stone topology through eventual consistency.

## Goals

1. **Unified contracts**: All services use identical data models for topology
2. **Cache-first discovery**: Instant results from cache, progressive updates from live discovery
3. **Eventual consistency**: Stones maintain local topology views through passive observation
4. **Graceful degradation**: System works without Lantern, with stale cache, or during network partitions

## Non-Goals

- Real-time consistency (use Lantern for authoritative view)
- Cross-subnet discovery without Lantern
- Persistent historical topology (audit logging)

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Shared Topology Contract                      │
│         TopologyEntry { stone_id, endpoint, status, ... }       │
└─────────────────────────────────────────────────────────────────┘
                              │
        ┌─────────────────────┼─────────────────────┐
        ▼                     ▼                     ▼
   ┌─────────┐          ┌─────────┐          ┌─────────┐
   │  Rake   │          │  Moss   │          │ Lantern │
   │ (query) │          │ (cache) │          │ (auth)  │
   └────┬────┘          └────┬────┘          └────┬────┘
        │                    │                    │
        │◄── HTTP API ───────┤                    │
        │                    │                    │
        │   UDP Broadcast / Heartbeats            │
        │◄───────────────────┼───────────────────►│
        │                    │                    │
        │         mDNS (local network)            │
        │◄───────────────────┼───────────────────►│
```

### Discovery Layers

| Layer | Protocol | Cross-Subnet | Data Available | Speed |
|-------|----------|--------------|----------------|-------|
| **mDNS** | Multicast DNS | No | stone_id, stone_name (TXT records) | Immediate (passive) |
| **UDP** | Broadcast | No | Full DiscoveryResponse | ~100ms (on request) |
| **Lantern** | HTTP | Yes | Full topology + auth | ~50ms (cached) |

### Component Responsibilities

| Component | Role | Cache Type | Persistence |
|-----------|------|------------|-------------|
| **Moss** | Lurk-listener, topology authority for local stone | In-memory + disk | `garden-topology.json` |
| **Rake** | Query consumer, fallback to UDP | None (queries Moss) | N/A |
| **Lantern** | Authoritative registry for multi-subnet | In-memory + SQLite | `lantern.db` |

---

## Data Model

### TopologyEntry

The canonical topology entry used by all components:

```rust
/// garden_common::types

/// Unified topology entry for all components
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TopologyEntry {
    // === Identity (stable across renames) ===

    /// GUID v7, generated once per stone, persisted
    pub stone_id: String,

    /// Hostname, can change after first-boot initialization
    pub stone_name: String,

    // === Network ===

    /// HTTP API endpoint (e.g., "http://192.168.1.100:7185")
    pub endpoint: String,

    /// Moss version for compatibility checking
    pub moss_version: String,

    // === State ===

    /// Current connectivity status
    pub status: StoneStatus,

    /// Last successful contact (heartbeat or HTTP probe)
    pub last_seen: DateTime<Utc>,

    /// When this stone was first discovered
    pub first_seen: DateTime<Utc>,

    // === Optional Extended Data ===

    /// Hardware capabilities (loaded lazily via HTTP)
    #[serde(skip_serializing_if = "Option::is_none")]
    pub capabilities: Option<HardwareCapabilities>,

    /// Running services summary
    #[serde(skip_serializing_if = "Vec::is_empty", default)]
    pub services: Vec<ServiceSummary>,

    /// Lantern endpoint if known
    #[serde(skip_serializing_if = "Option::is_none")]
    pub lantern_endpoint: Option<String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum StoneStatus {
    /// Actively responding to probes
    Online,
    /// Not responding, TTL expired
    Offline,
    /// Initial state before validation
    Unknown,
    /// Reachable but health check failing
    Degraded,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServiceSummary {
    pub name: String,
    pub service_type: String,
    pub status: String,
}
```

### TopologySnapshot

Response format for `/api/v1/garden/topology`:

```rust
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TopologySnapshot {
    /// This stone's ID
    pub self_stone_id: String,

    /// All known stones (including self)
    pub entries: Vec<TopologyEntry>,

    /// When this snapshot was generated
    pub timestamp: DateTime<Utc>,

    /// Data source indicator
    pub source: TopologySource,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum TopologySource {
    /// From local Moss cache
    Cache,
    /// Fresh from Lantern registry
    Lantern,
    /// Aggregated from live UDP discovery
    Discovery,
}
```

---

## Protocol Extensions

### New Message Type: StoneHeartbeat

Periodic unprompted announcement for passive topology building:

```rust
/// Heartbeat announcement (broadcast, unprompted)
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StoneHeartbeat {
    /// Discriminator for message parsing
    pub message_type: String,  // Always "heartbeat"

    /// Stone identity
    pub stone_id: String,
    pub stone_name: String,
    pub stone_endpoint: String,
    pub moss_version: String,

    /// Monotonic counter for ordering/deduplication
    pub sequence: u64,

    /// Hash of services for quick change detection
    pub services_hash: String,

    /// Lantern endpoint if registered
    #[serde(skip_serializing_if = "Option::is_none")]
    pub lantern_endpoint: Option<String>,
}
```

### Message Discrimination

All messages share UDP port 7184. Listeners discriminate by field presence:

| Message Type | Discriminating Field |
|--------------|---------------------|
| `DiscoveryRequest` | `"discover"` field present |
| `DiscoveryResponse` | `"stone_endpoint"` field, no `"message_type"` |
| `StoneHeartbeat` | `"message_type": "heartbeat"` |

---

## Timing Constants

Standardized across all components:

```rust
/// garden_common::constants::topology

/// Heartbeat announcement interval
pub const HEARTBEAT_INTERVAL: Duration = Duration::from_secs(30);

/// Maximum jitter added to heartbeat interval
pub const HEARTBEAT_JITTER_MAX: Duration = Duration::from_secs(5);

/// Entry considered stale (show warning)
pub const TOPOLOGY_TTL_SOFT: Duration = Duration::from_secs(60);

/// Entry considered offline (3x heartbeat)
pub const TOPOLOGY_TTL_HARD: Duration = Duration::from_secs(90);

/// Entry removed from cache (5x heartbeat)
pub const TOPOLOGY_TTL_EXPIRY: Duration = Duration::from_secs(150);

/// HTTP probe timeout for cached stones
pub const PROBE_TIMEOUT_CACHED: Duration = Duration::from_millis(1500);

/// HTTP probe timeout for discovered stones
pub const PROBE_TIMEOUT_DISCOVERED: Duration = Duration::from_secs(3);

/// UDP discovery window for rake observe
pub const DISCOVERY_WINDOW: Duration = Duration::from_secs(5);

/// Cache write debounce delay
pub const CACHE_WRITE_DEBOUNCE: Duration = Duration::from_millis(500);

/// Cache periodic flush interval
pub const CACHE_FLUSH_INTERVAL: Duration = Duration::from_secs(30);
```

---

## Moss Topology Cache

### Cache Structure

```rust
/// In-memory topology cache with persistence
pub struct TopologyCache {
    /// Stone entries keyed by stone_id
    entries: HashMap<String, TopologyCacheEntry>,

    /// Sequence numbers for deduplication
    stone_sequences: HashMap<String, u64>,

    /// Dirty flag for persistence
    dirty: AtomicBool,

    /// Last persistence time
    last_flush: Mutex<Instant>,

    /// Persistence provider
    storage: JsonStorage<TopologyState>,
}

/// Cache entry with metadata
pub struct TopologyCacheEntry {
    pub entry: TopologyEntry,

    /// How this entry was discovered
    pub source: DiscoverySource,

    /// When last validated via HTTP probe
    pub last_validated: Option<DateTime<Utc>>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DiscoverySource {
    /// Passive mDNS observation (TXT records with stone_id)
    Mdns,
    /// Direct UDP response to our request
    UdpResponse,
    /// Overheard UDP response to another's request
    Lurked,
    /// Received heartbeat announcement
    Heartbeat,
    /// Direct HTTP probe
    HttpProbe,
    /// From Lantern registry
    Lantern,
}
```

### Persistence Format

File: `{CONFIG_DIR}/garden-topology.json`

```json
{
  "version": 1,
  "self_stone_id": "019abc12-...",
  "last_updated": "2026-01-22T12:00:00Z",
  "entries": [
    {
      "stone_id": "019abc34-...",
      "stone_name": "stone-02",
      "endpoint": "http://192.168.1.102:7185",
      "moss_version": "0.1.0.42",
      "status": "online",
      "last_seen": "2026-01-22T11:59:30Z",
      "first_seen": "2026-01-20T08:00:00Z",
      "services": [
        {"name": "mongodb", "service_type": "mongodb", "status": "running"}
      ]
    }
  ]
}
```

### Write Strategy

1. **Debounced on change**: Mark dirty, wait 500ms for more changes, then flush
2. **Periodic flush**: Every 30 seconds if dirty
3. **Graceful shutdown**: Immediate flush on SIGTERM/SIGINT
4. **Atomic writes**: Write to `.tmp`, sync, rename

### Corruption Recovery

On load failure:
1. Log warning with corruption details
2. Rename corrupted file to `.corrupted`
3. Start with empty topology
4. Rebuild through live discovery

---

## Moss mDNS Lurk-Listener

### Overview

mDNS provides immediate topology awareness on startup - before any UDP requests or Lantern queries. When Moss starts, it begins passively listening for mDNS service announcements from other stones.

```
┌─────────────────────────────────────────────────────────────────┐
│                      Moss Startup                                │
└─────────────────────────────────────────────────────────────────┘
                              │
         ┌────────────────────┼────────────────────┐
         ▼                    ▼                    ▼
┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│ Register mDNS   │  │ Start mDNS      │  │ Load persisted  │
│ service (self)  │  │ lurk-listener   │  │ topology cache  │
└─────────────────┘  └────────┬────────┘  └─────────────────┘
                              │
                              ▼
                    ┌─────────────────────┐
                    │ On ServiceResolved: │
                    │ - Extract stone_id  │
                    │ - Add to hot-cache  │
                    │ - Mark source=Mdns  │
                    └─────────────────────┘
```

### mDNS TXT Records

Each Moss announces via mDNS with these TXT record properties:

| Key | Value | Description |
|-----|-------|-------------|
| `stone_id` | GUID v7 | Stable identity (e.g., `019abc12-...`) |
| `stone_name` | hostname | Human-readable name (e.g., `stone-01`) |

### mDNS Listener Implementation

```rust
/// Background task to listen for mDNS announcements
pub async fn mdns_lurk_listener(topology_cache: Arc<TopologyCache>) {
    #[cfg(not(target_os = "windows"))]
    {
        use mdns_sd::{ServiceDaemon, ServiceEvent};

        let mdns = match ServiceDaemon::new() {
            Ok(daemon) => daemon,
            Err(e) => {
                tracing::warn!(error = ?e, "mDNS lurk-listener unavailable");
                return;
            }
        };

        let service_type = "_moss._tcp.local.";
        let receiver = match mdns.browse(service_type) {
            Ok(r) => r,
            Err(e) => {
                tracing::warn!(error = ?e, "mDNS browse failed");
                return;
            }
        };

        tracing::info!("mDNS lurk-listener started");

        loop {
            match receiver.recv_async().await {
                Ok(ServiceEvent::ServiceResolved(info)) => {
                    // Extract stone_id from TXT records
                    let stone_id = info.get_properties()
                        .iter()
                        .find(|p| p.key() == "stone_id")
                        .and_then(|p| p.val_str().map(|s| s.to_string()));

                    let stone_name = info.get_properties()
                        .iter()
                        .find(|p| p.key() == "stone_name")
                        .and_then(|p| p.val_str().map(|s| s.to_string()))
                        .unwrap_or_else(|| {
                            info.get_fullname()
                                .split('.')
                                .next()
                                .unwrap_or("unknown")
                                .to_string()
                        });

                    if let Some(ip) = info.get_addresses().iter().next() {
                        let endpoint = format!("http://{}:{}", ip, info.get_port());

                        tracing::debug!(
                            stone_id = ?stone_id,
                            stone_name = %stone_name,
                            endpoint = %endpoint,
                            "mDNS: Discovered neighbor stone"
                        );

                        // Add to topology cache with mDNS source
                        topology_cache.observe_mdns(
                            stone_id,
                            stone_name,
                            endpoint,
                        ).await;
                    }
                }
                Ok(ServiceEvent::ServiceRemoved(_, fullname)) => {
                    tracing::debug!(service = %fullname, "mDNS: Service removed");
                    // Optionally mark stone as potentially offline
                }
                Ok(_) => {}
                Err(e) => {
                    tracing::debug!(error = ?e, "mDNS recv error");
                }
            }
        }
    }

    #[cfg(target_os = "windows")]
    {
        tracing::debug!("mDNS lurk-listener not available on Windows");
    }
}
```

### Cache Integration

mDNS entries have partial data (no capabilities, no services). The cache handles this:

```rust
impl TopologyCache {
    /// Observe a stone via mDNS announcement
    pub async fn observe_mdns(
        &self,
        stone_id: Option<String>,
        stone_name: String,
        endpoint: String,
    ) {
        // Use stone_id as key if available, otherwise stone_name
        let cache_key = stone_id.as_ref()
            .unwrap_or(&stone_name)
            .clone();

        let mut entries = self.entries.write().await;

        // If entry exists, just update endpoint and last_seen
        // Don't downgrade a richer entry (e.g., from UDP)
        if let Some(existing) = entries.get_mut(&cache_key) {
            existing.entry.endpoint = endpoint;
            existing.entry.last_seen = Utc::now();
            // Keep existing source if it's richer than mDNS
            if existing.source == DiscoverySource::Mdns {
                // Still mDNS-only, update timestamp
            }
            return;
        }

        // New entry from mDNS
        entries.insert(
            cache_key,
            TopologyCacheEntry {
                entry: TopologyEntry {
                    stone_id: stone_id.unwrap_or_default(),
                    stone_name,
                    endpoint,
                    moss_version: String::new(), // Unknown via mDNS
                    status: StoneStatus::Unknown, // Needs HTTP validation
                    last_seen: Utc::now(),
                    first_seen: Utc::now(),
                    capabilities: None,
                    services: vec![],
                    lantern_endpoint: None,
                },
                source: DiscoverySource::Mdns,
                last_validated: None,
            },
        );

        self.dirty.store(true, Ordering::SeqCst);
    }
}
```

### Benefits

1. **Instant awareness**: Stones appear in cache before any active discovery
2. **Zero-cost passive**: No network requests required
3. **Early endpoint**: Even partial data (endpoint only) enables HTTP probe
4. **Startup optimization**: Hot-cache populated immediately on boot

---

## Moss UDP Lurk-Listener

### Passive Observation

Moss listens on UDP port 7184 for ALL traffic, not just requests:

```rust
// In udp_listener_inner, process all message types:

match parse_discovery_message(&buf[..len]) {
    DiscoveryMessage::Request(req) => {
        // Existing: respond to discovery request
        handle_discovery_request(req, addr).await;
    }
    DiscoveryMessage::Response(resp) => {
        // NEW: Cache observed response (someone else's discovery)
        topology_cache.observe_response(resp, DiscoverySource::Lurked).await;
    }
    DiscoveryMessage::Heartbeat(hb) => {
        // NEW: Cache heartbeat announcement
        topology_cache.observe_heartbeat(hb).await;
    }
    DiscoveryMessage::Unknown => {
        // Ignore malformed messages
    }
}
```

### Heartbeat Announcer

New background task for periodic announcements:

```rust
pub async fn heartbeat_announcer(state: AppState) {
    let mut sequence = 0u64;

    loop {
        // Jittered interval
        let jitter = calculate_jitter(&state.stone_id);
        tokio::time::sleep(HEARTBEAT_INTERVAL + jitter).await;

        sequence += 1;

        let heartbeat = StoneHeartbeat {
            message_type: "heartbeat".into(),
            stone_id: state.stone_id.clone(),
            stone_name: state.stone_name.clone(),
            stone_endpoint: state.api_endpoint.clone(),
            moss_version: VERSION.into(),
            sequence,
            services_hash: compute_services_hash(&state).await,
            lantern_endpoint: state.lantern_endpoint.clone(),
        };

        broadcast_udp(&heartbeat).await;
    }
}
```

---

## Moss Topology API

New endpoint for CLI consumption:

```
GET /api/v1/garden/topology
```

### Response

```json
{
  "data": {
    "self_stone_id": "019abc12-...",
    "entries": [...],
    "timestamp": "2026-01-22T12:00:00Z",
    "source": "cache"
  }
}
```

### Query Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `status` | string | Filter by status: `online`, `offline`, `all` (default: `online`) |
| `include_self` | bool | Include this stone in results (default: `true`) |
| `validate` | bool | HTTP probe all entries before returning (default: `false`) |

---

## Rake Observe Flow

### Optimized Discovery Pattern

```
┌─────────────────────────────────────────────────────────────────┐
│                        rake observe                              │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
                 ┌────────────────────────┐
                 │ Query local Moss API   │ ◄─── 200ms timeout
                 │ GET /garden/topology   │      (fail-fast)
                 └────────────────────────┘
                              │
              ┌───────────────┴───────────────┐
              │                               │
         [Success]                       [Timeout/Error]
              │                               │
              ▼                               ▼
┌─────────────────────────┐     ┌─────────────────────────┐
│ Display cached topology │     │ Fall back to UDP        │
│ with staleness markers  │     │ broadcast discovery     │
│ "(cached 45s ago)"      │     │ (existing behavior)     │
└─────────────────────────┘     └─────────────────────────┘
              │                               │
              ▼                               │
┌─────────────────────────┐                   │
│ Background: UDP + HTTP  │◄──────────────────┘
│ 1. Broadcast discover   │
│ 2. HTTP validate cached │
│ 3. Merge new responses  │
└─────────────────────────┘
              │
              ▼
┌─────────────────────────┐
│ Progressive update:     │
│ - Upgrade to "verified" │
│ - Add new discoveries   │
│ - Mark offline          │
└─────────────────────────┘
```

### Parallelization Strategy

| Aspect | Strategy |
|--------|----------|
| **Connection pool** | 2 per host, 20 total max |
| **Concurrency limit** | Semaphore: 10 for ≤10 stones, 15 for 11-30, 20 for 30+ |
| **Timeout (cached)** | 1.5 seconds |
| **Timeout (discovered)** | 3.0 seconds |
| **Result streaming** | mpsc channel with 32-entry buffer |
| **Display** | Mutex-guarded atomic stdout writes |

### Deduplication

Track seen endpoints to avoid duplicate fetches:

```rust
let seen_endpoints = Arc::new(Mutex::new(HashSet::<String>::new()));

// Before spawning fetch task:
{
    let mut seen = seen_endpoints.lock().unwrap();
    if seen.contains(&endpoint) {
        continue;  // Already fetching
    }
    seen.insert(endpoint.clone());
}
```

---

## Multi-Subnet Support

UDP broadcast does not cross subnets. For multi-subnet gardens:

```
┌─────────────────────────────────────────────────────────────────┐
│  Subnet A (192.168.1.0/24)                                      │
│                                                                 │
│  Stone-01 ←──broadcast──→ Stone-02                             │
│      │                        │                                 │
│      └────────┬───────────────┘                                 │
│               │                                                 │
│         ┌─────▼─────┐                                           │
│         │  Lantern  │ ◄── Authoritative cross-subnet registry   │
│         └─────┬─────┘                                           │
└───────────────│─────────────────────────────────────────────────┘
                │ (HTTP - routable)
┌───────────────│─────────────────────────────────────────────────┐
│  Subnet B (192.168.2.0/24)                                      │
│               │                                                 │
│         Stone-03 ──registers──→ Lantern                        │
│               │                                                 │
│         Local broadcast only reaches subnet B stones            │
└─────────────────────────────────────────────────────────────────┘
```

When Lantern is present:
1. Moss registers with Lantern (existing behavior)
2. Moss periodically fetches topology from Lantern
3. Lantern topology supplements local broadcast observations
4. Rake queries local Moss, which merges local + Lantern views

---

## Implementation Phases

### Phase 1: Shared Types (Low Risk)

1. Add `TopologyEntry`, `StoneStatus`, `TopologySnapshot` to `garden_common::types`
2. Add timing constants to `garden_common::constants`
3. Standardize TTLs across existing code

### Phase 1.5: mDNS Integration (Low Risk) ✓ IMPLEMENTED

1. ✅ Moss announces `stone_id` and `stone_name` in mDNS TXT records
2. ✅ Rake extracts `stone_id` from mDNS TXT records during discovery
3. Add mDNS lurk-listener to Moss for passive topology population

### Phase 2: Moss Topology Cache (Medium Risk)

1. Implement `TopologyCache` with in-memory storage
2. Add persistence to `garden-topology.json`
3. Add `/api/v1/garden/topology` endpoint
4. Update existing discovery listener to populate cache

### Phase 3: Moss Lurk-Listener (Medium Risk)

1. Extend UDP listener to process DiscoveryResponse from others
2. Add message type discrimination
3. Implement heartbeat observer (before announcer)

### Phase 4: Moss Heartbeat Announcer (Low Risk)

1. Add `StoneHeartbeat` message type
2. Implement heartbeat background task
3. Add jitter calculation

### Phase 5: Rake Integration (Low Risk)

1. Query local Moss topology API first
2. Implement parallel cache + discovery flow
3. Add progressive display with staleness markers

### Phase 6: Lantern Integration (Future)

1. Moss fetches topology from Lantern periodically
2. Merge Lantern topology with local observations
3. Lantern provides cross-subnet visibility

---

## Testing Strategy

### Unit Tests

- TopologyCache CRUD operations
- TTL expiration and cleanup
- Deduplication logic
- Persistence round-trip

### Integration Tests

- Multi-stone discovery simulation
- Cache + UDP discovery merge
- Graceful degradation (Moss down)
- Corruption recovery

### Load Tests

- 50-stone topology cache performance
- UDP broadcast storm handling
- Memory usage under churn

---

## Metrics (Future)

| Metric | Description |
|--------|-------------|
| `topology_cache_size` | Number of entries in cache |
| `topology_cache_hits` | API requests served from cache |
| `topology_heartbeats_sent` | Heartbeats broadcasted |
| `topology_heartbeats_received` | Heartbeats observed |
| `topology_stale_entries` | Entries past soft TTL |
| `topology_offline_entries` | Entries past hard TTL |

---

## Open Questions

1. **Heartbeat opt-out**: Should stones be able to disable heartbeat announcements (e.g., for security)?
2. **Topology size limits**: At what point do we require Lantern? Current spec suggests 200+ stones.
3. **Capability caching**: Should full `HardwareCapabilities` be cached, or just summary?
4. **Event streaming**: Add SSE endpoint for `rake watch` continuous monitoring?

---

## References

- `zen-garden-spec-discovery.md` - Base discovery protocol
- `zen-garden-spec-cricket.md` - Orchestration context
- `docs/reference/patterns/network-singleton-pattern.md` - UDP socket management
