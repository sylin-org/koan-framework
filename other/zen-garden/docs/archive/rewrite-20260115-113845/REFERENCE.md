# Zen Garden: Technical Reference

**Complete API and tooling documentation for developers integrating with Zen Garden.**

---

## Table of Contents

1. [Connection String Protocol](#connection-string-protocol)
2. [garden-rake Deployment Tool](#garden-rake-deployment-tool)
3. [mDNS Service Announcement](#mdns-service-announcement)
4. [Lantern API](#lantern-api)
5. [Stone Manifest System](#stone-manifest-system)
6. [Performance Characteristics](#performance-characteristics)
7. [Error Handling](#error-handling)
8. [Integration Patterns](#integration-patterns)

---

## Connection String Protocol

### Overview

Zen Garden connection strings enable automatic service discovery:

```
zen-garden:<service-type>[/<database>]
```

**Examples:**
```bash
zen-garden:mongodb          # Discover any MongoDB Stone
zen-garden:mongodb/mydb     # Discover MongoDB + specify database
zen-garden:redis            # Discover Redis Stone
zen-garden:postgres/app     # Discover PostgreSQL + specify database
```

### Resolution Process

**Without Lantern (Peer-to-Peer):**
1. App broadcasts mDNS query: "Who offers mongodb?"
2. Stones respond with location
3. App connects to first responder
4. Latency: 50-100ms

**With Lantern (Directory):**
1. App queries Lantern HTTP API: `GET /api/resolve?service=mongodb`
2. Lantern returns cached location
3. App connects to Stone
4. Latency: 5-10ms

**Fallback to Manual:**
```bash
# If discovery fails, use traditional connection string
MONGODB_URI=mongodb://192.168.1.50:27017/mydb
```

### Language Integration

**C# (Koan):**
```csharp
// Automatic resolution in appsettings.json
"ConnectionString": "zen-garden:mongodb/mydb"

// Injected as standard IMongoClient
public class TodoService(IMongoClient mongo) { }
```

**Node.js:**
```javascript
const MongoClient = require('mongodb').MongoClient;
const resolver = require('@zen-garden/resolver');

const uri = await resolver.resolve(process.env.MONGODB_URI);
// zen-garden:mongodb → mongodb://stone-01:27017
const client = await MongoClient.connect(uri);
```

**Python:**
```python
from pymongo import MongoClient
from zen_garden import resolve

uri = resolve(os.getenv('MONGODB_URI'))
# zen-garden:mongodb → mongodb://stone-01:27017
client = MongoClient(uri)
```

**Any Language (Generic HTTP):**
```bash
curl http://lantern.local/api/resolve?service=mongodb
# {"uri": "mongodb://stone-01:27017", "stone": "stone-01", "healthy": true}
```

---

## garden-rake Deployment Tool

### Overview

`garden-rake` pushes containers to compute Stones automatically—no Kubernetes, no YAML hell.

### Installation

```bash
# Install garden-rake CLI
curl -sSL https://get.zen-garden.dev/rake | bash

# Verify
garden-rake --version
```

### Basic Usage

**Deploy an app:**
```bash
garden-rake push myapp --image myapp:latest

# Output:
# [rake] discovering compute stones...
# [rake] found: compute-stone-01 (4 cores, 8GB RAM, 50GB free)
# [rake] pulling image: myapp:latest
# [rake] starting container: myapp-a7f3
# [rake] app live at: http://myapp.garden/
```

**What it does:**
1. Discovers compute Stones via `zen-garden:docker`
2. Selects Stone with available resources
3. Pulls container image
4. Starts container with auto-restart policy
5. Configures mDNS hostname: `myapp.garden`

### Complete Workflow Example

```bash
# Terminal 1: Lantern running
[lantern] stone joined: db-stone-01 (mongodb)
[lantern] stone joined: storage-stone-01 (minio)
[lantern] stone joined: compute-stone-01 (docker)

# Terminal 2: Deploy app with environment
garden-rake push webapp \
  --image webapp:latest \
  --env APP_DB=zen-garden:mongodb \
  --env APP_FILES=zen-garden:storage \
  --replicas 2

# Output:
# [rake] resolved: zen-garden:mongodb → mongodb://db-stone-01:27017
# [rake] resolved: zen-garden:storage → http://storage-stone-01:9000
# [rake] deploying to: compute-stone-01 (replica 1)
# [rake] deploying to: compute-stone-02 (replica 2)
# [rake] containers started: webapp-a7f3, webapp-b2e9
# [rake] load balanced at: http://webapp.garden/
```

### Command Reference

**Push app:**
```bash
garden-rake push <name> --image <image> [options]

Options:
  --image <image>       Container image (required)
  --env <KEY=VALUE>     Environment variable (repeatable)
  --port <port>         Expose port (default: auto-detect)
  --replicas <N>        Number of instances (default: 1)
  --stone <name>        Target specific Stone (default: auto-select)
  --restart <policy>    Restart policy (default: unless-stopped)
```

**List deployments:**
```bash
garden-rake list

# Output:
# webapp         compute-stone-01   running   2 replicas
# api-server     compute-stone-02   running   1 replica
# worker         compute-stone-01   running   1 replica
```

**Scale app:**
```bash
garden-rake scale webapp --replicas 4
# [rake] scaling webapp: 2 → 4 replicas
# [rake] deployed to: compute-stone-01, compute-stone-02
```

**Remove app:**
```bash
garden-rake remove webapp
# [rake] stopping containers: webapp-a7f3, webapp-b2e9
# [rake] removed: webapp
```

**View logs:**
```bash
garden-rake logs webapp
# [compute-stone-01] webapp-a7f3: App listening on :8080
# [compute-stone-02] webapp-b2e9: App listening on :8080
```

### Resource Selection

`garden-rake` automatically selects Stones based on available resources:

**Selection criteria:**
1. CPU availability (prefer Stones with free cores)
2. RAM availability (prefer Stones with free memory)
3. Disk space (require 2x image size free)
4. Current load (avoid overloaded Stones)

**Example:**
```bash
garden-rake push heavy-app --image heavy:latest

# [rake] resource requirements: 4 cores, 8GB RAM, 10GB disk
# [rake] scanning compute stones...
# [rake] compute-stone-01: 2 cores available (insufficient)
# [rake] compute-stone-02: 8 cores available, 16GB RAM free ✓
# [rake] deploying to: compute-stone-02
```

### Environment Variable Resolution

Apps deployed via `garden-rake` automatically resolve `zen-garden:*` URIs:

```bash
garden-rake push myapp \
  --image myapp:latest \
  --env DB=zen-garden:mongodb \
  --env CACHE=zen-garden:redis \
  --env QUEUE=zen-garden:rabbitmq

# Resolved at runtime to:
# DB=mongodb://db-stone-01:27017
# CACHE=redis://cache-stone-01:6379
# QUEUE=amqp://queue-stone-01:5672
```

### Stone Management Commands

**Offer service on Stone:**
```bash
garden-rake offer mongodb --stone db-stone-01

# Installs and announces MongoDB on specified Stone
```

**List available services:**
```bash
garden-rake catalog

# Available offerings:
#   mongodb, postgresql, redis, rabbitmq, minio, etc.
```

**Check Stone status:**
```bash
garden-rake status --stone compute-stone-01

# Stone: compute-stone-01
# Status: healthy
# CPU: 4 cores (2 used, 2 free)
# RAM: 16GB (8GB used, 8GB free)
# Disk: 500GB (100GB used, 400GB free)
# Running apps: webapp (2 replicas), worker (1 replica)
```

---
    if (stone == null)
        throw new ServiceDiscoveryException($"No Stone offering {serviceType}");
    
    // 4. Generate connection string
    var connectionString = $"{serviceType}://{stone.Host}:{stone.Port}";
    if (database != null)
        connectionString += $"/{database}";
    
    return connectionString;
}
```

---

### Discovery Timeout

**Default**: 1 second (configurable via `GardenOptions.DiscoveryTimeout`)

```csharp
services.Configure<GardenOptions>(options =>
{
    options.DiscoveryTimeout = TimeSpan.FromSeconds(2); // Increase for slow networks
    options.EnableAutoDiscovery = true; // Disable for manual control
});
```

**Rationale**: Fast failure enables rapid feedback during development. Production environments typically have stable mDNS announcement (discovery succeeds <100ms).

---

### Connection String Caching

**Behavior**: Discovered connection strings cached for 5 minutes (default).

```csharp
// Cache key: serviceType (e.g., "mongodb")
// Cache value: { Host: "192.168.1.100", Port: 27017, DiscoveredAt: DateTime.UtcNow }
// TTL: 5 minutes (handles IP changes from DHCP lease renewals)
```

**Configuration**:
```csharp
services.Configure<GardenOptions>(options =>
{
    options.CacheDuration = TimeSpan.FromMinutes(10); // Increase for stable networks
    options.EnableCaching = true; // Disable for testing
});
```

---

## mDNS Service Announcement

### Protocol Specification

Zen-garden uses standard mDNS (RFC 6762) with service-specific TXT records.

### Service Type Format

```
_<service-name>._tcp.local.
```

**Examples**:
- MongoDB: `_mongodb._tcp.local.`
- PostgreSQL: `_postgresql._tcp.local.`
- Redis: `_redis._tcp.local.`

---

### Announcement Structure

```
# DNS Service Discovery (PTR record)
_services._dns-sd._udp.local. PTR _mongodb._tcp.local.

# Service Instance (SRV + A records)
mongodb-stone-01._mongodb._tcp.local. SRV 0 0 27017 stone-01.local.
stone-01.local. A 192.168.1.100

# Service Metadata (TXT record)
mongodb-stone-01._mongodb._tcp.local. TXT "version=6.0.3" "replicaSet=false" "auth=true"
```

---

### TXT Record Schema

**Standard Fields** (all services):
- `version`: Service version (e.g., `6.0.3`)
- `offering`: Service type (e.g., `mongodb`, `postgresql`)
- `auth`: Authentication required (`true` | `false`)
- `secure`: TLS/SSL enabled (`true` | `false`)

**Service-Specific Fields**:

#### MongoDB
```
TXT "version=6.0.3" "replicaSet=false" "auth=true" "databases=app1,app2,admin"
```

#### PostgreSQL
```
TXT "version=15.2" "databases=postgres,app1" "auth=true" "ssl=require"
```

#### Redis
```
TXT "version=7.0.8" "auth=true" "maxmemory=2gb" "eviction=allkeys-lru"
```

---

### Garden-Rake Announcement (Rust)

```rust
use mdns_sd::{ServiceDaemon, ServiceInfo};

pub fn announce_mongodb(port: u16, databases: Vec<String>) -> Result<()> {
    let mdns = ServiceDaemon::new()?;
    
    let service_type = "_mongodb._tcp.local.";
    let instance_name = format!("mongodb-{}", hostname());
    
    let txt_records = vec![
        format!("version={}", get_mongodb_version()?),
        format!("databases={}", databases.join(",")),
        "auth=true".to_string(),
        "replicaSet=false".to_string(),
    ];
    
    let service = ServiceInfo::new(
        service_type,
        &instance_name,
        &hostname(),
        (), // IPv4/IPv6 addresses (auto-detected)
        port,
        &txt_records,
    )?;
    
    mdns.register(service)?;
    Ok(())
}
```

---

### Discovery Client (C#)

```csharp
using Makaretu.Dns;

public class MdnsDiscoveryClient
{
    private readonly MulticastService _mdns = new();
    private readonly ServiceDiscovery _sd;
    
    public MdnsDiscoveryClient()
    {
        _sd = new ServiceDiscovery(_mdns);
    }
    
    public async Task<StoneInfo?> DiscoverService(
        string serviceType,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<StoneInfo?>();
        
        _sd.ServiceInstanceDiscovered += (s, e) =>
        {
            if (e.ServiceInstanceName.Contains(serviceType))
            {
                var stone = new StoneInfo
                {
                    Host = e.Hostname,
                    Port = e.Port,
                    Metadata = ParseTxtRecords(e.Message)
                };
                tcs.TrySetResult(stone);
            }
        };
        
        _sd.QueryServiceInstances($"_{serviceType}._tcp");
        
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        
        try
        {
            return await tcs.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            return null; // Timeout or cancellation
        }
    }
}
```

---

## Partition Key Emission

### Overview

The Zen-garden adapter's core responsibility is **translating entity operations into partition keys** for the generic record API.

### Partition Key Format

```
<app-id>:<db-id>:<model-name>[:<set>]
```

**Components**:
- `<app-id>`: Application identifier (from configuration)
- `<db-id>`: Database identifier (from connection string or default)
- `<model-name>`: Entity type name (lowercase)
- `<set>`: Optional set discriminator (e.g., `backup`, `archive`)

---

### Examples

#### Basic Entity Save
```csharp
// Entity code
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

await new Todo { Title = "Buy milk" }.Save();

// Adapter emits
// POST /api/record?set=app1:db1:todo
// Body: { "Id": "abc123", "JEntity": { "Title": "Buy milk" } }
```

---

#### Set Discriminator
```csharp
// Entity code with set
await new Todo { Title = "Backup item" }.Save(set: "backup");

// Adapter emits
// POST /api/record?set=app1:db1:todo:backup
```

---

#### Query Operation
```csharp
// Entity code
var todos = await Todo.All();

// Adapter emits
// GET /api/record?set=app1:db1:todo
// Response: [{ "Id": "abc123", "JEntity": { "Title": "Buy milk" } }]
```

---

### Adapter Implementation

```csharp
public class ZenGardenAdapter : IDataAdapter
{
    private readonly HttpClient _httpClient;
    private readonly string _appId;
    private readonly string _dbId;
    
    public ZenGardenAdapter(
        HttpClient httpClient,
        IOptions<GardenOptions> options)
    {
        _httpClient = httpClient;
        _appId = options.Value.AppId ?? "default";
        _dbId = options.Value.DbId ?? "default";
    }
    
    public async Task Save<T>(T entity, string? set = null) where T : IEntity
    {
        var partitionKey = BuildPartitionKey<T>(set);
        var record = new StoneRecord
        {
            Id = entity.Id,
            JEntity = JObject.FromObject(entity)
        };
        
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/record?set={partitionKey}",
            record);
        
        response.EnsureSuccessStatusCode();
    }
    
    public async Task<T[]> Query<T>(string? set = null) where T : IEntity
    {
        var partitionKey = BuildPartitionKey<T>(set);
        var records = await _httpClient.GetFromJsonAsync<StoneRecord[]>(
            $"/api/record?set={partitionKey}");
        
        return records
            .Select(r => r.JEntity.ToObject<T>())
            .ToArray();
    }
    
    private string BuildPartitionKey<T>(string? set = null)
    {
        var modelName = typeof(T).Name.ToLowerInvariant();
        return set == null
            ? $"{_appId}:{_dbId}:{modelName}"
            : $"{_appId}:{_dbId}:{modelName}:{set}";
    }
}
```

---

### Configuration

```csharp
// appsettings.json
{
  "Koan": {
    "Data": {
      "ConnectionString": "zen-garden:mongodb/mydb",
      "Garden": {
        "AppId": "app1",      // Default: "default"
        "DbId": "mydb",       // Extracted from connection string
        "DiscoveryTimeout": "00:00:02",
        "CacheDuration": "00:05:00"
      }
    }
  }
}

// Startup configuration
services.AddKoanData(options =>
{
    options.ConnectionString = Configuration["Koan:Data:ConnectionString"];
    options.UseZenGarden(garden =>
    {
        garden.AppId = "app1";
        garden.DbId = "mydb";
    });
});
```

---

## Adapter API Reference

### Core Interfaces

#### IDataAdapter

```csharp
public interface IDataAdapter
{
    Task Save<T>(T entity, string? set = null) where T : IEntity;
    Task<T?> Get<T>(string id, string? set = null) where T : IEntity;
    Task<T[]> Query<T>(string? set = null) where T : IEntity;
    Task Delete<T>(string id, string? set = null) where T : IEntity;
}
```

---

#### IEntity

```csharp
public interface IEntity
{
    string Id { get; set; }
}

public abstract class Entity<T> : IEntity where T : Entity<T>
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    public async Task Save(string? set = null)
    {
        var adapter = ServiceLocator.GetService<IDataAdapter>();
        await adapter.Save((T)this, set);
    }
    
    public static async Task<T[]> All(string? set = null)
    {
        var adapter = ServiceLocator.GetService<IDataAdapter>();
        return await adapter.Query<T>(set);
    }
    
    public static async Task<T?> Get(string id, string? set = null)
    {
        var adapter = ServiceLocator.GetService<IDataAdapter>();
        return await adapter.Get<T>(id, set);
    }
}
```

---

### Stone Record API

#### Endpoints

```
POST   /api/record?set={partitionKey}     # Save record
GET    /api/record?set={partitionKey}     # Query records
GET    /api/record/{id}?set={partitionKey} # Get by ID
DELETE /api/record/{id}?set={partitionKey} # Delete record
```

---

#### Request/Response Schemas

**StoneRecord**:
```csharp
public class StoneRecord
{
    public string Id { get; set; } = "";
    public JObject JEntity { get; set; } = new();
}
```

**Save Request**:
```http
POST /api/record?set=app1:db1:todo HTTP/1.1
Content-Type: application/json

{
  "Id": "abc123",
  "JEntity": {
    "Title": "Buy milk",
    "Completed": false
  }
}
```

**Save Response**:
```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "Id": "abc123",
  "Success": true
}
```

---

**Query Request**:
```http
GET /api/record?set=app1:db1:todo HTTP/1.1
```

**Query Response**:
```http
HTTP/1.1 200 OK
Content-Type: application/json

[
  {
    "Id": "abc123",
    "JEntity": {
      "Title": "Buy milk",
      "Completed": false
    }
  },
  {
    "Id": "def456",
    "JEntity": {
      "Title": "Walk dog",
      "Completed": true
    }
  }
]
```

---

## Stone-to-Pond Binding Protocol

### Overview

Stone-to-Pond binding (Phase 5 feature) enables cryptographic binding of Stones to Secure Ponds. This section documents the binding protocol for future implementation.

**Note**: This feature is not part of Hello World (Phase 1) or Service Directory (Phase 2). Documented here for completeness.

---

### Binding Workflow

```
1. Virgin Stone boots → LED: Solid Blue
2. User connects Stone to Secure Pond network
3. Stone queries mDNS for _pond._tcp.local.
4. Stone finds Lantern → initiates binding handshake
5. Lantern displays: "New Stone detected: Bind to Secure Pond? [Yes] [No]"
6. User confirms → LED: Amber Pulse (binding in progress)
7. Cryptographic handshake:
   a. Stone generates Ed25519 keypair
   b. Lantern generates pond master key (if first binding)
   c. Stone sends public key → Lantern
   d. Lantern signs Stone public key with pond master key
   e. Lantern sends signed pebble → Stone
8. Stone stores pebble in encrypted partition
9. Binding complete → LED: Pulsing Green
```

---

### Cryptographic Primitives

**Key Generation**:
- Stone Keypair: Ed25519 (signing)
- Pond Master Key: Ed25519 (signing)
- Pebble Encryption: AES-256-GCM with HKDF-SHA256 key derivation

**Key Derivation**:
```
Input: HMAC-SHA256(CPU_SERIAL || MAC_ADDRESS || "koan-pebble-v1")
Salt: 32 random bytes (generated on first boot)
Info: "zen-garden-pebble-encryption-key"
Output: AES-256 key (32 bytes)
```

---

### Pebble Structure

```csharp
public class Pebble
{
    public string PondId { get; set; }              // UUID
    public byte[] PondMasterPublicKey { get; set; } // Ed25519 (32 bytes)
    public byte[] StonePrivateKey { get; set; }     // Ed25519 (64 bytes)
    public DateTime BindingTimestamp { get; set; }  // ISO 8601
    public ulong KeyRotationCounter { get; set; }   // Increments on key rotation
}
```

**Encrypted Pebble Storage**:
```
┌─ Pebble Partition (4MB) ─────────────────────┐
│ IV (12 bytes) + Ciphertext + Auth Tag (16 bytes) │
│ Encrypted with AES-256-GCM                    │
│ Key derived from CPU_SERIAL + MAC_ADDRESS     │
└───────────────────────────────────────────────┘
```

---

### Binding Verification

**On Stone Boot**:
1. Stone reads encrypted pebble
2. Stone queries mDNS for `_pond._tcp.local.`
3. Stone verifies discovered Pond ID matches pebble Pond ID
4. If match → LED: Pulsing Green (normal operation)
5. If mismatch → LED: Red Blink (bound to different pond)

**Mutual Authentication** (future):
```
1. Stone proves identity: Signs challenge with Stone private key
2. Lantern verifies: Checks signature against stored Stone public key
3. Lantern proves identity: Signs response with pond master key
4. Stone verifies: Checks signature against pebble pond master public key
```

---

### Factory Reset

**Soft Reset** (Data Wipe):
```bash
$ garden-wipe data
⚠️  WARNING: This will delete all data. Pebble preserved (Stone stays bound).
Press physical button for 10 seconds to confirm.

[User holds button 10-19 seconds]
✓ Data partition wiped
✓ Pebble preserved
✓ Stone remains bound to pond: home-pond-01
```

**Hard Reset** (Factory Reset):
```bash
$ garden-wipe factory-reset
⚠️  WARNING: This will UNBIND Stone and delete all data.
Press physical button for 20 seconds to confirm.

[User holds button 20+ seconds]
✓ Data partition wiped
✓ Pebble destroyed
✓ Tombstone backup created (7-day recovery)
✓ Stone is now virgin (solid blue LED)
```

---

## Error Handling

### Discovery Errors

#### ServiceDiscoveryException
```csharp
try
{
    var connectionString = await garden.ResolveConnectionString(
        "zen-garden:mongodb/mydb",
        ct);
}
catch (ServiceDiscoveryException ex)
{
    // No Stone offering 'mongodb' found within timeout
    Console.WriteLine($"Discovery failed: {ex.Message}");
    
    // Fallback: Use manual connection string
    connectionString = "mongodb://192.168.1.100:27017/mydb";
}
```

---

#### ConnectionTimeoutException
```csharp
try
{
    var stone = await garden.DiscoverStone("mongodb", ct);
}
catch (ConnectionTimeoutException ex)
{
    // mDNS query timed out (no response within DiscoveryTimeout)
    Console.WriteLine($"Timeout: {ex.Message}");
    
    // Increase timeout or check network configuration
}
```

---

### Network Errors

#### Common Issues

**Issue 1: Docker Networking**
```
Error: No Stone discovered (mdns query timeout)
Cause: Docker default bridge mode doesn't forward mDNS multicast
Solution: Use host networking or macvlan
```

```yaml
# docker-compose.yml
services:
  app:
    network_mode: host  # Enable mDNS discovery
```

---

**Issue 2: VLAN Isolation**
```
Error: No Stone discovered
Cause: mDNS multicast blocked by VLAN configuration
Solution: Enable mDNS reflection on router or use centralized service directory (Phase 2)
```

---

**Issue 3: Firewall**
```
Error: Connection refused
Cause: Firewall blocking mDNS (UDP 5353)
Solution: Allow mDNS multicast traffic
```

```bash
# Linux (ufw)
sudo ufw allow 5353/udp

# Windows Firewall
New-NetFirewallRule -DisplayName "mDNS" -Direction Inbound -Protocol UDP -LocalPort 5353 -Action Allow
```

---

### Diagnostic Tools

#### Garden Diagnostic Dump
```bash
$ garden-diagnostic dump

Zen Garden Diagnostic Report
============================
Version: 0.1.0-alpha
Date: 2026-01-14T10:30:00Z

Network Configuration:
  Interface: eth0
  IP: 192.168.1.150
  Subnet: 192.168.1.0/24
  Gateway: 192.168.1.1

mDNS Status:
  Enabled: true
  Listening: 0.0.0.0:5353
  Multicast Group: 224.0.0.251

Service Discovery:
  MongoDB: ✓ Found (192.168.1.100:27017)
  PostgreSQL: ✗ Not found
  Redis: ✗ Not found

Troubleshooting:
  - Docker network mode: bridge (⚠️ Use 'host' mode for mDNS)
  - Firewall: ✓ Port 5353/udp open
  - Stones announced: 1/3 expected
```

---

## Performance Characteristics

### Discovery Latency

**Typical Performance** (LAN):
- mDNS query: 50-150ms
- Connection establishment: 10-50ms
- Total discovery latency: 60-200ms

**Worst Case** (timeout):
- Discovery timeout: 1,000ms (configurable)

---

### Connection Pooling

**Recommendation**: Use standard database connection pools (MongoDB driver, Npgsql).

```csharp
// MongoDB connection pooling (automatic)
services.AddSingleton<IMongoClient>(sp =>
{
    var garden = sp.GetRequiredService<IGardenClient>();
    var connectionString = await garden.ResolveConnectionString(
        "zen-garden:mongodb/mydb");
    
    return new MongoClient(new MongoClientSettings
    {
        ConnectionString = connectionString,
        MaxConnectionPoolSize = 100,
        MinConnectionPoolSize = 10
    });
});
```

---

### Cache Performance

**Connection String Cache**:
- TTL: 5 minutes (default)
- Cache hit: <1ms
- Cache miss: 60-200ms (mDNS query)

**Recommendation**: In production, use service directory (Phase 2) for centralized caching with TTL refresh.

---

## Integration Patterns

### Pattern 1: Transparent Discovery (Recommended)

```csharp
// appsettings.json
{
  "Koan": {
    "Data": {
      "ConnectionString": "zen-garden:mongodb/mydb"
    }
  }
}

// Startup.cs
services.AddKoanData(options =>
{
    options.ConnectionString = Configuration["Koan:Data:ConnectionString"];
});

// Entity code (zero changes)
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

await new Todo { Title = "Buy milk" }.Save();
var todos = await Todo.All();
```

---

### Pattern 2: Explicit Discovery

```csharp
// Manual control over discovery
var garden = serviceProvider.GetRequiredService<IGardenClient>();

var mongoConnectionString = await garden.ResolveConnectionString(
    "zen-garden:mongodb/mydb",
    ct);

var pgConnectionString = await garden.ResolveConnectionString(
    "zen-garden:postgresql/app1",
    ct);

// Use connection strings with standard drivers
var mongoClient = new MongoClient(mongoConnectionString);
var pgConnection = new NpgsqlConnection(pgConnectionString);
```

---

### Pattern 3: Fallback Chain

```csharp
public async Task<string> GetConnectionString(CancellationToken ct)
{
    try
    {
        // 1. Try discovery
        return await _garden.ResolveConnectionString(
            "zen-garden:mongodb/mydb",
            ct);
    }
    catch (ServiceDiscoveryException)
    {
        // 2. Fallback to environment variable
        var fallback = Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING");
        if (!string.IsNullOrEmpty(fallback))
            return fallback;
        
        // 3. Fallback to hardcoded localhost
        return "mongodb://localhost:27017/mydb";
    }
}
```

---

### Pattern 4: Multi-Service Discovery

```csharp
// Discover multiple services in parallel
var discoveryTasks = new[]
{
    _garden.ResolveConnectionString("zen-garden:mongodb/mydb", ct),
    _garden.ResolveConnectionString("zen-garden:redis", ct),
    _garden.ResolveConnectionString("zen-garden:postgresql/app1", ct)
};

var connectionStrings = await Task.WhenAll(discoveryTasks);

var (mongoCs, redisCs, pgCs) = (
    connectionStrings[0],
    connectionStrings[1],
    connectionStrings[2]
);
```

---

## Future Enhancements

### Phase 2: Service Directory
- Centralized service registry (Lantern stores service metadata)
- Cross-subnet discovery (no mDNS multicast required)
- Service health monitoring (Lantern pings Stones periodically)

### Phase 3: Multi-Service Support
- 20+ service types (PostgreSQL, Redis, Elasticsearch, RabbitMQ, MinIO)
- Standard service announcement protocol (published under CC BY 4.0)
- Third-party integration guide

### Phase 4: Web Dashboard
- Service health visualization (Lantern web UI)
- Stone management (binding status, LED states, factory reset)
- Troubleshooting wizard

### Phase 5: Stone-to-Pond Binding
- Secure Pond mode (cryptographic binding)
- Backup Stone tier (automated disaster recovery)
- Key rotation protocol (annual key refresh)

---

## Related Documents

- [README.md](./README.md) - Project overview
- [ROADMAP.md](./ROADMAP.md) - Development timeline
- [STRATEGY.md](./STRATEGY.md) - Business case
- [STONE-BINDING-SECURITY-EVALUATION.md](./STONE-BINDING-SECURITY-EVALUATION.md) - Security architecture
- [HARDWARE.md](./HARDWARE.md) - Reference designs *(pending)*

---

**Document Version**: 1.0  
**Last Reviewed**: January 14, 2026  
**Next Review**: After Phase 2 completion
# Limitations & Boundaries

**What Zen Garden does—and what it doesn't do.**

---

## Honest Scope

Zen Garden's first promise is **discovery**, not orchestration.

This is a deliberate boundary. Discovery is hard enough to get right, and trying to be a general-purpose orchestrator would compromise the core value: **plug-and-play infrastructure**.

---

## What Zen Garden Provides

✅ **Service discovery**: "Where is MongoDB?" → automatic answer  
✅ **Location abstraction**: Apps don't need hardcoded IPs  
✅ **Self-healing connections**: IP changes → automatic reconnection  
✅ **Visibility**: Lantern shows what's in your garden  
✅ **Opt-in security**: Start simple, harden when needed  

---

## What Zen Garden Does NOT Provide

❌ **Cluster orchestration**: Use service-native replication (MongoDB replica sets, Postgres replication)  
❌ **Automatic failover**: Services handle their own HA logic  
❌ **Data consistency**: Use database-native mechanisms (transactions, consensus)  
❌ **Container scheduling**: Use Docker Compose, systemd, or K8s if needed  
❌ **Cross-datacenter**: Designed for single-site deployments (LAN-first)  

---

## Known Tradeoffs

### 1. mDNS Across VLANs/Subnets

**Issue**: Service discovery is easiest on a flat LAN. Multi-subnet environments need repeaters/reflectors or a directory bridge.

**Mitigation**: 
- Deploy a Lantern as central directory (works across subnets)
- Use mDNS reflector (Avahi, reflector.py)
- Keep simple deployments on single subnet

**When This Matters**: Enterprise networks with complex segmentation.

---

### 2. Stale Caches

**Issue**: Discovery systems trade chatter for caching. If a stone disappears suddenly (power loss, network failure), clients might have stale location info.

**Mitigation**:
- Aggressive retry logic on connection failure
- Re-resolve on error (don't assume cached location is permanent)
- Health checks at application layer

**When This Matters**: High-availability production workloads.

---

### 3. Service Semantics

**Issue**: Zen Garden can discover endpoints, but databases and stateful systems still require deliberate replication models.

**Example**: Connecting to "a MongoDB" isn't the same as connecting to "the MongoDB primary in a replica set."

**Evolution Path**: Metadata can include capability flags (e.g., `replicaSet=true`, `role=primary`) so clients can choose intelligently.

**When This Matters**: Multi-node database clusters.

---

### 4. Observability

**Issue**: Dynamic systems are harder to reason about than static configs. "Where is MongoDB?" becomes "which MongoDB, and is it healthy?"

**Mitigation**:
- Lantern UI is not optional for production—it's how operators build trust
- Health check endpoints should be standard
- Logs/metrics should include service identity

**When This Matters**: Debugging outages at 2am.

---

## Where Resilience Comes From

**Resilience is a service-level concern, not a discovery-level concern.**

Zen Garden makes it easier to *compose* resilient systems by removing static wiring. Here's how:

### Example: MongoDB Replica Set

**Without Zen Garden:**
```bash
# Hardcoded connection string with all replicas
MONGODB_URI=mongodb://192.168.1.50:27017,192.168.1.51:27017,192.168.1.52:27017/?replicaSet=rs0
```

**With Zen Garden (Future Evolution):**
```bash
# Discover replica set members dynamically
MONGODB_URI=zen-garden:mongodb?replicaSet=rs0

# Behind the scenes:
# - Lantern tracks all MongoDB instances
# - Clients get connection string with all members
# - New replicas join → automatically discoverable
```

**Key Point**: MongoDB *still owns* replica set logic (elections, consensus, data consistency). Zen Garden just makes the members discoverable.

---

## Failure Modes to Prepare For

### Network Partition
**Symptom**: Stones can't reach lantern or each other.  
**Impact**: Discovery falls back to cached info or fails.  
**Preparation**: Test your app's behavior when discovery is unavailable.

### Stone Theft/Misplacement
**Symptom**: Stone announces on wrong network.  
**Impact**: Without Pond (security), stolen stones can join any garden.  
**Preparation**: Use Pond mode for sensitive deployments.

### IP Exhaustion (DHCP Pool Full)
**Symptom**: New stones can't get an IP.  
**Impact**: Can't join network, can't announce.  
**Preparation**: Monitor DHCP pool, use static IPs for critical stones.

### DNS/mDNS Conflicts
**Symptom**: Name collisions (two stones claim same service name).  
**Impact**: Clients might connect to wrong instance.  
**Preparation**: Enforce unique service identities in Lantern.

---

## When NOT to Use Zen Garden

❌ **Multi-datacenter deployments**: Designed for single-site (LAN)  
❌ **Sub-millisecond latency requirements**: Discovery adds overhead  
❌ **Regulatory environments requiring static configs**: Auditors want hardcoded IPs  
❌ **When you need Kubernetes**: If you're already there, stay there  

---

## When to Use Zen Garden

✅ **Homelab/personal projects**: Eliminate IP management  
✅ **Small business apps**: Own your data, reduce cloud costs  
✅ **Edge deployments**: Local-first infrastructure  
✅ **Dev/test environments**: Rapid provisioning, zero config  
✅ **Privacy-sensitive workloads**: Physical ownership required  

---

## Keeping Scope Honest

Zen Garden doesn't pretend to replace databases' correctness models or Kubernetes' orchestration. It's a **composable layer** that makes small infrastructure behave coherently.

If that sounds limiting, it's meant to. The best infrastructure is the simplest infrastructure that solves your problem—and for the "other 90%" of deployments, that's not a control plane. It's just plugging things in and having them work.

---

**Next**: [Security Approach](../security/approach.md) | [Architecture Overview](../architecture/overview.md)
# Zen Garden: Manifest-Based Offering System

**Status**: In Development (Week 1)  
**Architecture Decision**: [DATA-0088](../../docs/decisions/DATA-0088-adapter-auto-configuration-resolver-pipeline.md)

---

## Overview

Zen Garden uses a **manifest-based system** to define and deploy services on stones. Each service (MongoDB, PostgreSQL, Redis, etc.) is described in a YAML manifest that specifies image, ports, volumes, and healthchecks.

The `garden-rake` CLI tool reads these manifests and orchestrates Docker Compose deployments, eliminating manual configuration.

---

## Commands

### `garden-rake catalog`

Lists all available services and templates.

```bash
$ garden-rake catalog

Available offerings:
  [data]
    mongodb       - Official MongoDB 7.x document database
    postgresql    - PostgreSQL 16 with pgvector
    sqlserver     - SQL Server 2022 Express
    redis         - Redis Stack (JSON, Search)
    couchbase     - Couchbase Server Community
    elasticsearch - Elasticsearch 8.x
    opensearch    - OpenSearch 2.x
  
  [vector]
    weaviate      - Weaviate vector database
    milvus        - Milvus vector database (standalone)
  
  [cache]
    redis         - Redis 7.x in-memory cache
  
  [messaging]
    rabbitmq      - RabbitMQ 3.x with management UI
  
  [ai]
    ollama        - Ollama local LLM runtime
  
  [secrets]
    vault         - HashiCorp Vault (dev mode)
  
  [observability]
    aspire        - .NET Aspire Dashboard

Templates:
  database      - MongoDB + PostgreSQL + SQL Server
  cache         - Redis
  messaging     - RabbitMQ
  search        - Elasticsearch + OpenSearch
  vector        - Weaviate + Milvus
  ai            - Ollama
  secrets       - Vault
  observability - Aspire Dashboard
  fullstack     - PostgreSQL + Redis + RabbitMQ + Ollama
```

---

### `garden-rake offer <service>`

Adds a service to the stone's running stack by modifying `/etc/garden/docker-compose.yml`.

```bash
$ garden-rake offer mongodb

🌿 Offering service: mongodb
   Reading manifest: /etc/garden/manifests/data/mongodb.yml
   Adding to compose: /etc/garden/docker-compose.yml
   Pulling image: mongo:7
   Starting service: docker compose up -d mongodb
   Waiting for health check...
✓ Stone now offers: mongodb (mongodb://stone.local:27017)
```

**How it works**:
1. Reads manifest from `/etc/garden/manifests/<category>/<service>.yml`
2. Converts manifest to Docker Compose service definition
3. Merges into existing `/etc/garden/docker-compose.yml`
4. Runs `docker compose up -d <service>` to start only the new service
5. Waits for health check to pass
6. Updates mDNS TXT record with new offering

**Multiple services**:
```bash
$ garden-rake offer postgresql redis rabbitmq
✓ Added 3 services to compose stack
```

---

### `garden-rake remove <service>`

Removes a service from the running stack.

```bash
$ garden-rake remove mongodb

🌿 Removing service: mongodb
   Stopping container: docker compose rm -s mongodb
   Removing from compose: /etc/garden/docker-compose.yml
   Preserving volumes: mongo-data
✓ Service removed: mongodb

$ garden-rake remove mongodb --volumes
✓ Service and volumes removed: mongodb (mongo-data)
```

---

### `garden-rake offer --template <name>`

Deploys a predefined service bundle.

```bash
$ garden-rake offer --template fullstack

🌿 Offering template: fullstack (PostgreSQL + Redis + RabbitMQ + Ollama)
   [1/4] postgresql: ✓
   [2/4] redis: ✓
   [3/4] rabbitmq: ✓
   [4/4] ollama: ✓
✓ Stone now offers: postgresql, redis, rabbitmq, ollama
```

---

### `garden-rake status`

Shows running services on the stone.

```bash
$ garden-rake status

Active offerings:
  mongodb       - Running (port 27017, healthy)
  redis         - Running (port 6379, healthy)
  ollama        - Running (port 11434, healthy)
```

---

### `garden-rake announce <service> --port <port>`

Announces a service via mDNS (Week 2 integration).

```bash
$ garden-rake announce mongodb --port 27017

🌸 Announcing: mongodb (port 27017)
   Service: _koan-stone._tcp.local.
   Press Ctrl+C to stop
```

---

## Manifest Schema

Each service manifest is a YAML file with the following structure:

```yaml
name: mongodb                          # Service identifier
description: Official MongoDB 7.x      # Human-readable description
category: data                         # Category (data, vector, cache, etc.)
image: mongo:7                         # Docker image reference
port: 27017                            # Primary port
offering: mongodb                      # mDNS offering identifier

environment:                           # Environment variables
  MONGO_INITDB_ROOT_USERNAME: admin
  MONGO_INITDB_ROOT_PASSWORD: ${MONGO_PASSWORD:-changeme}

volumes:                               # Volume mappings
  - mongo-data:/data/db
  - mongo-config:/data/configdb

healthcheck:                           # Docker healthcheck
  test: ["CMD", "mongosh", "--eval", "db.adminCommand('ping')"]
  interval: 10s
  timeout: 5s
  retries: 5
  start_period: 10s

restart: unless-stopped                # Restart policy

labels:                                # Metadata
  zen-garden.service: mongodb
  zen-garden.category: data
```

---

## Directory Structure

```
manifests/
  data/
    mongodb.yml
    postgresql.yml
    sqlserver.yml
    redis.yml
    couchbase.yml
    elasticsearch.yml
    opensearch.yml
  vector/
    weaviate.yml
    milvus.yml
  cache/
    redis.yml
  messaging/
    rabbitmq.yml
  ai/
    ollama.yml
  secrets/
    vault.yml
  observability/
    aspire.yml
  templates/
    database.yml
    cache.yml
    messaging.yml
    search.yml
    vector.yml
    ai.yml
    secrets.yml
    observability.yml
    fullstack.yml
```

---

## Environment Variables

All manifests use `${VARIABLE:-default}` for environment variable substitution:

```bash
# Override defaults
export MONGO_PASSWORD=secure123
export POSTGRES_PASSWORD=secure456

garden-rake offer mongodb postgresql
```

**Common variables**:
- `MONGO_PASSWORD` - MongoDB root password
- `POSTGRES_PASSWORD` - PostgreSQL password
- `POSTGRES_USER` - PostgreSQL username
- `POSTGRES_DB` - PostgreSQL database name
- `SA_PASSWORD` - SQL Server SA password
- `REDIS_PASSWORD` - Redis password
- `RABBITMQ_USER`, `RABBITMQ_PASSWORD` - RabbitMQ credentials
- `VAULT_TOKEN` - Vault root token (dev mode)

---

## Custom Manifests

Users can provide custom manifests via file path:

```bash
$ garden-rake offer --manifest ./my-service.yml

🌿 Offering custom service: my-service
   Loaded manifest: ./my-service.yml
   ✓ Service started
```

**Custom manifest example**:

```yaml
name: timescaledb
description: TimescaleDB time-series database
category: data
image: timescale/timescaledb:latest-pg16
port: 5432
offering: timescaledb

environment:
  POSTGRES_PASSWORD: ${TIMESCALE_PASSWORD:-changeme}

volumes:
  - timescale-data:/var/lib/postgresql/data

healthcheck:
  test: ["CMD-SHELL", "pg_isready -U postgres"]
  interval: 10s

restart: unless-stopped
```

---

## Integration with Koan Adapters

Services deployed by `garden-rake offer` work seamlessly with Koan's protocol-based resolver:

```csharp
// Application code
options.UseMongoDb("zen-garden:mongodb");
options.UsePostgreSQL("zen-garden:postgresql");
options.UseRedis("zen-garden:redis");
```

See [DATA-0088](../../docs/decisions/DATA-0088-adapter-auto-configuration-resolver-pipeline.md) for architecture details.

---

## Roadmap

**Week 1 (Current)**:
- ✅ Manifest system implemented
- ✅ 14 service manifests + 9 templates
- ✅ `garden-rake offer` command
- ✅ `garden-rake catalog` command

**Week 2**:
- `garden-rake announce` auto-integration
- Koan.ZenGarden protocol resolver
- End-to-end discovery testing

**Week 3**:
- Interactive mode (`garden-rake offer` with menu)
- NewStone.ps1 integration
- Custom manifest validation

**Future**:
- Remote manifest registry
- Manifest versioning
- Multi-service orchestration (dependencies)
- Health monitoring and auto-restart
