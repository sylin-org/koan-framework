# Auto-Configuration Resolver Pipeline: Architecture Proposal

**Date**: January 14, 2026  
**Status**: Draft  
**Related**: [DATA-0088](../../../docs/decisions/DATA-0088-adapter-auto-configuration-resolver-pipeline.md)

---

## Current Architecture Analysis

### How MongoDB Auto-Configuration Works Today

**Flow**:
```
MongoOptionsConfigurator.ConfigureProviderSpecific()
  → checks if connectionString is "auto" or null
  → ResolveAutonomousConnection(database, username, password)
    → calls IServiceDiscoveryCoordinator.DiscoverServiceAsync("mongo", context)
      → ServiceDiscoveryCoordinator routes to MongoDiscoveryAdapter
        → MongoDiscoveryAdapter.DiscoverAsync(context)
          → BuildDiscoveryCandidates() creates priority-ordered list:
            1. Environment variables (MONGO_URLS)
            2. Explicit configuration
            3. Aspire service discovery (if AspireAppHost mode)
            4. Container instance (if KoanEnv.InContainer)
            5. Local fallback (localhost)
          → ValidateCandidate() for each:
            - Calls ValidateServiceHealth() (MongoDB ping command)
            - Returns first healthy candidate
        → Returns AdapterDiscoveryResult(url, method, isHealthy)
  → MongoOptionsConfigurator sets options.ConnectionString
```

**Key Components**:

1. **`IServiceDiscoveryCoordinator`** (Koan.Core)
   - Pure delegation - routes service name to registered adapter
   - Zero provider-specific knowledge
   - Registered adapters by service name/aliases

2. **`ServiceDiscoveryAdapterBase`** (Koan.Core)
   - Base class with common discovery patterns
   - Template method: `BuildDiscoveryCandidates()`
   - Pluggable validation: `ValidateServiceHealth()`
   - Handles: environment vars, explicit config, Aspire, container/local detection

3. **`MongoDiscoveryAdapter`** (Koan.Data.Connector.Mongo)
   - MongoDB-specific implementation
   - Reads `KoanServiceAttribute` from `MongoAdapterFactory`
   - Implements MongoDB ping health check
   - Handles MongoDB connection string formatting

4. **`MongoOptionsConfigurator`** (Koan.Data.Connector.Mongo)
   - Configuration-time orchestration
   - Calls discovery coordinator when `connectionString == "auto"`
   - Applies discovered URL to options

**Current Discovery Methods** (in priority order):
1. Service-specific environment variables (e.g., `MONGO_URLS`)
2. Explicit configuration (`Koan:Data:Mongo:ConnectionString`)
3. Aspire service discovery (`services:mongodb:default:0`)
4. Container instance (`mongodb://mongo:27017` when `KoanEnv.InContainer`)
5. Local fallback (`mongodb://localhost:27017`)

---

## Proposed Auto-Configuration Architecture

### Design Goals

1. **Unified pipeline** for all adapter types (data, cache, messaging, AI)
2. **Zen-garden as optional resolver** in existing discovery chain
3. **Zero breaking changes** to current architecture
4. **Graceful fallback** - zen-garden → existing methods
5. **Package reference enables** - `Koan.ZenGarden` auto-registers resolver

### Proposed Flow

```
MongoOptionsConfigurator.ConfigureProviderSpecific()
  → checks if connectionString uses protocol ("zen-garden:mongodb")
    → calls ConnectionProtocolRegistry.ResolveAsync("zen-garden:mongodb")
      → Looks up "zen-garden" protocol resolver (if Koan.ZenGarden referenced) ← NEW
        → ZenGardenProtocolResolver.ResolveAsync("mongodb")
          → Queries mDNS for "_koan-stone._tcp.local." offering "mongodb"
          → Returns "mongodb://192.168.1.100:27017" (native)
             OR "http://localhost:5000" (gateway)
      → If protocol not found → logs warning → uses connection string as-is
  → If connectionString is "auto" or null
    → ResolveAutonomousConnection() (existing IServiceDiscoveryCoordinator)
      → Environment vars → Explicit config → Aspire → Container → Local
  → MongoOptionsConfigurator sets options.ConnectionString
```

### Implementation Strategy

**Protocol-Based Resolver Registry** (RECOMMENDED)

Implement protocol resolver registration in `Koan.Core`, zen-garden registers as `"zen-garden:"` protocol handler.

**Advantages**:
- ✅ **Koan.Core protocol-agnostic** - never aware of zen-garden
- ✅ Extensible for other protocols (consul:, etcd:, aspire:)
- ✅ Automatic registration via `IKoanInitializer`
- ✅ Works for all adapter types (data, cache, messaging, AI)
- ✅ Graceful degradation - protocol not found → logs warning → uses as-is
- ✅ Clean separation - protocol parsing vs. service resolution

**Implementation**:

```csharp
// Koan.ZenGarden/Discovery/ZenGardenDiscoveryAdapter.cs

/// <summary>
/// Universal zen-garden discovery adapter.
/// Handles all service types via mDNS Stone announcement.
/// Priority 5 (higher than native adapters at priority 10).
/// </summary>
internal sealed class ZenGardenDiscoveryAdapter : IServiceDiscoveryAdapter
{
    private readonly GardenClient _gardenClient;
    private readonly ILogger<ZenGardenDiscoveryAdapter> _logger;

    public string ServiceName => "*"; // Wildcard - handles all services
    public string[] Aliases => Array.Empty<string>();
    public int Priority => 5; // Higher priority than native adapters

    public ZenGardenDiscoveryAdapter(
        GardenClient gardenClient,
        ILogger<ZenGardenDiscoveryAdapter> logger)
    {
        _gardenClient = gardenClient;
        _logger = logger;
    }

    public async Task<AdapterDiscoveryResult> DiscoverAsync(
        DiscoveryContext context,
        CancellationToken cancellationToken = default)
    {
        KoanLog.ConfigDebug(_logger, "zengarden.discovery.start", null,
            ("service", context.ServiceName));

        try
        {
            // Query mDNS for Stone offering this service
            var stone = await _gardenClient.FindOfferingAsync(
                context.ServiceName,
                cancellationToken);

            if (stone == null)
            {
                KoanLog.ConfigDebug(_logger, "zengarden.discovery", "no-stone",
                    ("service", context.ServiceName));
                return AdapterDiscoveryResult.Failed(
                    context.ServiceName,
                    "No zen-garden Stone found");
            }

            // Build connection string based on service type
            var connectionString = BuildConnectionString(
                context.ServiceName,
                stone,
                context.Parameters);

            KoanLog.ConfigInfo(_logger, "zengarden.discovery", "success",
                ("service", context.ServiceName),
                ("stone", stone.Host),
                ("port", stone.Port));

            // Validate health using service-specific validation
            // (delegate to native adapter's health check)
            var isHealthy = await ValidateServiceHealth(
                connectionString,
                context,
                cancellationToken);

            return AdapterDiscoveryResult.Success(
                context.ServiceName,
                connectionString,
                "zen-garden-mDNS",
                isHealthy);
        }
        catch (Exception ex)
        {
            KoanLog.ConfigWarning(_logger, "zengarden.discovery", "exception",
                ("service", context.ServiceName),
                ("error", ex.Message));
            return AdapterDiscoveryResult.Failed(
                context.ServiceName,
                $"Zen-garden exception: {ex.Message}");
        }
    }

    private string BuildConnectionString(
        string serviceName,
        Stone stone,
        IDictionary<string, object>? parameters)
    {
        // Service-specific connection string formatting
        return serviceName.ToLower() switch
        {
            "mongo" or "mongodb" => BuildMongoConnectionString(stone, parameters),
            "postgres" or "postgresql" => BuildPostgresConnectionString(stone, parameters),
            "redis" => BuildRedisConnectionString(stone, parameters),
            "rabbitmq" => BuildRabbitMqConnectionString(stone, parameters),
            _ => $"{serviceName}://{stone.Host}:{stone.Port}"
        };
    }

    private string BuildMongoConnectionString(Stone stone, IDictionary<string, object>? parameters)
    {
        var auth = "";
        var database = "";

        if (parameters != null)
        {
            if (parameters.TryGetValue("username", out var user) &&
                parameters.TryGetValue("password", out var pass))
            {
                auth = $"{user}:{pass}@";
            }

            if (parameters.TryGetValue("database", out var db))
            {
                database = $"/{db}";
            }
        }

        return $"mongodb://{auth}{stone.Host}:{stone.Port}{database}";
    }

    private string BuildPostgresConnectionString(Stone stone, IDictionary<string, object>? parameters)
    {
        var parts = new List<string>
        {
            $"Host={stone.Host}",
            $"Port={stone.Port}"
        };

        if (parameters != null)
        {
            if (parameters.TryGetValue("username", out var user))
                parts.Add($"Username={user}");
            if (parameters.TryGetValue("password", out var pass))
                parts.Add($"Password={pass}");
            if (parameters.TryGetValue("database", out var db))
                parts.Add($"Database={db}");
        }

        return string.Join(";", parts);
    }

    private string BuildRedisConnectionString(Stone stone, IDictionary<string, object>? parameters)
    {
        return $"{stone.Host}:{stone.Port}";
    }

    private string BuildRabbitMqConnectionString(Stone stone, IDictionary<string, object>? parameters)
    {
        var auth = "";
        if (parameters != null &&
            parameters.TryGetValue("username", out var user) &&
            parameters.TryGetValue("password", out var pass))
        {
            auth = $"{user}:{pass}@";
        }

        return $"amqp://{auth}{stone.Host}:{stone.Port}";
    }

    private async Task<bool> ValidateServiceHealth(
        string connectionString,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        if (!context.RequireHealthValidation) return true;

        // Delegate to native adapter's health check
        // (MongoDB ping, PostgreSQL SELECT 1, etc.)
        // Option: Cache native health validators by service name
        // For now: return true (Stone existence is implicit health)
        return true;
    }
}

// Koan.ZenGarden/Initialization/KoanAutoRegistrar.cs

/// <summary>
/// Auto-registers zen-garden discovery adapter when package is referenced.
/// </summary>
public class KoanAutoRegistrar : IKoanInitializer
{
    public void Initialize(IServiceCollection services, IConfiguration configuration)
    {
        // Register GardenClient
        services.AddSingleton<GardenClient>();

        // Register zen-garden discovery adapter with highest priority
        services.AddSingleton<IServiceDiscoveryAdapter, ZenGardenDiscoveryAdapter>();
    }
}
```

---

**Option B: Zen-Garden as Explicit Resolver Method**

Add zen-garden as a discovery method **within** each adapter's `BuildDiscoveryCandidates()`.

**Disadvantages**:
- ❌ Requires changes to every native adapter (12+ adapters)
- ❌ Tight coupling - each adapter must know about zen-garden
- ❌ Duplicates zen-garden logic across adapters
- ❌ Harder to disable/remove zen-garden later

**NOT RECOMMENDED** - violates existing architecture patterns.

---

## Proposed Changes

### 1. Update `ServiceDiscoveryCoordinator`

**Current**: Routes by exact service name match  
**Proposed**: Add wildcard adapter support

```csharp
// Koan.Core/Orchestration/ServiceDiscoveryCoordinator.cs

public async Task<AdapterDiscoveryResult> DiscoverServiceAsync(
    string serviceName,
    DiscoveryContext? context = null,
    CancellationToken cancellationToken = default)
{
    context ??= new DiscoveryContext();
    context.ServiceName = serviceName; // Pass service name to adapters

    // Try specific adapter first
    if (_adapters.TryGetValue(serviceName.ToLowerInvariant(), out var adapter))
    {
        var result = await TryAdapter(adapter, serviceName, context, cancellationToken);
        if (result.IsSuccessful) return result;
    }

    // Try wildcard adapters (zen-garden)
    if (_adapters.TryGetValue("*", out var wildcardAdapter))
    {
        var result = await TryAdapter(wildcardAdapter, serviceName, context, cancellationToken);
        if (result.IsSuccessful) return result;
    }

    KoanLog.ConfigWarning(_logger, LogActions.Lookup, "no-adapter", ("service", serviceName));
    return AdapterDiscoveryResult.NoAdapter(serviceName);
}

private async Task<AdapterDiscoveryResult> TryAdapter(
    IServiceDiscoveryAdapter adapter,
    string serviceName,
    DiscoveryContext context,
    CancellationToken cancellationToken)
{
    KoanLog.ConfigDebug(_logger, LogActions.Delegate, null,
        ("service", serviceName),
        ("adapter", adapter.GetType().Name));

    try
    {
        var result = await adapter.DiscoverAsync(context, cancellationToken);
        var outcome = result.IsSuccessful ? LogOutcomes.Success : LogOutcomes.Failure;
        KoanLog.ConfigInfo(_logger, LogActions.Result, outcome,
            ("service", serviceName),
            ("url", result.ServiceUrl));
        return result;
    }
    catch (Exception ex)
    {
        KoanLog.ConfigError(_logger, LogActions.Result, "exception",
            ("service", serviceName),
            ("adapter", adapter.GetType().Name),
            ("error", ex.Message));
        return AdapterDiscoveryResult.Failed(serviceName, $"Adapter exception: {ex.Message}");
    }
}
```

### 2. Update `DiscoveryContext`

Add `ServiceName` property for wildcard adapters:

```csharp
// Koan.Core/Orchestration/Abstractions/DiscoveryContext.cs

public class DiscoveryContext
{
    public string ServiceName { get; set; } = string.Empty; // ← NEW
    public OrchestrationMode OrchestrationMode { get; set; }
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public bool RequireHealthValidation { get; set; } = true;
    public IDictionary<string, object>? Parameters { get; set; }
}
```

### 3. Create `Koan.ZenGarden` Package

**Structure**:
```
Koan.ZenGarden/
├── GardenClient.cs                      (mDNS query logic)
├── Stone.cs                             (record Stone(Host, Port, Offering))
├── Discovery/
│   └── ZenGardenDiscoveryAdapter.cs     (IServiceDiscoveryAdapter impl)
└── Initialization/
    └── KoanAutoRegistrar.cs             (auto-registration)
```

**Package Dependencies**:
- `Koan.Core` (for IServiceDiscoveryAdapter)
- `Zeroconf` (for mDNS queries)

### 4. Update Week 1 Roadmap

**Days 1-2**: Environment setup + architecture finalization  
**Days 3-4**: Implement `ZenGardenDiscoveryAdapter` and wildcard routing  
**Days 5-7**: Testing (MongoDB, PostgreSQL, Redis discovery via zen-garden)

---

## User Experience Examples

### Scenario 1: Implicit Zen-Garden (Zero-Config)

**User adds package**:
```xml
<PackageReference Include="Koan.ZenGarden" Version="1.0.0" />
```

**Adapter bootstrap**:
```csharp
// MongoOptionsConfigurator.ConfigureProviderSpecific()
// connectionString is null → calls ResolveAutonomousConnection()

var options = new MongoOptions { ConnectionString = null };
configurator.ConfigureProviderSpecific(options);

// Logs:
// [Info] mongo.discovery: auto-mode
// [Debug] zengarden.discovery.start: service=mongo
// [Info] zengarden.discovery: success, stone=192.168.1.100, port=27017
// [Info] mongo.config: connection-explicit, source=zen-garden-mDNS
```

**Result**: `options.ConnectionString = "mongodb://192.168.1.100:27017"`

---

### Scenario 2: Zen-Garden Not Available (Graceful Fallback)

**User has `Koan.ZenGarden` but no Stones announcing**:

```csharp
var options = new MongoOptions { ConnectionString = null };
configurator.ConfigureProviderSpecific(options);

// Logs:
// [Debug] zengarden.discovery.start: service=mongo
// [Debug] zengarden.discovery: no-stone, service=mongo
// [Debug] mongo.discovery.try: method=local, url=mongodb://localhost:27017
// [Debug] mongo.discovery.health: success, url=mongodb://localhost:27017
// [Info] mongo.config: connection-explicit, source=local
```

**Result**: Falls back to localhost (existing behavior)

---

### Scenario 3: No Zen-Garden Package

**User does NOT reference `Koan.ZenGarden`**:

```csharp
var options = new MongoOptions { ConnectionString = null };
configurator.ConfigureProviderSpecific(options);

// Logs:
// [Debug] mongo.discovery.try: method=local, url=mongodb://localhost:27017
// [Debug] mongo.discovery.health: success
// [Info] mongo.config: connection-explicit, source=local
```

**Result**: Existing behavior unchanged

---

### Scenario 4: Cross-Adapter Discovery

**User references `Koan.ZenGarden` once, all adapters gain zen-garden discovery**:

```csharp
// MongoDB
services.AddKoanData(options => options.UseMongo()); // auto-discovers via zen-garden

// Redis Cache
services.AddKoanCache(options => options.UseRedis()); // auto-discovers via zen-garden

// RabbitMQ Messaging
services.AddKoanMessaging(options => options.UseRabbitMq()); // auto-discovers via zen-garden
```

**All three** discover their respective Stones via mDNS, fall back to local if unavailable.

---

## Migration Path

### Phase 1: Non-Breaking Addition (Week 1-2)

1. Add wildcard adapter support to `ServiceDiscoveryCoordinator`
2. Implement `ZenGardenDiscoveryAdapter`
3. Package `Koan.ZenGarden` with auto-registration
4. Test with MongoDB, PostgreSQL, Redis

**Impact**: Zero breaking changes, existing adapters work unchanged

### Phase 2: Documentation (Week 3)

1. Update adapter configuration guides
2. Add zen-garden quickstart
3. Document fallback behavior

### Phase 3: Validation (Week 4)

1. Integration tests: zen-garden → local fallback
2. Cross-platform tests (Windows, Ubuntu, macOS)
3. Multi-adapter tests (MongoDB + Redis + RabbitMQ)

---

## Success Criteria

**Functional**:
- ✅ `Koan.ZenGarden` package reference enables discovery for all adapters
- ✅ Zen-garden failure gracefully falls back to existing discovery methods
- ✅ No breaking changes to existing adapter configuration
- ✅ Works for data, cache, messaging, AI adapters

**Performance**:
- ✅ mDNS query completes in <500ms
- ✅ Fallback to local adds <100ms overhead

**Developer Experience**:
- ✅ Zero configuration required
- ✅ Clear logging shows discovery path (zen-garden → local)
- ✅ Works identically across Windows, Ubuntu, macOS

---

## Open Questions

1. **Health validation delegation**: Should `ZenGardenDiscoveryAdapter` delegate health checks to native adapters, or trust Stone existence as implicit health?
   - **Proposal**: Trust Stone existence initially, add delegated health checks in Phase 2

2. **Service name normalization**: How do we map Stone offerings to service names?
   - **Proposal**: Stone announces "mongodb", coordinator normalizes "mongo" → "mongodb"

3. **Gateway mode**: How does zen-garden discovery differ for gateway vs native Stones?
   - **Proposal**: Stone announces both offerings ("mongodb" + "database:documentDatabase::mongodb"), adapter picks based on capabilities

4. **Lantern integration**: When do we add Lantern-based discovery?
   - **Proposal**: Phase 2 Milestone 5 (Weeks 11-12), after mDNS validates

---

## Recommendation

**Implement Option A** (Zen-Garden as Priority Adapter) with wildcard routing in `ServiceDiscoveryCoordinator`.

**Rationale**:
- Minimal changes to existing architecture
- Clean separation of concerns
- Works for all adapter types
- Easy to disable/remove if needed
- Follows existing patterns

**Estimated LOC**:
- `ZenGardenDiscoveryAdapter`: ~200 LOC
- `ServiceDiscoveryCoordinator` changes: ~30 LOC
- `DiscoveryContext` changes: ~5 LOC
- `GardenClient` (from ROADMAP): ~100 LOC
- **Total**: ~335 LOC

**Timeline**: Week 1 implementation (Days 3-7)
