# Zen Garden: Development Roadmap

**Status**: Active Design Phase + Prototype Development  
**Maintained By**: Sylin.org (Koan Framework)  
**Start Date**: January 15, 2026  
**Target Prototype**: Hello World (February 2026)  
**Community Validation**: Ongoing

---

## Development Philosophy

**Incremental Value Delivery**: Each milestone independently valuable, testable, and provides learning feedback.

**Community-First**: Build in the open, gather feedback early, iterate based on real usage.

**"Make it work, make it right, make it fast"**: Prove concept → Add polish → Optimize performance

**Open Standard Focus**: Protocol specification alongside reference implementation.

---

## Phase 1: Hello World (3 Weeks)

### Goal

Prove core value proposition: "Plug in MongoDB → App auto-discovers it"

**Timeline**: January 13 - February 3, 2026

### Week 1: Garden Rake Foundation

**Days 1-2: Environment Setup & Service Manifests**

- Rust project skeleton (`garden-rake/`)
- C# library skeleton (`Koan.ZenGarden/`)
- Service manifests (14 services across 7 categories)
- Template definitions (9 multi-service bundles)
- Document "Contributing" guidelines

**Days 3-5: Manifest-Based Service Offering**

**Implementation** (~400 LOC):

```rust
// garden-rake/src/commands/offer.rs

use crate::manifests::ManifestRegistry;
use crate::docker::DockerCompose;
use anyhow::Result;

pub async fn offer_service(service_name: &str) -> Result<()> {
    println!("🌿 Offering service: {}", service_name);

    // 1. Load manifest (embedded or custom)
    let manifest = ManifestRegistry::load(service_name)?;
    println!("   Loaded manifest: {} ({})", manifest.name, manifest.description);

    // 2. Validate prerequisites
    check_docker_installed()?;
    check_port_available(manifest.port)?;
    println!("   Prerequisites: ✓");

    // 3. Pull image
    println!("   Pulling image: {}", manifest.image);
    docker_pull(&manifest.image).await?;

    // 4. Generate compose file
    let compose = manifest.to_compose_yaml();
    std::fs::write("/etc/garden/compose.yml", compose)?;
    println!("   Compose file: ✓");

    // 5. Start service
    DockerCompose::up("/etc/garden").await?;
    println!("   Service started: ✓");

    // 6. Announce via mDNS (Milestone 2)
    // announce_service(&manifest).await?;

    println!("✓ Stone now offers: {}", service_name);
    Ok(())
}

// garden-rake/src/manifests/mod.rs

pub struct ManifestRegistry;

impl ManifestRegistry {
    pub fn load(name: &str) -> Result<ServiceManifest> {
        // Load from embedded manifests
        let yaml = match name {
            "mongodb" => include_str!("../../manifests/data/mongodb.yml"),
            "postgresql" => include_str!("../../manifests/data/postgresql.yml"),
            "redis" => include_str!("../../manifests/cache/redis.yml"),
            "ollama" => include_str!("../../manifests/ai/ollama.yml"),
            // ... all 14 services
            _ => return Err(anyhow!("Unknown service: {}", name)),
        };
        
        serde_yaml::from_str(yaml)
    }
    
    pub fn list_all() -> Vec<String> {
        vec![
            "mongodb", "postgresql", "sqlserver", "redis",
            "rabbitmq", "ollama", "weaviate", // ...
        ]
    }
}
```

**Deliverable**: `garden-rake` binary with manifest system

**Test Commands**:

```bash
# List available services
$ garden-rake catalog
Available offerings:
  [data]
    mongodb      - Official MongoDB 7.x document database
    postgresql   - PostgreSQL 16 with pgvector
    redis        - Redis Stack (JSON, Search)
  [ai]
    ollama       - Ollama local LLM runtime
  ...

# Offer single service
$ garden-rake offer mongodb
🌿 Offering service: mongodb
   Loaded manifest: mongodb (Official MongoDB 7.x)
   Prerequisites: ✓
   Pulling image: mongo:7
   Service started: ✓
✓ Stone now offers: mongodb

# Offer multiple services
$ garden-rake offer postgresql redis

# Offer template
$ garden-rake offer --template fullstack
✓ Offering: postgresql, redis, rabbitmq, ollama

# Verify running services
$ docker ps
$ garden-rake status
```

---

### Week 2: Discovery Library

**Days 1-2: Koan.Garden Client (mDNS Query)**

**Implementation** (~100 LOC):

```csharp
// Koan.Garden/GardenClient.cs

using Zeroconf;

namespace Koan.Garden;

public class GardenClient
{
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(1);

    public async Task<Stone?> FindOfferingAsync(
        string offering,
        CancellationToken ct = default)
    {
        var responses = await ZeroconfResolver.ResolveAsync(
            "_koan-stone._tcp.local.",
            timeout: _timeout,
            cancellationToken: ct);

        foreach (var response in responses)
        {
            if (response.Services.TryGetValue("offering", out var value)
                && value == offering)
            {
                return new Stone(
                    Host: response.IPAddress,
                    Port: int.Parse(response.Services["port"]),
                    Offering: offering
                );
            }
        }
        return null;
    }
}

public record Stone(string Host, int Port, string Offering);
```

**Day 3: Protocol-Based Resolver Architecture (DATA-0088)**

**Implementation** (~80 LOC):

```csharp
// Koan.Core/Infrastructure/ConnectionProtocolRegistry.cs (~50 LOC)

namespace Koan.Core.Infrastructure;

public static class ConnectionProtocolRegistry
{
    private static readonly Dictionary<string, IConnectionProtocolResolver> _resolvers = new();

    public static void RegisterProtocol(string protocol, IConnectionProtocolResolver resolver)
    {
        _resolvers[protocol.ToLowerInvariant()] = resolver;
    }

    public static async Task<Stone?> ResolveAsync(string connectionString, CancellationToken ct = default)
    {
        // Parse "zen-garden:mongodb" -> ("zen-garden", "mongodb")
        var parts = connectionString.Split(':', 2);
        if (parts.Length != 2) return null;

        var (protocol, identifier) = (parts[0], parts[1]);
        if (!_resolvers.TryGetValue(protocol, out var resolver)) return null;

        return await resolver.ResolveAsync(identifier, ct);
    }

    public static bool HasProtocol(string connectionString)
    {
        var protocol = connectionString.Split(':')[0];
        return _resolvers.ContainsKey(protocol.ToLowerInvariant());
    }
}

// Koan.Core/Infrastructure/IConnectionProtocolResolver.cs (~10 LOC)

public interface IConnectionProtocolResolver
{
    Task<Stone?> ResolveAsync(string identifier, CancellationToken ct);
}

public record Stone(string Host, int Port, string Offering);

// Koan.ZenGarden/ZenGardenProtocolResolver.cs (~30 LOC)

public class ZenGardenProtocolResolver : IConnectionProtocolResolver
{
    private readonly GardenClient _client;

    public ZenGardenProtocolResolver(GardenClient client) => _client = client;

    public async Task<Stone?> ResolveAsync(string identifier, CancellationToken ct)
    {
        // identifier = "mongodb", "postgresql", etc.
        return await _client.FindOfferingAsync(identifier, ct);
    }
}

// Koan.ZenGarden/Initialization/KoanAutoRegistrar.cs (~20 LOC)
// Registers "zen-garden" protocol on module load
ConnectionProtocolRegistry.RegisterProtocol("zen-garden", new ZenGardenProtocolResolver(client));

public class KoanAutoRegistrar : IKoanInitializer
{
    public void Initialize(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAutoConfigurationResolver, ZenGardenAutoConfigurationResolver>();
        services.AddSingleton<GardenClient>();
    }
}
```

**Architecture Note**: See [DATA-0088](../../../docs/decisions/DATA-0088-adapter-auto-configuration-resolver-pipeline.md) for full resolver pipeline design.

**Deliverable**: `Koan.Garden` NuGet package

**Test Code**:

```csharp
var garden = new GardenClient();
var stone = await garden.FindOfferingAsync("mongodb");
Console.WriteLine($"Found: {stone.Host}:{stone.Port}");
```

---

**Days 4-5: Adapter Configurator Integration**

**Implementation** (~80 LOC):

```csharp
// Koan.Data.Connector.Mongo/MongoOptionsConfigurator.cs (updated)

protected override void ConfigureProviderSpecific(MongoOptions options)
{
    var connectionString = options.ConnectionString;

    // Check for protocol prefix
    if (ConnectionProtocolRegistry.HasProtocol(connectionString))
    {
        var stone = await ConnectionProtocolRegistry.ResolveAsync(connectionString, ct);
        if (stone != null)
        {
            // Adapter translates Stone -> connection string
            var translated = BuildMongoConnectionString(
                stone.Host,
                stone.Port,
                options.DatabaseName,
                options.Username,
                options.Password
            );

            KoanLog.ConfigInfo(Logger, "zen-garden-resolved",
                ("host", stone.Host),
                ("port", stone.Port),
                ("offering", stone.Offering));

            options.ConnectionString = translated;
            return;
        }
    }

    // Existing fallback logic...
}

private static string BuildMongoConnectionString(
    string host,
    int port,
    string? database,
    string? username,
    string? password)
{
    var auth = string.IsNullOrEmpty(username) ? "" : $"{username}:{password}@";
    var db = string.IsNullOrEmpty(database) ? "" : $"/{database}";
    return $"mongodb://{auth}{host}:{port}{db}";
}
```

**Key Principle**: Protocol resolver returns `Stone` only. Each adapter configurator implements its own connection string translation logic.

**User Stories**:

1. **Explicit protocol (recommended)**:
   ```csharp
   options.UseMongoDb("zen-garden:mongodb");
   <!-- Add package reference - marks app as zen-garden compatible -->
   <PackageReference Include="Koan.ZenGarden" Version="1.0.0" />
   ```
   - Zen-garden resolver auto-registers via `IKoanInitializer`
   - **ALL adapters** (data, cache, messaging, AI) gain zen-garden discovery automatically
   - Falls back to standalone auto-config if zen-garden unavailable

2. **Explicit zen-garden (opt-in)**:
   ```csharp
   services.AddKoanData(options => {
       options.UseMongoDb("zen-garden:mongodb");
   });
   ```
   - Same behavior, explicit in configuration
   - Useful for debugging or per-environment control

**Deliverable**: Updated `Koan.Core` (cross-cutting) and `Koan.Data.MongoDB` packages

**Note**: This architecture supports **all adapter types** - data (MongoDB, PostgreSQL, Redis), cache (Redis, Memcached), messaging (RabbitMQ, Kafka), AI (OpenAI, LM Studio), etc.

---

### Week 3: Testing & Demo

**Days 1-2: Integration Tests**

**Test Scenario 1: Docker Compose (Same Machine)**

```yaml
# docker-compose.yml
version: "3.8"
services:
  mongodb:
    image: mongo:7
    ports:
      - "27017:27017"
    environment:
      MONGO_INITDB_ROOT_USERNAME: admin
      MONGO_INITDB_ROOT_PASSWORD: changeme

  stone:
    image: ubuntu:22.04
    volumes:
      - ./garden-rake:/usr/local/bin/garden-rake
      - /var/run/docker.sock:/var/run/docker.sock
    command: >
      sh -c "garden-rake offer mongodb &&
             garden-rake announce mongodb --port 27017"
    network_mode: host
    depends_on:
      - mongodb

  app:
    build: ./examples/hello-world
    environment:
      - KOAN__DATA__CONNECTIONSTRING=zen-garden:mongodb
    depends_on:
      - stone
```

**Expected Result**:

```bash
$ docker-compose up
stone_1  | 🌿 Offering service: mongodb
stone_1  | ✓ Stone now offers: mongodb
stone_1  | 🌸 Announcing: mongodb (port 27017)
app_1    | [Config] Resolved zen-garden:mongodb -> mongodb://192.168.1.100:27017
app_1    | Connected to MongoDB successfully
```

---

**Test Scenario 2: Two Ubuntu VMs**

```bash
# VM1 (192.168.1.100)
$ garden-rake offer mongodb
✓ Stone now offers: mongodb
$ garden-rake announce mongodb --port 27017
🌸 Announcing: mongodb (port 27017)

# VM2 (192.168.1.101)
$ docker run -e KOAN__DATA__CONNECTIONSTRING=zen-garden:mongodb my-app
# Expected: App discovers 192.168.1.100:27017 via mDNS
```

---

**Test Scenario 3: Ubuntu + macOS**

```bash
# Ubuntu physical (192.168.1.100)
$ garden-rake offer mongodb
✓ Stone now offers: mongodb
$ garden-rake announce mongodb --port 27017
🌸 Announcing: mongodb (port 27017)

# macOS laptop (192.168.1.50)
$ docker run -e KOAN__DATA__CONNECTIONSTRING=zen-garden:mongodb my-app
# Expected: Cross-platform discovery works (avahi/Bonjour compatibility)
```

---

**Day 3 (OPTIONAL): Gateway Mode Prototype**

**Goal**: Demonstrate database-agnostic applications using existing EntityController infrastructure

**Prototype Implementation** (~250 LOC total):

1. **Koan.Data.Adapter.ZenGarden** (~200 LOC):

   - HTTP client implementing `IDataRepository<TEntity, TKey>`
   - Maps operations to EntityController endpoints
   - GET `/entities/{id}` → `GetAsync()`
   - POST `/entities` → `UpsertAsync()`
   - DELETE `/entities/{id}` → `DeleteAsync()`

2. **Garden Rake Enhancement** (~20 LOC):

   - Announce dual ports: `data-port=27017`, `router-port=5000`
   - Enable both Direct mode and Gateway mode

3. **Sample Gateway App** (~30 LOC):
   - Todo app using ONLY `Koan.Data.Adapter.ZenGarden`
   - Zero database drivers
   - Demonstrates true database-agnostic client

**Test Flow**:

```bash
# Stone machine: Run Koan app WITH database adapter
$ cd samples/stone-app
$ dotnet run # Exposes EntityController<Todo> on port 5000

# Client machine: Run app with ONLY ZenGarden adapter
$ cd samples/client-app
$ dotnet run # Connects via HTTP to stone's EntityController
# Client has NO MongoDB driver, only HTTP client
```

**Why Optional**:

- Core Hello World value: Zero-config Direct mode
- Gateway mode demonstrates long-term vision
- Can be deferred to Milestone 4 without blocking launch

---

**Days 4-5: Documentation & Demo**

**Documentation Deliverables**:

- README.md (90-second setup instructions)
- Quick-start guide (Docker Compose all-in-one)
- Troubleshooting guide (mDNS debugging with `avahi-browse`)
- Architecture diagram (simplified)
- Gateway pattern documentation (if prototype completed)

**Demo Assets**:

1. **90-Second Demo Video**

   - Split terminal: stone (left), app (right)
   - Show discovery logs in real-time
   - No IP addresses in config
   - BONUS: Gateway mode demo (if prototype completed)

2. **Animated GIF** (for social media)

   - Terminal recording of discovery flow
   - 10-15 seconds, loops

3. **Launch Blog Post**

   - Problem statement
   - Demo walkthrough
   - Revolutionary insight: EntityController enables Gateway mode
   - Call to action (GitHub star, try it)

4. **Community Posts**
   - Reddit (r/selfhosted, r/homelab)
   - Hacker News ("Show HN")
   - Twitter/X with GIF
   - Technical deep-dive: Gateway pattern architecture

**Days 6-7: mDNS Announcement (Milestone 2 prep)**

**Implementation** (~100 LOC):

```rust
// garden-rake/src/commands/announce.rs

use mdns_sd::{ServiceDaemon, ServiceInfo};

pub async fn announce_service(service_name: &str, port: u16) -> Result<()> {
    println!("🌸 Announcing: {} (port {})", service_name, port);

    let mdns = ServiceDaemon::new()?;
    
    let service_info = ServiceInfo::new(
        "_koan-stone._tcp.local.",
        &format!("stone-{}", service_name),
        &format!("stone-{}._koan-stone._tcp.local.", service_name),
        "",
        port,
        &[
            ("offering", service_name),
            ("port", &port.to_string()),
        ][..],
    )?;

    mdns.register(service_info)?;
    println!("   Press Ctrl+C to stop");

    loop {
        tokio::time::sleep(Duration::from_secs(1)).await;
    }
}
```

**Note**: This enables Week 2 discovery integration. For Milestone 0.5, `offer` command deploys services only; `announce` command is separate (Week 2 auto-integrates both).

---

## Week 1 Summary

**Deliverables**:
1. ✅ `garden-rake` CLI with manifest system (14 services)
2. ✅ `garden-rake catalog` - list available offerings
3. ✅ `garden-rake offer <service>` - deploy services via manifests
4. ✅ `garden-rake offer --template <name>` - deploy service bundles
5. ✅ `garden-rake announce` - mDNS announcement (prep for Week 2)
6. ✅ Service manifests (14 YAML files, 9 templates)
7. ✅ Protocol-based resolver architecture (DATA-0088)

**LOC Breakdown**:
- Service manifests: ~500 lines (14 services + 9 templates)
- `garden-rake` CLI: ~600 lines
  - Commands (offer, catalog, announce): ~250 LOC
  - Manifest registry: ~100 LOC
  - Docker integration: ~150 LOC
  - Main/CLI parser: ~100 LOC
- Infrastructure (Koan.Core): ~80 LOC
  - ConnectionProtocolRegistry: ~50 LOC
  - IConnectionProtocolResolver: ~10 LOC
  - Stone record: ~5 LOC
- **Total**: ~1,180 LOC

---

**Core (Required)**:

- ✅ Agent announces successfully on Ubuntu, macOS
- ✅ Library discovers stone within 5 seconds
- ✅ App connects without manual config (Direct mode)
- ✅ Total setup time <5 minutes
- ✅ Demo video recorded and polished
- ✅ Documentation clear and complete

**Gateway Mode (Optional)**:

- 🎯 Gateway prototype demonstrates HTTP-based data access
- 🎯 Client app runs with ZERO database drivers
- 🎯 Documentation shows both Direct and Gateway modes
- 🎯 Community discussions highlight revolutionary architecture

**Go/No-Go Decision**: If all core criteria met, proceed to Milestone 1. If not, extend 1 week to resolve blockers.

---

## Phase 2: Full MVP (Weeks 4-14)

### Milestone 1: Multi-Service Support (Week 4-5)

**Goal**: Support PostgreSQL, Redis in addition to MongoDB

**Timeline**: February 4-17, 2026

**Work Items**:

1. **Agent Enhancement** (~100 LOC):

   - Support multiple offerings from single agent
   - Config file: `/etc/stone/offerings.toml`

   ```toml
   [[offering]]
   type = "mongodb"
   port = 27017

   [[offering]]
   type = "postgresql"
   port = 5432

   [[offering]]
   type = "redis"
   port = 6379
   ```

2. **PostgreSQL Adapter Integration** (~50 LOC):

   ```csharp
   // Koan.Data.PostgreSQL/PostgreSqlOptionsExtensions.cs

   if (connectionString.StartsWith("zen-garden:postgres"))
   {
       var garden = new GardenClient();
       var stone = await garden.FindOfferingAsync("postgresql");
       connectionString = $"Host={stone.Host};Port={stone.Port}";
   }
   ```

3. **Redis Adapter Integration** (~50 LOC):

   ```csharp
   // Koan.Cache.Redis/RedisOptionsExtensions.cs

   if (connectionString.StartsWith("zen-garden:redis"))
   {
       var garden = new GardenClient();
       var stone = await garden.FindOfferingAsync("redis");
       connectionString = $"{stone.Host}:{stone.Port}";
   }
   ```

4. **Integration Test**: App discovers 3 different database types

**Deliverable**: `zen-garden:postgres`, `zen-garden:redis` work

**LOC**: ~200 total

---

### Milestone 2: Authentication (Week 6-7)

**Goal**: Secure MongoDB connections with username/password

**Timeline**: February 18 - March 3, 2026

**Work Items**:

1. **Stone Config File** (~50 LOC):

   ```toml
   # /etc/stone/config.toml

   [offerings.mongodb]
   port = 27017
   username = "admin"
   password = "secret123"

   [offerings.postgresql]
   port = 5432
   username = "pgadmin"
   password = "pgpass"
   ```

2. **Agent Enhancement** (~100 LOC):

   - Read credentials from config
   - Announce credentials in TXT records (base64-encoded)
   - Format: `creds=base64(username:password)`

3. **Library Enhancement** (~100 LOC):

   - Parse credentials from TXT records
   - Include in connection string

4. **Pebble HMAC Verification** (~150 LOC):
   - Shared secret prevents rogue stones
   - Environment variable: `GARDEN_PEBBLE=secret123`
   - Stone signs announcements with HMAC
   - Library verifies signature before trusting

**Test Flow**:

```bash
# Stone with authentication
$ GARDEN_PEBBLE=mysecret garden-rake \
    --offering mongodb \
    --port 27017 \
    --username admin \
    --password secret123

# App with matching pebble
$ GARDEN_PEBBLE=mysecret docker run my-app
# Expected: Secure connection with credentials
```

**Deliverable**: Secure discovery with authentication

**LOC**: ~400 total

---

### Milestone 3: Garden Isolation (Week 8)

**Goal**: Multiple isolated gardens on same network

**Timeline**: March 4-10, 2026

**Work Items**:

1. **Garden Name in Config** (~50 LOC):

   ```toml
   [garden]
   name = "home-prod"
   pebble = "prod-secret"

   [[offering]]
   type = "mongodb"
   port = 27017
   ```

2. **Agent Enhancement** (~50 LOC):

   - Include garden name in announcement
   - Sign with garden-specific pebble

3. **Library Enhancement** (~100 LOC):
   - Filter by garden name (env: `GARDEN_NAME=home-prod`)
   - Verify HMAC with garden-specific pebble
   - Ignore stones from other gardens

**Test Scenario**:

```bash
# Garden 1: home-prod
$ GARDEN_NAME=home-prod GARDEN_PEBBLE=prod123 \
  garden-rake --offering mongodb --port 27017

# Garden 2: home-dev
$ GARDEN_NAME=home-dev GARDEN_PEBBLE=dev456 \
  garden-rake --offering mongodb --port 27018

# App connects to home-prod only
$ GARDEN_NAME=home-prod GARDEN_PEBBLE=prod123 docker run my-app
# Expected: Discovers port 27017, ignores 27018
```

**Deliverable**: Two gardens on same subnet don't see each other's stones

**LOC**: ~200 total

---

### Milestone 4: Production Gateway Mode (Week 9-10)

**Goal**: Database-agnostic applications via HTTP gateway (production-ready)

**Timeline**: March 11-24, 2026

**Work Items**:

1. **Koan.Data.Adapter.ZenGarden Production Release** (~400 LOC):

   - Complete `IDataRepository<TEntity, TKey>` implementation
   - Connection pooling and retry logic
   - Error handling and fallback strategies
   - Response caching (optional)
   - Health checks and circuit breakers

2. **ZenGardenRecord Implementation** (~100 LOC):

   ```csharp
   // Universal entity container (stone-side)
   public class ZenGardenRecord : IEntity<string>
   {
       public string Id { get; init; }
       public string EntityType { get; init; }  // "Todo", "BlogPost"
       public JsonDocument Data { get; init; }   // Actual entity
       public DateTimeOffset CreatedAt { get; init; }
       public DateTimeOffset UpdatedAt { get; init; }
   }

   // Stone configuration
   builder.Services.AddControllers()
       .AddEntityController<ZenGardenRecord>();
   ```

3. **Partition-Based Storage** (~50 LOC):

   - Use `partition` parameter via `?set=` query string
   - Each entity type gets dedicated collection
   - Example: `ZenGardenRecord:app1:Todo`

4. **Authentication & Rate Limiting** (~100 LOC):

   - JWT bearer token support
   - Stone-side rate limiting via ASP.NET Core middleware
   - Client-side backoff and retry
   - Per-client quotas

5. **Enhanced Garden Announcement** (~20 LOC):

   - Dual-port announcement: `data-port` + `router-port`
   - Capability flags: `direct=true`, `gateway=true`

6. **Performance Optimization** (~100 LOC):
   - HTTP/2 support
   - Response compression (gzip, brotli)
   - Connection keep-alive
   - Batch operation support

**Test Scenarios**:

1. **Type Compatibility Test**:

   - Client defines `Todo`, `BlogPost`, `Invoice` types
   - Stone has ZERO knowledge of these types
   - All operations work transparently

2. **Performance Benchmark**:

   - Compare Direct vs Gateway latency
   - Target: <10ms overhead for single operations
   - Target: <30% throughput reduction

3. **Polyglot Clients**:

   - .NET client (ZenGarden adapter)
   - Python client (raw HTTP)
   - JavaScript client (fetch API)

4. **Database Portability**:
   - Start with MongoDB stone
   - Swap to PostgreSQL stone
   - Client code unchanged (zero redeployment)

**Deliverables**:

- ✅ Production-ready `Koan.Data.Adapter.ZenGarden` NuGet package
- ✅ `ZenGardenRecord` universal entity wrapper
- ✅ JWT authentication and rate limiting
- ✅ Performance benchmarks (Direct vs Gateway)
- ✅ Multi-language client samples
- ✅ Database swap demo (MongoDB → PostgreSQL)
- ✅ Documentation: When to use Gateway vs Direct mode

**LOC**: ~770 total

---

### Milestone 5: Cache & AI Gateway APIs (Week 11-12)

**Goal**: Extend Gateway mode to cache and AI services

**Timeline**: March 25 - April 7, 2026

**Work Items**:

1. **CacheController** (~100 LOC):

   ```csharp
   [Route("api/cache")]
   public class CacheController : ControllerBase
   {
       [HttpGet("{key}")] Task<byte[]?> Get(string key);
       [HttpPost("{key}")] Task Set(string key, [FromBody] CacheSetRequest request);
       [HttpDelete("{key}")] Task Remove(string key);
       [HttpPost("refresh/{key}")] Task Refresh(string key);
   }
   ```

2. **Koan.Cache.Adapter.ZenGarden** (~150 LOC):

   - HTTP client implementing `IDistributedCache`
   - Translates cache operations to HTTP calls

3. **AiController Enhancement** (~50 LOC):

   - Add missing IAiAdapter operations for Gateway mode
   - Model management endpoints
   - Adapter lifecycle operations

4. **Koan.AI.Adapter.ZenGarden** (~200 LOC):
   - HTTP client implementing `IAiAdapter`
   - Chat, streaming, embeddings via HTTP

**Test Scenarios**:

- Cache operations via HTTP (get, set, remove, refresh)
- AI chat and embeddings via HTTP
- Polyglot cache/AI clients (Python, JavaScript)

**Deliverables**:

- ✅ CacheController HTTP API
- ✅ Enhanced AiController
- ✅ ZenGarden cache and AI adapters
- ✅ Multi-language samples

**LOC**: ~500 total

---

### Milestone 6: Windows Support (Week 13)

**Goal**: Windows developers can discover Linux/macOS stones

**Timeline**: April 8-14, 2026

**Work Items**:

1. **Lantern HTTP API (Coordinator)** (~500 LOC):

   ```
   POST /register (stone registers itself)
   GET /discover?offering=mongodb (client discovers)
   ```

2. **Agent: HTTP Fallback** (~100 LOC):

   - If mDNS fails, register with Lantern HTTP endpoint
   - Heartbeat mechanism (30-second intervals)

3. **Library: HTTP Fallback** (~100 LOC):

   - If mDNS fails, query Lantern HTTP API
   - Environment variable: `LANTERN_URL=http://lantern:8080`

4. **Documentation**: Windows setup guide

**Test Flow**:

```bash
# Lantern coordinator (any machine)
$ docker run -p 8080:8080 koan/lantern

# Stone (Linux)
$ LANTERN_URL=http://192.168.1.50:8080 \
  garden-rake --offering mongodb --port 27017

# App (Windows 11 + Docker Desktop)
$ docker run -e LANTERN_URL=http://192.168.1.50:8080 my-app
# Expected: Discovery via Lantern HTTP
```

**Deliverable**: Windows 11 + Docker Desktop can discover stones

**LOC**: ~700 total

---

### Milestone 7: Dashboard UI (Week 14)

**Goal**: Visual dashboard showing all stones and their status

**Timeline**: April 15-21, 2026

**Work Items**:

1. **Lantern Web UI** (~400 LOC - Blazor or React):

   - Table: Stone name, offerings, status, uptime
   - Real-time updates (polling every 5 seconds)
   - Gateway mode monitoring (HTTP traffic, latency)

2. **Agent Health Endpoint** (~50 LOC):

   ```json
   GET /health
   {
     "status": "healthy",
     "offerings": ["mongodb"],
     "uptime": 3600,
     "gateway": {
       "enabled": true,
       "port": 5000,
       "requests_per_sec": 125
     }
   }
   ```

3. **Lantern Aggregation** (~100 LOC):
   - Polls all stones every 5 seconds
   - Caches status in memory
   - Serves to dashboard

**Deliverable**: Dashboard accessible at `http://lantern:8080`

**LOC**: ~550 total

---

## MVP Complete Checklist (Week 14)

**Core Features**:

- ✅ mDNS discovery (Ubuntu, macOS)
- ✅ HTTP discovery (Windows, cross-subnet)
- ✅ MongoDB, PostgreSQL, Redis support
- ✅ Authentication (username/password)
- ✅ Garden isolation (pebble HMAC)
- ✅ Gateway mode (HTTP-based data access)
- ✅ Dashboard UI (stone status)

**Gateway Mode Features**:

- ✅ Database-agnostic clients (zero database drivers)
- ✅ JWT authentication and rate limiting
- ✅ Multi-language support (Python, JavaScript, Go)
- ✅ Database portability (swap without client redeployment)

**Cross-Platform**:

- ✅ Linux (Ubuntu 20.04, 22.04, 24.04)
- ✅ macOS (Monterey, Ventura, Sonoma)
- ✅ Windows 11 (via Lantern)

**Scenarios Validated**:

- ✅ Single subnet, homogeneous (3× Ubuntu)
- ✅ Windows dev → Linux stone
- ✅ Multi-subnet with Lantern
- ✅ macOS → Linux

**Developer Experience**:

- ✅ Setup time <10 minutes
- ✅ Documentation clear and complete
- ✅ CLI tooling (`garden-cli diagnose`)

**Total LOC (Phase 2)**: ~3,920 lines

---

## Phase 3: Production Hardening (Weeks 15-22)

**Focus**: Security, reliability, performance for production use

**Timeline**: April 22 - June 10, 2026

### Work Items

**1. mTLS Encryption** (Week 15-16):

- Encrypted stone-to-client communication
- Certificate management (Let's Encrypt integration)
- Mutual TLS verification
- **LOC**: ~800

**2. RBAC + Audit Logs** (Week 17-18):

- Role-based access control
- Audit trail for all discovery events
- User/service account management
- Compliance reporting
- **LOC**: ~1,000

**3. Lantern HA** (Week 19-20):

- Multi-lantern with failover
- Priority-based election (Raft consensus)
- State synchronization
- **LOC**: ~1,200

**4. Performance Optimization** (Week 21-22):

- Cache discovery results (TTL)
- Reduce mDNS traffic (heartbeat tuning)
- HTTP/2 server push
- Connection pooling
- **LOC**: ~600

**Exit Criteria**: Production-ready for enterprise use

**Total LOC (Phase 3)**: ~3,600 lines

---

## Phase 4: Advanced Features (Weeks 23-28)

**Focus**: Expand capabilities, ecosystem growth

**Timeline**: June 11 - July 23, 2026

### Work Items

**1. Pod-Based Identity** (Week 23-25):

- Solid Protocol integration
- OIDC provider (Google, Microsoft, GitHub)
- Fine-grained ACLs per entity
- **LOC**: ~1,500

**2. AI Model Discovery** (Week 26-27):

- Ollama integration (LLM models)
- Qdrant support (vector databases)
- GPU capability detection
- Model version management
- **LOC**: ~800

**3. Storage Discovery** (Week 28):

- Minio support (S3-compatible)
- SeaweedFS support (distributed storage)
- Capacity-based selection
- Automatic replication
- **LOC**: ~600

**Total LOC (Phase 4)**: ~2,900 lines

---

## Team Structure

### Core Team (Week 1-14)

**Backend Engineer** (1 FTE):

- Rust agent development
- C# library and adapters
- Integration testing

**Frontend Engineer** (0.5 FTE):

- Dashboard UI (Week 14 only)
- Demo videos and marketing materials

**DevOps Engineer** (0.5 FTE):

- CI/CD pipeline
- Testing infrastructure
- Docker image management

### Extended Team (Week 15+)

**Security Engineer** (0.5 FTE):

- mTLS implementation
- RBAC and audit logs
- Penetration testing

**Site Reliability Engineer** (0.5 FTE):

- Monitoring and alerting
- Performance optimization
- Incident response

---

## Daily Workflow

**Standup** (15 minutes):

1. What did you complete yesterday?
2. What are you working on today?
3. Any blockers?

**Definition of Done**:

- ✅ Code merged to `main`
- ✅ Tests passing (unit + integration)
- ✅ Documentation updated
- ✅ Demo-able (can show to team)

**Weekly Demos** (Fridays):

- Show progress to stakeholders
- Gather feedback
- Adjust roadmap if needed

---

## Risk Management

### Technical Risks

**Risk**: mDNS unreliable on Windows  
**Mitigation**: Lantern HTTP fallback (Milestone 6)  
**Contingency**: Document Windows limitations, ship Ubuntu/macOS first

**Risk**: Gateway mode type compatibility issues  
**Mitigation**: ZenGardenRecord wrapper with thorough testing  
**Contingency**: Simplify to Direct mode only, defer Gateway to Phase 5

**Risk**: Performance degradation in Gateway mode  
**Mitigation**: HTTP/2, compression, connection pooling  
**Contingency**: Document performance characteristics, optimize hot paths

### Schedule Risks

**Risk**: Team velocity slower than estimated  
**Mitigation**: Weekly progress reviews, adjust scope  
**Contingency**: Cut features (defer Milestones 6-7 to Phase 3)

**Risk**: Cross-platform issues delay testing  
**Mitigation**: Automated test matrix (GitHub Actions)  
**Contingency**: Ship Ubuntu-only first, expand platforms incrementally

### Adoption Risks

**Risk**: Security concerns block adoption  
**Mitigation**: "Lab Mode" branding for Hello World, Phase 3 hardening roadmap  
**Contingency**: Prioritize Phase 3 (mTLS, RBAC) based on feedback

**Risk**: Competition releases similar solution  
**Mitigation**: Speed to market (3-week Hello World)  
**Contingency**: Differentiate on DX, simplicity, Koan integration, open standard approach

---

## Success Metrics

### What We Measure (And Why)

**Community Adoption Metrics:**

**Phase 1 (Weeks 1-6):**
- **Weekly Active Users**: Stones actively announcing services (opt-in telemetry)
- **Time-to-first-connection**: Median time from install to successful discovery (<5 min target)
- **Community content**: Blog posts, tutorials, DIY Stone guides created by users
- **Cross-platform validation**: Successful deployments on Linux, macOS, Windows

**Phase 2 (Weeks 7-14):**
- **30-day retention**: Users still running Stones after 30 days (40%+ target)
- **Multi-service adoption**: % of users deploying 2+ service types
- **Educational institutions**: Schools, libraries, makerspaces using Zen Garden
- **Geographic diversity**: Adoption outside North America/Europe

**Phase 3+ (Post-MVP):**
- **E-waste impact**: Devices repurposed (self-reported surveys)
- **Alternative implementations**: Python, Go, JavaScript clients developed by community
- **Integration partnerships**: Nextcloud, Home Assistant, Frigate integrations
- **Protocol extensions**: Community-contributed service types in manifest registry

**What We Don't Measure:**
- ❌ Revenue (not a commercial product)
- ❌ Enterprise adoption rates (not the target)
- ❌ Vanity metrics (GitHub stars without usage)

---

## Post-MVP: Community Growth

### Open Source Strategy

1. **Public from Day 1**: Build in open, not "open source later"
2. **Contributing Guidelines**: Lower barrier to contribution
3. **Good First Issues**: Label issues for newcomers
4. **Recognition**: Contributor spotlight, CONTRIBUTORS.md

### Community Outreach

1. **Content**: Blog posts, demo videos, workshop materials
2. **Social Media**: Reddit (r/homelab, r/selfhosted), Hacker News, Mastodon
3. **Conferences**: Submit talks to .NET Conf, FOSDEM, local meetups
4. **Integrations**: Partner with Nextcloud, Home Assistant, Frigate communities

### Educational Focus

1. **Makerspaces**: "Zen Garden setup nights" workshop templates
2. **Libraries**: Public computing infrastructure guides
3. **Schools**: Computer science curriculum materials
4. **Environmental groups**: E-waste reduction case studies

---

## Code Samples Repository

### Hello World Sample

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan(); // ← Garden auto-wires if Koan.Garden is referenced
var app = builder.Build();

app.MapGet("/todos", async (CancellationToken ct) =>
{
    var todos = await Todo.All(ct);
    return Results.Ok(todos);
});

app.Run();
```

```json
// appsettings.json - NO connection string needed
{
  // MongoDB adapter will auto-discover via Garden
}
```

### Gateway Mode Sample

```csharp
// Client app - NO database driver installed
var builder = WebApplication.CreateBuilder(args);

// Only Koan.Data.Adapter.ZenGarden package referenced
builder.Services.AddKoan();

// Rest of app code unchanged
```

### Multi-Language Client (Python)

```python
# Python client using Gateway mode
import requests

BASE_URL = "http://stone.local:5000"

# Create Todo
response = requests.post(f"{BASE_URL}/api/todos", json={
    "title": "Buy milk",
    "done": False
})
todo = response.json()

# Query Todos
response = requests.get(f"{BASE_URL}/api/todos")
todos = response.json()
```

---

## Appendices

### A. Tool Recommendations

**Development**:

- **Rust**: VS Code + rust-analyzer
- **C#**: Rider or VS Code + C# DevKit
- **Containers**: Docker Desktop or Rancher Desktop

**Infrastructure**:

- **VMs**: Multipass (lightweight Ubuntu VMs)
- **Networking**: Wireshark (mDNS debugging)
- **Testing**: GitHub Actions (CI/CD)

**Communication**:

- **Async**: GitHub Discussions, Discord
- **Sync**: Daily standup (Zoom/Teams)
- **Docs**: Markdown in repo, DocFX for site

---

### B. Definition of Done (DoD)

**Per Milestone**:

1. ✅ All acceptance criteria met
2. ✅ Tests passing (>90% coverage)
3. ✅ Documentation updated
4. ✅ Demo recorded
5. ✅ Announced to community
6. ✅ Retrospective completed

**Per Week**:

1. ✅ Weekly demo delivered
2. ✅ GitHub Issues triaged
3. ✅ Community questions answered
4. ✅ Roadmap updated (if scope changed)

---

### C. Retrospective Template

**What Went Well?**

- [Celebrate wins]

**What Didn't Go Well?**

- [Identify problems]

**What Will We Change?**

- [Actionable improvements]

**Shoutouts**:

- [Recognize team members]

---

### D. LOC Summary by Phase

| Phase       | Milestone                       | LOC   | Cumulative |
| ----------- | ------------------------------- | ----- | ---------- |
| **Phase 1** | Hello World                     | 550   | 550        |
| **Phase 2** | Multi-Service                   | 200   | 750        |
|             | Authentication                  | 400   | 1,150      |
|             | Garden Isolation                | 200   | 1,350      |
|             | Gateway Mode                    | 770   | 2,120      |
|             | Cache & AI Gateway              | 500   | 2,620      |
|             | Windows Support                 | 700   | 3,320      |
|             | Dashboard UI                    | 550   | **3,870**  |
| **Phase 3** | Production Hardening            | 3,600 | **7,470**  |
| **Phase 4** | Advanced Features               | 2,900 | **10,370** |
| **Phase 5** | Stone Binding Security (Opt-In) | 2,400 | **12,770** |

**Total Project LOC**: ~12,800 lines (includes optional security binding)

---

## Phase 5: Stone-to-Pond Binding Security (Opt-In Beta)

**Status**: CONDITIONAL GO (pending P0 recommendations)  
**Timeline**: 9-12 months after MVP launch  
**Prerequisites**: MVP validation, user research complete, leadership approval  
**Scope**: Cryptographic binding system (disabled by default)

### Overview

**Purpose**: Prevent bound Stones from being used on unauthorized ponds. Stones cryptographically bind to first pond, require factory reset to move.

**Key Concepts**:

- **Virgin Stone**: Unconfigured Stone (fresh from factory)
- **Pebble**: Cryptographic keys stored on dedicated partition
- **Binding**: Virgin Stone connects to secured pond → binds permanently
- **Factory Reset**: Physical button or command drops pebble + data (Stone becomes virgin)
- **Data Wipe**: Drops data only, preserves pebble (Stone stays bound)

**Market Positioning**: Opt-in security feature for privacy-conscious users. Beta test with r/homelab community to validate demand before broad rollout.

**Reference**: See [STONE-BINDING-SECURITY-EVALUATION.md](./STONE-BINDING-SECURITY-EVALUATION.md) for full team evaluation.

---

### Milestone 1: Cryptographic Foundation (Months 1-2)

**Goal**: Implement secure binding protocol with encrypted pebble storage.

#### Week 1-2: Cryptographic Specification

**Deliverables**:

1. **Public specification document** (~50 pages):

   - Key derivation (HKDF-SHA256)
   - Signing algorithm (Ed25519)
   - Encryption (AES-256-GCM)
   - Mutual authentication protocol (TLS 1.3 with PSK)
   - Threat model and attack surface analysis
   - Key rotation strategy

2. **Proof-of-concept** (~400 LOC):

```rust
// garden-rake/src/crypto/pebble.rs

use aes_gcm::{Aes256Gcm, Key, Nonce};
use ed25519_dalek::{Keypair, PublicKey, SecretKey, Signature};
use hkdf::Hkdf;
use sha2::Sha256;

/// Pebble: Cryptographic binding keys for Stone-to-Pond association
pub struct Pebble {
    pond_id: Uuid,
    pond_public_key: PublicKey,
    stone_keypair: Keypair,
    binding_timestamp: DateTime<Utc>,
    key_rotation_counter: u64,
}

impl Pebble {
    /// Generate new pebble when virgin Stone binds to pond
    pub fn generate(pond_id: Uuid, pond_public_key: PublicKey) -> Self {
        let stone_keypair = Keypair::generate(&mut rand::thread_rng());

        Self {
            pond_id,
            pond_public_key,
            stone_keypair,
            binding_timestamp: Utc::now(),
            key_rotation_counter: 0,
        }
    }

    /// Encrypt pebble for storage on partition
    pub fn encrypt(&self, device_id: &[u8]) -> Result<Vec<u8>, CryptoError> {
        // Derive encryption key from device-unique hardware ID
        let hk = Hkdf::<Sha256>::new(None, device_id);
        let mut key_material = [0u8; 32];
        hk.expand(b"zen-garden-pebble-encryption-key", &mut key_material)?;

        let key = Key::from_slice(&key_material);
        let cipher = Aes256Gcm::new(key);

        // Generate random nonce (IV)
        let nonce_bytes = rand::random::<[u8; 12]>();
        let nonce = Nonce::from_slice(&nonce_bytes);

        // Serialize pebble to bytes
        let plaintext = bincode::serialize(self)?;

        // Encrypt with authentication tag
        let ciphertext = cipher.encrypt(nonce, plaintext.as_ref())?;

        // Return: nonce || ciphertext (tag included in ciphertext)
        Ok([&nonce_bytes[..], &ciphertext[..]].concat())
    }

    /// Decrypt pebble from storage partition
    pub fn decrypt(encrypted: &[u8], device_id: &[u8]) -> Result<Self, CryptoError> {
        // Derive same encryption key
        let hk = Hkdf::<Sha256>::new(None, device_id);
        let mut key_material = [0u8; 32];
        hk.expand(b"zen-garden-pebble-encryption-key", &mut key_material)?;

        let key = Key::from_slice(&key_material);
        let cipher = Aes256Gcm::new(key);

        // Extract nonce from first 12 bytes
        let nonce = Nonce::from_slice(&encrypted[..12]);
        let ciphertext = &encrypted[12..];

        // Decrypt and verify authentication tag
        let plaintext = cipher.decrypt(nonce, ciphertext)?;

        // Deserialize pebble
        Ok(bincode::deserialize(&plaintext)?)
    }

    /// Sign message with Stone's private key (proves binding)
    pub fn sign(&self, message: &[u8]) -> Signature {
        self.stone_keypair.sign(message)
    }

    /// Verify pond's signature (mutual authentication)
    pub fn verify_pond(&self, message: &[u8], signature: &Signature) -> bool {
        self.pond_public_key.verify(message, signature).is_ok()
    }
}
```

**Acceptance Criteria**:

- ✅ Pebble encrypted with AES-256-GCM (128-bit auth tag)
- ✅ Key derived from device-unique ID (CPU serial + MAC address)
- ✅ TPM 2.0 integration (if available on Stone hardware)
- ✅ Unit tests: encryption roundtrip, signature verification, key derivation
- ✅ Security review by external cryptographer

---

#### Week 3-4: Binding Protocol

**Implementation** (~500 LOC):

```rust
// garden-rake/src/binding/protocol.rs

/// Binding ceremony: Virgin Stone connects to secured pond
pub async fn bind_to_pond(
    stone_id: Uuid,
    pond_discovery: PondAdvertisement,
) -> Result<Pebble, BindingError> {
    // Step 1: User confirmation (LED + button press)
    led::set_state(LedState::AmberPulse)?;
    println!("🔐 New pond detected: {}", pond_discovery.name);
    println!("   Press button to bind to this pond (30 second timeout)");

    let confirmed = button::wait_for_press(Duration::from_secs(30)).await?;
    if !confirmed {
        led::set_state(LedState::SolidBlue)?; // Remain virgin
        return Err(BindingError::UserCancelled);
    }

    // Step 2: Mutual authentication handshake
    let stone_keypair = Keypair::generate(&mut rand::thread_rng());

    let handshake = BindingHandshake {
        stone_id,
        stone_public_key: stone_keypair.public,
        timestamp: Utc::now(),
    };

    let handshake_bytes = bincode::serialize(&handshake)?;
    let handshake_signature = stone_keypair.sign(&handshake_bytes);

    // Step 3: Send binding request to pond
    let response = pond_client::request_binding(
        pond_discovery.address,
        handshake,
        handshake_signature,
    ).await?;

    // Step 4: Verify pond's signature
    let response_bytes = bincode::serialize(&response)?;
    if !pond_discovery.public_key.verify(&response_bytes, &response.signature).is_ok() {
        return Err(BindingError::PondAuthenticationFailed);
    }

    // Step 5: Generate pebble
    let pebble = Pebble::generate(
        pond_discovery.pond_id,
        pond_discovery.public_key,
    );

    // Step 6: Store encrypted pebble on partition
    let device_id = hardware::get_device_id()?;
    let encrypted_pebble = pebble.encrypt(&device_id)?;

    partition::write_pebble(&encrypted_pebble)?;

    // Step 7: LED feedback
    led::set_state(LedState::PulsingGreen)?;
    println!("✅ Bound to pond: {}", pond_discovery.name);

    // Step 8: Immutable audit log
    audit_log::record_binding(pond_discovery.pond_id, Utc::now())?;

    Ok(pebble)
}

/// Verify Stone is bound to current pond
pub fn verify_binding(pebble: &Pebble, pond_id: Uuid) -> Result<(), BindingError> {
    if pebble.pond_id != pond_id {
        led::set_state(LedState::RedBlinkSlow)?;
        return Err(BindingError::WrongPond {
            expected: pebble.pond_id,
            actual: pond_id,
        });
    }

    Ok(())
}
```

**Acceptance Criteria**:

- ✅ User confirmation required (LED + button press within 30 seconds)
- ✅ Mutual authentication (Stone ↔ Pond)
- ✅ Encrypted pebble storage on dedicated partition
- ✅ Immutable audit log of binding events
- ✅ LED state indicators (amber=binding, green=bound, red=wrong pond)
- ✅ Integration tests: virgin→binding→bound→verify workflow

---

### Milestone 2: Web UI & User Experience (Months 3-4)

**Goal**: Non-CLI interface for binding management (addresses Support Engineer's NO-GO concerns).

#### Binding Dashboard (~800 LOC)

**Features**:

1. **Stone List View**:

   - Shows all discovered Stones with binding status
   - LED state visualization (maps physical LED to on-screen indicator)
   - Quick actions: Unbind, Wipe Data, Factory Reset

2. **Binding Confirmation Wizard**:

   - Virgin Stone detected → show pond name + admin approval required
   - "Press button on Stone within 30 seconds to bind"
   - Real-time LED state sync (amber pulse → green solid)

3. **Troubleshooting Guide**:
   - "Stone won't connect" → checks binding state → suggests fix
   - LED blink pattern decoder (red blink slow = bound to other pond)
   - One-click "Factory Reset" with confirmation dialogs

**Mockup** (React + TypeScript):

```tsx
// pond-ui/src/components/BindingDashboard.tsx

import { useStones, useBindingState } from '../hooks';
import { LedIndicator, ConfirmDialog } from '../components';

export function BindingDashboard() {
  const { stones, refresh } = useStones();
  const { bind, unbind, factoryReset } = useBindingState();

  const [selectedStone, setSelectedStone] = useState<Stone | null>(null);
  const [showResetDialog, setShowResetDialog] = useState(false);

  return (
    <div className="binding-dashboard">
      <h2>Stones in Pond: {stones.length}</h2>

      {stones.map(stone => (
        <StoneCard key={stone.id}>
          <LedIndicator state={stone.ledState} />

          <div className="stone-info">
            <h3>{stone.name}</h3>
            <BindingStatus stone={stone} />
            <DataUsage used={stone.dataUsed} total={stone.dataTotal} />
          </div>

          <div className="stone-actions">
            {stone.bindingState === 'virgin' && (
              <button onClick={() => bind(stone.id)}>
                Bind to This Pond
              </button>
            )}

            {stone.bindingState === 'bound-other' && (
              <div className="warning">
                ⚠️ Bound to: {stone.boundPondName}
                <button onClick={() => {
                  setSelectedStone(stone);
                  setShowResetDialog(true);
                }}>
                  Factory Reset
                </button>
              </div>
            )}

            {stone.bindingState === 'bound-here' && (
              <>
                <button onClick={() => unbind(stone.id)}>
                  Unbind & Wipe
                </button>
                <button onClick={() => /* data wipe only */}>
                  Wipe Data
                </button>
              </>
            )}
          </div>
        </StoneCard>
      ))}

      {showResetDialog && selectedStone && (
        <FactoryResetDialog
          stone={selectedStone}
          onConfirm={() => factoryReset(selectedStone.id)}
          onCancel={() => setShowResetDialog(false)}
        />
      )}
    </div>
  );
}

function BindingStatus({ stone }: { stone: Stone }) {
  const statusConfig = {
    'virgin': { icon: '💠', text: 'Virgin (Unbound)', color: 'blue' },
    'bound-here': { icon: '✅', text: 'Bound to This Pond', color: 'green' },
    'bound-other': { icon: '⚠️', text: 'Bound to Other Pond', color: 'red' },
    'binding': { icon: '🔄', text: 'Binding in Progress...', color: 'amber' },
  };

  const config = statusConfig[stone.bindingState];

  return (
    <div className={`binding-status ${config.color}`}>
      <span>{config.icon}</span>
      <span>{config.text}</span>
      {stone.bindingState === 'bound-here' && (
        <small>Bound {formatDistanceToNow(stone.bindingTimestamp)}</small>
      )}
    </div>
  );
}
```

**Acceptance Criteria**:

- ✅ Non-technical users can manage binding without CLI
- ✅ LED state visualized on-screen (colorblind-accessible labels)
- ✅ Factory reset requires multiple confirmations (prevents accidental data loss)
- ✅ Binding audit log visible in UI (timestamp, pond ID, user)
- ✅ Usability testing with 10 non-technical users (>8/10 success rate binding Stone without docs)

---

### Milestone 3: Disaster Recovery (Month 5)

**Goal**: Pond master failure doesn't brick all bound Stones (addresses Systems Architect's concerns).

#### Pond Key Escrow with Stone Quorum (~600 LOC)

**Implementation**:

```rust
// garden-rake/src/recovery/escrow.rs

use shamir_secret_sharing::{split_secret, reconstruct_secret};

/// Split pond master key into N shards, require M-of-N for recovery
pub fn escrow_pond_key(
    pond_master_key: &[u8; 32],
    n_stones: usize,
    threshold: usize,
) -> Vec<EncryptedShard> {
    // Split key using Shamir's Secret Sharing
    let shards = split_secret(threshold, n_stones, pond_master_key);

    // Encrypt each shard with Stone's public key
    shards.into_iter()
        .enumerate()
        .map(|(i, shard)| {
            let stone_public_key = get_stone_public_key(i);
            EncryptedShard {
                index: i,
                data: encrypt_with_public_key(&shard, &stone_public_key),
            }
        })
        .collect()
}

/// Recover pond master key from M-of-N Stones
pub async fn recover_pond_key(
    admin_password: &str,
    connected_stones: &[StoneConnection],
) -> Result<[u8; 32], RecoveryError> {
    if connected_stones.len() < QUORUM_THRESHOLD {
        return Err(RecoveryError::InsufficientStones {
            required: QUORUM_THRESHOLD,
            available: connected_stones.len(),
        });
    }

    println!("🔐 Pond Recovery Mode");
    println!("   Detected {} Stones (need {})", connected_stones.len(), QUORUM_THRESHOLD);

    // Step 1: Each Stone displays unique LED pattern (confirms participation)
    for (i, stone) in connected_stones.iter().enumerate() {
        stone.led_pattern(RecoveryPattern::Participant(i)).await?;
        println!("   Stone {}/{} ready [●]", i + 1, QUORUM_THRESHOLD);
    }

    // Step 2: Verify admin credentials
    let admin_hash = hash_password(admin_password);
    if !verify_admin(admin_hash).await? {
        return Err(RecoveryError::InvalidCredentials);
    }

    // Step 3: Collect encrypted shards from Stones
    let mut decrypted_shards = Vec::new();
    for stone in connected_stones.iter().take(QUORUM_THRESHOLD) {
        let encrypted_shard = stone.request_escrow_shard().await?;
        let decrypted = stone.decrypt_shard(&encrypted_shard).await?;
        decrypted_shards.push(decrypted);
    }

    // Step 4: Reconstruct pond master key
    let reconstructed_key = reconstruct_secret(&decrypted_shards)?;

    println!("✅ Pond master key recovered");
    println!("✅ Creating new pond controller...");

    Ok(reconstructed_key.try_into().unwrap())
}
```

**CLI Workflow**:

```bash
$ garden-admin recover --quorum

🔐 Pond Recovery Mode
   Scans for Stones... Found 5 Stones
   Required: 3 Stones for quorum

Connect 3 Stones physically (USB or network):
   Stone 1/3: storage-stone-01 [●] Ready
   Stone 2/3: compute-stone-02 [●] Ready
   Stone 3/3: cache-stone-01   [●] Ready

✅ Quorum achieved

Enter original pond admin password: ********
Verifying...

✅ Pond master key recovered
✅ New pond controller created
   Pond ID: a3f7c2e1-4d9a-4b32-8e7f-1c9a4d5e6f7a

Re-issuing pebbles to 5 Stones...
   ⏳ storage-stone-01... ✅
   ⏳ compute-stone-02... ✅
   ⏳ cache-stone-01...   ✅
   ⏳ ai-stone-01...      ✅
   ⏳ gateway-stone-01... ✅

⚠️  All Stones will reboot to apply new pebbles
    Data preserved, bindings updated

Reboot Stones now? [Y/n]: Y

✅ Recovery complete
   New pond controller running at: 192.168.1.100
   Dashboard: http://192.168.1.100:8080
```

**Acceptance Criteria**:

- ✅ 3-of-5 quorum (configurable)
- ✅ LED patterns during quorum ceremony (visual confirmation)
- ✅ Admin password verification (prevents unauthorized recovery)
- ✅ Automatic pebble re-issuance to all Stones
- ✅ Integration test: simulate pond failure → recover with 3 Stones → verify data access

---

### Milestone 4: Hardware Integration (Month 6)

**Goal**: Physical button + LED integration for all Stone tiers.

#### Component Selection & BOM

| Stone Tier         | Button Type               | LED Type                          | BOM Cost | NRE (Mold)           |
| ------------------ | ------------------------- | --------------------------------- | -------- | -------------------- |
| Budget ($30-70)    | Capacitive touch pad      | Single white LED (blink patterns) | $0.40    | $0 (PCB only)        |
| Standard ($50-120) | Recessed momentary switch | RGB LED (WS2812B)                 | $0.80    | $5K (case redesign)  |
| Premium ($150-250) | Capacitive touch + haptic | RGB LED array (8 LEDs)            | $2.20    | $12K (case redesign) |

**Trade-off Decision**: Use capacitive touch for budget tier (eliminates mechanical failure), recessed button for standard/premium (familiar UX).

#### Button Firmware (~200 LOC)

```c
// stone-firmware/button.c

#define BUTTON_PIN GPIO_NUM_12
#define DEBOUNCE_MS 50
#define DATA_WIPE_DURATION_MS 10000
#define FACTORY_RESET_DURATION_MS 20000

typedef enum {
    BUTTON_STATE_IDLE,
    BUTTON_STATE_PRESSED,
    BUTTON_STATE_DATA_WIPE_PENDING,
    BUTTON_STATE_FACTORY_RESET_PENDING
} button_state_t;

static button_state_t button_state = BUTTON_STATE_IDLE;
static uint32_t button_press_start_ms = 0;

void button_task(void *pvParameters) {
    gpio_set_direction(BUTTON_PIN, GPIO_MODE_INPUT);
    gpio_set_pull_mode(BUTTON_PIN, GPIO_PULLUP_ONLY);

    while (1) {
        bool button_pressed = (gpio_get_level(BUTTON_PIN) == 0);
        uint32_t current_ms = xTaskGetTickCount() * portTICK_PERIOD_MS;

        if (button_pressed && button_state == BUTTON_STATE_IDLE) {
            // Button just pressed
            button_press_start_ms = current_ms;
            button_state = BUTTON_STATE_PRESSED;
            led_set_pattern(LED_PATTERN_SLOW_RED_BLINK);

        } else if (button_pressed && button_state == BUTTON_STATE_PRESSED) {
            // Button held - check duration
            uint32_t hold_duration = current_ms - button_press_start_ms;

            if (hold_duration >= FACTORY_RESET_DURATION_MS) {
                button_state = BUTTON_STATE_FACTORY_RESET_PENDING;
                led_set_pattern(LED_PATTERN_RAPID_RED_STROBE);

            } else if (hold_duration >= DATA_WIPE_DURATION_MS) {
                button_state = BUTTON_STATE_DATA_WIPE_PENDING;
                led_set_pattern(LED_PATTERN_FAST_RED_BLINK);
            }

        } else if (!button_pressed && button_state != BUTTON_STATE_IDLE) {
            // Button released - execute action
            uint32_t hold_duration = current_ms - button_press_start_ms;

            if (hold_duration < 3000) {
                // Too short - ignore
                led_restore_normal_pattern();

            } else if (button_state == BUTTON_STATE_FACTORY_RESET_PENDING) {
                // Factory reset
                trigger_factory_reset();

            } else if (button_state == BUTTON_STATE_DATA_WIPE_PENDING) {
                // Data wipe only
                trigger_data_wipe();
            }

            button_state = BUTTON_STATE_IDLE;
        }

        vTaskDelay(pdMS_TO_TICKS(DEBOUNCE_MS));
    }
}

void trigger_factory_reset() {
    printf("🔴 FACTORY RESET INITIATED\n");
    led_set_pattern(LED_PATTERN_SOLID_RED);

    // Drop pebble partition
    partition_wipe("pebble");

    // Drop data partition
    partition_wipe("data");

    // Reboot as virgin Stone
    esp_restart();
}

void trigger_data_wipe() {
    printf("⚠️  DATA WIPE INITIATED (pebble preserved)\n");
    led_set_pattern(LED_PATTERN_AMBER_PULSE);

    // Drop data partition only
    partition_wipe("data");

    // Recreate empty data partition
    partition_format("data");

    led_set_pattern(LED_PATTERN_PULSING_GREEN);
    printf("✅ Data wiped, binding preserved\n");
}
```

**Acceptance Criteria**:

- ✅ Button debouncing (50ms)
- ✅ Progressive LED warnings (slow red → fast red → rapid strobe)
- ✅ Duration thresholds (10s = data wipe, 20s = factory reset)
- ✅ Accidental press protection (requires 3+ second hold)
- ✅ Hardware testing on all Stone tiers (budget, standard, premium)

---

### Milestone 5: Beta Program (Months 7-9)

**Goal**: Validate market demand and UX with real users before broad rollout.

#### Beta Cohort Selection

**Target**: 50-100 users from security-focused communities

- r/homelab (30 users)
- r/selfhosted (20 users)
- r/privacy (10 users)
- InfoSec Twitter followers (10 users)
- Hacker News community (10 users)

**Selection Criteria**:

- ✅ Currently self-hosting ≥3 services
- ✅ Owns ≥2 Stones (to test binding across multiple devices)
- ✅ Willing to provide weekly feedback
- ✅ Technical enough to recover from bugs

#### Beta Metrics

**Quantitative**:

- **Enable rate**: % of beta users who enable binding (target: ≥60%)
- **Factory reset frequency**: # of resets per user per month (target: <2)
- **Support ticket rate**: % of binding-related tickets (target: <5%)
- **Time to first successful binding**: Median time (target: <3 minutes)

**Qualitative**:

- **User satisfaction**: Post-beta survey score (target: ≥8/10)
- **UX pain points**: Common complaints (address in iteration)
- **Feature requests**: Most requested improvements
- **Testimonials**: "Binding makes me feel secure" vs "Binding is annoying"

#### Decision Gate (Week 9)

**Proceed to default-on (v2.5) IF**:

- ≥60% enable rate (proves demand)
- ≥8/10 satisfaction score
- <5% support ticket rate
- ≥10 positive testimonials

**Keep opt-in (v2.0-2.4) IF**:

- 20-59% enable rate (niche feature)
- 6-7/10 satisfaction score
- 5-10% support ticket rate

**Deprioritize/remove IF**:

- <20% enable rate (no demand)
- <6/10 satisfaction score
- > 10% support ticket rate
- Consistent feedback: "Binding is friction without benefit"

---

### Milestone 6: Production Launch (Months 10-12)

**Goal**: Full documentation, hardware manufacturing, support training.

#### Documentation Suite

1. **User Guide** (~30 pages):

   - "What is Stone binding?" (metaphor: physical key)
   - LED state reference (photos + descriptions)
   - Factory reset walkthrough (step-by-step)
   - Troubleshooting common issues (15 scenarios)

2. **Video Tutorials** (~15 minutes total):

   - Binding your first Stone (3 min)
   - Factory reset process (2 min)
   - Disaster recovery with quorum (5 min)
   - Multi-pond setup for consultants (5 min)

3. **Security Whitepaper** (~50 pages):
   - Threat model
   - Cryptographic primitives specification
   - Attack surface analysis
   - Comparison to Synology/QNAP security
   - External audit results

#### Hardware Manufacturing

**Order Quantities** (based on beta adoption rate):

- If >60% enable rate: Order 5,000 Stones with button/LED hardware
- If 20-59%: Order 1,000 Stones (test production run)
- If <20%: Software-only (no hardware changes)

**Lead Times**:

- Capacitive touch PCBs: 6 weeks
- Injection molds (case redesign): 12 weeks
- RGB LED procurement: 4 weeks
- Assembly + QA: 3 weeks

**Total**: 15-week lead time from order to first units

#### Support Training

**Support Team Onboarding** (3 days):

- Day 1: Binding concepts, LED states, common issues
- Day 2: Troubleshooting workshop (roleplay 20 scenarios)
- Day 3: RMA process, disaster recovery, escalation paths

**Runbook**:

- 15 common support scenarios with step-by-step resolutions
- LED state decoder (blink pattern → diagnostic)
- Factory reset decision tree (when to reset vs troubleshoot)

---

### LOC Summary: Binding Feature

| Milestone | Component                | LOC                      |
| --------- | ------------------------ | ------------------------ |
| **M1**    | Cryptographic foundation | 900                      |
| **M2**    | Web UI + UX              | 800                      |
| **M3**    | Disaster recovery        | 600                      |
| **M4**    | Hardware integration     | 300                      |
| **M5**    | Beta program             | 0 (no new code)          |
| **M6**    | Production launch        | 0 (docs + manufacturing) |
| **Total** |                          | **2,600**                |

---

### Risk Matrix: Binding Feature

| Risk                                  | Likelihood | Impact                      | Mitigation                                                     |
| ------------------------------------- | ---------- | --------------------------- | -------------------------------------------------------------- |
| <20% users enable binding (no demand) | **MEDIUM** | HIGH (wasted 12 months)     | **Opt-in beta** validates demand before hardware investment    |
| Support volume explosion              | MEDIUM     | HIGH (2-4 FTEs)             | **Web UI** + **7-day undo** + **LED troubleshooting**          |
| Cryptographic vulnerability           | LOW        | CRITICAL (binding bypassed) | **External audit** + **public specification** + **bug bounty** |
| Hardware button failures              | LOW        | MEDIUM (RMA cost)           | **Capacitive touch** (no mechanical parts) for budget tier     |
| Pond disaster recovery failure        | LOW        | HIGH (data loss)            | **3-of-N quorum** + **pond key escrow**                        |

**Overall Risk**: **MEDIUM** (conditional on opt-in beta success)

---

### Success Criteria Summary

**Beta Program (Milestone 5)**:

- ✅ ≥60% of beta users enable binding
- ✅ ≥8/10 user satisfaction score
- ✅ <5% of support tickets related to binding
- ✅ ≥10 positive testimonials ("Binding gives me peace of mind")

**Production Launch (Milestone 6)**:

- ✅ <2 factory resets per user per year (indicates stable UX)
- ✅ Disaster recovery tested in ≥5 real-world pond failures
- ✅ Zero cryptographic vulnerabilities found in external audit
- ✅ Support team resolves 90% of binding issues without escalation

**Long-Term (6-12 months post-launch)**:

- ✅ Binding feature mentioned in ≥50% of Zen Garden reviews (indicates differentiation)
- ✅ Competitor analysis shows Synology/QNAP adding similar features (validation)
- ✅ Hardware sales: ≥70% of new Stones ordered with button/LED (demand proof)

---

**Document Version**: 1.1  
**Last Updated**: January 13, 2026  
**Next Review**: Weekly (Fridays after demo)  
**Owner**: Development Team Lead

---

_"Security is a feature, but only if users can understand it."_
