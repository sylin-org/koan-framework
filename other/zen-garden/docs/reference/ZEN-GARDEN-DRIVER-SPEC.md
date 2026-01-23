# Zen Garden Driver/Adapter Specification

**Version:** 1.0
**Status:** Reference Implementation
**Last Updated:** 2026-01-23

This specification defines the protocols and behaviors required for implementing Zen Garden client drivers and adapters. Drivers enable applications to discover, connect to, and communicate with Moss service daemons running on stones.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Port Assignments](#2-port-assignments)
3. [Stone Discovery](#3-stone-discovery)
4. [Endpoint Resolution](#4-endpoint-resolution)
5. [Tending (Stone Pinning)](#5-tending-stone-pinning)
6. [Service Discovery](#6-service-discovery)
7. [Caching Strategy](#7-caching-strategy)
8. [API Response Format](#8-api-response-format)
9. [Error Handling](#9-error-handling)
10. [Health Checking](#10-health-checking)
11. [Type Definitions](#11-type-definitions)
12. [Implementation Checklist](#12-implementation-checklist)

---

## 1. Overview

### 1.1 Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Zen Garden                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   ┌─────────┐    UDP 7184     ┌─────────┐    UDP 7184          │
│   │  Rake   │◄───────────────►│  Moss   │◄─────────────┐       │
│   │ (CLI)   │                 │ (Stone) │              │       │
│   └────┬────┘                 └────┬────┘              │       │
│        │                           │                   │       │
│        │  HTTP 7185                │ HTTP 7185         │       │
│        └───────────────────────────┤                   │       │
│                                    │                   │       │
│                              ┌─────▼─────┐      ┌──────┴──────┐│
│                              │  Lantern  │      │    Moss     ││
│                              │ (Registry)│      │  (Stone 2)  ││
│                              │  :7186    │      │   :7185     ││
│                              └───────────┘      └─────────────┘│
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 Key Concepts

| Term | Description |
|------|-------------|
| **Stone** | A machine running the Moss daemon, hosting containerized services |
| **Moss** | The daemon process on each stone that manages services |
| **Lantern** | Optional central registry for cross-subnet discovery |
| **Tending** | Pinning a client to a specific stone for subsequent operations |
| **Chirp** | Periodic UDP broadcast by stones announcing their services |
| **Offering** | A service template/type (e.g., "mongodb", "redis") |

---

## 2. Port Assignments

```
UDP  7184   Discovery broadcast / Chirp announcements
HTTP 7185   Moss API (per-stone service management)
HTTP 7186   Lantern API (optional central registry)
```

### 2.1 Constants

```rust
pub const DISCOVERY_UDP: u16 = 7184;
pub const MOSS_HTTP: u16 = 7185;
pub const LANTERN_HTTP: u16 = 7186;
```

---

## 3. Stone Discovery

### 3.1 UDP Discovery Protocol

Stones are discovered via UDP broadcast on port 7184.

#### 3.1.1 Discovery Request

**Transport:** UDP broadcast to `255.255.255.255:7184`
**Format:** Raw JSON (no envelope wrapper)

```json
{
  "discover": "moss",
  "request_id": "req-1705421234567",
  "requester": "my-driver"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `discover` | string | Target daemon name. Always `"moss"` |
| `request_id` | string | Unique request ID. Format: `req-{timestamp_millis}` |
| `requester` | string | Identifier for your driver/client |

#### 3.1.2 Discovery Response

**Transport:** UDP unicast back to requester
**Format:** Raw JSON

```json
{
  "stone_id": "01936e8a-7b2c-7def-8123-456789abcdef",
  "stone_name": "stone-topaz-basin",
  "stone_endpoint": "http://192.168.1.100:7185",
  "moss_version": "0.1.0.42",
  "lantern_endpoint": "http://192.168.1.1:7186"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `stone_id` | string? | GUID v7, stable across hostname changes. **Cache this.** |
| `stone_name` | string | Current hostname/identifier (may change) |
| `stone_endpoint` | string | HTTP API endpoint for this stone |
| `moss_version` | string | Format: `{semver}.{build_number}` |
| `lantern_endpoint` | string? | Lantern registry URL if known |

#### 3.1.3 Response Timing

Stones use an election delay to prevent response storms:

```
delay_ms = hash(stone_name + request_id) % 2550
```

**Driver behavior:** Wait up to 3 seconds for responses. First responder wins for single-stone discovery; collect all for topology queries.

#### 3.1.4 Implementation Example

```python
import socket
import json
import time

def discover_stones(timeout_sec=3.0):
    """Discover all Moss stones on the local network."""
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
    sock.settimeout(0.1)  # Non-blocking reads

    request = {
        "discover": "moss",
        "request_id": f"req-{int(time.time() * 1000)}",
        "requester": "my-driver"
    }

    sock.sendto(json.dumps(request).encode(), ('255.255.255.255', 7184))

    stones = []
    deadline = time.time() + timeout_sec

    while time.time() < deadline:
        try:
            data, addr = sock.recvfrom(2048)
            response = json.loads(data.decode())
            stones.append(response)
        except socket.timeout:
            continue

    sock.close()
    return stones
```

### 3.2 Chirp Protocol (Passive Discovery)

Stones broadcast their state every 30 seconds. Drivers MAY listen for chirps to maintain a live topology cache without active polling.

#### 3.2.1 Chirp Envelope

**Transport:** UDP broadcast to `255.255.255.255:7184`
**Format:** JSON with envelope wrapper

```json
{
  "type": "stone_chirp",
  "data": {
    "stone_id": "01936e8a-7b2c-7def-8123-456789abcdef",
    "stone_name": "stone-topaz-basin",
    "endpoint": "http://192.168.1.100:7185",
    "moss_version": "0.1.0.42",
    "services": [
      {
        "name": "mongodb",
        "offering": "mongodb",
        "category": "database",
        "status": "Running"
      }
    ]
  }
}
```

#### 3.2.2 Chirp Timing

| Event | Interval |
|-------|----------|
| Periodic chirp | 30 seconds |
| Service state change | Immediate |
| Keep-alive (no change) | 5 minutes |

---

## 4. Endpoint Resolution

Drivers MUST implement a resolution chain to convert user-specified targets into HTTP endpoints.

### 4.1 Resolution Priority

```
1. Explicit target (user-provided URL or stone name)
2. Environment variable (GARDEN_STONE)
3. Tending state (cached stone preference)
4. Auto-discovery (UDP broadcast, first responder)
```

### 4.2 Target Format Normalization

Drivers MUST accept these target formats:

| Input | Normalization |
|-------|---------------|
| `http://192.168.1.100:7185` | Use as-is |
| `https://stone.example.com` | Use as-is |
| `192.168.1.100:7185` | Prepend `http://` |
| `192.168.1.100` | Append `:7185`, prepend `http://` |
| `stone-01.local` | Append `:7185`, prepend `http://` |
| `stone-01` | Resolve via mDNS, UDP discovery, or Lantern |

### 4.3 Stone Name Resolution

When given a bare stone name (no dots, no port), resolve in order:

```
1. Check local cache (by stone_name or stone_id, case-insensitive)
2. Try mDNS: http://{name}.local:7185
3. UDP discovery (match by stone_name or stone_id)
4. Lantern query (if available)
```

**Case sensitivity:** All name comparisons MUST be case-insensitive. Stone IDs are also valid resolution targets.

### 4.4 Implementation Example

```python
async def resolve_endpoint(target: str | None, cache: StoneCache) -> str:
    """Resolve a target specification to an HTTP endpoint."""

    # Priority 1: Explicit target
    if target:
        return normalize_target(target)

    # Priority 2: Environment variable
    if env_stone := os.environ.get('GARDEN_STONE'):
        return normalize_target(env_stone)

    # Priority 3: Tending state
    if tending := load_tending_state():
        if await is_reachable(tending.endpoint):
            return tending.endpoint
        # Stone offline - fall through to discovery

    # Priority 4: Auto-discovery
    stones = await discover_stones(timeout=3.0)
    if stones:
        return stones[0]['stone_endpoint']

    raise NoStonesFoundError()
```

---

## 5. Tending (Stone Pinning)

Tending persists a stone preference across client sessions. Once tended, the client automatically targets that stone until:
- The stone becomes unreachable
- The user explicitly changes tending
- The user specifies `--at` override

### 5.1 Tending State Format

**Location:** `~/.zen-garden/.tending`
**Format:** JSON

```json
{
  "stone_name": "stone-topaz-basin",
  "endpoint": "http://192.168.1.100:7185",
  "last_seen": "2026-01-23T12:34:56.789Z"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `stone_name` | string | Display name of tended stone |
| `endpoint` | string | HTTP endpoint URL |
| `last_seen` | ISO 8601 | When tending was last written (informational) |

### 5.2 Tending Behavior

**No TTL:** Tending does NOT expire. Validity is determined by reachability at use time.

**Reachability check:** Before using cached tending, verify stone is reachable:

```
GET {endpoint}/health
Timeout: 2 seconds
Success: HTTP 2xx
```

**Fallback:** If tended stone is unreachable:
1. Warn user: "Stone is sleeping (offline). Picking a new stone..."
2. Fall through to auto-discovery
3. Do NOT clear tending (user may want to return later)

### 5.3 Setting Tending

```
# By stone name (resolved automatically)
garden-rake tend stone-topaz-basin
garden-rake tend STONE-TOPAZ-BASIN  # Case-insensitive

# By stone_id
garden-rake tend 01936e8a-7b2c-7def-8123-456789abcdef

# By explicit endpoint
garden-rake tend http://192.168.1.100:7185

# Auto-select (first discovered)
garden-rake tend auto
```

---

## 6. Service Discovery

### 6.1 Service Search Endpoints

#### 6.1.1 List All Services

```http
GET /api/v1/services
```

**Response:**
```json
{
  "data": [
    {
      "name": "mongodb",
      "offering": "mongodb",
      "version": "7.0",
      "status": "Running",
      "health": "Healthy",
      "ports": { "native": 27017, "agnostic": null },
      "resources": { "cpu_percent": 2.5, "memory_mb": 512 }
    }
  ],
  "suggestions": ["Try: garden-rake find mongodb"]
}
```

#### 6.1.2 Search Services

```http
GET /api/v1/services?q={query}
GET /api/v1/services?name={exact_name}
GET /api/v1/services?category={category}
GET /api/v1/services?tag={tag}
GET /api/v1/services?fresh=true
```

**Query prefixes** (in `q` parameter):
- `c:`, `cat:`, `category:` → Category search
- `t:`, `tag:`, `tags:` → Tag search
- Plain text → Name search

**Examples:**
```
GET /api/v1/services?q=mongodb          # Name search
GET /api/v1/services?q=c:database       # Category search
GET /api/v1/services?q=t:nosql          # Tag search
GET /api/v1/services?fresh=true         # Force network scan
```

#### 6.1.3 Find Service (Cross-Garden)

```http
POST /api/v1/services/find
Content-Type: application/json

{
  "name": "mongodb",
  "category": null,
  "tags": [],
  "fresh": false
}
```

**Response:**
```json
{
  "data": {
    "found": true,
    "services": [
      {
        "name": "mongodb",
        "offering": "mongodb",
        "category": "database",
        "tags": ["nosql"],
        "status": "Running",
        "stone": {
          "id": "01936e8a-7b2c-7def-8123-456789abcdef",
          "name": "stone-topaz-basin",
          "endpoint": "http://192.168.1.100:7185"
        },
        "connection": {
          "protocol": "mongodb",
          "host": "192.168.1.100",
          "port": 27017,
          "connection_string": "mongodb://192.168.1.100:27017"
        }
      }
    ],
    "source": "cache",
    "timestamp": "2026-01-23T12:34:56.789Z"
  },
  "suggestions": null
}
```

### 6.2 Service Categories

Standard categories used for filtering:

```
database    - MongoDB, PostgreSQL, MySQL, Redis
cache       - Redis, Memcached
search      - Elasticsearch, Meilisearch, Qdrant
monitoring  - Prometheus, Grafana, InfluxDB
messaging   - RabbitMQ, NATS, Kafka
storage     - MinIO, SeaweedFS
ai          - Ollama, vLLM, text-generation-inference
```

### 6.3 Default Ports by Offering

When port information is unavailable (e.g., from chirps), use these defaults:

| Offering | Port |
|----------|------|
| mongodb | 27017 |
| redis | 6379 |
| postgres | 5432 |
| mysql/mariadb | 3306 |
| elasticsearch | 9200 |
| meilisearch | 7700 |
| qdrant | 6333 |
| minio | 9000 |
| rabbitmq | 5672 |
| nats | 4222 |
| influxdb | 8086 |
| grafana | 3000 |
| prometheus | 9090 |
| (default) | 8080 |

---

## 7. Caching Strategy

### 7.1 What to Cache

| Item | TTL | Key | Purpose |
|------|-----|-----|---------|
| Stone endpoints | 90s | `stone_id` | Avoid repeated discovery |
| Service lists | 30s | `endpoint` | Reduce API calls |
| Hardware capabilities | 5min | `stone_id` | Placement decisions |
| Topology (from chirps) | Continuous | `stone_id` | Live network view |

### 7.2 Cache Keys

**IMPORTANT:** Use `stone_id` (not `stone_name`) as the primary cache key. Stone names may change; stone IDs are immutable.

```python
class StoneCache:
    def get(self, key: str) -> CachedStone | None:
        """Lookup by stone_id or stone_name (case-insensitive)."""
        key_lower = key.lower()
        # Try stone_id first, then stone_name
        return self._by_id.get(key_lower) or self._by_name.get(key_lower)

    def insert(self, stone: DiscoveryResponse, caps: HardwareCapabilities):
        """Insert with dual indexing."""
        entry = CachedStone(...)
        if caps.stone_id:
            self._by_id[caps.stone_id.lower()] = entry
        self._by_name[caps.stone_name.lower()] = entry
```

### 7.3 Cache Invalidation

- **On unreachable:** Mark as stale, but don't delete (stone may return)
- **On explicit refresh:** Clear and rediscover
- **On chirp received:** Update entry, reset TTL

---

## 8. API Response Format

All Moss API endpoints return a standard wrapper:

### 8.1 Success Response

```json
{
  "data": { /* payload */ },
  "suggestions": ["Optional hint 1", "Optional hint 2"]
}
```

| Field | Type | Description |
|-------|------|-------------|
| `data` | T | Response payload (type varies by endpoint) |
| `suggestions` | string[]? | CLI hints for next actions (may be null) |

### 8.2 Client-Side Type

```typescript
interface ApiResponse<T> {
  data: T;
  suggestions?: string[];
}
```

```rust
#[derive(Deserialize)]
pub struct ApiResponse<T> {
    pub data: T,
    pub suggestions: Option<Vec<String>>,
}
```

---

## 9. Error Handling

### 9.1 Error Response Format

```json
{
  "error": {
    "code": "SERVICE_NOT_FOUND",
    "message": "Service 'mongodb' not found on this stone",
    "details": null
  }
}
```

### 9.2 Error Codes

| Code | HTTP Status | Description |
|------|-------------|-------------|
| `SERVICE_NOT_FOUND` | 404 | Requested service doesn't exist |
| `STONE_NOT_FOUND` | 404 | Requested stone not in garden |
| `OFFERING_NOT_FOUND` | 404 | Unknown offering template |
| `INVALID_REQUEST` | 400 | Malformed request body |
| `TEMPLATE_NOT_FOUND` | 400 | Manifest template missing |
| `DOCKER_ERROR` | 500 | Container operation failed |
| `DOCKER_UNAVAILABLE` | 503 | Docker daemon not running |
| `INTERNAL_ERROR` | 500 | Unexpected server error |

### 9.3 Retry Strategy

| Scenario | Retry | Backoff |
|----------|-------|---------|
| Network timeout | Yes | Exponential (1s, 2s, 4s) |
| 5xx error | Yes | Exponential with jitter |
| 4xx error | No | - |
| Stone unreachable | Fall back to discovery | - |

---

## 10. Health Checking

### 10.1 Health Endpoint

```http
GET /health
```

**Response (healthy):**
```json
{
  "status": "healthy",
  "timestamp": "2026-01-23T12:34:56.789Z",
  "components": {
    "docker": { "status": "healthy", "version": "24.0.7" },
    "disk": { "status": "healthy", "used_percent": 45 },
    "memory": { "status": "healthy", "used_percent": 62 }
  }
}
```

### 10.2 Health Status Values

| Status | Description |
|--------|-------------|
| `healthy` | All systems operational |
| `degraded` | Functioning with warnings |
| `unhealthy` | Critical issues detected |

### 10.3 Vitality Language (CLI Display)

For user-facing output, map health to vitality terms:

| Health | Vitality |
|--------|----------|
| healthy | thriving |
| degraded | needs attention |
| unhealthy | withering |
| (unreachable) | dormant |

---

## 11. Type Definitions

### 11.1 Discovery Types

```typescript
// Discovery request
interface DiscoveryRequest {
  discover: "moss";
  request_id: string;
  requester: string;
}

// Discovery response
interface DiscoveryResponse {
  stone_id?: string;
  stone_name: string;
  stone_endpoint: string;
  moss_version: string;
  lantern_endpoint?: string;
}

// Chirp service info
interface ChirpServiceInfo {
  name: string;
  offering: string;
  category: string;
  status: "Running" | "Stopped" | "Maintenance" | "Degraded" | "Unknown";
}

// Chirp payload
interface StoneChirpPayload {
  stone_id: string;
  stone_name: string;
  endpoint: string;
  moss_version: string;
  services: ChirpServiceInfo[];
}
```

### 11.2 Service Types

```typescript
interface ServiceInfo {
  name: string;
  offering: string;
  version: string;
  status: ServiceStatus;
  health: ServiceHealthStatus;
  ports: { native: number; agnostic?: number };
  resources?: ContainerResources;
}

type ServiceStatus = "Running" | "Stopped" | "Maintenance" | "Degraded" | "Unknown";
type ServiceHealthStatus = "Healthy" | "Degraded" | "Offline";
```

### 11.3 Hardware Capabilities

```typescript
interface HardwareCapabilities {
  stone_id?: string;
  stone_name: string;
  hardware: HardwareInventory;
  runtime?: RuntimeInfo;
  detection_status: "scanning" | "partial" | "complete";
}

interface HardwareInventory {
  cpu: { model?: string; cores: number; architecture: string };
  memory: { total_mb: number };
  gpus: GpuInfo[];
  disk?: { total_gb: number; disk_type?: string };
  storage: StorageDevice[];
  os_version?: string;
  kernel_version?: string;
  ai_capabilities?: AiCapabilitiesSummary;
}
```

### 11.4 Tending State

```typescript
interface TendingState {
  stone_name: string;
  endpoint: string;
  last_seen: string;  // ISO 8601
}
```

---

## 12. Implementation Checklist

### 12.1 Minimum Viable Driver

- [ ] UDP discovery (broadcast, parse responses)
- [ ] Endpoint normalization (all input formats)
- [ ] HTTP client with timeout handling
- [ ] Health check (`GET /health`)
- [ ] Service list (`GET /api/v1/services`)
- [ ] API response unwrapping
- [ ] Error response parsing

### 12.2 Full-Featured Driver

- [ ] Tending state persistence
- [ ] Stone name resolution (mDNS, UDP, Lantern)
- [ ] Case-insensitive matching
- [ ] Stone ID as primary cache key
- [ ] Service search with query prefixes
- [ ] Hardware capabilities caching
- [ ] Chirp listener (passive topology)
- [ ] Retry with exponential backoff
- [ ] Connection pooling

### 12.3 Timeout Reference

| Operation | Timeout |
|-----------|---------|
| UDP discovery | 3s (full), 2s (quick) |
| Health check | 2s |
| HTTP request (default) | 30s |
| HTTP connect | 5s |
| mDNS probe | 800ms |

### 12.4 Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `GARDEN_STONE` | - | Override target stone |
| `GARDEN_QUIET` | - | Suppress verbose output |
| `GARDEN_DISCOVERY_TIMEOUT_SECS` | 3 | UDP discovery timeout |
| `GARDEN_CACHE_TTL_SECS` | 90 | Stone cache TTL |
| `GARDEN_HTTP_REQUEST_TIMEOUT_SECS` | 30 | HTTP request timeout |

---

## Appendix A: Quick Reference

### Common Endpoints

```
GET  /health                     Health status
GET  /capabilities               Hardware inventory
GET  /api/v1/services            List services
GET  /api/v1/services?q=mongo    Search services
POST /api/v1/services/find       Cross-garden search
GET  /api/v1/garden/topology     Topology cache
```

### Quick Discovery (Python)

```python
import socket, json, time

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
sock.settimeout(3.0)

request = {"discover": "moss", "request_id": f"req-{int(time.time()*1000)}", "requester": "quickstart"}
sock.sendto(json.dumps(request).encode(), ('255.255.255.255', 7184))

data, _ = sock.recvfrom(2048)
stone = json.loads(data)
print(f"Found: {stone['stone_name']} at {stone['stone_endpoint']}")
```

### Quick Service Lookup (curl)

```bash
# Find MongoDB anywhere in the garden
curl -s http://stone-01.local:7185/api/v1/services?q=mongodb | jq '.data'

# Get connection string
curl -s -X POST http://stone-01.local:7185/api/v1/services/find \
  -H "Content-Type: application/json" \
  -d '{"name":"mongodb"}' | jq '.data.services[0].connection'
```

---

## Appendix B: Changelog

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2026-01-23 | Initial specification |
