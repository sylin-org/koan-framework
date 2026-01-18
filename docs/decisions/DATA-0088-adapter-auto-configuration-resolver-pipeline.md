# DATA-0088: Adapter auto-configuration resolver pipeline

Status: Proposed

## Context

Adapters across **all domains** (data, cache, messaging, AI) currently implement standalone auto-configuration when no connection string is provided - probing Docker Compose stacks, localhost, environment variables, etc. With zen-garden introduction, we need service discovery without duplicating logic or creating zen-garden-specific contracts.

**Scope**: This applies to all adapter types - MongoDB, PostgreSQL, Redis, RabbitMQ, OpenAI, LM Studio, etc.

## Decision

### 1. Protocol-based connection resolver registry in Koan.Core

Introduce a protocol resolver registry that handles custom connection string schemes:

```csharp
// Koan.Core (protocol-agnostic infrastructure)
public record Stone(string Host, int Port, string Offering);

public interface IConnectionProtocolResolver
{
    string Protocol { get; } // "zen-garden", "aspire", "consul", etc.
    Task<Stone?> ResolveAsync(
        string serviceIdentifier,
        CancellationToken ct = default);
}

// Protocol resolver registry (Koan.Core)
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
        // Parse protocol: "zen-garden:mongodb" → protocol="zen-garden", identifier="mongodb"
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
```

### 2. Protocol resolver implementations

**Zen-garden protocol resolver** (optional package):
- Protocol: `"zen-garden"`
- Probes: mDNS service discovery → Lantern directory
- Lives in: `Koan.ZenGarden` (optional dependency, **cross-cutting**)
- Registers automatically when package is referenced
- Returns **Stone info only** (host, port, offering)
- **Adapters translate Stone → connection string** (adapter knows its own format)
- Works for **all adapter types**: data, cache, messaging, AI

**Example implementation**:
```csharp
// Koan.ZenGarden/ZenGardenProtocolResolver.cs
public class ZenGardenProtocolResolver : IConnectionProtocolResolver
{
    private readonly GardenClient _client;
    public string Protocol => "zen-garden";

    public ZenGardenProtocolResolver(GardenClient client) => _client = client;

    public async Task<Stone?> ResolveAsync(
        string serviceIdentifier,
        CancellationToken ct)
    {
        // serviceIdentifier = "mongodb" from "zen-garden:mongodb"
        // Just find Stone - don't build connection string
        return await _client.FindOfferingAsync(serviceIdentifier, ct);
    }
}
```

**Standalone auto-config** (existing, unchanged):
- Uses existing `IServiceDiscoveryCoordinator` pattern
- Probes: Docker Compose labels → localhost → environment variables
- Lives in: adapter packages (MongoDB, PostgreSQL, Redis, etc.)
- Activated when connectionString is `null` or `"auto"`

### 3. Adapter bootstrap integration

Adapters check for protocol resolvers, then **translate Stone to connection string** using adapter-specific logic:

```csharp
// MongoOptionsConfigurator.ConfigureProviderSpecific()
public void ConfigureProviderSpecific(MongoOptions options)
{
    var connectionString = options.ConnectionString;

    // 1. Check if connection string uses custom protocol
    if (!string.IsNullOrEmpty(connectionString) && 
        ConnectionProtocolRegistry.HasProtocol(connectionString))
    {
        var stone = await ConnectionProtocolRegistry.ResolveAsync(
            connectionString,
            ct: CancellationToken.None);

        if (stone != null)
        {
            // Adapter translates Stone → MongoDB connection string
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
                "Protocol resolver failed for {ConnectionString}",
                connectionString);
        }
    }

    // 2. If still null/empty/"auto", use existing discovery coordinator
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

### 4. User experience paths

**Explicit protocol (per-adapter control)**:
```csharp
services.AddKoanData(options => {
    options.UseMongoDb("zen-garden:mongodb"); // Protocol-based resolution
});
```
- Adapter checks `ConnectionProtocolRegistry` for `"zen-garden"` resolver
- If `Koan.ZenGarden` package referenced → resolver registered → mDNS lookup
- If package not referenced → registry returns null → logs warning → uses connection string as-is
- **Koan.Core never knows zen-garden exists** - purely protocol-based

**Auto-discovery (zero-config)**:
```csharp
services.AddKoanData(options => {
    options.UseMongoDb(); // connectionString = null → auto-discovery
});
```
- Uses existing `IServiceDiscoveryCoordinator` (Docker Compose → localhost)
- **Not** zen-garden by default (zen-garden requires explicit protocol or separate integration)
- To enable zen-garden in auto-discovery mode, see integration option below

**Manual connection string (traditional)**:
```csharp
options.UseMongoDb("mongodb://localhost:27017");
```
- Skips auto-configuration entirely
- Direct connection as before

### 5. Optional: Zen-garden in auto-discovery mode

For users who want zen-garden as **first priority** in auto-discovery (not explicit protocol):

```csharp
// Koan.ZenGarden can optionally integrate with IServiceDiscoveryCoordinator
// by registering a high-priority wildcard adapter (see AUTO-CONFIGURATION-PROPOSAL.md)

services.AddKoanData(options => {
    options.UseMongoDb(); // null → auto-discovery
});

// With Koan.ZenGarden referenced:
// 1. Try zen-garden mDNS (if ZenGardenDiscoveryAdapter registered)
// 2. Fall back to Docker Compose
// 3. Fall back to localhost
```

This provides two integration modes:
- **Protocol-based** (explicit): `"zen-garden:mongodb"` - requires Koan.ZenGarden
- **Discovery-based** (implicit): `null` - zen-garden as first auto-discovery method

## Consequences

**Benefits**:
- **Koan.Core protocol-agnostic** - never aware of zen-garden or any specific resolver
- Protocol resolver registry extensible for other discovery systems (Consul, etcd, Aspire, etc.)
- Zero breaking changes - existing adapters work unchanged
- Clear separation: protocol resolution (explicit) vs. auto-discovery (implicit)
- Graceful degradation: protocol resolver not found → logs warning → uses connection string as-is
- Works for **all adapter types** (data, cache, messaging, AI) without adapter-specific code

**Complexity**:
- Resolver priority/ordering must be clear
- Error handling: what if zen-garden resolver fails? (Answer: fall through to next resolver)
- Logging: must clearly show which resolver succeeded

**Edge cases**:
- Multiple resolvers succeed: Use highest priority (zen-garden wins)
- All resolvers fail: Adapter initialization fails with helpful error
- Resolver throws exception: Log and continue to next resolver
- Circular dependencies: N/A (resolvers are stateless)

## Follow-ups

**Koan.Core** (protocol-agnostic infrastructure):
- Define `IConnectionProtocolResolver` interface
- Implement `ConnectionProtocolRegistry` static registry
- Add protocol parsing and resolver lookup logic (~50 LOC)

**Existing adapter configurators** (all types):
- Update `ConfigureProviderSpecific()` to check `ConnectionProtocolRegistry.HasProtocol()`
- Call `ConnectionProtocolRegistry.ResolveAsync()` before auto-discovery
- Examples: `MongoOptionsConfigurator`, `PostgresOptionsConfigurator`, `RedisOptionsConfigurator`
- **Estimated**: ~15 LOC change per configurator (12 adapters = ~180 LOC total)

**Koan.ZenGarden package** (protocol resolver):
- Implement `ZenGardenProtocolResolver : IConnectionProtocolResolver`
- Auto-register protocol resolver on package load (`IKoanInitializer`)
- Handle service identifier mapping (e.g., "mongodb" → Stone offering)
- Return native connection string (`mongodb://...`) or gateway URL (`http://...`)
- Optionally: Register `ZenGardenDiscoveryAdapter` for auto-discovery mode integration

**Documentation**:
- Update adapter configuration guides with auto-configuration behavior
- Document resolver priority and fallback chain
- Add troubleshooting guide for auto-config failures

## Related decisions

- zen-garden ROADMAP.md (Week 1 architecture)
- DATA-0061: Data access pagination and streaming
- ARCH-0044: Standardized module config and discovery
