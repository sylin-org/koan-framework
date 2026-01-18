# Moss Specification

**Discovery findings and design decisions for the Stone-resident service management agent.**

**Name:** Moss (lives on each Stone, spreads service information naturally)

**Status:** Design - Based on Koan EntityController discovery  
**Date:** January 15, 2026

---

## Executive Summary

**Moss** is the daemon service that runs on each Stone, listening for management requests and executing actions. It provides dynamic service installation/removal, mDNS announcements, and Docker Compose orchestration. Like moss on a stone, it lives on the infrastructure and naturally spreads information about available services.

**Rake** is the CLI tool that sends commands to Moss daemons across your garden.

**Architecture:**
```
Rake (CLI)  →  HTTP Request  →  Moss (daemon on Stone)  →  Docker/mDNS/Health
                                        ↓
                                  JSON Response
```

**Key findings from Koan codebase discovery:**
- EntityController provides complete CRUD API surface
- JSON filter syntax already implemented (JsonFilterBuilder)
- Bulk operations, transactions, set-based routing already exist
- Query patterns, pagination, and validation are production-ready

---

## 1. Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│              Stone (Physical Device)                    │
│                                                         │
│  ┌───────────────────────────────────────────────────┐ │
│  │   Moss (Rust daemon)                              │ │
│  │                                                   │ │
│  │  • Service Management API (:3001)                │ │
│  │  • mDNS Announcer (Avahi integration)            │ │
│  │  • Docker Compose Manager                        │ │
│  │  • Health Monitor                                │ │
│  └───────────────────────────────────────────────────┘ │
│                        ↓                                │
│  ┌───────────────────────────────────────────────────┐ │
│  │    Docker Compose Services                        │ │
│  │  • MongoDB (native port 27017)                   │ │
│  │    └── mongo-data-api sidecar (:8080)            │ │
│  │  • SQL Server (native port 1433)                 │ │
│  │    └── sqlserver-data-api sidecar (:8081)        │ │
│  │  • Redis (native port 6379)                      │ │
│  │    └── redis-data-api sidecar (:8082)            │ │
│  │  • Custom Services                               │ │
│  └───────────────────────────────────────────────────┘ │
│                                                         │
│  Note: Each database service has its own sidecar       │
│  (sidecars are per-service, not shared)                │
└─────────────────────────────────────────────────────────┘
```

---

## 2. Service Management API

**Consumed by:** Rake CLI tool

### **Endpoints**

```http
# Service Lifecycle
POST   /api/services/install          # Install offering
DELETE /api/services/{name}           # Uninstall offering
PUT    /api/services/{name}/upgrade   # Upgrade service version
GET    /api/services                  # List installed services
GET    /api/services/{name}           # Service details

# Configuration
GET    /api/compose                   # Current docker-compose.yml
POST   /api/compose/reload            # Reload compose file
GET    /api/manifests                 # Available service manifests
GET    /api/manifests/{name}          # Manifest details

# Announcements
GET    /api/announcements             # Current mDNS announcements
POST   /api/announcements/refresh     # Trigger immediate announce
GET    /api/pond/status               # Pond security status (if enabled)

# Health & Metadata
GET    /health                        # Agent + Docker health
GET    /info                          # Stone info (version, capabilities)
```

### **Rake CLI Commands**

**Moss discovery:**
```bash
# Rake discovers Moss daemons via mDNS (service type: _moss._tcp.local.)
garden-rake status                     # Status from local Moss
garden-rake status at stone-01         # Status from specific Stone
garden-rake status --all               # Status from all Stones
```

**Service installation:**
```bash
garden-rake offer mongo                # Install MongoDB locally (default)
garden-rake offer mongo here           # Install MongoDB on current Stone
garden-rake offer mongo at stone-01    # Install MongoDB on specific Stone
garden-rake offer mongo:7.0            # Install specific version
```

**Service management:**
```bash
garden-rake remove mongo               # Remove from local Stone
garden-rake remove mongo at stone-01   # Remove from specific Stone
garden-rake upgrade mongo              # Upgrade to latest version
garden-rake list                       # List local offerings
garden-rake list at stone-01           # List offerings on specific Stone
garden-rake list --all                 # List offerings across garden
```

### **Install Service Request**

```json
POST /api/services/install

// Option 1: From manifest
{
  "offering": "mongo",
  "version": "7.0"  // Optional, defaults to manifest default
}

// Option 2: Custom service block
{
  "name": "custom-app",
  "docker": {
    "image": "myregistry/app:latest",
    "ports": ["8080:8080"],
    "environment": {
      "DB_HOST": "mongo"
    },
    "volumes": ["/data:/app/data"]
  },
  "announce": {
    "offering": "custom-app",
    "port": 8080,
    "protocol": "http",
    "categories": ["compute", "api"]
  }
}
```

### **Service Details Response**

```json
GET /api/services/mongodb

{
  "name": "mongodb",
  "offering": "mongodb",
  "status": "running",
  "docker": {
    "containers": [
      {
        "name": "mongodb",
        "container_id": "abc123...",
        "image": "mongo:7.0",
        "ports": ["27017:27017"],
        "health": "healthy"
      },
      {
        "name": "mongodb-data-api",
        "container_id": "def456...",
        "image": "zen-garden/data-api:latest",
        "ports": ["8080:8080"],
        "health": "healthy"
      }
    ]
  },
  "announcements": [
    {
      "offering": "mongodb",
      "port": 27017,
      "protocol": "native",
      "categories": ["database", "document-database"]
    },
    {
      "offering": "mongodb-agnostic",
      "port": 8080,
      "protocol": "agnostic",
      "categories": ["database", "document-database"]
    }
  ],
  "installed_at": "2026-01-15T10:30:00Z",
  "last_health_check": "2026-01-15T14:22:15Z"
}
```

**Note:** Services can have multiple containers (native + sidecar). Each sidecar is dedicated to its parent service.

---

## 3. Agnostic Data API (Optional Sidecar)

**Based on Koan EntityController discovery.**

### **URL Structure**

```
/v1/data/{set}/entities/{type}
/v1/data/{set}/entities/{type}/{id}
```

**Pattern enforces security:** `[version]/data/[set]/[model]` prevents injection attacks via set names.

**Examples:**
```
GET  /v1/data/myapp/entities/users             # List users in 'myapp' set
GET  /v1/data/myapp/entities/users/123         # Get user by ID
POST /v1/data/myapp/entities/users             # Create user
PUT  /v1/data/myapp/entities/users/123         # Update user
DELETE /v1/data/myapp/entities/users/123       # Delete user
POST /v1/data/myapp/entities/users/bulk        # Bulk upsert
POST /v1/data/myapp/entities/users/query       # Advanced query
```

### **Discovery Endpoints**

```
GET  /v1/data/sets                             # List available sets
GET  /v1/data/sets/{set}/entities              # List entity types in set
GET  /v1/data/sets/{set}/entities/{type}/count # Entity count
```

### **Query Filter Syntax (JSON)**

**Discovered in Koan: `JsonFilterBuilder.cs`**

Supports MongoDB-like query syntax:

```json
POST /v1/data/myapp/entities/users/query
{
  "filter": {
    "age": { "$gte": 18 },
    "status": "active",
    "email": { "$exists": true }
  },
  "sort": [
    { "field": "createdAt", "descending": true }
  ],
  "page": 1,
  "pageSize": 25
}
```

**Supported operators:**
- Equality: `{ "name": "Alice" }`
- Comparison: `$gte`, `$lte`, `$gt`, `$lt`
- Set membership: `$in`, `$all`
- Existence: `$exists`
- Logical: `$and`, `$or`, `$not`
- String patterns: `{ "name": "Al*" }` (wildcard)

**Case-insensitive option:**
```json
{
  "filter": { "name": "alice" },
  "$options": { "ignoreCase": true }
}
```

### **Bulk Operations**

**Discovered in Koan: `POST /bulk` endpoint**

```json
POST /v1/data/myapp/entities/users/bulk
[
  { "id": "1", "name": "Alice", "age": 30 },
  { "id": "2", "name": "Bob", "age": 25 },
  { "id": "3", "name": "Charlie", "age": 35 }
]
```

Response:
```json
{
  "created": 2,
  "updated": 1,
  "errors": []
}
```

### **Pagination**

**Discovered in Koan: Query string parameters**

**Paginated by default.** All list and query endpoints return pages, not full result sets.

```
GET /v1/data/myapp/entities/users?page=2&pageSize=50&sort=-createdAt,name
```

**Batch filtering:**
```
GET /v1/data/myapp/entities/orders?filter={"userId":[1,2,3,4,5,6]}
```
Returns paginated results matching any userId in the array.

Response headers:
```
X-Page: 2
X-Page-Size: 50
X-Total-Count: 1247
X-Total-Pages: 25
```

### **Relationships**

**Trade-off accepted:** Relationships are handled via filter queries, not nested routes.

```
# Instead of: GET /myapp/users/123/orders
# Use: GET /v1/data/myapp/entities/orders?filter={"userId":123}
```

This keeps the API surface simple and reuses existing pagination/filtering infrastructure.

### **Set-Based Isolation**

**Discovered in Koan: `?set=` parameter and `EntityContext.With(partition:...)`**

Sets map to backend namespaces:
- **MongoDB (database mode):** Each set = separate database
- **MongoDB (collection-prefix mode):** Each set = collection prefix
- **PostgreSQL/SQL Server:** Each set = schema
- **Redis:** Each set = keyspace prefix

**Set cardinality:** Local use case (90% of the time), not a concern for small apps. For larger apps with many sets, internal routing to native segmentation can be discussed.

Example:
```
POST /v1/data/myapp/entities/users
→ MongoDB: db.myapp.users.insertOne(...)
→ PostgreSQL: INSERT INTO myapp.users ...
→ Redis: HSET myapp:users:123 ...
```

### **Transactions (Where Supported)**

**Discovered in Koan: EntityController supports transactions**

```json
POST /v1/data/myapp/transactions/begin
{
  "operations": [
    { "method": "POST", "path": "/v1/data/entities/users", "body": {...} },
    { "method": "PUT", "path": "/v1/data/entities/orders/123", "body": {...} },
    { "method": "DELETE", "path": "/v1/data/entities/temp-data/456" }
  ]
}
```

Backend support:
- ✅ MongoDB: Multi-document transactions (4.0+)
- ✅ PostgreSQL: Native transactions
- ✅ SQL Server: Native transactions
- ❌ Redis: Limited (MULTI/EXEC, different semantics)

---

## 4. mDNS Announcement Format

### **Service Type**

```
_koan-stone._tcp.local.
```

### **TXT Record Schema**

**Required fields:**
- `offering`: Service identifier (mongodb, postgresql, mongodb-agnostic, etc.)
- `port`: Service port
- `protocol`: Transport protocol (native, agnostic)
- `version`: Service version

**Optional fields:**
- `categories`: Comma-separated taxonomy (database,document-database)
- `capabilities`: Feature list (auth,ssl,replication)
- `tags`: User/auto tags (production,v8)
- `priority`: Selection priority (0-100, higher = preferred)
- `health`: Health status (healthy, degraded, unavailable)
- `set_mode`: Set mapping strategy (database, schema, collection-prefix)
- `fingerprint`: Certificate fingerprint (Pond security)

### **Example Announcements**

**Native MongoDB:**
```
stone-01-native._koan-stone._tcp.local.
TXT: offering=mongodb
     port=27017
     protocol=native
     version=7.0.4
     categories=dat (MongoDB's sidecar):**
```
stone-01-mongo-sidecar._koan-stone._tcp.local.
TXT: offering=mongodb-agnostic
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

**If Stone also runs SQL Server, its sidecar announces separately:**
```
stone-01-sqlserver-sidecar._koan-stone._tcp.local.
TXT: offering=sqlserver-agnostic
     port=8081
     protocol=agnostic
     version=1.0.2
     backend_service_version=2022
     categories=database,relational-database
     set_mode=schema
     capabilities=crud,query,filter,bulk,transactions
     priority=50
     health=healthy
```

**Important:** Sidecars are **per-service**. A Stone with 3 databases will have 3 sidecars (one for each).  backend_version=7.0.4
     categories=database
     set_mode=database
     capabilities=crud,query,filter,bulk,transactions
     priority=50
     health=healthy
```

---

## 5. Service Discovery Resolution

### **Client Resolution Algorithm**

```python
def resolve(connection_string):
    # Parse: zen-garden:mongodb[production]
    service_type, tags = parse(connection_string)
    
    # Query mDNS
    stones = mdns_query("_koan-stone._tcp.local.")
    
    # Filter by service type
    if service_type in KNOWN_SERVICES:  # mongodb, redis, etc.
        # Specific service - return native endpoint
        candidates = [s for s in stones 
                      if s.offering == service_type 
                      and s.protocol == 'native']
    else:
        # Generic category - return agnostic endpoint
        candidates = [s for s in stones 
                      if service_type in s.categories 
                      and s.protocol == 'agnostic']
    
    # Filter by tags (if specified)
    if tags:
        candidates = [s for s in candidates 
                      if all(tag in s.tags for tag in tags)]
    
    # Select best
    best = select_best(candidates, priority='priority', health='healthy')
    
    # Build connection string
    if best.protocol == 'native':
        return f"{best.offering}://{best.host}:{best.port}"
    else:
        return f"http://{best.host}:{best.port}"
```

### **Selection Priority**

When multiple Stones match:
1. **Health status:** `healthy` > `degraded` > `unavailable`
2. **Priority value:** Higher priority wins (0-100 scale)
3. **Response time:** Faster average response time preferred
4. **Round-robin:** Distribute load across equal candidates

---

## 6. Security Model

### **Authentication**

**Service Installation:**
- **Template-only installs:** Garden Agent only accepts service installations from curated offering templates (no ad-hoc Docker configurations via API)
- Templates are validated and signed
- Custom services require manifest files placed on Stone filesystem

**With Pond (mTLS):**
- Stone has certificate issued by Pond CA
- TXT record includes certificate fingerprint
- Clients validate certificate before connecting
- Mutual TLS for native protocols
- API key + cert validation for HTTP APIs

**Without Pond (Open):**
- No authentication on local network
- Assumes trusted network (home lab, small office)
- External exposure requires API gateway with auth
- Rate limiting at gateway level
- **Risk acknowledged:** mDNS spoofing possible, Stone management API open to LAN

**Pond Opt-In Story:**
"Set your stones, make sure everything is working, fill the pond."
- Users start without Pond (frictionless setup)
- Once satisfied, run `zen-garden pond init` to enable mTLS
- All Stones automatically request certificates and begin mutual auth
- One command transitions entire garden from open to secure
- One command transitions entire garden from open to secure

**Design principle:** Frictionless by default, secure when needed.

### **Authorization**

**Set-level isolation:**
- Applications operate within their assigned set
- No cross-set access without explicit configuration
- Backend enforcement (database/schema/collection boundaries)

**Future consideration:**
- Row-level security via query rewriting
- OAuth2 integration for Pond-enabled Stones
- API key management for external exposure

### **Input Validation**

**Discovered in Koan: JsonFilterBuilder validation**

- Filter expressions validated before execution
- Type safety enforced (no string-to-int confusion)
- SQL injection prevented via parameterization
- Expression tree compilation (no eval/exec)
- Error messages sanitized (no stack traces to clients)

---

## 7. Docker Compose Management

### **File Structure**

```
/home/stone/
├── docker-compose.yml         # Managed services (from manifests)
├── custom-services/
│   ├── my-app.yml             # User-provided
│   └── staging-db.yml         # User-provided
└── .garden/
    ├── manifests/             # Service templates
    ├── config.yml             # Agent configuration
    └── compose-backups/       # Rollback snapshots
```

### **Service Installation Flow**

1. **Receive install request** (manifest or custom)
2. **Validate Docker compose syntax**
3. **Check for port conflicts**
4. **Backup current docker-compose.yml**
5. **Merge new service into compose file**
6. **Write updated compose file atomically**
7. **Run `docker compose up -d [service]`**
8. **Wait for health check (up to 60s)**
9. **Announce service via mDNS**
10. **Return success/failure to client**

### **Rollback on Failure**

```bash
# If service fails to start
1. docker compose down [service]
2. Restore previous docker-compose.yml from backup
3. docker compose up -d  # Restore previous state
4. Return error with diagnostics
```

---

## 8. Implementation Discoveries from Koan

### **EntityController API Surface**

**File:** `src/Koan.Web/Controllers/EntityController.cs`

```csharp
[ApiController]
public abstract class EntityController<TEntity, TKey> : ControllerBase
{
    // Auto-generated endpoints:
    [HttpGet("")] GetCollection()           // List with filters
    [HttpGet("{id}")] GetById()             // Get by ID
    [HttpPost("")] Upsert()                 // Create/update
    [HttpPut("{id}")] Update()              // Update by ID
    [HttpDelete("{id}")] Delete()           // Delete by ID
    [HttpPost("query")] Query()             // Advanced query
    [HttpPost("bulk")] UpsertMany()         // Bulk operations
    [HttpDelete("bulk")] DeleteMany()       // Bulk delete
}
```

**Key features:**
- Set routing: `?set=myapp`
- Relationship expansion: `?with=orders`
- Field selection: `?shape=minimal`
- Pagination: `?page=1&pageSize=50`
- Sorting: `?sort=-createdAt,name`
- Filtering: `?filter={"status":"active"}`

### **JSON Filter Implementation**

**File:** `src/Koan.Web/Filtering/JsonFilterBuilder.cs`

```csharp
public static class JsonFilterBuilder
{
    public static bool TryBuild<TEntity>(
        string? json, 
        out Expression<Func<TEntity, bool>>? predicate,
        out string? error,
        BuildOptions? options = null)
    {
        // Parses JSON to LINQ Expression tree
        // Supports: equality, ranges, $in, $exists, $and, $or, $not
        // Wildcards: "name": "Al*" (begins), "*ice" (ends), "*li*" (contains)
        // Case-insensitive option available
    }
}
```

**Validation:**
- Invalid JSON → clear error message
- Invalid field names → caught at parse time
- Type mismatches → compile-time safe (Expression tree)
- No SQL injection risk (parameterized)

### **Bulk Operations**

**File:** `src/Koan.Web/Controllers/EntityController.cs`

```csharp
[HttpPost("bulk")]
public virtual async Task<IActionResult> UpsertMany(
    [FromBody] IEnumerable<TEntity> models, 
    CancellationToken ct)
{
    // Insert or update multiple entities
    // Batched for performance
    // Returns summary (created/updated counts)
}
```

Used in samples:
- S10.DevPortal: Bulk article imports
- S6.SnapVault: Bulk photo operations
- S16.PantryPal: Bulk recipe imports

### **Set-Based Routing**

**File:** `src/Koan.Web/Endpoints/EntityEndpointService.cs`

```csharp
using var _ = EntityContext.With(
    partition: string.IsNullOrWhiteSpace(request.Set) ? null : request.Set
);
```

- Sets isolation at data access layer
- Backend adapters handle mapping
- No application code changes needed

---

## 9. Design Decisions Summary

### **Resolved**

| Concern | Decision | Rationale |
|---------|----------|-----------|
| API versioning | `/v1/data/[set]/[model]/*` | Migration path for breaking changes |
| Relationships | Filter-based (not nested routes) | Simple API surface, reuses pagination/filtering infrastructure |
| Announcement collision | Non-issue | Apps explicitly choose `mongodb` (native) vs `database` (agnostic) |
| Service naming | Categories = agnostic, offerings = native | Clear semantic distinction |
| Query syntax | Use Koan JsonFilterBuilder | Already implemented, validated, MongoDB-like |
| Bulk operations | Standard `/bulk` endpoints | Koan pattern, proven in samples |
| Transactions | Expose where backend supports | MongoDB, Postgres, SQL Server have them |
| Schema auto-creation | Enabled by default | Target audience: frictionless > enterprise |
| Security | Template-only installs, Pond opt-in | Frictionless by default, secure when needed |
| Rate limiting | Gateway concern | API = internal, Gateway = external |
| Performance overhead | Accepted trade-off | Convenience > performance for agnostic APIs |
| Container allocation | One stone, one service (mental model) | Signal warning when user packs too many |
| Port management | Default to defaults, auto-reassign on conflict | Garden tooling handles conflicts transparently |
| Sidecar versioning | Independent from backend (lock when necessary) | MongoDB stable across versions, sidecar evolves separately |
| Backup strategy | Stone-level primary, API-level secondary | Backup entire Stone, expose data export endpoints |
| Set cardinality | Local use case (90%), non-concern | For larger apps, discuss internal routing to native segmentation |

### **To Be Implemented**

| Priority | Task | Notes |
|----------|------|-------|
| P0 | Moss HTTP API | Service install/uninstall, template-only policy |
| P0 | mDNS announcer | Avahi integration, dual announcements (native + agnostic) |
| P0 | Docker Compose manager | Atomic updates, rollback, port conflict resolution |
| P0 | Port allocation strategy | Default to defaults, auto-reassign on conflict, announce actual port |
| P1 | Agnostic Data API (C#) | Based on EntityController, /v1/data pattern |
| P1 | Schema support for EntityController | Index management, schema evolution |
| P1 | Agnostic caching standard | Cache-Control headers, ETag support, invalidation policy |
| P1 | Backup endpoints for agnostic API | Export/import via REST (Stone-level backups primary) |
| P1 | Container resource signaling | Warn when too many services packed on one Stone |
| P1 | Koan.ZenGarden client library | C# resolver |
| P2 | Health monitoring | Container health checks |
| P2 | Pond integration | mTLS authentication, one-command opt-in |
| P3 | JavaScript/Python clients | After C# validation |
| P3 | Quickstart for Zen Garden apps | Focus on app development, not Stone setup |

---

## 10. Sample Usage Patterns

### **Native Driver (MongoDB-specific)**

```csharp
// Application uses MongoDB.Driver
var uri = await ZenGarden.ResolveAsync("zen-garden:mongodb");
// → mongodb://stone-01:27017

var client = new MongoClient(uri);
var db = client.GetDatabase("myapp");
// Full MongoDB API available
```

### **Agnostic API (Database-neutral)**

```csharp
// Application uses Koan.ZenGarden.Client
var api = await ZenGarden.ResolveAsync("zen-garden:database");
// → http://stone-01:8080

var client = new ZenDataClient(api);
var users = await client.Set("myapp").Entity("users").Query(new {
    filter = new { status = "active" },
    sort = "-createdAt",
    page = 1,
    pageSize = 25
});

// Relationship via filter (trade-off accepted)
var orders = await client.Set("myapp").Entity("orders")
    .Query(new { filter = new { userId = 123 } });
```

### **Category-Based Discovery**

```javascript
// Node.js application
const { resolve } = require('@zen-garden/resolver');

// Any document database (MongoDB, CouchDB, RavenDB, etc.)
const uri = await resolve('zen-garden:document-database');
// → http://stone-02:8080

const users = await fetch(`${uri}/v1/data/myapp/entities/users`);
```

---

## 11. Next Steps

1. **Validate EntityController portability** - Can it run standalone?
2. **Prototype Garden Agent** - REST API + mDNS announcer (Python/Go)
3. **Test agnostic API** - EntityController with set-based routing
4. **Define Koan.ZenGarden API** - C# client library interface
5. **Update manifests** - Add sidecar service blocks
6. **Document query syntax** - Full JsonFilterBuilder reference
7. **Security model refinement** - Pond certificate integration

---

## References

- [AGNOSTIC-SIDECARS.md](AGNOSTIC-SIDECARS.md) - Sidecar concept overview
- [UNDERSTANDING.md](UNDERSTANDING.md) - Core Zen Garden concepts
- [REFERENCE.md](REFERENCE.md) - mDNS protocol details
- Koan EntityController: `src/Koan.Web/Controllers/EntityController.cs`
- Koan JsonFilterBuilder: `src/Koan.Web/Filtering/JsonFilterBuilder.cs`
- Sample: S10.DevPortal - EntityController usage patterns
- Sample: S16.PantryPal - Bulk operations examples

---

**Document Status:** Living specification - will be updated as implementation progresses.
