---
audience: [developer, api-client, contributor]
doc_type: reference
status: current
last_verified: 2026-01-19
canonical: true
note: "Authoritative technical reference for Zen Garden protocol. Complete API documentation covering connection string protocol, mDNS service announcement, Lantern HTTP API, TXT record schema, supported service types, client libraries, and error handling. Canonical until formal ZGP protocol specifications published."
related:
  - TECHNICAL-SPEC.md
  - SERVICE-INVENTORY.md
  - UNDERSTANDING.md
---

# Technical Reference

**Complete API documentation for Zen Garden protocol.**

---

## Table of Contents

1. [Connection String Protocol](#connection-string-protocol)
2. [mDNS Service Announcement](#mdns-service-announcement)
3. [Lantern HTTP API](#lantern-http-api)
4. [TXT Record Schema](#txt-record-schema)
5. [Supported Service Types](#supported-service-types)
6. [Client Libraries](#client-libraries)
7. [Error Handling](#error-handling)

> **Note:** Formal protocol specifications (ZGP-001 through ZGP-005) are in development. This document provides implementation guidance for current reference implementation.

---

## Connection String Protocol

### Format

```
zen-garden:<service-type>[/<database>]
```

### Examples

```bash
zen-garden:mongodb          # Discover MongoDB Stone
zen-garden:mongodb/mydb     # Discover MongoDB + specify database
zen-garden:redis            # Discover Redis Stone
zen-garden:postgres/app     # Discover PostgreSQL + specify database
zen-garden:minio            # Discover MinIO storage
zen-garden:ollama           # Discover Ollama LLM
```

### Resolution Process

**mDNS discovery (default):**
1. Broadcast query: "Who offers `<service-type>`?"
2. Stones respond with hostname + port
3. Resolve hostname to IP via mDNS
4. Return native connection string

**Lantern fallback (Windows/cross-subnet):**
1. Query Lantern API: `GET /api/resolve?service=<type>`
2. Receive cached Stone location
3. Return native connection string

**Timeout:** 1 second default (configurable)

### Language Integration

**C# (planned):**
```csharp
// appsettings.json
"ConnectionStrings": {
  "Database": "zen-garden:mongodb/mydb"
}

// Automatic resolution via Koan.ZenGarden
services.AddMongoClient(); // Resolves from config
```

**Node.js (planned):**
```javascript
const { resolve } = require('@zen-garden/resolver');

const uri = await resolve('zen-garden:mongodb');
// Returns: mongodb://stone-01.local:27017

const client = new MongoClient(uri);
```

**Python (planned):**
```python
from zen_garden import resolve

uri = resolve('zen-garden:mongodb')
# Returns: mongodb://stone-01.local:27017

client = MongoClient(uri)
```

**Generic HTTP (current):**
```bash
curl http://lantern:3000/api/resolve?service=mongodb
# {"uri": "mongodb://stone-01:27017", "healthy": true}
```

---

## mDNS Service Announcement

### Service Type Format

```
_koan-stone._tcp.local.
```

All Stones announce under this service type. Service differentiation via TXT records.

### Announcement Structure

**DNS-SD records:**
```
# Service advertisement
_services._dns-sd._udp.local. PTR _koan-stone._tcp.local.

# Instance name
stone-01._koan-stone._tcp.local. SRV 0 0 <port> stone-01.local.
stone-01.local. A 192.168.1.50

# Metadata
stone-01._koan-stone._tcp.local. TXT "offering=mongodb" "version=7.0" "port=27017"
```

### TXT Record Schema

**Required fields:**
- `offering`: Service type (mongodb, postgresql, redis, etc.)
- `version`: Service version string
- `port`: Service port number

**Optional fields:**
- `capabilities`: Comma-separated features (auth, ssl, replication)
- `priority`: Selection priority (0-100, higher = preferred)
- `health`: Health status (healthy, degraded, unavailable)
- `fingerprint`: Certificate fingerprint (Pond security)

**Example:**
```
offering=mongodb, version=7.0.4, port=27017, capabilities=auth,ssl, priority=100, health=healthy
```

---

## Lantern HTTP API

### Overview

HTTP directory service for Windows clients and cross-subnet discovery.

**Base URL:** `http://lantern:3000/api`

### Endpoints

#### Register Stone

```http
POST /api/register
Content-Type: application/json

{
  "offering": "mongodb",
  "hostname": "stone-01",
  "host": "192.168.1.50",
  "port": 27017,
  "version": "7.0.4",
  "capabilities": ["auth", "ssl"],
  "priority": 100
}
```

**Response:**
```json
{
  "registered": true,
  "expires_at": "2026-01-15T12:00:00Z"
}
```

**TTL:** 60 seconds (Stones re-register via heartbeat)

---

#### Resolve Service

```http
GET /api/resolve?service=<type>
```

**Response:**
```json
{
  "uri": "mongodb://stone-01:27017",
  "stone": "stone-01",
  "host": "192.168.1.50",
  "port": 27017,
  "healthy": true,
  "discovered_at": "2026-01-15T11:45:30Z"
}
```

**Error (not found):**
```json
{
  "error": "No Stone offering 'postgresql'",
  "available": ["mongodb", "redis", "minio"]
}
```

---

#### List All Stones

```http
GET /api/stones
```

**Response:**
```json
{
  "stones": [
    {
      "stone": "stone-01",
      "offering": "mongodb",
      "host": "192.168.1.50",
      "port": 27017,
      "healthy": true,
      "last_seen": "2026-01-15T11:59:45Z"
    },
    {
      "stone": "stone-02",
      "offering": "redis",
      "host": "192.168.1.51",
      "port": 6379,
      "healthy": true,
      "last_seen": "2026-01-15T11:59:50Z"
    }
  ]
}
```

---

#### Health Check

```http
GET /api/health
```

**Response:**
```json
{
  "status": "healthy",
  "stones_registered": 3,
  "uptime_seconds": 86400
}
```

---

## Supported Service Types

### Database Services

| Service Type | Default Port | Native Connection String |
|--------------|--------------|--------------------------|
| `mongodb` | 27017 | `mongodb://host:port[/db]` |
| `postgresql` | 5432 | `postgresql://host:port[/db]` |
| `mysql` | 3306 | `mysql://host:port[/db]` |
| `redis` | 6379 | `redis://host:port` |
| `sqlserver` | 1433 | `Server=host,port;Database=db` |
| `cassandra` | 9042 | `cassandra://host:port` |

### Storage Services

| Service Type | Default Port | Native Connection String |
|--------------|--------------|--------------------------|
| `minio` | 9000 | `http://host:port` (S3 API) |
| `nfs` | 2049 | `nfs://host:/path` |

### Messaging Services

| Service Type | Default Port | Native Connection String |
|--------------|--------------|--------------------------|
| `rabbitmq` | 5672 | `amqp://host:port` |
| `kafka` | 9092 | `kafka://host:port` |
| `nats` | 4222 | `nats://host:port` |

### Compute Services

| Service Type | Default Port | Native Connection String |
|--------------|--------------|--------------------------|
| `docker` | 2375 | `tcp://host:port` |
| `ollama` | 11434 | `http://host:port` |

---

## Client Libraries

### Current Status

**In development:** Reference implementations planned for:
- C# (Koan.ZenGarden)
- Rust (garden-rake CLI)
- Node.js (@zen-garden/resolver)
- Python (zen-garden-resolver)

---

## Garden Rake CLI Commands

### Overview

The `garden-rake` CLI provides comprehensive management and observation capabilities for Zen Garden infrastructure.

### Commands

#### observe - Garden State Observation

Display real-time state of all stones with resource metrics and offerings.

**Usage:**
```bash
# View all stones with all offerings
garden-rake observe

# View specific stone by name
garden-rake observe stone-01

# Filter by offerings across all stones
garden-rake observe --offering mongodb,redis
```

**Output:**
```
═══ GARDEN OVERVIEW ═══

●  stone-01 (Healthy, uptime: 1d 13h 57m)
   CPU: 20 @ 51.9%  │  Memory: 49.96 GB / 63.82 GB (78.3%)  │  Disk: 441.91 MB / 549.00 MB (80.5%)
   OFFERINGS:
   ├─ mongodb       Run   0.04%  147.07 MB  ↓ 1.39 KB  51s
   ├─ redis         Run   0.01%    9.08 MB  ↓ 1.06 KB  50s
   └─ postgresql    Run   0.01%   31.84 MB  ↓   998 B  50s

●  stone-02 (Healthy, uptime: 5d 2h 15m)
   CPU: 8 @ 23.4%  │  Memory: 8.12 GB / 16.00 GB (50.8%)  │  Disk: 120.50 GB / 500.00 GB (24.1%)
   OFFERINGS:
   ├─ rabbitmq      Run   0.12%  512.00 MB  ↓ 15.23 KB  2h 15m
   └─ elasticsearch Run   1.45%    2.50 GB   ↓ 125.67 KB  2h 10m
```

**Features:**
- **Multi-stone discovery**: Automatically discovers all stones via UDP broadcast
- **Real-time metrics**: CPU, memory, network I/O, and uptime
- **Health indicators**: ● (healthy), ◐ (degraded), ○ (offline)
- **Filtering**: View specific stones or offerings
- **Hidden service count**: Shows "+N other services" when filtered

**Column Legend:**
- **Name**: Service/offering name
- **Status**: Run (running), Stop (stopped), Maint (maintenance), Degr (degraded)
- **CPU**: Current CPU usage percentage
- **Memory**: Current memory consumption
- **Network**: Download (↓) in friendly units
- **Uptime**: Time since container started

#### Other Commands

**Service Management:**
```bash
garden-rake offer <offering>          # Install service
garden-rake remove <service>          # Remove service
garden-rake upgrade [service]         # Upgrade service(s)
garden-rake upgrade --all             # Upgrade all services
garden-rake list [--at <endpoint>]    # List services
```

**Stone Management:**
```bash
garden-rake status [--at <endpoint>]  # Get stone status
```

**Lifecycle Operations:**
```bash
garden-rake rest <service>            # Stop service (rest mode)
garden-rake wake <service>            # Start service (wake from rest)
```

**Security (Phase 3):**
```bash
garden-rake place keystone              # Initialize Pond
garden-rake invite <stone-name>       # Generate invitation
garden-rake place stone --code <code> # Join Pond
```

---

## Resource Monitoring

### Overview

Moss collects and exposes comprehensive resource metrics for both host systems and individual containers.

### Host-Level Metrics

**Collected every 30 seconds via health monitoring task:**

- **CPU**: Core count, usage percentage
- **Memory**: Total, used, available (bytes + percentages)
- **Disk**: Total, used, available for zen-garden volumes
- **Uptime**: System uptime in seconds

**Friendly Formatting:**
- Bytes: "8.2 GB", "512 MB", "1.5 TB"
- Percentages: "45.3%", "78.1%"
- Uptime: "2d 5h 30m", "1h 45m", "30s"

### Container-Level Metrics

**Collected from Docker stats API:**

- **CPU**: Usage percentage
- **Memory**: Current usage, limit, percentage
- **Network I/O**: Bytes received/transmitted
- **Block I/O**: Bytes read/written
- **Uptime**: Time since container started

**API Response Structure:**
```json
{
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
```

### Design Philosophy

**Lean Agent Principle**: Moss collects and reports metrics without applying thresholds or warnings. Client applications (Lantern UI, monitoring tools, `garden-rake observe`) consume these metrics and apply business logic for alerts and visualization.

**Benefits:**
- Moss remains lightweight and focused
- Flexible threshold policies per deployment
- Separation of concerns (data collection vs interpretation)
- Multiple consumers (CLI, UI, monitoring) can apply different rules

### Usage Examples

**Via HTTP API:**
```bash
# Get stone resources
curl http://stone-01:3001/info | jq '.resources'

# Get container resources
curl http://stone-01:3001/api/services | jq '.[].resources'
```

**Via garden-rake observe:**
```bash
# View all resources in friendly format
garden-rake observe

# Monitor specific offerings
garden-rake observe --offering mongodb,redis
```

**Integration Examples:**
```bash
# Export to monitoring tool
curl http://stone-01:3001/api/services | \
  jq -r '.[] | "\(.name),\(.resources.cpu_percent),\(.resources.memory_bytes)"' | \
  logger -t zen-garden-metrics

# Check if any service exceeds 80% memory
curl -s http://stone-01:3001/api/services | \
  jq '.[] | select(.resources.memory_percent > 80) | .name'
```

---### Implementation Guide

**Required functionality:**
1. mDNS query for `_koan-stone._tcp.local.`
2. Parse TXT records, filter by `offering` field
3. Resolve hostname to connection string
4. Cache result (5-minute TTL recommended)
5. Fallback to Lantern HTTP if mDNS fails

**Example (pseudocode):**
```
function resolve(zenGardenUri):
    parse serviceType from uri (zen-garden:mongodb → mongodb)
    
    try:
        stones = mdns_query("_koan-stone._tcp.local.")
        matching = filter(stones, txt["offering"] == serviceType)
        if matching.empty:
            throw ServiceNotFound
        
        stone = selectBestStone(matching) # Priority, health
        return buildConnectionString(stone, serviceType)
    
    catch MdnsTimeout:
        if LANTERN_URL set:
            return lanternResolve(serviceType)
        else:
            throw DiscoveryFailed
```

---

## Error Handling

### Discovery Failures

**mDNS timeout (1 second):**
```
Cause: No Stone responding, mDNS daemon not running, firewall blocking UDP 5353
Action: Fallback to Lantern, or throw ServiceDiscoveryException
```

**Service not found:**
```
Cause: No Stone announces requested service type
Action: Throw ServiceNotFoundException with available services list
```

**Multiple Stones (ambiguous):**
```
Cause: Multiple Stones offer same service without priority differentiation
Action: Select first discovered (deterministic but unpredictable)
Future: Use TXT "priority" field for selection
```

### Connection Failures

**Connection refused (post-discovery):**
```
Cause: Service crashed, firewall blocking port, Stone rebooting
Action: Retry with exponential backoff, log error, alert monitoring
```

**Authentication failure:**
```
Cause: Invalid credentials, TXT "auth=true" but app lacks credentials
Action: Throw authentication exception, prompt for credentials
```

---

## Performance Characteristics

### Discovery Latency

**mDNS (typical):**
- First query: 50-150ms
- Cached: <1ms
- Timeout: 1000ms

**Lantern HTTP:**
- Query: 5-20ms
- Cached: <1ms

### Announcement Interval

**Stones re-announce every 30 seconds** (configurable)

**Rationale:** Balance between network traffic and rapid Stone failure detection.

### Cache Duration

**Recommended: 5 minutes**

**Rationale:** Handles DHCP IP reassignment (typical lease: 1-24 hours) while minimizing stale connections.

---

## Security Considerations

### Baseline (No Pond)

**Threats:**
- Rogue Stone announcement (malicious device)
- Network sniffing (plaintext credentials)
- Man-in-the-middle attacks

**Mitigations:**
- Physical network security (WPA2/WPA3 WiFi)
- VLANs for isolation
- Firewall rules (restrict port access)

### With Pond (mTLS)

**Protections:**
- Certificate-based authentication
- Encrypted connections (TLS 1.3)
- Certificate pinning (prevents MITM)

**TXT record addition:**
```
fingerprint=sha256:abc123def456...
```

Apps validate certificate fingerprint before connecting.

See [SECURITY.md](SECURITY.md) for full threat model.

---

## Future Protocol Enhancements

**Roadmap items (not current implementation):**
- Service health metadata (CPU/memory/load in TXT records)
- Load balancing (priority + health-aware selection)
- Service dependencies (TXT "requires=mongodb,redis")
- IPv6 support (AAAA records)
- Plugin architecture (community service types)

**Status:** Tracked in [ROADMAP.md](ROADMAP.md)

---

## Further Reading

- [Understanding](UNDERSTANDING.md) - How discovery works
- [Getting Started](GETTING-STARTED.md) - Quick setup guide
- [Security](SECURITY.md) - Pond security model
- [Roadmap](ROADMAP.md) - Implementation timeline

---

## Protocol Specifications (Coming Soon)

Formal specifications in development:

- **ZGP-001**: Core mDNS protocol
- **ZGP-002**: Connection string resolution
- **ZGP-003**: Lantern HTTP API
- **ZGP-004**: Pond security (mTLS)
- **ZGP-005**: Conformance tests

Target: Q1 2026 (enables multiple implementations)
