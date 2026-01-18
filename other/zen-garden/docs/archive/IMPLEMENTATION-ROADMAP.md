# Implementation Roadmap

**Moss (Garden Agent) and Agnostic Sidecar Development Plan**

**Status:** Ready for implementation  
**Date:** January 15, 2026

---

## Phase 1: Foundation (P0)

**Goal:** Moss daemon + Rake CLI capable of installing services and announcing them via mDNS.

### 1.0 Rake CLI (Rust)

**Features:**
- Discover Moss daemons via mDNS (`_moss._tcp.local.`)
- Send HTTP requests to Moss API
- Parse and display responses
- Target selection: local (default), `here`, `at stone-name`, `--all`

**Commands:**
```bash
garden-rake status [at <stone>]        # Get Stone status
garden-rake offer <service> [at <stone>]  # Install service
garden-rake remove <service> [at <stone>] # Uninstall service
garden-rake list [at <stone>]          # List offerings
garden-rake upgrade <service> [at <stone>] # Upgrade service
```

**Implementation Notes:**
- Use `mdns-sd` crate for service discovery
- Use `reqwest` for HTTP client
- Use `clap` for CLI argument parsing
- Pretty-print JSON responses with `serde_json` + `colored`
- Default target: local Moss (127.0.0.1:3001)
- Handle multiple Stones for `--all` flag (parallel requests)

### 1.1 Moss HTTP API (Rust daemon)

**Endpoints:**
- `POST /api/services/install` - Install from offering template
- `DELETE /api/services/{name}` - Uninstall service
- `GET /api/services` - List installed services
- `GET /api/services/{name}` - Service details
- `GET /health` - Agent health status

**Security:**
- Template-only installation policy (no ad-hoc Docker configs)
- Templates validated against schema
- Custom services require manifest files on Stone filesystem

**Implementation Notes:**
- Moss uses offering templates from curated repository
- Reject requests with inline Docker configurations
- Log all service install/uninstall operations

---

### 1.2 mDNS Announcer (Avahi Integration)

**Features:**
- **Announce Moss itself** (`_moss._tcp.local.` on port 3001) for Rake discovery
- Announce native services (MongoDB on port 27017 as `_koan-stone._tcp.local.`)
- Announce agnostic sidecars (mongodb-agnostic on port 8080 as `_koan-stone._tcp.local.`)
- Dual announcements: one native + one agnostic per database service
- Health monitoring integration (update TXT records on health changes)

**Moss Self-Announcement:**
```
stone-01-moss._moss._tcp.local.
TXT: stone_name=stone-01
     version=0.1.0
     api_port=3001
     health=healthy
```

**TXT Record Schema:**
```
offering=mongodb-agnostic
port=8080
protocol=agnostic
version=1.0.2
backend_service_version=7.0.4
categories=database,document-database
set_mode=database
capabilities=crud,query,filter,bulk,transactions
priority=50
health=healthy
```

**Implementation Notes:**
- Use Avahi D-Bus API (or equivalent for platform)
- Refresh announcements on service start/stop
- Monitor container health, update TXT records

---

### 1.3 Docker Compose Manager

**Features:**
- Read existing `docker-compose.yml` on Stone
- Atomic updates (apply all changes or rollback)
- Port conflict detection and resolution
- Rollback on failure

**Port Conflict Resolution:**
- Default to service defaults (MongoDB 27017, Redis 6379, etc.)
- On conflict, auto-assign next available port
- Update docker-compose.yml with actual port
- Announce actual port via mDNS

**Example:**
```yaml
services:
  mongodb:
    image: mongo:7.0
    ports:
      - "27017:27017"  # Default
  
  mongodb-agnostic:
    image: koan/mongo-data-api:1.0
    ports:
      - "8080:8080"  # Default, reassign to 8081 if conflict
    depends_on:
      - mongodb
```

**Implementation Notes:**
- Parse docker-compose.yml using library (PyYAML, yq, Go yaml)
- Check port availability before applying changes
- Test container startup after updates
- Keep backup of previous compose file for rollback

---

### 1.4 Container Resource Signaling

**Goal:** Warn users when too many services packed on one Stone.

**Metrics:**
- Total containers running
- RAM usage (estimate ~300MB per sidecar, varies by backend)
- CPU load
- Disk I/O

**Thresholds:**
```
Stone Type    | Max Services | Warning Threshold
--------------|--------------|------------------
Mini (2GB)    | 2 services   | 1 service
Standard (4GB)| 4 services   | 3 services
Large (8GB+)  | 8 services   | 6 services
```

**Actions:**
- Log warning when approaching threshold
- Return warning in `/health` endpoint
- Optionally announce `health=degraded` via mDNS

---

## Phase 2: Agnostic Data API (P1)

**Goal:** HTTP REST API for database-neutral applications, based on Koan EntityController.

### 2.1 Agnostic Data API (C#)

**Based on:** `src/Koan.Web/Controllers/EntityController.cs`

**Endpoints:**
```
POST   /v1/data/{set}/entities/{type}
GET    /v1/data/{set}/entities/{type}/{id}
PUT    /v1/data/{set}/entities/{type}/{id}
DELETE /v1/data/{set}/entities/{type}/{id}
POST   /v1/data/{set}/entities/{type}/query
GET    /v1/data/{set}/entities/{type}
POST   /v1/data/{set}/entities/{type}/bulk
POST   /v1/data/{set}/transactions/begin
```

**Discovery Endpoints:**
```
GET /v1/data/sets
GET /v1/data/sets/{set}/entities
GET /v1/data/sets/{set}/entities/{type}/count
```

**Features:**
- JSON filter syntax (MongoDB-like: `$gte`, `$in`, `$exists`, etc.)
- Pagination by default (prevent large result sets)
- Bulk operations (`POST /bulk`)
- Transactions where backend supports (MongoDB, PostgreSQL, SQL Server)
- Set-based isolation (database/schema/collection-prefix mapping)

**Implementation Notes:**
- Create `Koan.ZenGarden.DataApi` project
- Inherit from `EntityController<TEntity, TKey>`
- Add `/v1/data` route prefix
- Override methods to enforce `/data/{set}/entities/{type}` pattern
- Validate set names (`^[a-z0-9-]{3,64}$`, reject reserved names)

---

### 2.2 Schema Support for EntityController

**Goal:** Add schema management to EntityController for index creation, migrations, etc.

**New Endpoints:**
```
GET    /v1/data/{set}/schema/{type}              # Get entity schema
POST   /v1/data/{set}/schema/{type}/index        # Create index
DELETE /v1/data/{set}/schema/{type}/index/{name} # Drop index
GET    /v1/data/{set}/schema/{type}/indexes      # List indexes
POST   /v1/data/{set}/schema/{type}/migrate      # Run migration
```

**Index Definition:**
```json
POST /v1/data/myapp/schema/users/index
{
  "name": "idx_email",
  "fields": [
    { "name": "email", "order": "asc" }
  ],
  "unique": true
}
```

**Implementation Notes:**
- Add `ISchemaManager` interface to Koan.Core
- Implement for MongoDB, PostgreSQL, SQL Server adapters
- Use backend-native index creation (no ORM abstraction)
- Document that Redis/in-memory adapters may not support indexes

---

### 2.3 Agnostic Caching Standard

**Goal:** Define caching headers and invalidation policy for agnostic APIs.

**HTTP Headers:**
```
Cache-Control: max-age=300, must-revalidate
ETag: "33a64df551425fcc55e4d42a148795d9f25f89d4"
Last-Modified: Wed, 15 Jan 2026 12:00:00 GMT
```

**Invalidation:**
- `POST`, `PUT`, `DELETE` operations invalidate cache
- Broadcast cache invalidation via Redis/message queue (if available)
- ETag-based conditional requests (`If-None-Match`)

**Implementation Notes:**
- Add caching middleware to agnostic sidecar
- Use Redis for distributed cache (if Stone has Redis installed)
- Fall back to in-memory cache (single-node)
- Document cache behavior in API docs

---

### 2.4 Backup Endpoints for Agnostic API

**Goal:** Allow applications to export/import data via REST API.

**Endpoints:**
```
POST /v1/data/{set}/backup/export    # Export all entities in set
POST /v1/data/{set}/backup/import    # Import entities (bulk upsert)
GET  /v1/data/{set}/backup/status    # Export/import job status
```

**Export Format:**
```json
{
  "set": "myapp",
  "entities": {
    "users": [...],
    "orders": [...],
    "products": [...]
  },
  "metadata": {
    "timestamp": "2026-01-15T12:00:00Z",
    "version": "1.0"
  }
}
```

**Implementation Notes:**
- Stream large exports (don't buffer entire result in memory)
- Use bulk operations for import (leverage existing `/bulk` endpoint)
- Return job ID for async operations (for large datasets)
- Stone-level backups (full Docker volume snapshots) remain primary strategy

---

## Phase 3: Client Library (P1)

**Goal:** C# client library for Zen Garden service discovery and data access.

### 3.1 Koan.ZenGarden Client Library

**Features:**
- mDNS resolver (discover Stones by service type or category)
- Connection string parser (`zen-garden:mongodb`, `zen-garden:database`)
- Native client factory (MongoDB.Driver, Npgsql, StackExchange.Redis)
- Agnostic data client (HTTP REST API wrapper)

**API:**
```csharp
// Native discovery
var uri = await ZenGarden.ResolveAsync("zen-garden:mongodb");
var client = new MongoClient(uri);

// Agnostic discovery
var api = await ZenGarden.ResolveAsync("zen-garden:database");
var client = new ZenDataClient(api);

// Query
var users = await client.Set("myapp").Entity("users").Query(new {
    filter = new { status = "active" },
    page = 1,
    pageSize = 25
});

// Relationships via filter
var orders = await client.Set("myapp").Entity("orders")
    .Query(new { filter = new { userId = 123 } });
```

**Implementation Notes:**
- Use Tmds.MDns for mDNS resolution (.NET)
- Wrap native drivers (MongoDB.Driver, Npgsql, StackExchange.Redis)
- Provide fluent API for agnostic data client
- Handle connection pooling, retries, circuit breakers

---

## Phase 4: Security & Operations (P2)

### 4.1 Pond Integration (mTLS)

**Goal:** One-command opt-in to secure all Stone-to-Stone communication.

**Story:**
"Set your stones, make sure everything is working, fill the pond."

**Command:**
```bash
zen-garden pond init
```

**Actions:**
1. Generate Pond CA certificate
2. Distribute CA to all Stones
3. Issue certificate to each Stone
4. Enable mTLS on all services
5. Update mDNS announcements with certificate fingerprints
6. Restart services with TLS enabled

**Implementation Notes:**
- Use Let's Encrypt or self-signed CA for home labs
- Store CA private key securely (encrypted on control plane Stone)
- Automate certificate renewal (60-day validity, auto-renew at 30 days)
- Add `cert_fingerprint` to TXT records
- Update Garden Agent to validate certificates before connecting

---

### 4.2 Health Monitoring

**Goal:** Monitor container health, update mDNS announcements.

**Metrics:**
- Container status (running, stopped, restarting)
- Response time (HTTP health checks)
- Error rates (log parsing)
- Resource usage (CPU, RAM, disk I/O)

**Health States:**
- `healthy` - All checks passing
- `degraded` - Some checks failing (still accepting requests)
- `unavailable` - Critical failures (stop announcing via mDNS)

**Implementation Notes:**
- Use Docker health checks (`HEALTHCHECK` in Dockerfile)
- Poll health endpoints every 30 seconds
- Update mDNS TXT records on state change
- Log health transitions for debugging

---

## Phase 5: Developer Experience (P3)

### 5.1 Quickstart for Zen Garden Apps

**Goal:** Documentation focused on app development, not Stone setup.

**Content:**
1. Create a Koan app (not a Stone)
2. Use `zen-garden:database` connection string
3. Push app to compute Stone
4. Garden Agent pulls image and starts container

**Example:**
```csharp
// appsettings.json
{
  "ConnectionStrings": {
    "Database": "zen-garden:database"
  }
}

// Program.cs
var connectionString = configuration.GetConnectionString("Database");
var client = await ZenGarden.ResolveAsync(connectionString);
```

**Implementation Notes:**
- Focus on Koan app development (not Stone provisioning)
- Document push workflow (app → Stone)
- Provide sample apps (S1.Web, S2.Api, etc.)
- Reference Koan EntityController documentation for filter syntax

---

### 5.2 Filter Syntax Examples

**Goal:** Comprehensive examples of JsonFilterBuilder query syntax.

**Content:**
- Equality, comparison operators
- Set membership (`$in`, `$all`)
- Logical operators (`$and`, `$or`, `$not`)
- Existence checks (`$exists`)
- Wildcards (`Al*`, `*ice`, `*li*`)
- Case-insensitive option

**Implementation Notes:**
- Reuse Koan documentation (reference existing EntityController docs)
- Add Zen Garden-specific examples (set-based queries)
- Provide language-specific examples (C#, JavaScript, Python)

---

### 5.3 JavaScript/Python Clients

**Goal:** Client libraries for non-.NET applications.

**JavaScript (`@zen-garden/client`):**
```javascript
const { resolve, ZenDataClient } = require('@zen-garden/client');

const uri = await resolve('zen-garden:database');
const client = new ZenDataClient(uri);

const users = await client.set('myapp').entity('users').query({
  filter: { status: 'active' },
  page: 1,
  pageSize: 25
});
```

**Python (`zen-garden-client`):**
```python
from zen_garden import resolve, ZenDataClient

uri = await resolve('zen-garden:database')
client = ZenDataClient(uri)

users = await client.set('myapp').entity('users').query(
    filter={'status': 'active'},
    page=1,
    page_size=25
)
```

**Implementation Notes:**
- mDNS resolution libraries (Zeroconf for Python, mdns-js for Node.js)
- HTTP client with connection pooling
- Same API surface as C# client (fluent, set-based)
- Publish to npm and PyPI

---

## Acceptance Criteria

**Phase 1 (Foundation):**
- ✅ Rake CLI discovers Moss daemons via mDNS
- ✅ Rake CLI sends commands to local/remote Stones
- ✅ Moss can install/uninstall services from templates
- ✅ Moss announces itself via mDNS (`_moss._tcp.local.`)
- ✅ mDNS announcements for native + agnostic services
- ✅ Docker Compose manager handles port conflicts
- ✅ Container resource warnings displayed in `/health`

**Phase 2 (Agnostic Data API):**
- ✅ Agnostic Data API implements full CRUD + query + bulk
- ✅ Pagination enforced by default
- ✅ Schema management endpoints functional
- ✅ Caching headers and invalidation working
- ✅ Backup/restore endpoints functional

**Phase 3 (Client Library):**
- ✅ C# client resolves Stones via mDNS
- ✅ Native client factory wraps MongoDB.Driver, Npgsql, Redis
- ✅ Agnostic client wraps HTTP REST API
- ✅ Relationship queries via filter working

**Phase 4 (Security):**
- ✅ Pond opt-in command (`zen-garden pond init`)
- ✅ mTLS enabled on all services
- ✅ Health monitoring updates mDNS announcements

**Phase 5 (Developer Experience):**
- ✅ Quickstart documentation published
- ✅ Filter syntax examples published (20+ examples)
- ✅ JavaScript and Python clients published

---

## Next Steps

1. **Commit pending NewStone.ps1 fixes** (systemd service for first-boot Docker startup)
2. **Scaffold `moss` Rust project** (daemon: HTTP API + mDNS announcer + Docker Compose manager)
3. **Scaffold `garden-rake` Rust project** (CLI: mDNS discovery + HTTP client + command parser)
4. **Create Koan.ZenGarden.DataApi project** (Agnostic Data API based on EntityController)
4. **Discovery: Schema management in Koan** (Search for existing index management patterns)
5. **Create Koan.ZenGarden client library** (Resolver + native/agnostic clients)

---

## Open Questions

- **Sidecar container image:** Should we use existing Koan.Web runtime or create dedicated lightweight image?
- **mDNS library:** Avahi (Linux), Bonjour (macOS/Windows), or cross-platform library (Tmds.MDns)?
- **Port allocation registry:** Should Moss maintain a port registry file (e.g., `/etc/zen-garden/ports.json`)?
- **Transaction scope:** Single-set only or cross-set transactions (where backends support)?
- **Schema migration format:** Use Koan schema format or adopt standard (e.g., JSON Schema, OpenAPI)?
