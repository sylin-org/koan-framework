# Zen Garden `find` Command Specification

**Status**: Proposal
**Author**: Architecture Team
**Date**: 2026-01-22
**Relates to**: `zen-garden-spec-topology-caching.md`, `CLI-DUAL-ERGONOMICS-DISCUSSION.md`

---

## Overview

The `find` command provides instant service discovery from the topology cache, returning connection strings for running services. It integrates with the "wish" concept for optional auto-provisioning.

### Design Goals

1. **Millisecond response**: Cache-first architecture, no network calls
2. **Connection-ready output**: Returns usable connection strings, not just service names
3. **Semantic search**: Find by name, category, or tags
4. **Wishful provisioning**: Auto-provision if not found (opt-in)
5. **App integration**: Supports `zen-garden:wish//` URI scheme for consuming applications

### Verb Swap Decision

| Old Command | New Command | Domain |
|-------------|-------------|--------|
| `find strays` | `locate strays` | Adoption (orphaned containers) |
| *(new)* | `find <service>` | Service discovery (connection strings) |

---

## Syntax

### Zen Syntax

```bash
# By name (exact match)
garden-rake find mongodb
garden-rake find mongodb fresh      # Bypass cache, live discovery
garden-rake find mongodb wishfully  # Auto-provision if not found

# By category (known categories: database, cache, search, monitoring, messaging)
garden-rake find database           # Implicit category (if recognized)
garden-rake find c:database         # Explicit category prefix
garden-rake find cat:database       # Alternative prefix
garden-rake find category:database  # Verbose prefix

# By tag
garden-rake find t:nosql            # Tag prefix
garden-rake find tag:document       # Alternative prefix
garden-rake find tags:realtime      # Verbose prefix

# Combined modifiers
garden-rake find c:database wishfully    # Provision suggestions for category
garden-rake find mongodb fresh quietly   # Fresh discovery, minimal output
```

### Normative Syntax

```bash
# By name
garden-rake services find --name mongodb
garden-rake services find --name mongodb --fresh
garden-rake services find --name mongodb --wishful

# By category
garden-rake services find --category database
garden-rake services find --category database --wishful

# By tag
garden-rake services find --tag nosql
garden-rake services find --tags nosql,document  # Multiple tags

# Output control
garden-rake services find --name mongodb --format json
garden-rake services find --name mongodb --format connection-string
```

### Dual Syntax Mapping

| Zen | Normative | Semantics |
|-----|-----------|-----------|
| `find <name>` | `services find --name <name>` | Find by service name |
| `find <name> fresh` | `services find --name <name> --fresh` | Bypass cache |
| `find <name> wishfully` | `services find --name <name> --wishful` | Auto-provision if missing |
| `find <name> quietly` | `services find --name <name> --quiet` | Minimal output |
| `find <name> patiently` | `services find --name <name> --wait --timeout 30` | Wait for service |
| `find c:<cat>` | `services find --category <cat>` | Find by category |
| `find t:<tag>` | `services find --tag <tag>` | Find by tag |

---

## Topology Cache Extension

### Current Cache Schema (`TopologyEntry.services`)

```rust
pub struct ServiceSummary {
    pub name: String,
    pub service_type: String,
    pub status: String,
}
```

### Extended Schema for `find`

```rust
/// Extended service information for discovery
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CachedService {
    // === Identity ===

    /// Service name (e.g., "mongodb", "redis-cache")
    pub name: String,

    /// Offering type (e.g., "mongodb", "redis")
    pub offering: String,

    // === Network ===

    /// Host-mapped port for client connections
    pub port: u16,

    /// Protocol for connection string (e.g., "mongodb", "redis", "postgresql")
    pub protocol: String,

    /// Stone hosting this service
    pub stone_id: String,
    pub stone_endpoint: String,

    // === Metadata ===

    /// Service category (database, cache, search, monitoring, messaging)
    pub category: String,

    /// Tags for semantic search
    pub tags: Vec<String>,

    /// Current status
    pub status: ServiceStatus,

    /// Connection string template (from offering manifest)
    pub connection_template: Option<String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum ServiceStatus {
    Running,
    Stopped,
    Starting,
    Degraded,
}
```

### Cache Population

Services are added to the topology cache through:

1. **Heartbeat announcements**: Stones include `services_hash` in heartbeats
2. **HTTP enrichment**: When hash changes, fetch `/api/v1/services` for details
3. **mDNS TXT records**: Basic service advertisements (name, port)
4. **Manual refresh**: `garden-rake find <name> fresh` forces live query

### Cache TTL

| Data | TTL | Rationale |
|------|-----|-----------|
| Service list | 90 seconds | Match topology TTL |
| Service status | 30 seconds | Status changes frequently |
| Connection template | Infinite | Templates don't change |

---

## Connection String Templates

### Offering Manifest Extension

```yaml
# offerings/mongodb.yaml
name: mongodb
category: database
tags: [document, nosql, json]

# Connection template with placeholders
connection_template: "mongodb://{host}:{port}"

# Extended template for authenticated access
connection_template_auth: "mongodb://{user}:{pass}@{host}:{port}/{database}?authSource=admin"

# Additional templates for different use cases
connection_templates:
  default: "mongodb://{host}:{port}"
  replica_set: "mongodb://{host}:{port}/?replicaSet={replica_set}"
  srv: "mongodb+srv://{host}"
```

### Template Resolution

```rust
/// Resolved connection URIs for a service
pub struct ResolvedConnection {
    pub hostname: String,      // e.g., "stone-02.local"
    pub ip: String,            // e.g., "192.168.1.102"
    pub port: u16,
    pub protocol: String,
    pub uris: Vec<String>,     // Hostname-first, then IP
}

/// Resolve connection URIs from cached service
pub fn resolve_connection(service: &CachedService, stone: &TopologyEntry) -> ResolvedConnection {
    let template = service.connection_template
        .as_ref()
        .unwrap_or(&format!("{}://{{host}}:{{port}}", service.protocol));

    // Extract hostname and IP from stone
    let hostname = format!("{}.local", stone.stone_name);
    let ip = extract_ip(&stone.endpoint);

    let uri_hostname = template
        .replace("{host}", &hostname)
        .replace("{port}", &service.port.to_string());

    let uri_ip = template
        .replace("{host}", &ip)
        .replace("{port}", &service.port.to_string());

    ResolvedConnection {
        hostname,
        ip,
        port: service.port,
        protocol: service.protocol.clone(),
        uris: vec![uri_hostname, uri_ip],  // Hostname first (more resilient)
    }
}
```

---

## Command Output

### Standard Output (Found)

```
$ garden-rake find mongodb

  mongodb (database) on stone-02
  mongodb://stone-02.local:27017

  Hint: Use `garden-rake find mongodb --format json` for machine-readable output
```

### Standard Output (Multiple Matches)

```
$ garden-rake find c:database

  mongodb (database) on stone-02
  mongodb://stone-02.local:27017

  postgresql (database) on stone-01
  postgresql://stone-01.local:5432

  Found 2 database services across 2 stones
```

### Standard Output (Not Found)

```
$ garden-rake find mongodb

  No running 'mongodb' service found in the garden

  Suggestions:
    garden-rake find mongodb wishfully  # Auto-provision mongodb
    garden-rake offer                   # View available offerings
    garden-rake find c:database         # Find any database
```

### Wishfully Mode - Exact Name

```
$ garden-rake find mongodb wishfully

  No running 'mongodb' service found

  Provisioning mongodb...
  Recommending placement: stone-02 (score: 87)

  Started: zen-offering-mongodb on stone-02
  Waiting for service health... ready

  mongodb://stone-02.local:27017
```

### Wishfully Mode - Category/Tag (Suggestions)

```
$ garden-rake find c:database wishfully

  No running database services found

  Available database offerings (pick one to install):

    1. garden-rake offer mongodb    # Document store, NoSQL
    2. garden-rake offer postgresql # Relational, SQL
    3. garden-rake offer redis      # Key-value, in-memory

  Or search by tag: garden-rake find t:sql
```

### JSON Output

```json
$ garden-rake find mongodb --format json

{
  "found": true,
  "services": [
    {
      "name": "mongodb",
      "offering": "mongodb",
      "category": "database",
      "tags": ["document", "nosql", "json"],
      "status": "running",
      "stone": {
        "id": "019abc34-...",
        "name": "stone-02",
        "endpoint": "http://192.168.1.102:7185"
      },
      "connection": {
        "hostname": "stone-02.local",
        "ip": "192.168.1.102",
        "port": 27017,
        "protocol": "mongodb",
        "uris": [
          "mongodb://stone-02.local:27017",
          "mongodb://192.168.1.102:27017"
        ]
      }
    }
  ],
  "source": "cache",
  "cache_age_seconds": 12,
  "timestamp": "2026-01-22T12:00:00Z"
}
```

**URI Ordering**: Hostname-based URI is listed first (more resilient to IP changes), IP-based URI second (fallback if mDNS unavailable).

### Connection String Only

```
$ garden-rake find mongodb --format uri
mongodb://stone-02.local:27017
```

Use `--format uri-ip` for IP-based fallback:
```
$ garden-rake find mongodb --format uri-ip
mongodb://192.168.1.102:27017
```

---

## Exit Codes

| Code | Meaning | Use Case |
|------|---------|----------|
| 0 | Found | Service(s) found, connection string returned |
| 1 | Not found | No matching services, no provisioning attempted |
| 2 | No stones | No stones reachable (network issue) |
| 3 | Provisioning failed | Wishful mode tried but failed |
| 4 | Timeout | Used with `patiently`, service didn't appear |

---

## App Integration: The Wish Protocol

### URI Scheme

Applications request services using special URIs:

| URI | Behavior |
|-----|----------|
| `zen-garden://mongodb` | Find only (read-only, equivalent to `find mongodb`) |
| `zen-garden:wish//mongodb` | Find or provision (read-write, equivalent to `find mongodb wishfully`) |
| `zen-garden://mongodb/mydb` | Find with database hint |
| `zen-garden:wish//mongodb/mydb` | Find or provision with database |

### Client Library Integration

```rust
/// Resolve a zen-garden URI to a connection string
pub async fn resolve_service(uri: &str) -> Result<String, ServiceError> {
    let parsed = parse_zen_garden_uri(uri)?;

    // Query local Moss API
    let result = moss_client.find_service(&parsed.service_name).await?;

    match (result, parsed.wishful) {
        (Some(service), _) => {
            // Found - return connection string
            Ok(service.connection_string)
        }
        (None, true) => {
            // Not found, but wishful - trigger provisioning
            let provisioned = moss_client.provision_service(&parsed.service_name).await?;
            Ok(provisioned.connection_string)
        }
        (None, false) => {
            // Not found, not wishful - error
            Err(ServiceError::NotFound(parsed.service_name))
        }
    }
}
```

### Event-Driven Connection

For wishful requests, apps can subscribe to service availability:

```rust
// App startup
let uri = "zen-garden:wish//mongodb";

// Non-blocking wish registration
let handle = wish_service(uri);

// App continues startup without database
// ...

// Later, when database is needed
let connection = handle.await?;  // Blocks until service available
```

---

## Zen Modifier Keywords

| Zen Keyword | Normative Flag | Meaning |
|-------------|----------------|---------|
| `fresh` | `--fresh` | Bypass cache, live discovery |
| `wishfully` | `--wishful` | Auto-provision if not found |
| `quietly` | `--quiet` | Minimal output (connection string only) |
| `patiently` | `--wait --timeout 30` | Wait for service to appear |
| `verbosely` | `--verbose` | Include debug information |

---

## Prefix Aliases

For semantic search disambiguation:

| Prefix | Aliases | Example |
|--------|---------|---------|
| Category | `c:`, `cat:`, `category:` | `find c:database` |
| Tag | `t:`, `tag:`, `tags:` | `find t:nosql` |

### Known Categories

Categories are recognized from the offering catalog:

- `database` - Data storage (mongodb, postgresql, mysql, sqlite)
- `cache` - Caching systems (redis, memcached, dragonfly)
- `search` - Search engines (elasticsearch, meilisearch, typesense)
- `monitoring` - Observability (grafana, prometheus, loki)
- `messaging` - Message queues (rabbitmq, nats, kafka)
- `storage` - Object storage (minio, seaweedfs)

### Implicit Category Detection

Bare words matching known categories are interpreted as category search:

```bash
garden-rake find database    # Interpreted as find c:database
garden-rake find cache       # Interpreted as find c:cache
garden-rake find myservice   # Interpreted as find by name
```

---

## API Endpoints

### Moss API

```
GET /api/v1/services/find?name=mongodb
GET /api/v1/services/find?category=database
GET /api/v1/services/find?tag=nosql
GET /api/v1/services/find?name=mongodb&fresh=true
```

### Response Schema

```json
{
  "data": {
    "found": true,
    "services": [...],
    "source": "cache",
    "cache_age_seconds": 12
  }
}
```

### Wishful Provisioning Endpoint

```
POST /api/v1/services/wish
{
  "offering": "mongodb",
  "preferences": []
}
```

---

## Implementation Phases

### Phase 1: Cache Extension

1. Extend `ServiceSummary` to `CachedService` in topology cache
2. Add `connection_template` field to offering manifests
3. Populate services in heartbeat observer

### Phase 2: Find Command (Rake)

1. Implement `find` command with name search
2. Add category/tag prefix parsing
3. Add `--format` output options
4. Integrate with topology cache API

### Phase 3: Wishfully Mode

1. Add `--wishful` flag
2. Integrate with placement recommendation API
3. Add provisioning flow with progress display
4. Handle suggestions for category/tag searches

### Phase 4: App Integration

1. Define `zen-garden://` URI scheme
2. Add client library support
3. Implement wish event subscription
4. Document integration patterns

---

## Related Commands

| Command | Purpose |
|---------|---------|
| `locate strays` | Find adoptable containers (was `find strays`) |
| `offer` | List/install offerings |
| `observe` | Garden-wide topology view |
| `list` | Services on tended stone |

---

## Open Questions

1. **Multiple matches**: Should `find mongodb` error if multiple mongodb instances exist, or return all?
   - **Proposal**: Return all with disambiguation hints

2. **Wish timeout**: How long should wishful provisioning wait before failing?
   - **Proposal**: 60 seconds default, configurable via `--timeout`

3. **Service aliases**: Should we support aliases like `find mongo` -> `mongodb`?
   - **Proposal**: Not in v1, consider for future

---

## References

- `zen-garden-spec-topology-caching.md` - Cache architecture
- `CLI-DUAL-ERGONOMICS-DISCUSSION.md` - Zen/Normative syntax patterns
- `intelligent-offering-placement.md` - Placement recommendation system
