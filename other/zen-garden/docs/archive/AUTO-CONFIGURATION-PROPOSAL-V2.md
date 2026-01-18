# Auto-Configuration Protocol Resolver: Architecture Proposal

**Date**: January 14, 2026  
**Status**: Draft - Protocol-Based Design  
**Related**: [DATA-0088](../../../docs/decisions/DATA-0088-adapter-auto-configuration-resolver-pipeline.md)

---

## Executive Summary

**Proposal**: Implement **protocol-based connection resolver registry** in `Koan.Core` where `Koan.ZenGarden` registers as `"zen-garden:"` protocol handler.

**Key Principle**: **Koan.Core never knows zen-garden exists** - purely protocol-agnostic registry.

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

2. **`ServiceDiscoveryAdapterBase`** (Koan.Core)
   - Base class with common discovery patterns
   - Template method: `BuildDiscoveryCandidates()`

3. **`MongoDiscoveryAdapter`** (Koan.Data.Connector.Mongo)
   - MongoDB-specific implementation
   - Reads `KoanServiceAttribute` from `MongoAdapterFactory`
   - Implements MongoDB ping health check

4. **`MongoOptionsConfigurator`** (Koan.Data.Connector.Mongo)
   - Configuration-time orchestration
   - Calls discovery coordinator when `connectionString == "auto"`

**Current Discovery Methods** (in priority order):
1. Service-specific environment variables (e.g., `MONGO_URLS`)
2. Explicit configuration (`Koan:Data:Mongo:ConnectionString`)
3. Aspire service discovery (`services:mongodb:default:0`)
4. Container instance (`mongodb://mongo:27017` when `KoanEnv.InContainer`)
5. Local fallback (`mongodb://localhost:27017`)

---

## Proposed Protocol-Based Architecture

### Design Goals

1. **Koan.Core protocol-agnostic** - never aware of specific protocols
2. **Extensible registry** - supports zen-garden, consul, etcd, aspire, etc.
3. **Graceful degradation** - protocol not found → log warning → use as-is
4. **Zero breaking changes** to existing discovery
5. **Works for all adapter types** (data, cache, messaging, AI)

### Proposed Flow

```
User code:
  options.UseMongoDb("zen-garden:mongodb");

MongoOptionsConfigurator.ConfigureProviderSpecific():
  1. Check if connectionString uses protocol
     ConnectionProtocolRegistry.HasProtocol("zen-garden:mongodb") → true
  
  2. Resolve protocol
     ConnectionProtocolRegistry.ResolveAsync("zen-garden:mongodb", parameters)
       → Parse: protocol="zen-garden", identifier="mongodb"
       → Lookup "zen-garden" resolver
       → ZenGardenProtocolResolver.ResolveAsync("mongodb", parameters)
          → mDNS query for Stone offering "mongodb"
          → Returns "mongodb://192.168.1.100:27017" (native)
             OR "http://localhost:5000" (gateway)
  
  3. If resolved != null:
     connectionString = "mongodb://192.168.1.100:27017"
  
  4. If connectionString still null/"auto":
     ResolveAutonomousConnection() (existing discovery coordinator)
```

---

## Implementation

### 1. Koan.Core - Protocol-Agnostic Registry

```csharp
// Koan.Core/ConnectionProtocolRegistry.cs (~50 LOC)

namespace Koan.Core;

/// <summary>
/// Stone information returned by protocol resolvers.
/// </summary>
public record Stone(string Host, int Port, string Offering);

/// <summary>
/// Protocol-based connection resolver registry.
/// Koan.Core is protocol-agnostic - never aware of specific resolvers.
/// Returns Stone info only - adapters translate to connection strings.
/// </summary>
public static class ConnectionProtocolRegistry
{
    private static readonly ConcurrentDictionary<string, IConnectionProtocolResolver> _resolvers = new();

    public static void RegisterProtocol(IConnectionProtocolResolver resolver)
    {
        _resolvers[resolver.Protocol.ToLowerInvariant()] = resolver;
    }

    public static async Task<Stone?> ResolveAsync(
        string connectionString,
        CancellationToken ct = default)
    {
        var colonIndex = connectionString.IndexOf(':');
        if (colonIndex <= 0) return null;

        var protocol = connectionString.Substring(0, colonIndex).ToLowerInvariant();
        var identifier = connectionString.Substring(colonIndex + 1);

        if (_resolvers.TryGetValue(protocol, out var resolver))
        {
            return await resolver.ResolveAsync(identifier, ct);
        }

        return null; // No resolver registered for this protocol
    }

    public static bool HasProtocol(string connectionString)
    {
        var colonIndex = connectionString.IndexOf(':');
        if (colonIndex <= 0) return false;

        var protocol = connectionString.Substring(0, colonIndex).ToLowerInvariant();
        return _resolvers.ContainsKey(protocol);
    }
}

public interface IConnectionProtocolResolver
{
    string Protocol { get; } // "zen-garden", "consul", "aspire", etc.
    Task<Stone?> ResolveAsync(
        string serviceIdentifier,
        CancellationToken ct);
}
```

### 2. Adapter Configurator Updates

**Add protocol resolution check** (~15 LOC per adapter):

```csharp
// MongoOptionsConfigurator.ConfigureProviderSpecific()

public void ConfigureProviderSpecific(MongoOptions options)
{
    var connectionString = options.ConnectionString;

    // 1. Check for protocol resolver ("zen-garden:mongodb", "consul:mongo", etc.)
    if (!string.IsNullOrEmpty(connectionString) &&
        ConnectionProtocolRegistry.HasProtocol(connectionString))
    {
        var stone = await ConnectionProtocolRegistry.ResolveAsync(
            connectionString,
            ct: CancellationToken.None);

        if (stone != null)
        {
            // MongoDB adapter translates Stone → MongoDB connection string
            connectionString = BuildMongoConnectionString(
                stone,
                options.Database,
                options.Username,
                options.Password);

            _logger.LogInformation(
                "Resolved protocol: {Original} → {Host}:{Port} → {ConnectionString}",
                options.ConnectionString, stone.Host, stone.Port, connectionString);
        }
        else
        {
            _logger.LogWarning(
                "Protocol resolver failed: {ConnectionString}",
                connectionString);
        }
    }

    // 2. If null/empty/"auto", use existing discovery coordinator
    if (string.IsNullOrEmpty(connectionString) ||
        connectionString.Equals("auto", StringComparison.OrdinalIgnoreCase))
    {
        connectionString = ResolveAutonomousConnection(); // Existing logic
    }

    options.ConnectionString = connectionString;
}

// MongoDB adapter knows how to build its own connection string
private string BuildMongoConnectionString(
    Stone stone,
    string? database,
    string? username,
    string? password)
{
    var auth = string.IsNullOrEmpty(username) ? "" : $"{username}:{password ?? ""}@";
    var db = string.IsNullOrEmpty(database) ? "" : $"/{database}";
    return $"mongodb://{auth}{stone.Host}:{stone.Port}{db}";
}
```

### 3. Koan.ZenGarden - Protocol Resolver

```csharp
// Koan.ZenGarden/ZenGardenProtocolResolver.cs (~150 LOC)

namespace Koan.ZenGarden;

/// <summary>
/// Zen-garden protocol resolver: "zen-garden:mongodb" → "mongodb://host:port"
/// Koan.Core never knows this exists - pure protocol registration.
/// </summary>
internal sealed class ZenGardenProtocolResolver : IConnectionProtocolResolver
{
    private readonly GardenClient _gardenClient;
    private readonly ILogger<ZenGardenProtocolResolver> _logger;

    public string Protocol => "zen-garden";

    public ZenGardenProtocolResolver(
        GardenClient gardenClient,
        ILogger<ZenGardenProtocolResolver> logger)
    {
        _gardenClient = gardenClient;
        _logger = logger;
    }

    public async Task<Stone?> ResolveAsync(
        string serviceIdentifier,
        CancellationToken ct)
    {
        KoanLog.ConfigDebug(_logger, "zengarden.protocol.resolve", null,
            ("identifier", serviceIdentifier));

        try
        {
            // Query mDNS for Stone - return Stone info only
            // Adapters will translate Stone → connection string
            var stone = await _gardenClient.FindOfferingAsync(serviceIdentifier, ct);

            if (stone == null)
            {
                KoanLog.ConfigDebug(_logger, "zengarden.protocol", "no-stone",
                    ("identifier", serviceIdentifier));
                return null;
            }

            KoanLog.ConfigInfo(_logger, "zengarden.protocol", "success",
                ("identifier", serviceIdentifier),
                ("stone", stone.Host),
                ("port", stone.Port));

            return stone;
        }
        catch (Exception ex)
        {
            KoanLog.ConfigWarning(_logger, "zengarden.protocol", "exception",
                ("identifier", serviceIdentifier),
                ("error", ex.Message));
            return null;
        }
    }
}

// Koan.ZenGarden/Initialization/KoanAutoRegistrar.cs (~20 LOC)

/// <summary>
/// Auto-registers zen-garden protocol resolver when package is referenced.
/// Koan.Core never knows this exists.
/// </summary>
public class KoanAutoRegistrar : IKoanInitializer
{
    public void Initialize(IServiceCollection services, IConfiguration configuration)
    {
        // Register GardenClient
        services.AddSingleton<GardenClient>();

        // Build and register protocol resolver
        var serviceProvider = services.BuildServiceProvider();
        var client = new GardenClient();
        var logger = serviceProvider.GetService<ILogger<ZenGardenProtocolResolver>>();
        var resolver = new ZenGardenProtocolResolver(client, logger);

        ConnectionProtocolRegistry.RegisterProtocol(resolver);
    }
}
```

---

## User Experience Examples

### Scenario 1: Explicit Protocol (Zen-Garden)

```csharp
services.AddKoanData(options => {
    options.UseMongoDb("zen-garden:mongodb");
});
```

**Logs**:
```
[Debug] mongo.config: checking protocol resolver
[Debug] zengarden.protocol.resolve: identifier=mongodb
[Info] zengarden.protocol: success, stone=192.168.1.100, port=27017
[Info] mongo.config: resolved protocol, zen-garden:mongodb → mongodb://192.168.1.100:27017
```

**Result**: `connectionString = "mongodb://192.168.1.100:27017"`

### Scenario 2: Protocol Not Found (Package Not Referenced)

```csharp
services.AddKoanData(options => {
    options.UseMongoDb("zen-garden:mongodb"); // Koan.ZenGarden not referenced
});
```

**Logs**:
```
[Debug] mongo.config: checking protocol resolver
[Warning] mongo.config: protocol resolver failed, zen-garden:mongodb
[Info] mongo.config: using connection string as-is
```

**Result**: Adapter attempts connection to `"zen-garden:mongodb"` literally (will fail)

### Scenario 3: Auto-Discovery (Existing Behavior)

```csharp
services.AddKoanData(options => {
    options.UseMongoDb(); // connectionString = null
});
```

**Logs**:
```
[Info] mongo.discovery: auto-mode
[Debug] mongo.discovery.try: method=local, url=mongodb://localhost:27017
[Debug] mongo.discovery.health: success
[Info] mongo.config: connection-explicit, source=local
```

**Result**: Existing discovery coordinator (unchanged)

### Scenario 4: Cross-Adapter Protocol Usage

```csharp
// Single Koan.ZenGarden package reference enables all

services.AddKoanData(options =>
    options.UseMongo("zen-garden:mongodb"));

services.AddKoanCache(options =>
    options.UseRedis("zen-garden:redis"));

services.AddKoanMessaging(options =>
    options.UseRabbitMq("zen-garden:rabbitmq"));
```

**All three** use same protocol resolver.

---

## Implementation Plan

### Week 1 Tasks

**Days 1-2**: Architecture finalization + environment setup

**Days 3-4**: Core implementation
- `ConnectionProtocolRegistry` (Koan.Core) - ~50 LOC
- `IConnectionProtocolResolver` interface - ~10 LOC
- `ZenGardenProtocolResolver` (Koan.ZenGarden) - ~150 LOC

**Days 5-7**: Adapter integration + testing
- Update `MongoOptionsConfigurator` - ~15 LOC
- Update `PostgresOptionsConfigurator` - ~15 LOC
- Update `RedisOptionsConfigurator` - ~15 LOC
- Integration tests (Docker Compose, cross-platform)

### Estimated LOC

| Component | LOC |
|-----------|-----|
| `ConnectionProtocolRegistry` (Koan.Core) | ~50 |
| `IConnectionProtocolResolver` interface | ~10 |
| `ZenGardenProtocolResolver` | ~150 |
| `GardenClient` (from ROADMAP) | ~100 |
| Adapter configurator updates (12 adapters × 15 LOC) | ~180 |
| **Total** | **~490** |

---

## Benefits

✅ **Koan.Core protocol-agnostic** - never aware of zen-garden or any protocol  
✅ **Extensible** - future protocols (consul:, etcd:, aspire:) follow same pattern  
✅ **Zero breaking changes** - existing discovery coordinator unchanged  
✅ **Graceful degradation** - protocol not found → logs warning → uses as-is  
✅ **Cross-adapter support** - single package enables all adapter types  
✅ **Clean separation** - protocol parsing (Koan.Core) vs. resolution (protocol packages)  

---

## Open Questions

1. **Protocol resolution timing**: Should protocol resolution be async in configurators?
   - **Current**: Synchronous with `.GetAwaiter().GetResult()`
   - **Better**: Make `ConfigureProviderSpecific` async in future refactor

2. **Service identifier normalization**: "mongodb" vs "mongo" in Stone announcements?
   - **Proposal**: Stone announces "mongodb", resolver handles normalization

3. **Gateway vs native selection**: How does protocol resolver choose?
   - **Proposal**: Check Stone capabilities, prefer native if available

4. **Fallback to auto-discovery**: Should `"zen-garden:mongodb"` fall back to auto-discovery if no Stone found?
   - **Current proposal**: No - protocol is explicit, failure is explicit
   - **Alternative**: Yes - fall through to auto-discovery for resilience

---

## Recommendation

**Implement protocol-based resolver registry** in `Koan.Core` with zen-garden as first protocol handler.

**Timeline**: Week 1 implementation (January 13-20, 2026)

**Risk**: LOW - non-breaking addition, isolated to configurator updates
