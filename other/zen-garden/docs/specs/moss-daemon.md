# Garden-Moss Daemon Specification

**Purpose:** Technical specification for the Moss daemon - HTTP API, service orchestration, discovery.  
**Audience:** Developers implementing Moss, maintainers debugging production issues.

---

## Table of Contents

1. [Overview](#overview)
2. [Configuration](#configuration)
3. [HTTP API Server](#http-api-server)
4. [mDNS Announcer](#mdns-announcer)
5. [Docker Compose Manager](#docker-compose-manager)
6. [Service Template Handler](#service-template-handler)
7. [Health Monitor](#health-monitor)
8. [Resource Monitor](#resource-monitor)
9. [Lantern Integration](#lantern-integration)
10. [UDP Broadcast Discovery](#udp-broadcast-discovery)
11. [Stone Registry](#stone-registry)

---

## Overview

**Binary:** `garden-moss`  
**Installation:** `/usr/local/bin/garden-moss`  
**Service:** `garden-moss.service` (systemd)  
**Port:** 7185 (HTTP API)  
**Language:** Rust (Axum framework)  
**Config:** `/etc/zen-garden/garden-moss.toml`

### Responsibilities

1. Listen for management requests via HTTP API
2. Execute service lifecycle operations (install, uninstall, upgrade)
3. Announce self and services via mDNS
4. Monitor container health and update announcements
5. Manage Docker Compose atomically with rollback
6. Detect and resolve port conflicts
7. Signal resource warnings when Stone overloaded
8. Validate service templates before installation
9. Respond to Lantern discovery requests (optional)
10. Listen for `moss_online` lifecycle broadcasts and maintain Stone registry
11. Respond to UDP broadcast discovery requests with election-based response

---

## Configuration

### File Locations

- **Linux:** `/etc/zen-garden/garden-moss.toml`
- **Windows:** `./moss.toml` (current directory)

### Configuration Priority

1. CLI arguments (`--stone-name`, `--port`, `--log-level`)
2. Environment variables (`STONE_NAME`, `PORT`, `RUST_LOG`)
3. Configuration file (`moss.toml`)
4. Built-in defaults

### File Format

```toml
# Stone identifier (unique name for this node)
stone_name = "stone-01"

# HTTP server port for Moss API
port = 7185

# Logging verbosity level (trace, debug, info, warn, error)
log_level = "info"

# Docker Compose configuration
[docker]
compose_path = "/opt/garden-moss/docker-compose.yml"
compose_timeout = 300  # seconds

# mDNS configuration
[mdns]
ttl = 120  # seconds
cache_flush = true

# Health monitoring
[health]
check_interval = 30  # seconds
restart_threshold = 3  # restarts before marking degraded

# Discovery configuration
[discovery]
fast_sync_on_startup = true
fast_sync_timeout = 5  # seconds
udp_discovery_port = 3004
lifecycle_broadcast_port = 3003
```

### CLI Arguments

```bash
# Override stone name
garden-moss --stone-name stone-production

# Override port
garden-moss --port 8080

# Override log level
garden-moss --log-level debug

# Force restart (kill existing processes)
garden-moss --force

# Combine options
garden-moss --stone-name stone-02 --port 3002 --log-level trace
```

### Environment Variables

```bash
# Set via environment
export STONE_NAME=stone-production
export PORT=8080
export RUST_LOG=debug
garden-moss

# Or inline
STONE_NAME=stone-dev PORT=3002 garden-moss
```

---

## HTTP API Server

**Framework:** Axum (Tokio async runtime)

### Endpoints

```
# Operations (RPC-style, path parameters)
POST   /api/operations/offer/{offering}   # Install service from template
POST   /api/operations/remove/{target}    # Uninstall service or keystone
POST   /api/operations/upgrade            # Upgrade all services (default)
POST   /api/operations/upgrade/{service}  # Upgrade specific service
POST   /api/operations/rest/{service}     # Stop service, preserve data
POST   /api/operations/wake/{service}     # Resume service
POST   /api/operations/reload             # Reload compose file

# Collections (RESTful for queries)
GET    /api/services               # List installed services
GET    /api/services/{name}        # Service details
GET    /api/compose                # Current docker-compose.yml
GET    /api/offerings              # Validated offerings index
GET    /api/offerings/{name}       # Offering details
POST   /api/offerings/refresh      # Rebuild offerings index
GET    /api/announcements          # Current mDNS announcements
GET    /api/garden/stones          # Known Stones (from broadcasts)

# Health & Metadata
GET    /health                     # Daemon + container health
GET    /info                       # Stone info (name, version, resources)
GET    /capabilities               # Hardware and software capabilities
GET    /metrics                    # Prometheus metrics

# Administrative
POST   /admin/shutdown             # Graceful daemon shutdown
```

### Operation ID Tracking

**Purpose:** Correlate distributed operations across multiple Stones

**Format:** GUIDv7 (time-ordered, RFC 9562)

**Behavior:**

- Rake generates operation ID for `--all` operations (e.g., `upgrade all`)
- Operation ID included in request body to each Moss
- Each Moss logs: `"Started upgrade for services [mongodb, redis], operation_id: 01936d2e..."`
- Enables correlation in centralized logging (Loki, Elasticsearch, etc.)

**API Parameters:**

`operation_id` (optional string) in request body for all operations:
- `/api/operations/offer/{offering}`
- `/api/operations/remove/{target}`
- `/api/operations/upgrade` and `/api/operations/upgrade/{service}`
- `/api/operations/rest/{service}`
- `/api/operations/wake/{service}`
- `/api/operations/reload`

### Request/Response Examples

#### Install Service

```http
POST /api/operations/offer/mongodb
Content-Type: application/json

{
  "version": "7.0",  // Optional: defaults to latest
  "operation_id": "01936d2e-8f4a-7890-b123-456789abcdef"
}
```

**Response 201:**

```json
{
  "status": "installed",
  "offering": "mongodb",
  "version": "7.0.4",
  "operation_id": "01936d2e-8f4a-7890-b123-456789abcdef",
  "ports": {
    "native": 27017,
    "agnostic": 8080
  },
  "containers": ["mongodb", "mongodb-agnostic"],
  "announced": true
}
```

#### Remove Service

```http
POST /api/operations/remove/mongodb
Content-Type: application/json

{
  "volumes": true,  // Optional: remove volumes too
  "operation_id": "01936d2e-..."
}
```

**Response 200:**

```json
{
  "status": "removed",
  "target": "mongodb",
  "operation_id": "01936d2e-..."
}
```

#### Upgrade Service

```http
# Upgrade all services (default)
POST /api/operations/upgrade
Content-Type: application/json

{
  "dry_run": false,
  "operation_id": "01936d2e-..."
}

# Upgrade specific service
POST /api/operations/upgrade/mongodb
Content-Type: application/json

{
  "version": "8.0",  // Optional: specific version
  "dry_run": false,
  "operation_id": "01936d2e-..."
}
```

**Response 200:**

```json
{
  "status": "upgraded",
  "services": ["mongodb"],
  "changes": [
    { "service": "mongodb", "from": "7.0.4", "to": "8.0.1" }
  ],
  "operation_id": "01936d2e-..."
}
```

#### List Services

```http
GET /api/services
```

**Response 200:**

```json
{
  "services": [
    {
      "name": "mongodb",
      "offering": "mongodb",
      "version": "7.0.4",
      "status": "running",
      "health": "healthy",
      "ports": { "native": 27017, "agnostic": 8080 },
      "uptime": 3600,
      "memory_mb": 450,
      "resources": {
        "cpu_percent": 2.5,
        "memory_bytes": 471859200,
        "memory_friendly": "450.00 MB",
        "uptime_seconds": 3600,
        "uptime_friendly": "1h 0m"
      }
    }
  ],
  "total": 1,
  "stone_health": "healthy"
}
```

#### Health Check

```http
GET /health
```

**Response 200:**

```json
{
  "status": "healthy",
  "moss_version": "0.1.0",
  "stone_name": "stone-01",
  "docker_running": true,
  "containers_running": 2,
  "containers_total": 2,
  "warnings": []
}
```

**With warnings:**

```json
{
  "status": "degraded",
  "warnings": [
    "High container count (6) for Stone capacity",
    "MongoDB container restarting (3 times in 10 minutes)"
  ]
}
```

#### Stone Information

```http
GET /info
```

**Response 200:**

```json
{
  "name": "stone-01",
  "api_endpoint": "http://192.168.1.100:7185",
  "health": "Healthy",
  "capabilities": {
    "max_services": 10,
    "stone_type": "standard"
  },
  "moss_version": "0.1.0",
  "resources": {
    "cpu": {
      "cores": 8,
      "usage_percent": 45.2,
      "usage_friendly": "45.2%"
    },
    "memory": {
      "total_bytes": 17179869184,
      "used_bytes": 12884901888,
      "available_bytes": 4294967296,
      "used_percent": 75.0,
      "total_friendly": "16.00 GB",
      "used_friendly": "12.00 GB",
      "available_friendly": "4.00 GB"
    },
    "disk": {
      "total_bytes": 536870912000,
      "used_bytes": 322122547200,
      "available_bytes": 214748364800,
      "used_percent": 60.0,
      "path": "/",
      "total_friendly": "500.00 GB",
      "used_friendly": "300.00 GB",
      "available_friendly": "200.00 GB"
    },
    "uptime_seconds": 86400,
    "uptime_friendly": "1d 0h 0m"
  }
}
```

### Error Responses

```json
400 Bad Request:
{
  "error": "invalid_offering",
  "message": "Offering 'mysql' not found in manifest registry",
  "details": {
    "available_offerings": ["mongodb", "redis", "postgresql"]
  }
}

409 Conflict:
{
  "error": "port_conflict",
  "message": "Port 27017 already in use by 'redis'",
  "details": {
    "requested_port": 27017,
    "conflicting_service": "redis"
  }
}
```

### Technical Requirements

- Async/await with Tokio runtime
- Graceful shutdown on SIGTERM
- Structured JSON logging
- CORS enabled (for future web dashboard)
- Request/response validation with serde

---

## mDNS Announcer

**Library:** `mdns-sd` crate  
**Service Type (self):** `_moss._tcp.local.`  
**Service Type (services):** `_koan-stone._tcp.local.`

### Announcement Types

#### Moss Self-Announcement

```
stone-01-moss._moss._tcp.local.
TXT: stone_name=stone-01
     version=0.1.0
     api_port=3001
     health=healthy
```

#### Native Service Announcement

```
stone-01-mongodb._koan-stone._tcp.local.
TXT: offering=mongodb
     port=27017
     protocol=native
     version=7.0.4
     categories=database,document-database
     health=healthy
     priority=50
```

#### Agnostic Sidecar Announcement

```
stone-01-mongodb-agnostic._koan-stone._tcp.local.
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

### Responsibilities

1. Announce Moss itself for Rake discovery
2. Announce native services with proper TXT records
3. Announce agnostic sidecars with capabilities
4. Update health status in real-time
5. Remove announcements when services stop
6. Handle network interface changes gracefully

### Technical Requirements

- Spawn mDNS responder on startup
- Re-announce on service lifecycle events
- Update TXT records on health changes
- TTL: 120 seconds (configurable)
- Log all announcement events

---

## Docker Compose Manager

**Compose File:** `/opt/garden-moss/docker-compose.yml`

### Responsibilities

1. Read and parse existing docker-compose.yml
2. Add services to compose file atomically
3. Remove services from compose file
4. Update service versions
5. Detect port conflicts before applying
6. Apply changes with rollback on failure
7. Validate container startup after changes

### Port Conflict Resolution

**Strategy:** Default to service defaults, auto-reassign on conflict.

**Behavior:**

- MongoDB requests port 27017 but it's already in use
- Assign 27018 instead
- Update docker-compose.yml with actual port
- Announce actual port via mDNS
- Log warning about reassignment

### Atomic Application Flow

```
1. Backup current docker-compose.yml
2. Merge new service into compose structure
3. Write updated compose file atomically
4. Validate YAML syntax
5. Run: docker compose up -d
6. Wait for container health (60s timeout)
7. On failure:
   - docker compose down [service]
   - Restore backup compose file
   - docker compose up -d (restore previous state)
   - Return error with diagnostics
8. On success:
   - Delete backup
   - Return success response
```

### Technical Requirements

- YAML parsing with `serde_yaml`
- Atomic file writes (write temp, rename)
- Process spawning with `tokio::process`
- Port availability checks (TCP bind test)
- Container health validation after startup

---

## Service Template Handler

**Template Location:** `/etc/zen-garden/templates/`

### Template Format

Templates are YAML files defining:
- Service metadata (name, offering, description, categories)
- Version options with defaults
- Docker configuration for native and agnostic modes
- mDNS announcement details

### Validation Rules

1. Template must have `name`, `offering`, `docker` sections
2. Image tags must be valid (no shell injection)
3. Port numbers: 1-65535
4. No arbitrary shell commands
5. Environment variables: `${VAR}` or `${VAR:-default}` only
6. Volume names: `^[a-z0-9-]+$`

### Technical Requirements

- YAML parsing with schema validation
- Variable substitution (VERSION, passwords)
- Template registry (scan directory on startup)
- Reject templates with invalid syntax

---

## Health Monitor

**Check Interval:** 30 seconds

### Responsibilities

1. Check Docker daemon status
2. Check container status (running, restarting, stopped)
3. Monitor resource usage (RAM, CPU)
4. Detect restart loops (>3 restarts in 10 min)
5. Update mDNS TXT records with health status
6. Log health transitions

### Health States

- **Healthy:** All checks passing
- **Degraded:** Some issues but functional
- **Unavailable:** Critical failure

### Check Logic

```
For each service:
  1. Container exists? → No: Unavailable
  2. Container running? → No: Unavailable
  3. Restart count > 3? → Yes: Degraded
  4. Memory > 80% limit? → Yes: Degraded
  5. All checks pass → Healthy
```

### Technical Requirements

- Docker API client (`bollard` crate)
- Background task spawned on startup
- Shared state with Arc<RwLock<...>>
- Integration with mDNS announcer

---

## Resource Monitor

### Stone Capacity Thresholds

- **Mini Stone (2GB RAM):** max 2 services, warn at 1
- **Standard Stone (4GB RAM):** max 4 services, warn at 3
- **Large Stone (8GB+ RAM):** max 8 services, warn at 6

### Responsibilities

1. Count running containers
2. Estimate total RAM usage
3. Compare against Stone capacity
4. Warn when approaching limits
5. Include warnings in `/health` endpoint

### Warning Examples

- "High container count (6) for Stone capacity (Standard: 4 max)"
- "Total RAM usage (3.2GB) approaching system limit (4GB)"
- "Container 'mongodb' restarting frequently (5 times in 10 minutes)"

### Technical Requirements

- System info (`sysinfo` crate)
- Container stats via Docker API
- Warnings stored in-memory (cleared on restart)

---

## Lantern Integration

**Purpose:** Enable Lantern UI dashboard to monitor and control the garden

**Behavior:** Moss responds to Lantern UDP broadcast requests with state updates

### Discovery Protocol

**Lantern broadcasts via UDP** (periodic, e.g., every 30s):

```
UDP broadcast to network (port 7184)
Message: "LANTERN_GATHER http://lantern-host:3000/gather"
```

### Lifecycle Broadcasts

**On startup:**

```json
UDP broadcast to network (port 7184)
{
  "event": "moss_online",
  "stone_name": "stone-01",
  "moss_version": "0.1.0",
  "timestamp": "2026-01-15T12:00:00Z",
  "capabilities": {
    "max_services": 4,
    "stone_type": "standard",
    "features": ["compose", "health", "pond"]
  },
  "api_endpoint": "http://stone-01.local:7185"
}
```

**On shutdown:**

```json
UDP broadcast to network (port 7184)
{
  "event": "moss_offline",
  "stone_name": "stone-01",
  "timestamp": "2026-01-15T16:30:00Z",
  "reason": "graceful_shutdown"
}
```

### State Payload

**First response (full graph):**

```json
{
  "mode": "full",
  "stone_name": "stone-01",
  "moss_version": "0.1.0",
  "services": [
    {
      "name": "mongodb",
      "status": "running",
      "health": "healthy"
    }
  ],
  "resources": {
    "cpu_percent": 15.3,
    "memory_used_mb": 1200
  }
}
```

**Subsequent responses (diff only):**

```json
{
  "mode": "diff",
  "stone_name": "stone-01",
  "timestamp": "2026-01-15T12:34:56Z",
  "changes": {
    "services": [
      {
        "name": "mongodb",
        "health": "degraded"
      }
    ]
  }
}
```

---

## UDP Broadcast Discovery

**Purpose:** Enable Rake discovery on Windows without mDNS browse capability

**Port:** 3004 (listens for Rake discovery requests)

### Behavior

When Moss receives discovery broadcast:

1. Parse request: `{ "discover": "moss", "request_id": "uuid", "requester": "rake-cli" }`
2. Calculate election delay: `blake3::hash(stone_name + request_id)[0] * 10` ms
3. Wait for delay (0-2550ms range, deterministic but unpredictable)
4. Send unicast UDP response to requester IP:3005

### Response Payload

```json
{
  "stone_name": "stone-01",
  "stone_endpoint": "http://stone-01.local:7185",
  "lantern_endpoint": "http://stone-09.local:3002",
  "moss_version": "0.1.0"
}
```

### Election Algorithm

Use BLAKE3 hash of `stone_name + request_id` to calculate deterministic delay (0-2550ms).

**Properties:**

- Deterministic: Same stone + request always produces same delay
- Unpredictable: Different request IDs produce different order
- Prevents reply storm: Only one Stone likely to respond
- No coordination needed: Each Moss calculates independently

---

## Stone Registry

**Data source:** `moss_online` broadcasts received on UDP port 3003

### In-Memory Registry

**Structure:**

```rust
struct StoneRegistry {
    stones: HashMap<String, StoneInfo>,
    lantern: Option<LanternInfo>,
    state_cursor: String,  // GUIDv7
}

struct StoneInfo {
    name: String,
    api_endpoint: String,
    health: String,
    capabilities: Capabilities,
    last_seen: Instant,
    moss_version: String,
}
```

### Update Logic

- `moss_online` received → Add/update Stone, **generate new cursor**
- `moss_offline` received → Mark offline immediately, **generate new cursor**
- TTL check (every 30s) → Mark offline if `last_seen > 90s ago`, **generate new cursor if changed**
- **Cursor generation:** GUIDv7 timestamp-based, increments on topology change

### API Endpoints

- `GET /api/garden/stones` - Full topology
- `GET /api/garden/stones?since=<cursor>` - Delta updates

### Cursor-Based Polling

```http
GET /api/garden/stones?since=01936d2e-8f4a-7890-b123-456789abcdef
```

**Response (no changes):**

```json
{
  "has_updates": false,
  "cursor": "01936d2e-8f4a-7890-b123-456789abcdef"
}
```

**Response (with changes):**

```json
{
  "has_updates": true,
  "cursor": "01936d3f-9a2b-8901-c234-567890abcdef",
  "changes": {
    "added": [
      {
        "name": "stone-03",
        "api_endpoint": "http://stone-03.local:7185"
      }
    ],
    "removed": ["stone-02"]
  }
}
```

---

## Next Steps

- **Rake CLI specification:** [rake-cli.md](rake-cli.md)
- **Service offerings:** [offerings.md](offerings.md)
- **Discovery protocol:** [discovery.md](discovery.md)
- **Security specification:** [security.md](security.md)
