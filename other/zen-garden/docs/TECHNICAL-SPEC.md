# Zen Garden Technical Specification

**Comprehensive development reference for Garden-Moss Daemon and Rake CLI**

**Date:** January 15, 2026  
**Status:** Ready for implementation  
**Purpose:** Single source of truth for Moss/Rake development

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Architecture Overview](#architecture-overview)
3. [Glossary](#glossary)
4. [Garden-Moss Daemon](#moss-daemon)
5. [Rake CLI](#rake-cli)
6. [Service Templates & Offerings](#service-templates--offerings)
7. [Agnostic Data API](#agnostic-data-api)
8. [mDNS Discovery](#mdns-discovery)
9. [Implementation Roadmap](#implementation-roadmap)
10. [Technology Stack](#technology-stack)

---

## Executive Summary

**Zen Garden** is an infrastructure management system for home labs and small teams that treats physical machines as a distributed compute fabric. It enables frictionless service deployment and discovery across multiple "Stones" (physical devices) using mDNS-based service discovery and Docker Compose orchestration.

### Core Components

**Moss** - Rust daemon running on each Stone that manages services via HTTP API, announces services through mDNS, and orchestrates Docker Compose configurations.

**Rake** - Rust CLI tool for discovering Stones and sending management commands (`garden-rake offer mongodb`, `garden-rake list --all`).

**Pond** - Optional security layer providing mTLS authentication between Stones (see SECURITY-SPEC.md).

### Design Philosophy

- **Frictionless by default** - Zero configuration, sane defaults, auto-discovery
- **Hot cache architecture** - Topology always available, zero discovery for common case
- **Localhost-first** - Most operations are instant (<1ms) with no network overhead
- **Template-driven** - Curated offerings prevent ad-hoc Docker configurations
- **Observable** - Visual feedback shows system health at all times
- **Home lab optimized** - Simple for solo admins, scales to small teams

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│              Stone (Physical Device)                    │
│                                                         │
│  ┌───────────────────────────────────────────────────┐ │
│  │   Moss (Rust daemon)                              │ │
│  │   Port: 7185                                      │ │
│  │                                                   │ │
│  │  • HTTP API (service management)                 │ │
│  │  • mDNS Announcer (self + services)              │ │
│  │  • Docker Compose Manager                        │ │
│  │  • Health Monitor                                │ │
│  │  • Resource Monitor                              │ │
│  │  • Template Validator                            │ │
│  └───────────────────────────────────────────────────┘ │
│                        ↓                                │
│  ┌───────────────────────────────────────────────────┐ │
│  │    Docker Compose Services                        │ │
│  │                                                   │ │
│  │  • MongoDB (native port 27017)                   │ │
│  │    └── mongodb-agnostic sidecar (:8080)          │ │
│  │  • Redis (native port 6379)                      │ │
│  │    └── redis-agnostic sidecar (:8081)            │ │
│  │  • Custom application services                   │ │
│  └───────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│         Developer Machine / Any Stone                   │
│                                                         │
│  ┌───────────────────────────────────────────────────┐ │
│  │   Rake CLI (garden-rake)                          │ │
│  │                                                   │ │
│  │  1. Discover Garden-Moss Daemons via mDNS                │ │
│  │  2. Send HTTP commands to target Stone(s)        │ │
│  │  3. Display results with visual feedback         │ │
│  └───────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘

Communication Flow:
Rake → mDNS query (_moss._tcp.local.) → Discover Moss endpoints
Rake → HTTP POST (offer service) → Moss
Moss → Docker Compose (update docker-compose.yml)
Moss → Docker (docker compose up -d)
Moss → mDNS announce (service available)
```

---

## Glossary

**Stone** - Physical device running Garden-Moss Daemon (laptop, server, Raspberry Pi)

**Moss** - Daemon service on each Stone handling management requests (port 7185)

**Rake** - CLI tool for sending commands to Stones (`garden-rake`)

**Offering** - Pre-defined service template (mongodb, redis, postgresql)

**Native Service** - Database/service on its native protocol (MongoDB port 27017)

**Agnostic Sidecar** - HTTP REST API wrapping native service (port 8080+)

**Pond** - Security model connecting Stones with mTLS certificates

**Pebble** - Encrypted file containing Pond CA keypair

**Cornerstone** - First Stone with Pond authority (certificate issuer)

**Set** - Logical namespace for application data (maps to database/schema/prefix)

---

## Design Decisions and Constraints

### Scale Assumptions

**Target:** 10 Stones maximum for Phase 1  
**Tested:** Up to 20 Stones in Docker test environment  
**Future:** Redesign for P2P communication beyond 100 Stones

**Rationale:**

- Home lab / small team focus (typically 3-5 Stones)
- UDP broadcast efficient for small networks
- Simplicity over premature optimization
- Proven pattern for Docker Compose, Kubernetes (early versions)

### Concurrency and State Management

**Service Status Tracking:**

Moss maintains service status for each installed offering: Running (container healthy, accepting connections), Stopped (container stopped, data preserved), Maintenance (operation in progress), Degraded (container running but failing health checks), or Unknown (state cannot be determined).

**Concurrent Operation Handling:**

- Moss tracks service status in memory
- Operations that modify service state check status first
- If service is in `Maintenance`, return HTTP 202 Accepted
- 202 response indicates: "Request acknowledged, service busy, retry later"
- **Only return 202 if ALL requested services are under maintenance**
- If some services available, operation proceeds on those services only

**Example:**

```json
POST /api/operations/upgrade
Request: {} (upgrade all services)

Scenario A: mongodb=Running, redis=Maintenance
Response 200:
{
  "status": "partial",
  "upgraded": ["mongodb"],
  "skipped": [{"service": "redis", "reason": "maintenance"}]
}

Scenario B: mongodb=Maintenance, redis=Maintenance
Response 202:
{
  "status": "accepted",
  "message": "All services under maintenance, retry later"
}
```

### Garden-Wide Operations

**Command Semantics:**

- Stone-level: `garden-rake upgrade --all --at stone-01` targets all services on one Stone
- Garden-wide: `garden-rake upgrade --all` targets all services on ALL Stones

**Implementation:**

For garden-wide `--all` operations:

1. Rake sends request to localhost Moss (or discovered Moss if no local)
2. Localhost Moss becomes coordinator
3. Moss discovers all Stones via hot cache (`GET /api/garden/stones`)
4. Moss broadcasts UDP message to all Stones: `{"operation": "upgrade", "operation_id": "..."}`
5. Each Moss executes operation locally, logs with operation_id
6. Coordinator Moss aggregates responses, returns to Rake

**Benefits:**

- Single HTTP request from Rake (simpler client)
- Moss-to-Moss coordination (leverages existing UDP infrastructure)
- Automatic retry/fallback if some Stones unreachable
- Coordinated operation tracking via operation_id

### Error Handling

**Standard Error Response Format (RFC 7807 Problem Details):**

```json
{
  "type": "https://zen-garden.dev/errors/service-in-maintenance",
  "status": 202,
  "title": "Service In Maintenance",
  "detail": "MongoDB is currently being upgraded",
  "instance": "/api/operations/upgrade/mongodb",
  "operation_id": "01936d2e-8f4a-7890-b123-456789abcdef"
}
```

**HTTP Status Codes:**

- `200 OK` - Operation succeeded
- `201 Created` - Service installed
- `202 Accepted` - Request acknowledged, service(s) busy
- `400 Bad Request` - Invalid parameters
- `404 Not Found` - Service/offering not found
- `409 Conflict` - Operation conflicts with current state (e.g., service already installed)
- `500 Internal Server Error` - Moss internal failure
- `503 Service Unavailable` - Docker daemon unreachable

### Architectural Deferrals

**Atomic Rollback:**

- **Status:** Deferred to architecture team
- **Scope:** Compose file corruption recovery, partial upgrade failures
- **Phase 1:** Best-effort rollback (restore previous compose file)
- **Phase 2:** Transaction log, snapshot-based rollback

**Network Partition Tolerance:**

- **Status:** Not a concern for Phase 1
- **Assumption:** Stones operate on same local network
- **Mitigation:** Eventual consistency via UDP broadcasts, TTL fallback

**Clock Skew:**

- **Status:** Not a concern
- **Rationale:** GUIDv7 cursors are relative to single issuer (Moss)
- **Implication:** Cursor ordering valid within Stone, cross-Stone ordering not guaranteed (acceptable)

**UDP Packet Loss:**

- **Status:** Unlikely in local networks
- **Mitigation:** TTL fallback (90s) for missed lifecycle broadcasts
- **Future:** Redesign for larger swarms with TCP fallback or acknowledgment protocol

**Garden-Lantern registration Race Conditions:**

- **Status:** Not an issue
- **Rationale:** Eventual consistency assumed, local network ensures convergence within seconds
- **Behavior:** Multiple Moss instances may register simultaneously, last-write-wins acceptable

### Moss Startup Fast-Sync

**Purpose:** Rebuild Stone registry quickly after Moss restart

**Behavior:**

1. Moss boots, Stone registry empty
2. Query Lantern HTTP directory (if configured): `GET /api/stones`
3. For each known Stone endpoint, send HTTP probe: `GET /api/garden/stones`
4. Aggregate responses, populate registry with current cursors
5. Begin listening to UDP lifecycle broadcasts
6. Passive updates via ongoing broadcasts

**Fallback:** If Lantern unavailable, rely solely on passive UDP broadcasts (registry rebuilds within 90s via TTL checks)

**Configuration:** Set `fast_sync_on_startup = true` with `fast_sync_timeout = 5` seconds in `[discovery]` section.

---

## Garden-Moss Daemon

### Overview

**Binary:** `garden-moss`  
**Installation:** `/usr/local/bin/garden-moss`  
**Service:** `garden-moss.service` (systemd)  
**Port:** 7185 (HTTP API)  
**Language:** Rust (Axum framework)  
**Config:** `/etc/zen-garden/garden-moss.toml`

### Configuration

**File Locations:**

- Linux: `/etc/zen-garden/garden-moss.toml`
- Windows: `./moss.toml` (current directory)

**Configuration Priority (highest to lowest):**

1. CLI arguments (`--stone-name`, `--port`, `--log-level`)
2. Environment variables (`STONE_NAME`, `PORT`, `RUST_LOG`)
3. Configuration file (`moss.toml`)
4. Built-in defaults

**File Format:**

```toml
# Stone identifier (unique name for this node)
stone_name = "stone-01"

# HTTP server port for Moss API
port = 7185

# Logging verbosity level (trace, debug, info, warn, error)
log_level = "info"
```

**CLI Arguments:**

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

**Environment Variables:**

```bash
# Set via environment
export STONE_NAME=stone-production
export PORT=8080
export RUST_LOG=debug
moss

# Or inline
STONE_NAME=stone-dev PORT=3002 moss
```

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

### Feature 1: HTTP API Server

**Framework:** Axum (Tokio async runtime)

#### Endpoints

```
# Operations (RPC-style, path parameters, defaults to ALL when viable)
POST   /api/operations/offer/{offering}   # Install service from template
POST   /api/operations/remove/{target}    # Uninstall service or pebble
POST   /api/operations/upgrade            # Upgrade all services (default)
POST   /api/operations/upgrade/{service}  # Upgrade specific service
POST   /api/operations/rest/{service}     # Stop service, preserve data
POST   /api/operations/wake/{service}     # Resume service
POST   /api/operations/reload             # Reload compose file

# Collections (RESTful for queries)
GET    /api/services               # List installed services
GET    /api/services/{name}        # Service details
GET    /api/compose                # Current docker-compose.yml
GET    /api/offerings              # Validated offerings index (includes tags + compatibility decision)
GET    /api/offerings/{name}       # Offering details (image, ports, tags, compatibility)
POST   /api/offerings/refresh      # Rebuild offerings index (template/frontmatter changes)
GET    /api/announcements          # Current mDNS announcements
GET    /api/garden/stones          # Known Stones (from moss_online broadcasts)

# Health & Metadata
GET    /health                     # Daemon + container health
GET    /info                       # Stone info (name, version, resources)

# Interactive API (optional)
GET    /api/docs                   # Swagger UI (if enabled in config)
GET    /api/openapi.json           # OpenAPI spec (if enabled)

# Lantern Integration (optional - Lantern HTTP endpoint)
POST   /gather                     # Lantern receives Moss state updates
```

#### Operation ID Tracking

**Purpose:** Correlate distributed operations across multiple Stones

**Format:** GUIDv7 (time-ordered, RFC 9562)

**Behavior:**

- Rake generates operation ID for `--all` operations (e.g., `upgrade all`)
- Operation ID included in request body to each Moss
- Each Moss logs: `"Started upgrade for services [mongodb, redis], operation_id: 01936d2e..."`
- Each Moss logs: `"Finished upgrade, status: success, operation_id: 01936d2e..."`
- Enables correlation in centralized logging (Loki, Elasticsearch, etc.)

**API Parameters:**

- `operation_id` (optional string) in request body for all operations:
  - `/api/operations/offer/{offering}`
  - `/api/operations/remove/{target}`
  - `/api/operations/upgrade` and `/api/operations/upgrade/{service}`
  - `/api/operations/rest/{service}`
  - `/api/operations/wake/{service}`
  - `/api/operations/reload`

#### Request/Response Examples

**Install Service:**

```json
POST /api/operations/offer/mongodb
{
  "version": "7.0",  // Optional: defaults to latest
  "operation_id": "01936d2e-8f4a-7890-b123-456789abcdef"  // Optional GUIDv7 for group operations
}

Response 201:
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

**Remove Service:**

```json
POST /api/operations/remove/mongodb
{
  "volumes": true,      // Optional: remove volumes too
  "operation_id": "01936d2e-..."
}

Response 200:
{
  "status": "removed",
  "target": "mongodb",
  "operation_id": "01936d2e-..."
}
```

**Upgrade Service:**

```json
# Upgrade all services (default)
POST /api/operations/upgrade
{
  "dry_run": false,
  "operation_id": "01936d2e-..."
}

# Upgrade specific service
POST /api/operations/upgrade/mongodb
{
  "version": "8.0",     // Optional: specific version
  "dry_run": false,
  "operation_id": "01936d2e-..."
}

Response 200:
{
  "status": "upgraded",
  "services": ["mongodb"],
  "changes": [
    { "service": "mongodb", "from": "7.0.4", "to": "8.0.1" }
  ],
  "operation_id": "01936d2e-..."
}
```

**List Services:**

```json
GET /api/services

Response 200:
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
        "cpu_friendly": "2.50%",
        "memory_bytes": 471859200,
        "memory_limit_bytes": 8589934592,
        "memory_percent": 5.49,
        "memory_friendly": "450.00 MB",
        "memory_limit_friendly": "8.00 GB",
        "network_rx_bytes": 1024000,
        "network_tx_bytes": 512000,
        "network_rx_friendly": "1000.00 KB",
        "network_tx_friendly": "500.00 KB",
        "block_read_bytes": 104857600,
        "block_write_bytes": 52428800,
        "block_read_friendly": "100.00 MB",
        "block_write_friendly": "50.00 MB",
        "uptime_seconds": 3600,
        "uptime_friendly": "1h 0m"
      }
    }
  ],
  "total": 1,
  "stone_health": "healthy"
}
```

**Stone Information with Resources:**

```json
GET /info

Response 200:
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

**Health Check:**

```json
GET /health

Response 200:
{
  "status": "healthy",
  "moss_version": "0.1.0",
  "stone_name": "stone-01",
  "docker_running": true,
  "containers_running": 2,
  "containers_total": 2,
  "warnings": []
}

# With warnings:
{
  "status": "degraded",
  "warnings": [
    "High container count (6) for Stone capacity",
    "MongoDB container restarting (3 times in 10 minutes)"
  ]
}
```

**Garden Topology (Known Stones):**

```json
GET /api/garden/stones

Response 200:
{
  "stones": [
    {
      "name": "stone-01",
      "api_endpoint": "http://stone-01.local:7185",
      "health": "healthy",
      "last_seen": "2026-01-15T10:30:45Z",
      "capabilities": {
        "max_services": 4,
        "stone_type": "standard",
        "features": ["compose", "health", "pond"]
      },
      "moss_version": "0.1.0"
    },
    {
      "name": "stone-02",
      "api_endpoint": "http://stone-02.local:7185",
      "health": "healthy",
      "last_seen": "2026-01-15T10:30:42Z",
      "capabilities": {
        "max_services": 2,
        "stone_type": "pebble"
      }
    }
  ],
  "lantern": {
    "endpoint": "http://stone-09.local:3002",
    "status": "online",
    "last_broadcast": "2026-01-15T10:30:30Z"
  },
  "total_stones": 2
}
```

**Purpose:** Used by Rake CLI for UDP broadcast discovery fallback. After receiving one Stone's endpoint via UDP, Rake queries this endpoint to discover all Stones in the garden.

**Data Source:** Moss builds this list from `moss_online` lifecycle broadcasts received on UDP port 3003. Stones are marked offline after 90s TTL or immediate `moss_offline` broadcast.

**Cursor-Based Updates (Efficient Polling):**

Remote Rake instances can poll for changes using a cursor to avoid unnecessary data transfer:

```json
GET /api/garden/stones?since=01936d2e-8f4a-7890-b123-456789abcdef

Response 200 (no changes):
{
  "has_updates": false,
  "cursor": "01936d2e-8f4a-7890-b123-456789abcdef"
}

Response 200 (with changes):
{
  "has_updates": true,
  "cursor": "01936d3f-9a2b-8901-c234-567890abcdef",  // New cursor
  "changes": {
    "added": [
      {
        "name": "stone-03",
        "api_endpoint": "http://stone-03.local:7185",
        "health": "healthy",
        "last_seen": "2026-01-15T10:35:12Z"
      }
    ],
    "updated": [
      {
        "name": "stone-01",
        "health": "degraded",  // Changed
        "last_seen": "2026-01-15T10:35:10Z"
      }
    ],
    "removed": ["stone-02"]  // Went offline
  }
}
```

**Cursor format:** GUIDv7 (time-ordered), generated each time topology changes

**Cursor validation:**

- Moss stores only current cursor (single String field)
- Matching cursor → "no changes" response
- Different cursor → full topology + new cursor

**Benefits:**

- Minimal bandwidth: "No changes" response is <100 bytes
- Efficient polling: 30-60s interval with near-zero cost
- Delta updates: Only changed Stones transferred (when cursor matches last poll)
- State consistency: Cursor guarantees no missed updates
- Trivial memory: 16 bytes vs 100KB for multi-cursor cache

**Error Response:**

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

#### Technical Requirements

- Async/await with Tokio runtime
- Graceful shutdown on SIGTERM
- Structured JSON logging
- CORS enabled (for future web dashboard)
- Request/response validation with serde

#### Optional: Swagger UI

**Library:** `utoipa` + `utoipa-swagger-ui`

**Purpose:** Web-based interactive API interface for direct command execution

**Features:**

- Auto-generated OpenAPI spec from Axum routes
- Interactive "Try it out" buttons for all endpoints
- Execute operations directly from browser (alternative to Rake CLI)
- Useful for:
  - Manual administrative tasks
  - Debugging/troubleshooting
  - API exploration and learning
  - Mobile/remote access (phone browser)
  - Ad-hoc operations without installing Rake

**Configuration:** Default disabled for security (`swagger_enabled = false`), configurable path (`swagger_path = "/api/docs"`), can require Pond authentication when active (`swagger_require_pond_auth = true`).

**Endpoints:**

- `GET /api/docs` - Swagger UI interface
- `GET /api/openapi.json` - OpenAPI 3.0 specification

**Security:**

- Default: **disabled** (production safety)
- When enabled without Pond: accessible to anyone on network
- When enabled with Pond: requires valid mTLS certificate
- Returns 404 when disabled (no hint it exists)

**Implementation:** Use `utoipa` crate for OpenAPI schema generation and `utoipa_swagger_ui` for Swagger UI integration. Define API documentation with derive macros for paths and schemas.

### Feature 2: mDNS Announcer

**Library:** `mdns-sd` crate  
**Service Type (self):** `_moss._tcp.local.`  
**Service Type (services):** `_koan-stone._tcp.local.`

#### Announcement Types

**Moss Self-Announcement:**

```
stone-01-moss._moss._tcp.local.
TXT: stone_name=stone-01
     version=0.1.0
     api_port=3001
     health=healthy
```

**Native Service Announcement:**

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

**Agnostic Sidecar Announcement:**

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

#### Responsibilities

1. Announce Moss itself for Rake discovery
2. Announce native services with proper TXT records
3. Announce agnostic sidecars with capabilities
4. Update health status in real-time
5. Remove announcements when services stop
6. Handle network interface changes gracefully

#### Technical Requirements

- Spawn mDNS responder on startup
- Re-announce on service lifecycle events
- Update TXT records on health changes
- TTL: 120 seconds (configurable)
- Log all announcement events

### Feature 3: Docker Compose Manager

**Compose File:** `/etc/zen-garden/docker-compose.yml`

#### Responsibilities

1. Read and parse existing docker-compose.yml
2. Add services to compose file atomically
3. Remove services from compose file
4. Update service versions
5. Detect port conflicts before applying
6. Apply changes with rollback on failure
7. Validate container startup after changes

#### Port Conflict Resolution

Strategy: Default to service defaults, auto-reassign on conflict. When MongoDB requests port 27017 but it's already in use, assign 27018 instead, update docker-compose.yml with actual port, announce actual port via mDNS, and log warning about the reassignment.

#### Atomic Application Flow

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

#### Technical Requirements

- YAML parsing with `serde_yaml`
- Atomic file writes (write temp, rename)
- Process spawning with `tokio::process`
- Port availability checks (TCP bind test)
- Container health validation after startup

### Feature 4: Service Template Handler

**Template Location:** `/etc/zen-garden/templates/`

#### Template Format

Templates are YAML files defining service metadata (name, offering, description, categories), version options with defaults, Docker configuration for both native and agnostic modes (images, ports, volumes, environment variables), and mDNS announcement details (offering names, protocols, capabilities).

#### Validation Rules

1. Template must have `name`, `offering`, `docker` sections
2. Image tags must be valid (no shell injection)
3. Port numbers: 1-65535
4. No arbitrary shell commands
5. Environment variables: `${VAR}` or `${VAR:-default}` only
6. Volume names: `^[a-z0-9-]+$`

#### Technical Requirements

- YAML parsing with schema validation
- Variable substitution (VERSION, passwords)
- Template registry (scan directory on startup)
- Reject templates with invalid syntax

### Feature 5: Health Monitor

**Check Interval:** 30 seconds

#### Responsibilities

1. Check Docker daemon status
2. Check container status (running, restarting, stopped)
3. Monitor resource usage (RAM, CPU)
4. Detect restart loops (>3 restarts in 10 min)
5. Update mDNS TXT records with health status
6. Log health transitions

#### Health States

Services can be Healthy (all checks passing), Degraded (some issues but functional), or Unavailable (critical failure).

#### Check Logic

```
For each service:
  1. Container exists? → No: Unavailable
  2. Container running? → No: Unavailable
  3. Restart count > 3? → Yes: Degraded
  4. Memory > 80% limit? → Yes: Degraded
  5. All checks pass → Healthy
```

#### Technical Requirements

- Docker API client (`bollard` crate)
- Background task spawned on startup
- Shared state with Arc<RwLock<...>>
- Integration with mDNS announcer

### Feature 6: Resource Monitor

#### Stone Capacity Thresholds

Mini Stone (2GB RAM): max 2 services, warn at 1. Standard Stone (4GB RAM): max 4 services, warn at 3. Large Stone (8GB+ RAM): max 8 services, warn at 6.

#### Responsibilities

1. Count running containers
2. Estimate total RAM usage
3. Compare against Stone capacity
4. Warn when approaching limits
5. Include warnings in `/health` endpoint

#### Warning Examples

- "High container count (6) for Stone capacity (Standard: 4 max)"
- "Total RAM usage (3.2GB) approaching system limit (4GB)"
- "Container 'mongodb' restarting frequently (5 times in 10 minutes)"

#### Technical Requirements

- System info (`sysinfo` crate)
- Container stats via Docker API
- Warnings stored in-memory (cleared on restart)

### Feature 7: Configuration

**Config File:** `/etc/zen-garden/garden-moss.toml`

Configuration includes stone name, API port and CORS settings, Docker compose paths, mDNS settings with TTL and caching, health check intervals and thresholds, logging level and format options.

#### Technical Requirements

- TOML parsing (`toml` or `config` crate)
- Environment variable overrides (`MOSS_API_PORT`)
- Sensible defaults (works without config file)

### Feature 8: Lantern Integration (Optional)

**Purpose:** Enable Lantern UI dashboard to monitor and control the garden

**Behavior:** Moss responds to Lantern UDP broadcast requests with state updates

#### Configuration

Default disabled (`enabled = false`), configurable response mode (auto/full/diff), UDP listener enabled by default for Lantern broadcasts.

#### Discovery Protocol

**Lantern broadcasts via UDP** (periodic, e.g., every 30s):

```
UDP broadcast to network (port 7184)
Message: "LANTERN_GATHER http://lantern-host:3000/gather"
```

**Moss lifecycle broadcasts** (always sent, regardless of Lantern presence):

**On startup:**

```
UDP broadcast to network (port 7184)
Message: {
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

```
UDP broadcast to network (port 7184)
Message: {
  "event": "moss_offline",
  "stone_name": "stone-01",
  "timestamp": "2026-01-15T16:30:00Z",
  "reason": "graceful_shutdown"  // or "restart", "upgrade", etc.
}
```

**Moss responds to Lantern gather requests** (POST or UDP broadcast):

**Option A: HTTP POST to Lantern endpoint** - POST to Lantern's /gather endpoint with JSON state payload.

**Option B: UDP broadcast response** (supports multiple Lanterns) - UDP broadcast to network (port 7184) with JSON state message.

#### State Payload

**First response (full graph):**

```json
{
  "mode": "full",
  "stone_name": "stone-01",
  "moss_version": "0.1.0",
  "capabilities": {
    "max_services": 4,
    "stone_type": "standard",
    "features": ["compose", "health", "pond"]
  },
  "services": [
    {
      "name": "mongodb",
      "offering": "mongodb",
      "version": "7.0.4",
      "status": "running",
      "health": "healthy",
      "ports": { "native": 27017, "agnostic": 8080 },
      "uptime": 3600,
      "memory_mb": 450
    }
  ],
  "resources": {
    "cpu_percent": 15.3,
    "memory_used_mb": 1200,
    "memory_total_mb": 4096,
    "disk_used_gb": 42.1
  },
  "warnings": []
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
        "health": "degraded", // Changed from healthy
        "memory_mb": 520 // Changed from 450
      }
    ],
    "resources": {
      "cpu_percent": 42.7 // Changed from 15.3
    },
    "warnings": ["MongoDB container restarting (3 times in 10 minutes)"]
  }
}
```

#### Implementation Flow

On Moss startup: bind UDP socket on port 3002, broadcast "moss_online" event immediately, start background task listening for Lantern broadcasts. On Lantern broadcast received: parse endpoint URL, check if first response to this Lantern (prepare full state vs diff since last response), POST state to Lantern's /gather endpoint or broadcast via UDP, cache last sent state per Lantern. On Moss shutdown (SIGTERM handler): broadcast "moss_offline" event, wait 500ms for delivery, proceed with graceful shutdown.

#### Lifecycle Event Handling

**Lantern/other Stones receive lifecycle events:**

- `moss_online`: Add Stone to registry, mark as healthy
- `moss_offline`: Mark Stone as offline immediately (don't wait for TTL)

**TTL fallback:** If Moss crashes without broadcasting offline:

- Lantern/Stones mark as offline after TTL expires (e.g., 90s)
- Prevents stale "online" state for crashed/powered-off Stones

**Benefits:**

- Immediate visibility when Stones join/leave
- Graceful shutdown notification (planned maintenance)
- TTL provides safety net for crashes
- Other Stones can react to topology changes

#### Multi-Lantern Support (Under Discussion)

**Challenge:** Multiple Lanterns may exist on network

- Home lab primary + backup
- Multi-site deployments
- Redundancy for high availability

**Option A: HTTP POST (current)**

- Moss POSTs to each Lantern individually
- Each Lantern includes its endpoint in broadcast
- Simpler, direct communication

**Option B: UDP Broadcast Response**

- Moss broadcasts state updates via UDP
- All Lanterns receive same data
- More resilient, supports N Lanterns
- Potential for network congestion

**Decision pending:** Will finalize in implementation phase

#### Lantern Dashboard Use Cases

Once Lantern receives Moss responses, the UI can:

- Display garden topology (all Stones + services)
- Show real-time health status across all Stones
- Execute operations via Garden-Moss HTTP API (using OpenAPI spec)
- Alert on warnings/degraded states
- Visualize resource utilization

#### Benefits

- **Proactive announcements**: Stones broadcast online/offline immediately
- **Immediate visibility**: Lantern/Stones see topology changes in real-time
- **Graceful shutdown**: Planned maintenance broadcasts "offline" event
- **Crash safety**: TTL fallback handles ungraceful termination
- **UDP broadcast**: Efficient discovery, no IP tracking needed
- **Moss self-announces**: No Lantern polling individual IPs
- **Efficient diffs**: Reduce network overhead after first response
- **Resilient**: Moss works independently without Lantern
- **Multi-Lantern ready**: Architecture supports redundancy
- **Opt-in**: Disabled by default, no dependency on Lantern

---

### Feature 9: UDP Broadcast Discovery Listener

**Purpose:** Enable Rake discovery on Windows without mDNS browse capability

**Port:** 3004 (listens for Rake discovery requests)

**Library:** Tokio UDP socket

**Stone Registry:** Maintains hot cache from lifecycle broadcasts (single cursor, updated on topology changes)

**Library:** Tokio UDP socket

**Stone Registry:** Maintains hot cache from lifecycle broadcasts (single cursor, updated on topology changes)

#### Behavior

When Moss receives a discovery broadcast:

1. Parse request: `{ "discover": "moss", "request_id": "uuid", "requester": "rake-cli" }`
2. Calculate election delay: `blake3::hash(stone_name + request_id)[0] * 10` ms
3. Wait for delay (0-2550ms range, deterministic but unpredictable)
4. Check if another Moss already responded (optional early exit optimization)
5. Send unicast UDP response to requester IP:3005

#### Response Payload

```json
{
  "stone_name": "stone-01",
  "stone_endpoint": "http://stone-01.local:7185",
  "lantern_endpoint": "http://stone-09.local:3002", // If Lantern discovered
  "moss_version": "0.1.0"
}
```

**Lantern endpoint logic:**

- If Moss has received Lantern broadcasts → include `lantern_endpoint`
- If no Lantern on network → omit field or set to `null`
- Rake can use Lantern endpoint for web UI access or directory fallback

#### Election Algorithm

Same algorithm as Pond security (consistent, well-tested): Use BLAKE3 hash of stone_name + request_id to calculate deterministic delay (0-2550ms). Each stone independently calculates its response delay, preventing reply storms.

**Properties:**

- Deterministic: Same stone + request always produces same delay
- Unpredictable: Different request IDs produce different order
- Prevents reply storm: Only one Stone likely to respond
- No coordination needed: Each Moss calculates independently

#### Stone Registry Tracking

**Data source:** `moss_online` broadcasts received on UDP port 3003

**In-memory registry:** StoneRegistry contains HashMap of StoneInfo (name, api_endpoint, health, capabilities, last_seen, moss_version), optional LanternInfo (endpoint, last_broadcast), and state_cursor (GUIDv7 updated on topology changes).

**Update logic:**

- `moss_online` received → Add/update Stone in registry, **generate new cursor**
- `moss_offline` received → Mark Stone as offline immediately, **generate new cursor**
- TTL check (every 30s) → Mark Stones offline if `last_seen > 90s ago`, **generate new cursor if changed**
- Lantern broadcast received → Update `lantern.last_broadcast`
- **Cursor generation:** GUIDv7 timestamp-based, increments on any topology change

**API endpoints:**

- `GET /api/garden/stones` - Full topology
- `GET /api/garden/stones?since=<cursor>` - Delta updates (cursor-based polling)

**Cursor validation (simplified):**

- Moss stores only **current cursor** (single String field)
- Query with matching cursor → return "no changes" (has_updates: false)
- Query with different cursor → return full topology + new cursor
- **Rationale:** 99.98% memory savings (100KB → 16 bytes), trivial complexity, handles 30s polling perfectly

#### Configuration

Default localhost-first discovery enabled, UDP broadcast with 3s timeout, retry on failure with max 3 retries, endpoint cache TTL of 300 seconds for remote Rake, background poll interval of 30 seconds with cursor-based delta updates enabled by default.

#### Benefits

- **Windows-compatible**: Solves mDNS browse limitation
- **Distributed topology awareness**: Each Moss knows about all other Stones
- **No central directory dependency**: Works without Lantern
- **Idempotent discovery**: Rake can re-query anytime
- **Lantern-aware**: Response includes web UI endpoint when available

---

## Rake CLI

### Overview

**Binary:** `garden-rake`  
**Installation:** User PATH or `/usr/local/bin/`  
**Language:** Rust (clap for CLI parsing)

### Responsibilities

1. Query localhost Moss for hot-cached topology (zero discovery)
2. Fall back to network discovery if no local Moss (UDP broadcast, mDNS, Lantern)
3. Send HTTP requests to Moss API
4. Parse command-line arguments and flags
5. Handle target selection (local, specific Stone, all Stones)
6. Generate operation IDs for group operations (`--all`)
7. Pretty-print responses with colors and tables
8. Provide user-friendly error messages

### Feature 1: Hot Cache Discovery (Localhost-First)

**Strategy:** Query hot cache instead of performing network discovery

**Common case (90%):** Rake runs on a Stone (alongside Moss)

- Query: `GET http://localhost:7185/api/garden/stones`
- Latency: <1ms (localhost HTTP)
- Discovery: Zero (Moss already has topology from `moss_online` broadcasts)
- Result: Instant access to full garden topology

**Special case (10%):** Rake runs on developer machine (no local Moss)

- UDP discovery to get any Moss HTTP endpoint
- Cache that endpoint and initial topology
- Background polling (30s-60s) using cursor-based updates
  - "Any updates since cursor X?" → "No" (no data transfer)
  - "Any updates since cursor X?" → "Yes" (delta + new cursor)
- Cursor-based polling minimizes bandwidth, only transfers changes
- On error, rediscover via UDP and resync full topology

#### Discovery Flow

Priority order: (1) Try localhost:7185 (instant, zero discovery, most common case), (2) UDP broadcast discovery to query any Moss cache, (3) mDNS browse (Linux/macOS), (4) Lantern HTTP query, (5) Manual --at flag specification. Query localhost first, then fall back to network methods only when needed.

#### Target Selection

Targets can be Local (default, localhost or hostname), Specific (--at stone-01), or All (--all for garden-wide operations).

#### Discovery Priority (Hot Cache Strategy)

**Discovery methods** (attempted in order):

1. **Localhost Cache Query** (instant, zero discovery) - Query `http://localhost:7185/api/garden/stones`
   - **Common case:** Rake runs on Stone → <1ms, full topology available
   - **Benefit:** Zero network discovery, immediate access to hot cache
2. **UDP Broadcast + Election** (Windows-compatible) - Single Stone responds with endpoint
   - Rake queries that Stone's cache: `GET /api/garden/stones`
   - **Benefit:** One broadcast reveals full topology via cache
3. **mDNS Browse** (Linux/macOS fallback) - Browse `_moss._tcp.local.`
   - Used when localhost and UDP broadcast both fail
   - **Benefit:** Zero-config discovery on Unix-like systems
4. **Lantern HTTP** (directory fallback) - Query `GET http://lantern:3000/api/resolve`
   - Used when no Moss accessible directly
   - **Benefit:** Works across subnets, centralized directory
5. **Manual --at** (always works) - Direct connection to specified Stone
   - Example: `garden-rake status --at stone-01.local`
   - **Benefit:** Explicit control, bypasses discovery

**Key Insight:** Most operations are localhost queries with zero discovery overhead. Network discovery only needed for remote administration.

**Retry on failure:** If connection to cached Stone fails, re-query cache automatically (up to 3 retries).

**Cache freshness:** Moss maintains hot cache from continuous `moss_online` broadcasts (TTL 90s).

### Feature 1a: UDP Broadcast Discovery (Windows-Compatible)

**Purpose:** Enable auto-discovery on Windows without mDNS browse or Lantern dependency.

**Port:** 3004 (Rake discovery broadcasts)

#### Protocol Flow

```
1. Rake broadcasts discovery request
   UDP broadcast to 255.255.255.255:3004
   {
     "discover": "moss",
     "request_id": "550e8400-e29b-41d4-a716-446655440000",
     "requester": "rake-cli"
   }

2. All Moss instances receive broadcast
   Calculate election delay using blake3 hash (same algorithm as Pond):
   let hash = blake3::hash(format!("{}{}", stone_name, request_id));
   let delay_ms = (hash[0] as u64) * 10;  // Range: 0-2550ms

3. First responder (lowest hash) replies
   UDP unicast back to requester IP:3005
   {
     "stone_name": "stone-01",
     "stone_endpoint": "http://stone-01.local:7185",
     "lantern_endpoint": "http://stone-09.local:3002",  // If present (Web UI/dashboard)
     "moss_version": "0.1.0"
   }

4. Rake queries that Stone for full topology
   GET http://stone-01.local:7185/api/garden/stones
   {
     "stones": [
       {
         "name": "stone-01",
         "api_endpoint": "http://stone-01.local:7185",
         "health": "healthy",
         "capabilities": { "max_services": 4 }
       },
       {
         "name": "stone-02",
         "api_endpoint": "http://stone-02.local:7185",
         "health": "healthy"
       }
     ],
     "lantern": {
       "endpoint": "http://stone-09.local:3002",
       "status": "online"
     }
   }
```

#### Benefits

- **Zero-discovery common case**: Localhost query <1ms, no network discovery needed
- **Windows first-class support**: UDP broadcast solves mDNS browse limitation
- **No Lantern dependency**: Works with pure Moss network
- **Hot cache always available**: Every Moss has full topology from broadcasts
- **Single query reveals all**: One Stone responds, provides cached full topology
- **Election prevents reply storm**: Deterministic but unpredictable response
- **Idempotent**: Re-run anytime for fresh topology
- **Lantern-aware**: Response includes web UI endpoint if available

#### Configuration

Same as above - localhost-first with UDP broadcast fallback, endpoint caching, and cursor-based polling for efficiency.

#### Error Handling

- No response (timeout): Fall back to Lantern HTTP or manual `--at`
- Invalid response: Log warning, try next discovery method
- Connection to returned endpoint fails: Re-run discovery automatically

### Feature 2: HTTP Client

**Library:** `reqwest` crate

#### Client Operations

Send POST requests to Moss API endpoints for operations (offer, remove, upgrade, wake) with JSON payloads including operation_id for group operations. Send GET requests for queries (list services, get health, query garden stones). Include proper timeouts and retry logic.

#### Error Handling

- Connection refused → "Cannot reach Moss on stone-XX"
- Timeout → "Request timed out (is Moss running?)"
- 4xx → Display API error message
- 5xx → "Internal server error on stone-XX"

### Feature 3: Command Parser

**Library:** `clap` crate (subcommands)

#### Command Structure

Commands follow pattern: `garden-rake <SUBCOMMAND> [ARGS] [FLAGS]` with consistent flags across subcommands (--at for specific Stone, --all for garden-wide operations).

#### Subcommands

**Status:** `garden-rake status [--at stone-01] [--all]` - Show service status for local Stone, specific Stone, or all Stones.

**Offer (List/Query/Install):**

- `garden-rake offer` lists validated offerings by category.
- `garden-rake offer <name>` installs when `<name>` matches a known offering on the targeted stone.
- `garden-rake offer <query>` prints top ranked recommendations when `<query>` is not a known offering name.

Flags:

- `--at <endpoint|stone-name|anywhere>` targets a specific stone; `anywhere` runs cross-stone recommendations.
- `--prefer <tokens>` biases ranking (strong but non-blocking). Example: `--prefer ssd,nvme`.
- `--anywhere-on-fail` (install only) auto-runs cross-stone recommendations after a compatibility failure.

Related:

- `garden-rake offer <name> info` shows offering details + compatibility decision.
- `garden-rake offer refresh` rebuilds the offerings index on the targeted stone.

**Remove:** `garden-rake remove <SERVICE> [--at stone-01] [--volumes]` - Remove service, optionally including volumes. Special command `garden-rake remove pebble` removes Pond security from all Stones.

**List:** `garden-rake list [--at stone-01] [--all]` - List all available service offerings and installed services.

**Upgrade:** `garden-rake upgrade [SERVICE] [--at stone-01] [--all] [--version X.X]` - Upgrade specific service or all services to latest/specified version.

**Rest/Wake (Service Lifecycle):** `garden-rake rest <SERVICE>` stops a service, `garden-rake wake <SERVICE>` starts it again. Both support --at and --all flags.

**Security (Pond):** `garden-rake offer pebble` installs Pond mTLS security layer, `garden-rake remove pebble` removes it from all Stones.

### Feature 4: Output Formatting

**Library:** `colored` for colors, custom table formatting

#### Status Output

```
Stone: stone-01
Status: Healthy
Moss Version: 0.1.0
Docker: Running

Services (2):
┌──────────┬─────────┬─────────┬────────┬──────────┬────────────┐
│ Name     │ Offering│ Version │ Health │ Uptime   │ Memory     │
├──────────┼─────────┼─────────┼────────┼──────────┼────────────┤
│ mongodb  │ mongodb │ 7.0.4   │ ✓      │ 2d 3h    │ 450 MB     │
│ redis    │ redis   │ 7.2.3   │ ✓      │ 5h 23m   │ 80 MB      │
└──────────┴─────────┴─────────┴────────┴──────────┴────────────┘

Total: 2 services, 530 MB memory
```

#### Install Output

```
Discovering Garden-Moss Daemons... ✓ (found stone-01)
Installing mongodb on stone-01...
  [1/4] Validating template... ✓
  [2/4] Checking port availability... ✓ (27017, 8080)
  [3/4] Updating docker-compose.yml... ✓
  [4/4] Starting containers... ✓

✓ Successfully installed mongodb 7.0.4

Services:
  - mongodb (native): mongodb://stone-01.local:27017
  - mongodb-agnostic (HTTP): http://stone-01.local:8080

Next steps:
  1. Use connection string: zen-garden:mongodb
  2. Check status: garden-rake status
```

#### JSON Output (--json flag)

### Feature 5: Error Handling

#### User-Friendly Errors

#### Error Philosophy

- **Template validation:** Warn and proceed (log issues)
- **Batch operations (--all):** Continue with other Stones on failure
- **Destructive operations:** Require explicit confirmation
- **Network errors:** Provide actionable troubleshooting steps

# Service not found

✗ Error: Offering 'mysql' not found

Available offerings:

- mongodb
- postgresql
- redis
- sqlserver

Try: garden-rake offer <service>

````

### Feature 6: Target Selection

#### Parallel Execution (--all)

```rust
async fn execute_on_all_stones(operation: Operation) -> Result<()> {
    let endpoints = discover_moss(Target::All).await?;

    let tasks: Vec<_> = endpoints.iter().map(|endpoint| {
        let op = operation.clone();
        tokio::spawn(async move {
            let client = MossClient::new(endpoint);
            op.execute(&client).await
        })
    }).collect();

    let results = join_all(tasks).await;
    display_aggregate_results(results)?;

    Ok(())
}
````

---

## Service Templates & Offerings

### Overview

Zen Garden uses curated service templates called "offerings" to ensure consistent, validated deployments. Each offering defines both native and optional agnostic sidecar configurations.

### Taxonomy and query recommendations

Offerings include lightweight metadata used for discovery and recommendations:

- **Category**: a single stable category (e.g., `data`, `cache`, `search`, `vector`, `messaging`).
- **Tags**: short lowercase tokens describing intent (e.g., `database`, `document`, `sql`, `nosql`).
- **Synonym dictionary**: `manifests/taxonomy.dictionary.yaml` maps user tokens (e.g., `db`, `doc`) to canonical tokens.

Rake uses category + tags (and compatibility) to provide query-based recommendations:

- `garden-rake offer database,document` prints the top 3 recommendations on the targeted stone.
- `garden-rake offer database,document --at anywhere --prefer ssd` ranks `(stone, offering)` pairs across discovered stones.

Compatibility `fail` offerings are never recommended.

### Offering Registry Structure

```
/etc/zen-garden/templates/
├── mongodb.yml
├── postgresql.yml
├── redis.yml
├── sqlserver.yml
├── mysql.yml
└── custom/
    └── user-defined-app.yml
```

### Native vs Agnostic Services

**Native Service** - Database/service on its native protocol

- Examples: MongoDB (port 27017), PostgreSQL (5432), Redis (6379)
- Uses vendor-specific drivers
- Full feature set available
- Best performance (no HTTP overhead)

**Agnostic Sidecar** - HTTP REST API wrapping native service

- Port: 8080+ (auto-assigned)
- Database-neutral HTTP API
- Based on Koan EntityController patterns
- Enables backend portability

**Sidecars are per-service, not shared:**

- Stone running MongoDB + SQL Server = 2 sidecars
- Each sidecar dedicated to its parent service
- Independent port allocation per sidecar

### Service Discovery

**Specific requests (native):**

```
zen-garden:mongodb → MongoDB native (port 27017)
zen-garden:redis → Redis native (port 6379)
```

**Category requests (agnostic):**

```
zen-garden:database → Any database sidecar (port 8080+)
zen-garden:document-database → MongoDB/CouchDB sidecar
```

---

## Agnostic Data API

### Overview

Optional HTTP REST API that provides database-neutral access to services. Based on Koan EntityController patterns discovered in the codebase.

**Status:** Future implementation (documented for completeness)

### URL Structure

```
/v1/data/{set}/entities/{type}
/v1/data/{set}/entities/{type}/{id}
```

**Pattern enforces security:** Version + set + model prevents injection attacks.

### Endpoints

```
# CRUD Operations
GET    /v1/data/{set}/entities/{type}        # List with pagination
GET    /v1/data/{set}/entities/{type}/{id}   # Get by ID
POST   /v1/data/{set}/entities/{type}        # Create
PUT    /v1/data/{set}/entities/{type}/{id}   # Update
DELETE /v1/data/{set}/entities/{type}/{id}   # Delete

# Advanced Operations
POST   /v1/data/{set}/entities/{type}/query  # Filter query
POST   /v1/data/{set}/entities/{type}/bulk   # Bulk upsert

# Discovery
GET    /v1/data/sets                         # List sets
GET    /v1/data/sets/{set}/entities          # List entity types
```

### Query Filter Syntax

Based on Koan JsonFilterBuilder (MongoDB-like):

```json
POST /v1/data/myapp/entities/users/query
{
  "filter": {
    "age": { "$gte": 18 },
    "status": "active",
    "email": { "$exists": true }
  },
  "sort": [{ "field": "createdAt", "descending": true }],
  "page": 1,
  "pageSize": 25
}
```

**Supported operators:** `$gte`, `$lte`, `$gt`, `$lt`, `$in`, `$all`, `$exists`, `$and`, `$or`, `$not`, wildcards (`Al*`)

### Set-Based Isolation

Sets map to backend namespaces:

- **MongoDB (database mode):** Each set = separate database
- **MongoDB (collection mode):** Each set = collection prefix
- **PostgreSQL/SQL Server:** Each set = schema
- **Redis:** Each set = keyspace prefix

Example:

```
POST /v1/data/myapp/entities/users
→ MongoDB: db.myapp.users.insertOne(...)
→ PostgreSQL: INSERT INTO myapp.users ...
→ Redis: HSET myapp:users:123 ...
```

### Pagination

**All list/query endpoints return pages by default.**

```
GET /v1/data/myapp/entities/users?page=2&pageSize=50&sort=-createdAt

Response Headers:
X-Page: 2
X-Page-Size: 50
X-Total-Count: 1247
X-Total-Pages: 25
```

### Bulk Operations

```json
POST /v1/data/myapp/entities/users/bulk
[
  { "id": "1", "name": "Alice", "age": 30 },
  { "id": "2", "name": "Bob", "age": 25 }
]

Response:
{
  "created": 1,
  "updated": 1,
  "errors": []
}
```

---

## mDNS Discovery

### Service Types

**Moss Self-Announcement:** `_moss._tcp.local.`  
**Service Announcement:** `_koan-stone._tcp.local.`

### TXT Record Schema

**Required fields:**

- `offering` - Service identifier
- `port` - Service port
- `protocol` - Transport (native, agnostic)
- `version` - Service version

**Optional fields:**

- `categories` - Comma-separated taxonomy
- `capabilities` - Feature list
- `tags` - User/auto tags
- `priority` - Selection priority (0-100)
- `health` - Health status
- `set_mode` - Set mapping strategy

### Client Resolution Algorithm

```
1. Parse connection string: zen-garden:mongodb[tags]
2. Query mDNS: _koan-stone._tcp.local.
3. Filter by service type:
   - Known service (mongodb) → native endpoints
   - Generic category (database) → agnostic endpoints
4. Filter by tags (if specified)
5. Select best:
   - Priority: health > priority > response time
   - Load balance: round-robin across equals
6. Build connection string
```

---

## Development & Testing Strategy

### Docker-Based Multi-Stone Testing

**Purpose:** Test multi-Moss communication (UDP, mDNS, HTTP) locally without physical hardware.

**Benefits:**

- Spin up 3-4 Moss containers simultaneously
- Isolated network namespaces (simulates real distributed environment)
- Test UDP broadcasts, mDNS discovery, and HTTP APIs
- Fast iteration (seconds to restart topology)
- CI/CD integration ready
- Validates lifecycle events, failover, cursor polling

**Setup:**

```yaml
# tests/docker-compose.test.yml
version: "3.8"

services:
  stone-01:
    build: ../../moss
    container_name: stone-01
    hostname: stone-01
    ports:
      - "3001:7185"
    environment:
      - STONE_NAME=stone-01
      - RUST_LOG=debug
    networks:
      garden:
        ipv4_address: 172.20.0.11

  stone-02:
    build: ../../moss
    container_name: stone-02
    hostname: stone-02
    ports:
      - "3002:7185"
    environment:
      - STONE_NAME=stone-02
      - RUST_LOG=debug
    networks:
      garden:
        ipv4_address: 172.20.0.12

  stone-03:
    build: ../../moss
    container_name: stone-03
    hostname: stone-03
    ports:
      - "3003:7185"
    environment:
      - STONE_NAME=stone-03
      - RUST_LOG=debug
    networks:
      garden:
        ipv4_address: 172.20.0.13

  lantern:
    build: ../../lantern
    container_name: lantern
    hostname: lantern
    ports:
      - "3000:3000"
    environment:
      - FLASK_ENV=development
    networks:
      garden:
        ipv4_address: 172.20.0.10

networks:
  garden:
    driver: bridge
    ipam:
      config:
        - subnet: 172.20.0.0/16
```

**Test Scenarios:**

1. **UDP Broadcast Discovery:**

   ```bash
   docker-compose -f tests/docker-compose.test.yml up -d
   # All Stones receive broadcasts on 172.20.0.0/16
   docker logs stone-01  # Should see UDP broadcasts from stone-02, stone-03
   ```

2. **mDNS Discovery:**

   ```bash
   docker exec stone-01 avahi-browse -a
   # Should see _moss._tcp.local announcements from other Stones
   ```

3. **Lifecycle Events:**

   ```bash
   docker stop stone-02
   docker logs stone-01  # Should see moss_offline event
   docker start stone-02
   docker logs stone-01  # Should see moss_online event
   ```

4. **Cursor-Based Polling:**

   ```bash
   # From host, test cursor polling
   curl http://localhost:7185/api/garden/stones
   CURSOR=$(curl -s http://localhost:7185/api/garden/stones | jq -r .cursor)
   curl "http://localhost:7185/api/garden/stones?since=$CURSOR"
   # Should return has_updates: false
   ```

5. **Multi-Garden-Lantern registration:**
   ```bash
   docker-compose up --scale lantern=2
   # Verify Stones register with both Lanterns
   ```

**Rake Testing:**

```bash
# Build Rake, point at Docker network
export MOSS_ENDPOINT=http://localhost:7185
cargo run --bin garden-rake -- list
cargo run --bin garden-rake -- offer mongodb --at stone-01

# Test discovery from outside Docker network
cargo run --bin garden-rake -- list --all
# Should discover all 3 Stones via HTTP queries
```

**CI/CD Integration:**

```yaml
# .github/workflows/integration-tests.yml
name: Integration Tests
on: [push, pull_request]

jobs:
  multi-stone-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Build images
        run: |
          docker-compose -f tests/docker-compose.test.yml build
      - name: Run multi-Stone tests
        run: |
          docker-compose -f tests/docker-compose.test.yml up -d
          sleep 5  # Wait for startup
          ./tests/integration/test-discovery.sh
          ./tests/integration/test-lifecycle.sh
          ./tests/integration/test-cursor-polling.sh
      - name: Cleanup
        run: docker-compose -f tests/docker-compose.test.yml down
```

**Performance Validation:**

Docker testing validates real-world performance targets:

- Localhost cache query: <1ms (same machine)
- UDP broadcast response: <100ms (172.20.0.0/16 network)
- HTTP cursor poll: <50ms (container-to-container)
- Full topology response: <200ms (3-5KB JSON)

**Development Workflow:**

```bash
# Terminal 1: Watch Moss logs
docker-compose -f tests/docker-compose.test.yml logs -f stone-01

# Terminal 2: Interactive testing
docker exec -it stone-01 bash
curl http://stone-02:7185/api/garden/stones

# Terminal 3: Rake commands
cargo watch -x "run --bin garden-rake -- list --all"
```

---

## Implementation Roadmap

> **📋 See [DEVELOPMENT-PLAN.md](../DEVELOPMENT-PLAN.md) for detailed day-by-day implementation guide**

This section provides a high-level overview of the implementation phases. For detailed work breakdown, code examples, testing procedures, and cross-platform considerations, refer to the separate Development Plan document.

### Phase 0: Foundation Setup

**Duration:** 2 days  
**Deliverable:** Rust workspace with shared types, Docker build system, CI pipeline

**Key Tasks:**

- Create workspace (moss, garden-rake, common crates)
- Define shared types (ServiceInfo, StoneInfo, HealthStatus)
- Setup Docker build for Moss
- Configure cross-compilation for Windows Rake
- Establish GitHub Actions CI (Linux + Windows)

### Phase 1: Core Functionality

**Duration:** 10 days (6 increments)  
**Deliverable:** End-to-end `offer` command, auto-discovery, garden-wide operations

**Increments:**

1. **HTTP API Foundation** (Days 3-4) - Basic server + client communication
2. **Service Registry** (Days 5-6) - In-memory tracking, maintenance mode
3. **Docker Compose** (Days 7-8) - Service installation via templates
4. **UDP Discovery** (Days 9-10) - Windows-compatible broadcast discovery
5. **mDNS Announcements** (Days 11-12) - Linux mDNS integration
6. **Garden-Wide Operations** (Day 12) - Moss coordinator pattern

**Success Criteria:**

- ✅ `garden-rake offer mongodb` installs service
- ✅ `garden-rake list` shows services
- ✅ `garden-rake upgrade --all` coordinates across Stones
- ✅ Discovery works without `--at` flag
- ✅ Works on both Linux and Windows

### Phase 2: Production Hardening

**Duration:** 2 weeks  
**Deliverable:** Production-ready system for home labs

**Moss:**

- Health monitoring (background task)
- Resource monitoring (capacity warnings)
- Port conflict resolution
- Atomic compose updates with rollback

**Rake:**

- Enhanced `--all` parallel execution
- `--json` output format
- Enhanced error handling
- Progress indicators

### Phase 3: Advanced Features

**Duration:** 3-4 weeks  
**Deliverable:** Full-featured system with security and observability

**Features:**

- Lantern UI integration
- Pond security (mTLS)
- Cursor-based polling optimization
- Lifecycle event broadcasting
- Client bindings (Python, JavaScript)
- Prometheus metrics
- Operational runbook

---

## Technology Stack

#[derive(Debug, Clone, Serialize, Deserialize)]
pub enum HealthStatus {
Healthy,
Degraded,
Offline,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Ports {
pub native: u16,
pub agnostic: Option<u16>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StoneCapabilities {
pub max_services: u8,
pub stone_type: String,
}

```

**Afternoon:**
- **Moss team:** Setup Axum dependencies, create `main.rs` skeleton
- **Rake team:** Setup clap dependencies, create CLI skeleton with Linux/Windows conditional compilation
- **All:** Write passing unit tests for shared types

**Deliverable:** Workspace compiles on Linux and Windows, tests pass

#### Day 2: Build System & Docker Testing

**Morning:**
- Create Dockerfiles for Moss (Linux container)
- Create `tests/docker-compose.test.yml` (from spec)
- Setup cross-compilation for Windows Rake (`cross` tool)

**Moss Dockerfile:**

**Afternoon:**
- Setup GitHub Actions CI (Linux + Windows)
- Configure cross-compilation targets
- Test Docker build pipeline

**Deliverable:** `docker-compose build` succeeds, CI green on both platforms

---

### Phase 1: Core Functionality (Days 3-12)

**Duration:** 10 days
**Strategy:** Incremental, feature-paired development
**Testing:** Continuous validation with Docker multi-Stone environment

---

#### Increment 1: HTTP API Foundation (Days 3-4)

**Parallel Work:**

**Moss (Linux):**

**Rake (Linux + Windows):**

**Day 4: Add operation endpoints**
- Moss: `POST /api/operations/offer/{offering}` stub (returns 501 Not Implemented)
- Moss: `GET /api/services` stub (returns empty array)
- Rake: `offer` command skeleton
- Rake: `list` command skeleton

**Integration Test (Docker):**

**Cross-Platform Validation:**
- Linux: Native build + test
- Windows: Cross-compile + manual test on Windows VM

**Deliverable:** HTTP API responds, Rake can query Moss, works on Linux + Windows

---

#### Increment 2: Service Registry & Status (Days 5-6)

**Parallel Work:**

**Moss (Day 5):**

**Moss (Day 6):**
- Add service status tracking (Running, Stopped, Maintenance, etc.)
- Implement maintenance mode check
- Return HTTP 202 when service in maintenance

**Rake (Day 5-6):**
- Implement `list` command output formatting
- Add `--json` flag
- Add basic table output with status column

**Integration Test:**

**Deliverable:** Service registry functional, status tracking works, Rake displays formatted output

---

#### Increment 3: Docker Compose Integration (Days 7-8)

**Parallel Work:**

**Moss (Day 7):**

**Moss (Day 8):**
- Implement template loading from `templates/` directory
- Add template validation (compose syntax check)
- Update service registry after successful compose

**Rake (Day 7-8):**
- Implement `offer` command HTTP POST
- Add operation_id generation (GUIDv7)
- Display operation result

**Integration Test (Docker):**

**Deliverable:** `offer` command installs service via Docker Compose, end-to-end working

---

#### Increment 4: UDP Broadcast Discovery (Days 9-10)

**Parallel Work:**

**Moss (Day 9):**

**Moss (Day 10):**
- Add Stone registry (in-memory HashMap)
- Listen for `moss_online` broadcasts on UDP 3003
- Implement cursor generation (GUIDv7)

**Rake (Day 9):**

**Rake (Day 10):**
- Implement localhost-first discovery
- Cache discovered endpoint
- Test on both Linux and Windows

**Integration Test (Docker):**

**Cross-Platform Validation:**
- Linux: UDP + mDNS both working
- Windows: UDP broadcast discovery working
- Test firewall rules on Windows

**Deliverable:** Discovery working on both platforms, localhost-first optimization functional

---

#### Increment 5: mDNS Announcements (Days 11-12)

**Parallel Work:**

**Moss (Day 11):**

**Moss (Day 12):**
- Announce services when installed
- Update announcements on status change
- Remove announcements when service stopped

**Rake (Day 11-12):**
- Implement mDNS browse (Linux)
- Add mDNS query to discovery chain
- Ensure Windows gracefully skips mDNS

**Integration Test:**

**Deliverable:** mDNS working on Linux, gracefully skipped on Windows

---

#### Increment 6: Garden-Wide Operations (Day 12)

**Parallel Work:**

**Moss:**

**Rake:**
- Implement `--all` flag logic
- Detect garden-wide vs Stone-level
- Display aggregated results

**Integration Test:**

**Deliverable:** `upgrade --all` coordinates across Stones

---

### Phase 1 Completion Checklist

**Cross-Platform Validation:**
- [ ] Moss runs in Docker (Linux)
- [ ] Rake compiles and runs on Linux (native)
- [ ] Rake compiles and runs on Windows (cross-compiled)
- [ ] UDP discovery works on Windows
- [ ] mDNS discovery works on Linux
- [ ] Localhost-first discovery works on both platforms

**End-to-End Scenarios:**
- [ ] `garden-rake offer mongodb` installs service
- [ ] `garden-rake list` shows services
- [ ] `garden-rake upgrade mongodb` updates service
- [ ] `garden-rake upgrade --all` upgrades all services on one Stone
- [ ] `garden-rake upgrade --all` (garden-wide) coordinates across 3 Stones
- [ ] Discovery works without `--at` flag

**Testing:**
- [ ] Unit tests pass for common types
- [ ] Integration tests pass in Docker environment
- [ ] Manual testing on Windows confirms UDP discovery
- [ ] 3-Stone Docker scenario validates lifecycle

**Documentation:**
- [ ] README.md with build instructions (Linux + Windows)
- [ ] Troubleshooting guide for Windows firewall
- [ ] Example commands and expected outputs

---

### Phase 2: Production Hardening (P1)

**Duration:** 2 weeks

**Moss:**
- [ ] Health monitoring (background task)
- [ ] Resource monitoring (capacity warnings)
- [ ] Port conflict resolution
- [ ] Atomic compose updates with rollback

**Rake:**
- [ ] --all flag (parallel execution)
- [ ] --json output
- [ ] Enhanced error handling
- [ ] Progress indicators

**Milestone:** Production-ready for home labs

### Phase 2: Production Hardening (P1)

**Duration:** 2 weeks

**Moss:**
- [ ] Health monitoring (background task)
- [ ] Resource monitoring (capacity warnings)
- [ ] Port conflict resolution
- [ ] Atomic compose updates with rollback

**Rake:**
- [ ] --all flag (parallel execution)
- [ ] --json output
- [ ] Enhanced error handling
- [ ] Progress indicators

**Milestone:** Production-ready for home labs

---

### Development Workflow & Team Coordination

**Daily Standup Focus:**
- Feature parity status (Moss vs Rake)
- Platform coverage validation (Linux vs Windows)
- Integration test results
- Blockers requiring cross-team resolution

**Continuous Integration (CI/CD):**


**Feature Pairing Strategy:**

Each increment maintains this invariant:
```

IF Moss implements endpoint X
THEN Rake implements client for X
AND Rake works on BOTH Linux and Windows

```

**Branch Strategy:**
- `main` - Always deployable
- `increment/N-description` - Feature branches per increment
- Merge only when BOTH Moss and Rake complete + tests pass

**Testing Pyramid:**

1. **Unit Tests (60%):** Shared types, parsing, validation
2. **Integration Tests (30%):** Docker multi-Stone scenarios
3. **Manual Tests (10%):** Windows firewall, real hardware

---

### Windows-Specific Considerations

**Cross-Compilation Setup (Linux → Windows):**


**Windows Testing Checklist:**

- [ ] UDP broadcast discovery works (127.0.0.1:3004)
- [ ] Windows Firewall rules documented
- [ ] PowerShell scripts for firewall automation
- [ ] Antivirus compatibility validated (Windows Defender)
- [ ] UNC path handling (\\\\stone-01\\...)
- [ ] Console output encoding (UTF-8)

**Windows Firewall Rules:**


**Platform-Specific Code Patterns:**


---

### Risk Mitigation & Contingencies

**Risk: Windows UDP Blocked by Firewall**
- **Mitigation:** Document firewall rules in README, provide PowerShell script
- **Contingency:** Fallback to manual `--at` flag
- **Detection:** Add `garden-rake doctor` command to test UDP connectivity

**Risk: Moss/Rake Version Drift**
- **Mitigation:** Shared `common` crate, CI validates both build
- **Contingency:** Version check in HTTP headers (Phase 2)
- **Detection:** Integration tests catch incompatibilities

**Risk: Docker Not Installed on Stone**
- **Mitigation:** Pre-flight check in Moss startup
- **Contingency:** Return 503 Service Unavailable with clear error
- **Detection:** Health check endpoint validates Docker connectivity

**Risk: Cross-Platform Test Gap**
- **Mitigation:** Automated CI on both platforms
- **Contingency:** Manual test plan for Windows developer
- **Detection:** Weekly full validation on Windows VM

---

### Success Metrics (Phase 1)

**Feature Parity:**
- ✅ 100% API coverage: Every Moss endpoint has Rake command
- ✅ 100% platform coverage: All Rake commands work on Linux + Windows
- ✅ 100% test coverage: Every feature has Docker integration test

**Performance:**
- ✅ Discovery: <3 seconds (UDP/mDNS)
- ✅ Localhost cache: <1ms (90% of operations)
- ✅ Offer service: <30 seconds (depends on image pull)

**Quality:**
- ✅ CI green on both platforms
- ✅ Zero high-severity bugs in manual testing
- ✅ Documentation complete (README, troubleshooting, examples)

**Developer Experience:**
- ✅ One-command build: `cargo build --release`
- ✅ One-command test: `./tests/run-all.sh`
- ✅ Clear error messages on failure
- ✅ Zero-config for 90% of use cases (localhost discovery)

---

### Phase 3: Advanced Features (P2)

**Duration:** 3-4 weeks

- [ ] Agnostic Data API (C# implementation)
- [ ] Schema management endpoints
- [ ] Backup/restore endpoints
- [ ] Client library (Koan.ZenGarden)
- [ ] Pond security integration

**Milestone:** Enterprise features available

---

## Technology Stack

### Garden-Moss Daemon

**Language:** Rust 1.70+
**Runtime:** Tokio (async)
**HTTP:** Axum 0.6+
**mDNS:** mdns-sd 0.7+
**Docker:** Shell out to `docker compose` CLI
**Config:** toml 0.7+
**Logging:** tracing + tracing-subscriber
**Error:** anyhow + thiserror
**OpenAPI:** utoipa 4.0+ + utoipa-swagger-ui (optional)

### Rake CLI

**Language:** Rust 1.70+
**CLI:** clap 4.0+ (derive API)
**HTTP:** reqwest 0.11+
**mDNS:** mdns-sd 0.7+
**Output:** colored 2.0+
**Formatting:** Custom table rendering

### Shared (Common Crate)

**Serialization:** serde 1.0+ + serde_json
**Types:** Strong typing for ServiceInfo, HealthStatus, etc.
**Validation:** Custom validation logic

### Development Tools

**Testing:** cargo test, integration tests
**Linting:** clippy (strict mode)
**Formatting:** rustfmt
**Documentation:** rustdoc
**Build:** cargo build --release

---

## Open Questions

### Technical Decisions

1. **Docker API vs CLI:** Use bollard (Docker API) or shell out to `docker compose`?
   - **Recommendation:** CLI initially (simpler), migrate to API in P2

2. **Template distribution:** Embed in binary or read from filesystem?
   - **Recommendation:** Filesystem (allows user customization)

3. **Config hot-reload:** Watch config file for changes?
   - **Recommendation:** Not MVP, manual reload via API in P2

4. **Windows support:** Priority for Rake on Windows?
   - **Recommendation:** P2 (mDNS challenging on Windows)

5. **Metrics:** Prometheus metrics endpoint?
   - **Recommendation:** P2 (nice-to-have)

6. **Persistent state:** SQLite for Moss or in-memory only?
   - **Recommendation:** SQLite for used codes, health history

### Performance Targets

**Moss:**
- API response time: < 100ms (95th percentile)
- Service install: < 30 seconds
- mDNS announcement: < 5 seconds after startup
- Stone registry cache: Always hot (maintained from broadcasts)
- Health check cycle: 30 seconds
- Graceful shutdown: < 5 seconds

**Rake:**
- Localhost cache query: < 1ms (zero discovery, common case)
- Remote discovery: < 3 seconds (UDP broadcast or mDNS fallback)
- Command execution: < 1 second total (cache query + operation)
- --all flag: Parallel execution with progress feedback

**Discovery latency breakdown:**
- **Localhost (90% of operations):** <1ms, zero network traffic
- **UDP broadcast (Windows/remote):** <100ms, single Stone responds
- **mDNS browse (fallback):** <3 seconds, full network scan

---

## Success Criteria

### Moss (Daemon)

- ✅ Starts as systemd service on boot
- ✅ Responds to API requests within 100ms
- ✅ Announces self via mDNS within 5 seconds
- ✅ Installs service from template in < 30 seconds
- ✅ Detects port conflicts before applying
- ✅ Rolls back failed compose changes
- ✅ Updates mDNS on health changes
- ✅ Handles SIGTERM gracefully

### Rake (CLI)

- ✅ Discovers local Moss via localhost query < 1ms (zero discovery)
- ✅ Falls back to network discovery < 3 seconds when needed
- ✅ Queries hot cache for full topology (no repeated discovery)
- ✅ Sends API requests and parses responses
- ✅ Pretty-prints output with colors
- ✅ Handles --at and --all flags
- ✅ Shows user-friendly errors
- ✅ Provides --json output
- ✅ Works on Linux, macOS, Windows (with mDNS)

---

## References

### Internal Documentation

- [SECURITY-SPEC.md](SECURITY-SPEC.md) - Pond security model
- [UNDERSTANDING.md](UNDERSTANDING.md) - Core concepts
- [STORIES.md](STORIES.md) - User scenarios
- [HARDWARE.md](HARDWARE.md) - Stone hardware requirements

### External References

- Koan EntityController: `src/Koan.Web/Controllers/EntityController.cs`
- Koan JsonFilterBuilder: `src/Koan.Web/Filtering/JsonFilterBuilder.cs`
- mDNS RFC: RFC 6762 (Multicast DNS)
- Docker Compose spec: https://docs.docker.com/compose/compose-file/

---

**Document Status:** Living specification - updated as implementation progresses

**Last Updated:** January 15, 2026

**Contributors:** Development team, security team, architecture review
```
