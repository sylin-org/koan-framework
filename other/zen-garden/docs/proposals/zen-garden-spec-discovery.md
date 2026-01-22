# Zen Garden Discovery Architecture

> "Stones whisper in the dark; the Lantern shows them shining."

## Overview

Zen Garden uses a dual-mode discovery architecture that adapts to network topology:

1. **Autonomous Mode** (Lantern-less) - Stones discover each other via broadcast
2. **Registered Mode** (With Lantern) - Lantern maintains canonical topology

## Discovery Modes

### Autonomous Mode: Stones Whisper in the Dark

In gardens without a Lantern, stones are "blind and chatty":

```
Stone-01 ──broadcast──> [LAN]
Stone-02 ──broadcast──> [LAN]     All stones hear all broadcasts
Stone-03 ──broadcast──> [LAN]     Each maintains local topology cache
```

**Behavior:**
- Stones periodically announce themselves (every 30-60 seconds)
- All stones listen on UDP port 7184
- Each stone maintains its own topology cache of peer stones
- Rake can query any stone's `/api/v1/garden` for full topology
- Fully decentralized, no single point of failure
- Limited to broadcast domain (doesn't cross subnets/VLANs)

**Traffic analysis (100 stones, 30s interval):**
- ~3.3 broadcasts/second network-wide
- ~1.7 KB/s bandwidth (negligible)
- ~50KB memory per stone for topology cache

### Registered Mode: The Lantern Shows Them Shining

When a Lantern is present, it becomes the topology authority:

```
┌─────────────────────────────────────────────────────────────┐
│  BOOTSTRAP PHASE                                            │
│                                                             │
│  Stone-01 ──announce──> [LAN] ──heard by──> Lantern        │
│  Lantern ──"I am Lantern"──> Stone-01 (via broadcast/API)  │
│  Stone-01: transitions to Registered mode, goes quiet       │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│  STEADY STATE                                               │
│                                                             │
│  Lantern: maintains canonical topology ledger               │
│  Stones: silent (no periodic broadcasts)                    │
│  Rake ──query──> Lantern ──topology──> Rake                │
└─────────────────────────────────────────────────────────────┘
```

**Lantern claims the garden via hybrid approach:**
1. **Broadcast heartbeat** - Lantern periodically broadcasts "I am Lantern at http://..."
2. **Direct API call** - When Lantern discovers a new stone, it calls the stone directly

**Stone state transitions:**
- `Autonomous` → `Registered`: Upon receiving Lantern claim (broadcast or API)
- `Registered` → `Autonomous`: If no Lantern heartbeat for 3× interval

**Rare re-announcements in Registered mode:**
- IP address change (immediate re-registration)
- Network reconnect after disconnection
- Joining a Pond (peer stones need to know)
- Lantern reconnect after timeout

## Platform-Specific Discovery

### Linux Rake: mDNS + UDP in Parallel

```
Discovery runs both methods concurrently:
┌─────────────────────────────────────────────────────┐
│  Thread 1: mDNS browse (_moss._tcp.local.)         │
│  Thread 2: UDP broadcast (255.255.255.255:7184)    │
│                                                     │
│  Results stream as they arrive, deduplicated       │
│  by endpoint, processed immediately                │
└─────────────────────────────────────────────────────┘
3. Lantern query                             ← Cross-subnet fallback

Results are merged in real-time, duplicates filtered by endpoint.
```

**Why combined approach:**
- Linux Moss announces via mDNS (`_moss._tcp.local.`)
- Windows Moss does NOT announce via mDNS (limited Windows support)
- UDP broadcast catches Windows stones that mDNS misses
- Ensures mixed Linux/Windows gardens are fully discovered

### Windows Rake: UDP Broadcast Only

```
Discovery order:
1. UDP broadcast                             ← All stones (Linux + Windows)
2. Lantern query                             ← Cross-subnet fallback
```

**Why UDP only for Windows:**
- mDNS support requires Bonjour (iTunes) or manual setup
- UDP broadcast works reliably on Windows LANs
- Both Linux and Windows Moss respond to UDP discovery
- Multi-interface handling via `get_lan_bind_address()`

### Platform Matrix

| Rake Platform | Discovery Method | Linux Moss | Windows Moss |
|---------------|------------------|------------|--------------|
| Linux         | mDNS             | ✅         | ❌           |
| Linux         | UDP broadcast    | ✅         | ✅           |
| Windows       | UDP broadcast    | ✅         | ✅           |

**Key insight:** Linux Rake must always do UDP broadcast to find Windows stones,
even if mDNS finds all Linux stones.

## Protocol Details

### Stone Announcement (UDP Broadcast)

**Request** (from Rake or peer stone):
```json
{
  "discover": "moss",
  "request_id": "019abc...",
  "requester": "rake-cli"
}
```

**Response** (from Moss):
```json
{
  "stone_name": "stone-01",
  "stone_endpoint": "http://192.168.1.111:7185"
}
```

**Ports:**
- `7184` - Moss UDP discovery listener
- `7185` - Moss HTTP API
- `7187` - Lantern UDP discovery listener

### Lantern Claim (Broadcast)

**Lantern heartbeat broadcast:**
```json
{
  "type": "lantern_claim",
  "lantern_endpoint": "http://192.168.1.100:7187",
  "garden_id": "home-garden",
  "claim_time": "2024-01-21T12:00:00Z"
}
```

### mDNS Service Registration

**Service type:** `_moss._tcp.local.`

**TXT records:**
- `stone_name` - Human-readable stone name
- `version` - Moss version
- `garden` - Garden ID (if registered with Lantern)

## Topology Cache Management

### Stone-side Cache (Autonomous Mode)

```rust
struct TopologyCache {
    stones: HashMap<String, StoneEntry>,
    last_cleanup: Instant,
}

struct StoneEntry {
    endpoint: String,
    last_seen: Instant,
    capabilities_summary: Option<CapabilitiesSummary>,
}
```

**TTL policy:**
- Entry valid for 3× announcement interval
- Stale entries marked as "potentially down"
- Entries removed after 5× interval with no announcement

### Rake-side Cache

```rust
// Tending cache (persisted)
~/.zen-garden/tending.json
{
  "stone_name": "stone-01",
  "endpoint": "http://192.168.1.111:7185",
  "tended_at": "2024-01-21T12:00:00Z"
}

// Stone cache (in-memory, per-session)
STONE_CACHE: HashMap<String, CachedStoneInfo>
```

**Fresh mode:** `garden-rake list fresh` or `garden-rake list --fresh`
- Clears tending cache
- Forces fresh UDP/mDNS discovery

## Scalability Considerations

| Garden Size | Recommended Mode | Notes |
|-------------|------------------|-------|
| 1-10 stones | Autonomous | Simple, no infrastructure needed |
| 10-100 stones | Either | P2P still manageable (~3 broadcasts/sec) |
| 100-200 stones | Lantern recommended | Reduces broadcast noise |
| 200+ stones | Lantern required | P2P traffic becomes significant |
| Multi-site | Lantern required | Broadcast doesn't cross sites |

## Implementation Status

- [x] UDP broadcast discovery (Moss + Rake)
- [x] mDNS announcement (Moss, Linux only)
- [x] mDNS service browse (Rake, Linux only) - `discover_moss_mdns()`
- [x] Platform-aware parallel discovery - `discover_moss_auto()` (mDNS + UDP concurrent)
- [x] Multi-interface bind fix (`get_lan_bind_address()`)
- [x] Tending cache with TTL
- [x] Fresh mode flag (`--fresh` / `fresh` keyword)
- [ ] Stone topology cache (Moss) - for Autonomous mode
- [ ] Lantern claim protocol - broadcast + direct API
- [ ] Autonomous ↔ Registered mode transitions
- [ ] Lantern heartbeat broadcast
