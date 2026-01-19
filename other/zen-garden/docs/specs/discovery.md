# Discovery Protocol Specification

**Purpose:** Technical specification for mDNS service discovery, TXT records, and client resolution.  
**Audience:** Developers implementing discovery, operators troubleshooting network issues.

---

## Table of Contents

1. [Overview](#overview)
2. [mDNS Service Types](#mdns-service-types)
3. [TXT Record Schema](#txt-record-schema)
4. [Client Resolution Algorithm](#client-resolution-algorithm)
5. [Discovery Flow](#discovery-flow)
6. [UDP Broadcast Discovery](#udp-broadcast-discovery)
7. [Connection String Resolution](#connection-string-resolution)

---

## Overview

Zen Garden uses two discovery mechanisms:

- **mDNS (Linux/macOS):** Zero-config automatic discovery via Multicast DNS
- **UDP Broadcast (Windows):** Broadcast-based discovery for Windows without mDNS

Both protocols enable clients to discover Stones and services without manual configuration.

---

## mDNS Service Types

### Moss Self-Announcement

**Service Type:** `_moss._tcp.local.`  
**Instance Name:** `<stone-name>-moss._moss._tcp.local.`  
**Purpose:** Announce Moss daemon for Rake CLI discovery

**Example:**

```
stone-01-moss._moss._tcp.local.
Port: 7185
TXT: stone_name=stone-01
     version=0.1.0
     api_port=7185
     health=healthy
```

### Native Service Announcement

**Service Type:** `_koan-stone._tcp.local.`  
**Instance Name:** `<stone-name>-<offering>._koan-stone._tcp.local.`  
**Purpose:** Announce native database/service on its vendor protocol

**Example: MongoDB Native**

```
stone-01-mongodb._koan-stone._tcp.local.
Port: 27017
TXT: offering=mongodb
     port=27017
     protocol=native
     version=7.0.4
     categories=database,document-database
     health=healthy
     priority=50
```

### Agnostic Sidecar Announcement

**Service Type:** `_koan-stone._tcp.local.`  
**Instance Name:** `<stone-name>-<offering>-agnostic._koan-stone._tcp.local.`  
**Purpose:** Announce HTTP REST API wrapper for database-neutral access

**Example: MongoDB Agnostic Sidecar**

```
stone-01-mongodb-agnostic._koan-stone._tcp.local.
Port: 8080
TXT: offering=mongodb-agnostic
     port=8080
     protocol=agnostic
     version=1.0.2
     backend_service_version=7.0.4
     categories=database,document-database
     set_mode=database
     capabilities=crud,query,filter,bulk,transactions
     health=healthy
     priority=50
```

---

## TXT Record Schema

### Moss Daemon TXT Records

| Field        | Required | Example        | Description                                |
|--------------|----------|----------------|--------------------------------------------|
| `stone_name` | Yes      | `stone-01`     | Unique Stone identifier                    |
| `version`    | Yes      | `0.1.0`        | Moss daemon version                        |
| `api_port`   | Yes      | `7185`         | HTTP API port                              |
| `health`     | Yes      | `healthy`      | Health status: `healthy`, `degraded`, `offline` |

### Native Service TXT Records

| Field        | Required | Example                  | Description                                |
|--------------|----------|--------------------------|--------------------------------------------|
| `offering`   | Yes      | `mongodb`                | Offering name from template                |
| `port`       | Yes      | `27017`                  | Native protocol port                       |
| `protocol`   | Yes      | `native`                 | Always `native` for native services        |
| `version`    | Yes      | `7.0.4`                  | Service version (MongoDB 7.0.4)            |
| `categories` | No       | `database,document-database` | Comma-separated category tokens      |
| `health`     | Yes      | `healthy`                | Health status                              |
| `priority`   | No       | `50`                     | Priority for service selection (0-100)     |

### Agnostic Sidecar TXT Records

| Field                      | Required | Example                  | Description                                |
|----------------------------|----------|--------------------------|--------------------------------------------|
| `offering`                 | Yes      | `mongodb-agnostic`       | Offering name with `-agnostic` suffix      |
| `port`                     | Yes      | `8080`                   | HTTP REST API port                         |
| `protocol`                 | Yes      | `agnostic`               | Always `agnostic` for sidecars             |
| `version`                  | Yes      | `1.0.2`                  | Sidecar version                            |
| `backend_service_version`  | Yes      | `7.0.4`                  | Backend service version (MongoDB 7.0.4)    |
| `categories`               | No       | `database,document-database` | Comma-separated category tokens      |
| `set_mode`                 | No       | `database`               | Isolation mode: `database` or `collection` |
| `capabilities`             | No       | `crud,query,filter,bulk` | Comma-separated API capabilities           |
| `health`                   | Yes      | `healthy`                | Health status                              |
| `priority`                 | No       | `50`                     | Priority for service selection (0-100)     |

---

## Client Resolution Algorithm

### Goal

Resolve `zen-garden:mongodb` or `zen-garden:database` to a native connection string.

### Algorithm

```
1. Parse connection string:
   - Format: zen-garden:<service-type>[/<database>]
   - Example: zen-garden:mongodb/myapp

2. Query mDNS:
   - Browse _koan-stone._tcp.local.
   - Collect all service instances with TXT records

3. Filter by service type:
   - Known service (mongodb) → Filter protocol=native, offering=mongodb
   - Generic category (database) → Filter protocol=agnostic, categories CONTAINS database

4. Filter by tags (if specified):
   - zen-garden:database?tags=document → Filter categories CONTAINS document

5. Select best endpoint:
   - Rank by health: healthy > degraded > offline
   - Rank by priority: Higher priority first
   - Rank by response time: Fastest responder first

6. Build native connection string:
   - Native: mongodb://<stone-ip>:27017/myapp
   - Agnostic: http://<stone-ip>:8080/v1/data/myapp
```

---

## Discovery Flow

### Rake CLI Discovery Flow

**Priority order:**

1. **Localhost cache query** (if Rake running on Stone)
   - Query: `GET localhost:7185/api/garden/stones`
   - Latency: <1ms (zero discovery overhead)

2. **UDP broadcast discovery** (Windows-compatible)
   - Broadcast: UDP 255.255.255.255:3004
   - Response: Unicast with Stone topology
   - Latency: <100ms

3. **mDNS browse** (Linux/macOS)
   - Browse: `_moss._tcp.local.`
   - Discover all Moss daemons
   - Latency: <50ms

4. **Lantern HTTP query** (cross-subnet fallback)
   - Query: `GET <lantern-endpoint>/api/garden/stones`
   - Works across subnets
   - Latency: <200ms

5. **Manual `--at` flag** (explicit bypass)
   - Example: `garden-rake list --at stone-01`
   - Skips all discovery

### Application Client Discovery Flow

**Priority order:**

1. **Specific service (native protocol):**
   - Connection string: `zen-garden:mongodb/myapp`
   - Query mDNS: `_koan-stone._tcp.local.` → Filter `offering=mongodb` and `protocol=native`
   - Resolve to: `mongodb://10.0.1.10:27017/myapp`

2. **Category service (agnostic HTTP):**
   - Connection string: `zen-garden:database/myapp`
   - Query mDNS: `_koan-stone._tcp.local.` → Filter `categories CONTAINS database` and `protocol=agnostic`
   - Resolve to: `http://10.0.1.10:8080/v1/data/myapp`

---

## UDP Broadcast Discovery

**Purpose:** Windows-compatible discovery without mDNS support

### Protocol Flow

**1. Rake broadcasts request:**

```json
UDP 255.255.255.255:3004
{
  "discover": "moss",
  "request_id": "01933b83-1234-7abc-9000-abcdef123456",
  "requester": "rake-cli"
}
```

**2. All Moss daemons calculate election delay:**

```rust
// Election algorithm (prevents reply storm)
let hash = blake3::hash(format!("{}{}", stone_name, request_id).as_bytes());
let delay_ms = (hash.as_bytes()[0] as u64) * 10; // 0-2550ms
tokio::time::sleep(Duration::from_millis(delay_ms)).await;
```

**3. First responder unicast to requester:**

```json
UDP <requester-ip>:3005
{
  "stone_name": "stone-01",
  "stone_endpoint": "http://10.0.1.10:7185",
  "lantern_endpoint": "http://10.0.1.5:7184",
  "moss_version": "0.1.0"
}
```

**4. Rake queries Stone for full topology:**

```http
GET http://10.0.1.10:7185/api/garden/stones
```

### Benefits

- **Zero-discovery common case:** 90% of Rake invocations hit localhost cache (<1ms)
- **Windows first-class:** UDP broadcast works without mDNS daemon
- **No Lantern dependency:** Rake can discover Stones without centralized registry
- **Hot cache always available:** Moss maintains current topology via UDP broadcasts
- **Single query reveals all:** UDP response includes Lantern endpoint for full garden

### Configuration

```toml
# /etc/zen-garden/garden-moss.toml
[discovery]
udp_broadcast_port = 3004
udp_broadcast_timeout = 3000  # ms
udp_broadcast_retry = 3
election_hash_algorithm = "blake3"
```

---

## Connection String Resolution

### Connection String Format

```
zen-garden:<service-type>[/<database>][?options]
```

**Examples:**

- `zen-garden:mongodb` → Native MongoDB (any database)
- `zen-garden:mongodb/myapp` → Native MongoDB (myapp database)
- `zen-garden:database` → Agnostic HTTP API (any database)
- `zen-garden:database/myapp` → Agnostic HTTP API (myapp set)
- `zen-garden:document-database?tags=transactions` → Filter by tags

### Resolution Steps

**For native protocols:**

```python
# Connection string: zen-garden:mongodb/myapp

1. Query mDNS: _koan-stone._tcp.local.
2. Filter: protocol=native AND offering=mongodb
3. Select best: health > priority > latency
4. Resolve: mongodb://10.0.1.10:27017/myapp
```

**For agnostic HTTP:**

```python
# Connection string: zen-garden:database/myapp

1. Query mDNS: _koan-stone._tcp.local.
2. Filter: protocol=agnostic AND categories CONTAINS database
3. Select best: health > priority > latency
4. Resolve: http://10.0.1.10:8080/v1/data/myapp
```

### Client Library Support

**Planned client libraries:**

- **Python:** `zen_garden.connect("zen-garden:mongodb/myapp")`
- **JavaScript/Node.js:** `await connect("zen-garden:mongodb/myapp")`
- **.NET/C#:** `ZenGarden.Connect("zen-garden:mongodb/myapp")`

**Library responsibilities:**

1. Parse connection string
2. Query mDNS or UDP broadcast
3. Cache resolved endpoint (TTL: 5 minutes)
4. Reconnect on failure
5. Fallback to alternate endpoint

---

## Next Steps

- **Moss daemon specification:** [moss-daemon.md](moss-daemon.md)
- **Rake CLI specification:** [rake-cli.md](rake-cli.md)
- **Service offerings specification:** [offerings.md](offerings.md)
- **Troubleshooting discovery:** [../guides/troubleshooting.md](../guides/troubleshooting.md)
