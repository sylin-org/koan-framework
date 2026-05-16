# Koan × Zen Garden Integration Proposal

**Status:** Proposal  
**Author:** Claude (via Koan Engineering)  
**Date:** 2026-01-28  
**Zen Garden Driver Spec:** v2.0 (2026-01-27)  
**Koan Framework:** v0.6.3

> **The Missing Link:** Connect Koan's adapter pattern to Zen Garden's infrastructure discovery for true zero-config development.

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Architecture Overview](#2-architecture-overview)
3. [Package Structure](#3-package-structure)
4. [Core Abstractions](#4-core-abstractions)
5. [Discovery Client Implementation](#5-discovery-client-implementation)
6. [Service Discovery Adapter](#6-service-discovery-adapter)
7. [Protocol Resolver](#7-protocol-resolver)
8. [Tending (Stone Pinning)](#8-tending-stone-pinning)
9. [Health Integration](#9-health-integration)
10. [Auto-Registration](#10-auto-registration)
11. [Configuration](#11-configuration)
12. [Boot Telemetry](#12-boot-telemetry)
13. [Error Handling](#13-error-handling)
14. [Testing Strategy](#14-testing-strategy)
15. [Migration Path](#15-migration-path)
16. [Implementation Checklist](#16-implementation-checklist)
17. [Related Documents](#17-related-documents)

---

## 1. Executive Summary

### The Problem

Koan adapters currently discover infrastructure through:
1. **Explicit configuration** - `Koan:Data:Mongo:ConnectionString`
2. **Environment variables** - `MONGO_URLS`, `POSTGRES_URLS`
3. **Container/Localhost probing** - Docker Compose labels, localhost ports
4. **Aspire service discovery** - When running under Aspire AppHost

This works well but requires either:
- Manual configuration of connection strings
- Running inside Docker Compose or Aspire
- Services on localhost with default ports

**Zen Garden solves this** by automatically discovering services anywhere on the local network via UDP multicast, with persistent Stone preferences (tending) and cross-subnet support (Lantern).

### The Solution

Create `Koan.ZenGarden` - a package that:
1. **Plugs into the existing `IServiceDiscoveryAdapter` pattern** as the highest-priority discovery method
2. **Implements `IConnectionProtocolResolver`** for explicit `zen-garden:mongodb` connection strings
3. **Maintains full compatibility** - Koan.Core knows nothing about Zen Garden
4. **Follows "Reference = Intent"** - Adding the package enables discovery automatically

### Key Benefits

| Without Zen Garden | With Zen Garden |
|-------------------|-----------------|
| Configure MongoDB connection string | Add `Koan.ZenGarden` package, done |
| Run Docker Compose for local dev | Services found on any Stone |
| Restart app when infra moves | Automatic reconnection to healthy Stone |
| Manual failover | Automatic failover via two-level caching |
| Per-machine setup | Shared Garden across team |
| Discovery on every request? | Stone binding + offering cache (zero overhead) |

---

## 2. Architecture Overview

### 2.1 Integration Points

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              KOAN APPLICATION                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   Entity<Todo>.Save()                                                       │
│        │                                                                    │
│        ▼                                                                    │
│   Data<Todo, string>.UpsertAsync()                                          │
│        │                                                                    │
│        ▼                                                                    │
│   IDataService.GetRepository<Todo, string>()                                │
│        │                                                                    │
│        ▼                                                                    │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │              SERVICE DISCOVERY COORDINATOR                           │   │
│   │                                                                      │   │
│   │   Priority Chain:                                                    │   │
│   │   ┌────────────────────────────────────────────────────────────┐    │   │
│   │   │ 1. ZenGardenDiscoveryAdapter  ← NEW (Priority: 100)        │    │   │
│   │   │    • UDP multicast 239.255.42.99:7184                      │    │   │
│   │   │    • Query Stone HTTP API /api/v1/services/{name}          │    │   │
│   │   │    • Tending state (~/.zen-garden/.tending)                │    │   │
│   │   │    • Health validation via /health endpoint                │    │   │
│   │   ├────────────────────────────────────────────────────────────┤    │   │
│   │   │ 2. MongoDiscoveryAdapter (Priority: 50)                    │    │   │
│   │   │    • Explicit config, env vars, Aspire, localhost          │    │   │
│   │   ├────────────────────────────────────────────────────────────┤    │   │
│   │   │ 3. [Other Adapters]                                        │    │   │
│   │   └────────────────────────────────────────────────────────────┘    │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│        │                                                                    │
│        ▼                                                                    │
│   MongoAdapterFactory.Create<Todo, string>(sp, source)                      │
│        │                                                                    │
│        ▼                                                                    │
│   MongoRepository<Todo, string> ──────► MongoDB @ stone-alpha.local:27017   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘

                                    │
                                    │ UDP 7184 / HTTP 7185
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              ZEN GARDEN                                      │
│                                                                             │
│   ┌─────────────┐         ┌─────────────┐         ┌─────────────┐          │
│   │ stone-alpha │         │ stone-beta  │         │ stone-gamma │          │
│   │  (MongoDB)  │         │   (Redis)   │         │  (Ollama)   │          │
│   │  Moss:7185  │         │  Moss:7185  │         │  Moss:7185  │          │
│   └─────────────┘         └─────────────┘         └─────────────┘          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 Two Integration Modes

**Mode 1: Implicit Discovery (Highest Priority Adapter)**
```csharp
// Just reference the package - Zen Garden becomes first discovery method
services.AddKoan();

// MongoDB discovered via: Zen Garden → Docker → Localhost
var todo = await Todo.Get(id);
```

**Mode 2: Explicit Protocol (Connection String)**
```csharp
// Explicit zen-garden protocol
services.AddKoan(options => {
    options.UseMongoDb("zen-garden:mongodb");
    options.UseRedis("zen-garden:redis");
});
```

---

## 3. Package Structure

```
src/
├── Koan.ZenGarden/                           # Main integration package
│   ├── Koan.ZenGarden.csproj
│   ├── README.md
│   ├── TECHNICAL.md
│   │
│   ├── Infrastructure/
│   │   └── Constants.cs                      # Port numbers, timeouts, paths
│   │
│   ├── Client/                               # Zen Garden network client
│   │   ├── IZenGardenClient.cs              # Client abstraction
│   │   ├── ZenGardenClient.cs               # UDP + HTTP implementation
│   │   └── Models/
│   │       ├── Stone.cs                     # Stone entity
│   │       ├── DiscoveryRequest.cs          # UDP request
│   │       ├── DiscoveryResponse.cs         # UDP response
│   │       ├── ServiceInfo.cs               # Service from Stone API
│   │       └── ResolvedService.cs           # Cached offering resolution
│   │
│   ├── Tending/                              # Stone pinning persistence
│   │   ├── ITendingStore.cs
│   │   ├── TendingStore.cs                  # ~/.zen-garden/.tending
│   │   └── TendingState.cs
│   │
│   ├── Discovery/                            # Koan integration
│   │   ├── ZenGardenDiscoveryAdapter.cs     # IServiceDiscoveryAdapter
│   │   └── ZenGardenProtocolResolver.cs     # IConnectionProtocolResolver
│   │
│   ├── Health/
│   │   └── ZenGardenHealthContributor.cs    # IHealthContributor
│   │
│   └── Initialization/
│       └── KoanAutoRegistrar.cs             # IKoanAutoRegistrar
│
└── Koan.ZenGarden.Abstractions/              # Optional: shared types
    ├── Koan.ZenGarden.Abstractions.csproj
    └── ... (interfaces for testing/mocking)
```

### Dependencies

```xml
<!-- Koan.ZenGarden.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Koan.ZenGarden</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- Koan Core (orchestration abstractions) -->
    <ProjectReference Include="..\Koan.Core\Koan.Core.csproj" />
    <ProjectReference Include="..\Koan.Core.Adapters\Koan.Core.Adapters.csproj" />
    
    <!-- HTTP client for Stone API -->
    <PackageReference Include="System.Net.Http.Json" Version="9.0.0" />
  </ItemGroup>
</Project>
```

---

## 4. Core Abstractions

### 4.1 Stone Model

```csharp
// Koan.ZenGarden/Client/Models/Stone.cs
namespace Koan.ZenGarden.Client.Models;

/// <summary>
/// Represents a Zen Garden Stone (physical device running Moss).
/// </summary>
public sealed record Stone
{
    /// <summary>Immutable GUID v7 - use as cache key</summary>
    public string? StoneId { get; init; }
    
    /// <summary>Human-readable hostname (may change)</summary>
    public required string StoneName { get; init; }
    
    /// <summary>Full HTTP endpoint URL (e.g., http://192.168.1.100:7185)</summary>
    public required string StoneEndpoint { get; init; }
    
    /// <summary>Moss daemon version</summary>
    public string? MossVersion { get; init; }
    
    /// <summary>Lantern registry URL (for cross-subnet discovery)</summary>
    public string? LanternEndpoint { get; init; }
    
    /// <summary>Extract host from endpoint</summary>
    public string Host => new Uri(StoneEndpoint).Host;
    
    /// <summary>Extract port from endpoint</summary>
    public int Port => new Uri(StoneEndpoint).Port;
}
```

### 4.2 Service Info

```csharp
// Koan.ZenGarden/Client/Models/ServiceInfo.cs
namespace Koan.ZenGarden.Client.Models;

/// <summary>
/// Service running on a Stone (container managed by Moss).
/// </summary>
public sealed record ServiceInfo
{
    public required string Name { get; init; }
    public required string Offering { get; init; }
    public string? Version { get; init; }
    public required ServiceStatus Status { get; init; }
    public ServiceHealth Health { get; init; } = ServiceHealth.Unknown;
    public string? Category { get; init; }
    public string[]? Tags { get; init; }
    
    /// <summary>Connection info with pre-built URIs from Zen Garden</summary>
    public required ServiceConnection Connection { get; init; }
}

/// <summary>
/// Connection information provided by Zen Garden.
/// URIs are pre-built by Moss - clients should use these directly.
/// </summary>
public sealed record ServiceConnection
{
    public required string Hostname { get; init; }
    public required string Ip { get; init; }
    public required int Port { get; init; }
    public required string Protocol { get; init; }  // "tcp", "http", etc.
    
    /// <summary>Pre-built connection URIs from Moss (tcp://host:port format)</summary>
    public required string[] Uris { get; init; }
    
    /// <summary>Primary URI (hostname-based, with service-specific scheme)</summary>
    /// <remarks>
    /// Moss returns generic tcp:// URIs. For service-specific schemes (mongodb://, redis://),
    /// use GetUri(scheme) or let the adapter handle scheme rewriting.
    /// </remarks>
    public string PrimaryUri => Uris.FirstOrDefault() ?? $"{Protocol}://{Hostname}:{Port}";
    
    /// <summary>Get URI with a specific scheme (replaces tcp:// with mongodb://, etc.)</summary>
    public string GetUri(string scheme) => 
        PrimaryUri.Replace("tcp://", $"{scheme}://").Replace("http://", $"{scheme}://");
}

public enum ServiceStatus
{
    Installing,
    Running,
    Stopped,
    Maintenance,
    Degraded,
    Unknown
}

public enum ServiceHealth
{
    Healthy,
    Degraded,
    Offline,
    Unknown
}
```

### 4.3 Discovery Request/Response

```csharp
// Koan.ZenGarden/Client/Models/DiscoveryRequest.cs
namespace Koan.ZenGarden.Client.Models;

public sealed record DiscoveryRequest
{
    [JsonPropertyName("discover")]
    public string Discover { get; init; } = "moss";
    
    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }
    
    [JsonPropertyName("requester")]
    public string Requester { get; init; } = "koan-framework";
}

// Koan.ZenGarden/Client/Models/DiscoveryResponse.cs
public sealed record DiscoveryResponse
{
    [JsonPropertyName("stone_id")]
    public string? StoneId { get; init; }
    
    [JsonPropertyName("stone_name")]
    public required string StoneName { get; init; }
    
    [JsonPropertyName("stone_endpoint")]
    public required string StoneEndpoint { get; init; }
    
    [JsonPropertyName("moss_version")]
    public string? MossVersion { get; init; }
    
    [JsonPropertyName("lantern_endpoint")]
    public string? LanternEndpoint { get; init; }
    
    public Stone ToStone() => new()
    {
        StoneId = StoneId,
        StoneName = StoneName,
        StoneEndpoint = StoneEndpoint,
        MossVersion = MossVersion,
        LanternEndpoint = LanternEndpoint
    };
}

// Koan.ZenGarden/Client/Models/ChirpMessage.cs
/// <summary>
/// Passive announcement broadcast by Stones periodically.
/// Used for hot-cache topology maintenance.
/// </summary>
public sealed record ChirpMessage
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }  // "announce"
    
    [JsonPropertyName("stone_id")]
    public string? StoneId { get; init; }
    
    [JsonPropertyName("stone_name")]
    public required string StoneName { get; init; }
    
    [JsonPropertyName("stone_endpoint")]
    public required string StoneEndpoint { get; init; }
    
    [JsonPropertyName("offerings")]
    public IReadOnlyList<string>? Offerings { get; init; }
    
    public Stone ToStone() => new()
    {
        StoneId = StoneId,
        StoneName = StoneName,
        StoneEndpoint = StoneEndpoint
    };
}
```

---

## 5. Discovery Client Implementation

### 5.1 Client Interface

```csharp
// Koan.ZenGarden/Client/IZenGardenClient.cs
namespace Koan.ZenGarden.Client;

/// <summary>
/// Client for Zen Garden service discovery.
/// Handles UDP broadcast, Stone HTTP API, and caching.
/// </summary>
public interface IZenGardenClient : IDisposable
{
    /// <summary>
    /// Start the background Chirp listener to maintain hot-cache topology.
    /// Stones broadcast announcements periodically; these are cached for fast discovery.
    /// </summary>
    void StartChirpListener();
    
    /// <summary>
    /// Discover all Stones on the network via UDP multicast.
    /// </summary>
    /// <param name="timeout">Discovery timeout (default: 3 seconds)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of discovered Stones</returns>
    Task<IReadOnlyList<Stone>> DiscoverStonesAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find a specific service by offering name.
    /// Results are cached for the application lifetime - only re-discovers on cache miss or after InvalidateService().
    /// Resolution priority: Cached result → Tended Stone → Cached Stones → Fresh Discovery.
    /// </summary>
    /// <param name="offering">Service offering (e.g., "mongodb", "redis")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Service info with Stone endpoint, or null if not found</returns>
    Task<ResolvedService?> FindServiceAsync(
        string offering,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Invalidate cached resolution for a service.
    /// Call this when an offering connection fails - will re-search on the tended Stone.
    /// </summary>
    /// <param name="offering">Service offering to invalidate (e.g., "mongodb")</param>
    void InvalidateOffering(string offering);
    
    /// <summary>
    /// Invalidate the tended Stone binding.
    /// Call this when Moss connection fails - will trigger fresh Stone discovery.
    /// </summary>
    void InvalidateStone();
    
    /// <summary>
    /// Get a specific running service from a Stone by name.
    /// Uses GET /api/v1/services/{name} endpoint.
    /// Returns full service info including connection details and ports.
    /// </summary>
    Task<ServiceInfo?> GetServiceAsync(
        Stone stone,
        string serviceName,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// List all running services on a Stone.
    /// Uses GET /api/v1/services endpoint.
    /// </summary>
    Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(
        Stone stone,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if a Stone is reachable.
    /// Uses GET /health endpoint.
    /// </summary>
    Task<bool> IsStoneHealthyAsync(
        Stone stone,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolved service with connection information.
/// </summary>
public sealed record ResolvedService
{
    public required ServiceInfo Service { get; init; }
    public required Stone Stone { get; init; }
    public required string ConnectionString { get; init; }
}
```

### 5.2 Client Implementation

```csharp
// Koan.ZenGarden/Client/ZenGardenClient.cs
namespace Koan.ZenGarden.Client;

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Koan.ZenGarden.Client.Caching;
using Koan.ZenGarden.Client.Models;
using Koan.ZenGarden.Infrastructure;
using Koan.ZenGarden.Tending;

/// <summary>
/// Zen Garden client implementing UDP discovery and Stone HTTP API.
/// Per driver-specification.md v2.0.
/// </summary>
public sealed class ZenGardenClient : IZenGardenClient, IDisposable
{
    private readonly ITendingStore _tending;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ZenGardenClient> _logger;
    private readonly ZenGardenOptions _options;
    
    // Two-level cache:
    // 1. Tended Stone - which Stone to talk to (persistent in ~/.zen-garden/.tending)
    // 2. Offering cache - resolved URLs per offering (in-memory, app lifetime)
    private readonly ConcurrentDictionary<string, ResolvedService> _offeringCache = new(StringComparer.OrdinalIgnoreCase);
    
    // Cached Stone binding (loaded from tending or discovered)
    private Stone? _boundStone;
    
    // Hot-cache topology: passively discovered Stones from Chirp listener
    private readonly ConcurrentDictionary<string, Stone> _topologyCache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _chirpListenerCts;
    private Task? _chirpListenerTask;

    public ZenGardenClient(
        ITendingStore tending,
        HttpClient httpClient,
        ILogger<ZenGardenClient> logger,
        ZenGardenOptions? options = null)
    {
        _tending = tending;
        _httpClient = httpClient;
        _logger = logger;
        _options = options ?? new ZenGardenOptions();
        
        // Load tended Stone on startup if available
        var tendingState = _tending.Load();
        if (tendingState != null)
        {
            _boundStone = new Stone
            {
                StoneName = tendingState.StoneName,
                StoneEndpoint = tendingState.Endpoint
            };
            _logger.LogDebug("Loaded tended Stone: {Stone}", _boundStone.StoneName);
        }
    }

    public async Task<IReadOnlyList<Stone>> DiscoverStonesAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        timeout ??= TimeSpan.FromSeconds(_options.DiscoveryTimeoutSeconds);
        
        var request = new DiscoveryRequest
        {
            RequestId = $"koan-{Guid.NewGuid():N}",
            Requester = "koan-framework"
        };
        
        var payload = JsonSerializer.SerializeToUtf8Bytes(request);
        var stones = new Dictionary<string, Stone>(StringComparer.OrdinalIgnoreCase);
        
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.ReceiveTimeout = 100; // Non-blocking for streaming
        
        // Send to multicast (primary) and broadcast (fallback)
        var multicastEndpoint = new IPEndPoint(
            IPAddress.Parse(Constants.Discovery.MulticastGroup), 
            Constants.Discovery.Port);
        var broadcastEndpoint = new IPEndPoint(
            IPAddress.Broadcast, 
            Constants.Discovery.Port);
        
        try
        {
            await socket.SendToAsync(payload, SocketFlags.None, multicastEndpoint, cancellationToken);
            _logger.LogDebug("Sent discovery request to multicast {Endpoint}", multicastEndpoint);
        }
        catch (SocketException ex)
        {
            _logger.LogDebug("Multicast send failed, trying broadcast: {Error}", ex.Message);
        }
        
        try
        {
            await socket.SendToAsync(payload, SocketFlags.None, broadcastEndpoint, cancellationToken);
            _logger.LogDebug("Sent discovery request to broadcast {Endpoint}", broadcastEndpoint);
        }
        catch (SocketException ex)
        {
            _logger.LogWarning("Broadcast send failed: {Error}", ex.Message);
        }
        
        // Collect responses
        var deadline = DateTime.UtcNow.Add(timeout.Value);
        var buffer = new byte[4096];
        
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await socket.ReceiveFromAsync(
                    buffer, 
                    SocketFlags.None, 
                    new IPEndPoint(IPAddress.Any, 0), 
                    cancellationToken);
                
                var json = System.Text.Encoding.UTF8.GetString(buffer, 0, result.ReceivedBytes);
                var response = JsonSerializer.Deserialize<DiscoveryResponse>(json);
                
                if (response != null && !string.IsNullOrEmpty(response.StoneEndpoint))
                {
                    var stone = response.ToStone();
                    var key = stone.StoneId ?? stone.StoneName;
                    
                    if (!stones.ContainsKey(key))
                    {
                        stones[key] = stone;
                        _logger.LogDebug("Discovered Stone: {Name} at {Endpoint}", 
                            stone.StoneName, stone.StoneEndpoint);
                    }
                }
            }
            catch (SocketException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout on receive - continue until deadline
                await Task.Delay(50, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogDebug("Invalid discovery response: {Error}", ex.Message);
            }
        }
        
        _logger.LogInformation("Discovery complete: {Count} Stone(s) found", stones.Count);
        return stones.Values.ToList();
    }

    public async Task<ResolvedService?> FindServiceAsync(
        string offering,
        CancellationToken cancellationToken = default)
    {
        // Level 1: Check offering cache (in-memory, app lifetime)
        if (_offeringCache.TryGetValue(offering, out var cached))
        {
            _logger.LogDebug("Returning cached {Offering} → {ConnectionString}", 
                offering, cached.ConnectionString);
            return cached;
        }
        
        // Level 2: Ensure we have a bound Stone (from tending or discovery)
        var stone = await EnsureBoundStoneAsync(cancellationToken);
        if (stone == null)
        {
            _logger.LogWarning("No Stone available in Garden");
            return null;
        }
        
        // Level 3: Query for the running service on the bound Stone
        var service = await GetServiceAsync(stone, offering, cancellationToken);
        
        if (service is { Status: ServiceStatus.Running })
        {
            _logger.LogDebug("Found {Offering} on {Stone}", offering, stone.StoneName);
            return CacheOffering(service, stone);
        }
        
        _logger.LogWarning("Service {Offering} not found on Stone {Stone}", offering, stone.StoneName);
        return null;
    }
    
    /// <summary>
    /// Ensures we have a bound Stone, discovering one if needed.
    /// Uses parallel strategy: races cached Stones against UDP discovery.
    /// </summary>
    private async Task<Stone?> EnsureBoundStoneAsync(CancellationToken cancellationToken)
    {
        // Fast path: already bound and healthy?
        if (_boundStone != null)
        {
            if (await IsStoneHealthyAsync(_boundStone, TimeSpan.FromSeconds(2), cancellationToken))
            {
                return _boundStone;
            }
            
            _logger.LogInformation("Bound Stone {Stone} unreachable, discovering new Stone...", 
                _boundStone.StoneName);
            _boundStone = null;
        }
        
        // Parallel discovery: race cached topology against fresh UDP discovery
        // Whoever finds a healthy Stone first wins
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        var cachedStoneTask = FindHealthyCachedStoneAsync(cts.Token);
        var discoveryTask = DiscoverHealthyStoneAsync(cts.Token);
        
        var winner = await Task.WhenAny(cachedStoneTask, discoveryTask);
        var stone = await winner;
        
        if (stone != null)
        {
            // Cancel the slower path
            await cts.CancelAsync();
            return BindToStone(stone);
        }
        
        // First completed with null, wait for the other
        var other = winner == cachedStoneTask ? discoveryTask : cachedStoneTask;
        try
        {
            stone = await other;
            if (stone != null)
            {
                return BindToStone(stone);
            }
        }
        catch (OperationCanceledException) { }
        
        _logger.LogWarning("No healthy Stones found in Garden");
        return null;
    }
    
    private async Task<Stone?> FindHealthyCachedStoneAsync(CancellationToken cancellationToken)
    {
        // Check hot-cache topology (populated by Chirp listener)
        foreach (var stone in _topologyCache.Values)
        {
            if (cancellationToken.IsCancellationRequested) break;
            
            if (await IsStoneHealthyAsync(stone, TimeSpan.FromSeconds(1), cancellationToken))
            {
                _logger.LogDebug("Found healthy Stone in topology cache: {Stone}", stone.StoneName);
                return stone;
            }
        }
        return null;
    }
    
    private async Task<Stone?> DiscoverHealthyStoneAsync(CancellationToken cancellationToken)
    {
        var stones = await DiscoverStonesAsync(cancellationToken: cancellationToken);
        
        foreach (var stone in stones)
        {
            if (cancellationToken.IsCancellationRequested) break;
            
            if (await IsStoneHealthyAsync(stone, TimeSpan.FromSeconds(2), cancellationToken))
            {
                // Also add to topology cache for future fast lookups
                _topologyCache[stone.StoneId ?? stone.StoneName] = stone;
                return stone;
            }
        }
        return null;
    }
    
    private Stone BindToStone(Stone stone)
    {
        _boundStone = stone;
        _tending.Save(stone.StoneName, stone.StoneEndpoint);
        _logger.LogInformation("Bound to Stone: {Stone} at {Endpoint}", 
            stone.StoneName, stone.StoneEndpoint);
        return stone;
    }
    
    private ResolvedService CacheOffering(ServiceInfo service, Stone stone)
    {
        var resolved = new ResolvedService
        {
            Service = service,
            Stone = stone,
            ConnectionString = service.Connection.PrimaryUri
        };
        
        _offeringCache[service.Offering] = resolved;
        _logger.LogInformation("Cached offering {Offering} → {ConnectionString}", 
            service.Offering, resolved.ConnectionString);
        
        return resolved;
    }
    
    public void InvalidateOffering(string offering)
    {
        if (_offeringCache.TryRemove(offering, out var removed))
        {
            _logger.LogInformation(
                "Invalidated offering {Offering} (was {ConnectionString}) - will re-search on next request",
                offering, removed.ConnectionString);
        }
    }
    
    public void InvalidateStone()
    {
        if (_boundStone != null)
        {
            _logger.LogInformation(
                "Invalidated Stone binding (was {Stone}) - will re-discover on next request",
                _boundStone.StoneName);
            _boundStone = null;
            _tending.Clear();
            
            // Also clear all offering caches since they were on the old Stone
            _offeringCache.Clear();
        }
    }
    
    /// <summary>
    /// Start the Chirp listener to passively maintain hot-cache topology.
    /// Stones broadcast announcements periodically; we cache them for fast discovery.
    /// </summary>
    public void StartChirpListener()
    {
        if (_chirpListenerTask != null) return;
        
        _chirpListenerCts = new CancellationTokenSource();
        _chirpListenerTask = ListenForChirpsAsync(_chirpListenerCts.Token);
        _logger.LogInformation("Started Chirp listener for hot-cache topology");
    }
    
    private async Task ListenForChirpsAsync(CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        
        // Join multicast group to receive Stone announcements
        var multicastGroup = IPAddress.Parse(Constants.Discovery.MulticastGroup);
        socket.Bind(new IPEndPoint(IPAddress.Any, Constants.Discovery.Port));
        socket.SetSocketOption(
            SocketOptionLevel.IP,
            SocketOptionName.AddMembership,
            new MulticastOption(multicastGroup, IPAddress.Any));
        
        var buffer = new byte[4096];
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await socket.ReceiveFromAsync(
                    buffer, SocketFlags.None, 
                    new IPEndPoint(IPAddress.Any, 0), 
                    cancellationToken);
                
                var json = Encoding.UTF8.GetString(buffer, 0, result.ReceivedBytes);
                var chirp = JsonSerializer.Deserialize<ChirpMessage>(json);
                
                if (chirp?.Type == "announce" && !string.IsNullOrEmpty(chirp.StoneEndpoint))
                {
                    var stone = chirp.ToStone();
                    var key = stone.StoneId ?? stone.StoneName;
                    
                    _topologyCache[key] = stone;
                    _logger.LogTrace("Topology update: {Stone} at {Endpoint}", 
                        stone.StoneName, stone.StoneEndpoint);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug("Chirp listener error: {Error}", ex.Message);
                await Task.Delay(1000, cancellationToken);
            }
        }
    }
    
    public void Dispose()
    {
        _chirpListenerCts?.Cancel();
        _chirpListenerCts?.Dispose();
        _httpClient.Dispose();
    }

    public async Task<ServiceInfo?> GetServiceAsync(
        Stone stone,
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        // Use the /api/v1/services/{name} endpoint for running services with full connection info
        var url = $"{stone.StoneEndpoint}/api/v1/services/{Uri.EscapeDataString(serviceName)}";
        
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
                
            response.EnsureSuccessStatusCode();
            
            var wrapper = await response.Content.ReadFromJsonAsync<ApiResponse<ServiceInfo>>(
                cancellationToken: cancellationToken);
            
            return wrapper?.Data;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
    
    public async Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(
        Stone stone,
        CancellationToken cancellationToken = default)
    {
        // Use /api/v1/services endpoint to list all running services
        var url = $"{stone.StoneEndpoint}/api/v1/services";
        
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var wrapper = await response.Content.ReadFromJsonAsync<ServicesResponse>(
                cancellationToken: cancellationToken);
            
            return wrapper?.Data?.Services ?? Array.Empty<ServiceInfo>();
        }
        catch (HttpRequestException)
        {
            return Array.Empty<ServiceInfo>();
        }
    }

    public async Task<bool> IsStoneHealthyAsync(
        Stone stone,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        timeout ??= TimeSpan.FromSeconds(2);
        
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout.Value);
            
            var response = await _httpClient.GetAsync(
                $"{stone.StoneEndpoint}/health", 
                cts.Token);
            
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

internal sealed record ApiResponse<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; init; }
    
    [JsonPropertyName("suggestions")]
    public string[]? Suggestions { get; init; }
}

/// <summary>Response from GET /api/v1/services endpoint</summary>
internal sealed record ServicesResponse
{
    [JsonPropertyName("data")]
    public ServicesData? Data { get; init; }
}

internal sealed record ServicesData
{
    [JsonPropertyName("found")]
    public bool Found { get; init; }
    
    [JsonPropertyName("services")]
    public ServiceInfo[]? Services { get; init; }
    
    [JsonPropertyName("source")]
    public string? Source { get; init; }
    
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }
}
```

---

## 6. Service Discovery Adapter

This is the key integration point - a high-priority adapter that slots into Koan's existing discovery chain.

```csharp
// Koan.ZenGarden/Discovery/ZenGardenDiscoveryAdapter.cs
namespace Koan.ZenGarden.Discovery;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.ZenGarden.Client;
using Koan.ZenGarden.Infrastructure;

/// <summary>
/// Zen Garden service discovery adapter.
/// Highest priority (100) - checked before Docker/localhost/Aspire.
/// Per driver-specification.md: Tended Stone → Cached Stones → Fresh Discovery.
/// </summary>
public sealed class ZenGardenDiscoveryAdapter : IServiceDiscoveryAdapter
{
    private readonly IZenGardenClient _client;
    private readonly ILogger<ZenGardenDiscoveryAdapter> _logger;
    private readonly ZenGardenOptions _options;

    // Service name → Zen Garden offering mapping
    private static readonly Dictionary<string, string> OfferingMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mongo"] = "mongodb",
        ["mongodb"] = "mongodb",
        ["postgres"] = "postgres",
        ["postgresql"] = "postgres",
        ["redis"] = "redis",
        ["mysql"] = "mysql",
        ["mariadb"] = "mariadb",
        ["sqlserver"] = "sqlserver",
        ["mssql"] = "sqlserver",
        ["elasticsearch"] = "elasticsearch",
        ["opensearch"] = "opensearch",
        ["rabbitmq"] = "rabbitmq",
        ["nats"] = "nats",
        ["ollama"] = "ollama",
        ["weaviate"] = "weaviate",
        ["milvus"] = "milvus",
        ["qdrant"] = "qdrant",
        ["minio"] = "minio",
        ["couchbase"] = "couchbase"
    };

    public ZenGardenDiscoveryAdapter(
        IZenGardenClient client,
        ILogger<ZenGardenDiscoveryAdapter> logger,
        ZenGardenOptions? options = null)
    {
        _client = client;
        _logger = logger;
        _options = options ?? new ZenGardenOptions();
    }

    /// <summary>
    /// Matches any service that has a known Zen Garden offering.
    /// Acts as a wildcard adapter for all infrastructure services.
    /// </summary>
    public string ServiceName => Constants.Discovery.WildcardServiceName; // "*"

    /// <summary>All known offerings this adapter can discover</summary>
    public string[] Aliases => OfferingMap.Keys.ToArray();

    /// <summary>
    /// Highest priority (100) - checked before all other adapters.
    /// Per DATA-0088: zen-garden is first-priority in auto-discovery.
    /// </summary>
    public int Priority => 100;

    public async Task<AdapterDiscoveryResult> DiscoverAsync(
        DiscoveryContext context,
        CancellationToken cancellationToken = default)
    {
        // Skip if Zen Garden is disabled
        if (!_options.Enabled)
        {
            return AdapterDiscoveryResult.Skipped(ServiceName, "Zen Garden disabled via configuration");
        }

        // Get the actual service being requested from context parameters
        var serviceName = GetServiceNameFromContext(context);
        if (string.IsNullOrEmpty(serviceName))
        {
            return AdapterDiscoveryResult.Failed(ServiceName, "No service name in discovery context");
        }

        // Map to Zen Garden offering
        if (!OfferingMap.TryGetValue(serviceName, out var offering))
        {
            // Unknown service - let other adapters handle it
            return AdapterDiscoveryResult.NoAdapter(serviceName);
        }

        _logger.LogDebug("Zen Garden: Discovering {Offering} for {Service}...", offering, serviceName);

        try
        {
            var resolved = await _client.FindServiceAsync(offering, cancellationToken);
            
            if (resolved != null)
            {
                _logger.LogInformation(
                    "Zen Garden: Found {Offering} at {Stone} → {ConnectionString}",
                    offering, resolved.Stone.StoneName, resolved.ConnectionString);
                
                return AdapterDiscoveryResult.Success(
                    serviceName,
                    resolved.ConnectionString,
                    "zen-garden",
                    isHealthy: resolved.Service.Status == ServiceStatus.Running);
            }
            
            _logger.LogDebug("Zen Garden: {Offering} not found, falling back to other adapters", offering);
            return AdapterDiscoveryResult.Failed(serviceName, $"Service '{offering}' not found in Garden");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Zen Garden discovery failed for {Offering}", offering);
            return AdapterDiscoveryResult.Failed(serviceName, $"Discovery error: {ex.Message}");
        }
    }

    private static string? GetServiceNameFromContext(DiscoveryContext context)
    {
        if (context.Parameters?.TryGetValue("serviceName", out var value) == true)
        {
            return value?.ToString();
        }
        return null;
    }
}
```

---

## 7. Protocol Resolver

For explicit `zen-garden:mongodb` connection strings (per DATA-0088).

```csharp
// Koan.ZenGarden/Discovery/ZenGardenProtocolResolver.cs
namespace Koan.ZenGarden.Discovery;

using Microsoft.Extensions.Logging;
using Koan.ZenGarden.Client;
using Koan.ZenGarden.Client.Models;
using Koan.ZenGarden.Infrastructure;

/// <summary>
/// Protocol resolver for "zen-garden:&lt;service&gt;" connection strings.
/// Per DATA-0088: Adapters call ConnectionProtocolRegistry to resolve protocol-based strings.
/// 
/// Usage: options.UseMongoDb("zen-garden:mongodb/mydb")
/// </summary>
public sealed class ZenGardenProtocolResolver : IConnectionProtocolResolver
{
    private readonly IZenGardenClient _client;
    private readonly ILogger<ZenGardenProtocolResolver> _logger;

    public string Protocol => "zen-garden";

    public ZenGardenProtocolResolver(
        IZenGardenClient client,
        ILogger<ZenGardenProtocolResolver> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<ProtocolResolveResult?> ResolveAsync(
        string serviceIdentifier,
        CancellationToken cancellationToken = default)
    {
        // Parse: "mongodb" or "mongodb/mydb"
        var parts = serviceIdentifier.Split('/', 2);
        var offering = parts[0];
        var database = parts.Length > 1 ? parts[1] : null;

        _logger.LogDebug("Resolving zen-garden:{Offering} (database: {Database})", 
            offering, database ?? "(none)");

        try
        {
            var resolved = await _client.FindServiceAsync(offering, cancellationToken);
            
            if (resolved == null)
            {
                _logger.LogWarning("zen-garden:{Offering} - service not found", offering);
                return null;
            }

            // Use pre-built URI from Zen Garden
            var connectionString = resolved.ConnectionString;
            
            // Append database if specified (path segment for most protocols)
            if (!string.IsNullOrEmpty(database))
            {
                connectionString = $"{connectionString.TrimEnd('/')}/{database}";
            }

            _logger.LogInformation(
                "Resolved zen-garden:{Offering} → {ConnectionString}",
                offering, connectionString);

            return new ProtocolResolveResult
            {
                ConnectionString = connectionString,
                Host = resolved.Service.Connection.Hostname,
                Port = resolved.Service.Connection.Port,
                Offering = offering,
                StoneName = resolved.Stone.StoneName,
                StoneEndpoint = resolved.Stone.StoneEndpoint
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to resolve zen-garden:{Offering}", offering);
            return null;
        }
    }
}

/// <summary>
/// Result of protocol resolution.
/// </summary>
public sealed record ProtocolResolveResult
{
    public required string ConnectionString { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Offering { get; init; }
    public string? StoneName { get; init; }
    public string? StoneEndpoint { get; init; }
}
```

---

## 8. Tending (Stone Pinning)

Per driver-specification.md: Persist Stone preference to `~/.zen-garden/.tending`.

```csharp
// Koan.ZenGarden/Tending/TendingState.cs
namespace Koan.ZenGarden.Tending;

public sealed record TendingState
{
    [JsonPropertyName("stone_name")]
    public required string StoneName { get; init; }
    
    [JsonPropertyName("endpoint")]
    public required string Endpoint { get; init; }
    
    [JsonPropertyName("last_seen")]
    public required string LastSeen { get; init; }
}

// Koan.ZenGarden/Tending/ITendingStore.cs
public interface ITendingStore
{
    TendingState? Load();
    void Save(string stoneName, string endpoint);
    void Clear();
}

// Koan.ZenGarden/Tending/TendingStore.cs
public sealed class TendingStore : ITendingStore
{
    private static readonly string TendingPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".zen-garden",
        ".tending");

    private readonly ILogger<TendingStore> _logger;

    public TendingStore(ILogger<TendingStore> logger)
    {
        _logger = logger;
    }

    public TendingState? Load()
    {
        try
        {
            if (!File.Exists(TendingPath))
                return null;

            var json = File.ReadAllText(TendingPath);
            return JsonSerializer.Deserialize<TendingState>(json);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to load tending state: {Error}", ex.Message);
            return null;
        }
    }

    public void Save(string stoneName, string endpoint)
    {
        try
        {
            var state = new TendingState
            {
                StoneName = stoneName,
                Endpoint = endpoint,
                LastSeen = DateTime.UtcNow.ToString("O")
            };

            var directory = Path.GetDirectoryName(TendingPath)!;
            Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(TendingPath, json);

            _logger.LogDebug("Tending saved: {StoneName} at {Endpoint}", stoneName, endpoint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to save tending state: {Error}", ex.Message);
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(TendingPath))
            {
                File.Delete(TendingPath);
                _logger.LogDebug("Tending cleared");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to clear tending state: {Error}", ex.Message);
        }
    }
}
```

---

## 9. Health Integration

```csharp
// Koan.ZenGarden/Health/ZenGardenHealthContributor.cs
namespace Koan.ZenGarden.Health;

using Koan.Core.Observability.Health;
using Koan.ZenGarden.Client;
using Koan.ZenGarden.Tending;

/// <summary>
/// Health contributor for Zen Garden connectivity.
/// Reports tended Stone health and discovery capability.
/// </summary>
public sealed class ZenGardenHealthContributor : IHealthContributor
{
    private readonly IZenGardenClient _client;
    private readonly ITendingStore _tending;
    private readonly ILogger<ZenGardenHealthContributor> _logger;

    public string Name => "zen-garden";
    public int Order => 5; // Check early

    public ZenGardenHealthContributor(
        IZenGardenClient client,
        ITendingStore tending,
        ILogger<ZenGardenHealthContributor> logger)
    {
        _client = client;
        _tending = tending;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken ct = default)
    {
        var tendingState = _tending.Load();
        
        if (tendingState == null)
        {
            // No tending - try discovery
            try
            {
                var stones = await _client.DiscoverStonesAsync(
                    TimeSpan.FromSeconds(2), ct);
                
                if (stones.Count > 0)
                {
                    return HealthCheckResult.Healthy(
                        $"Discovered {stones.Count} Stone(s)");
                }
                
                return HealthCheckResult.Degraded("No Stones discovered");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy($"Discovery failed: {ex.Message}");
            }
        }

        // Check tended Stone health
        var stone = new Stone
        {
            StoneName = tendingState.StoneName,
            StoneEndpoint = tendingState.Endpoint
        };

        var isHealthy = await _client.IsStoneHealthyAsync(stone, TimeSpan.FromSeconds(2), ct);
        
        if (isHealthy)
        {
            return HealthCheckResult.Healthy($"Tended: {tendingState.StoneName}");
        }

        return HealthCheckResult.Degraded(
            $"Tended Stone '{tendingState.StoneName}' unreachable");
    }
}
```

---

## 10. Auto-Registration

```csharp
// Koan.ZenGarden/Initialization/KoanAutoRegistrar.cs
namespace Koan.ZenGarden.Initialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core.Modules;
using Koan.Core.Observability.Health;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.ZenGarden.Client;
using Koan.ZenGarden.Discovery;
using Koan.ZenGarden.Health;
using Koan.ZenGarden.Infrastructure;
using Koan.ZenGarden.Tending;

/// <summary>
/// Koan auto-registrar for Zen Garden integration.
/// "Reference = Intent" - adding this package enables Zen Garden discovery.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.ZenGarden";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly
        .GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Bind options
        services.AddKoanOptions<ZenGardenOptions>(Constants.Configuration.Section);
        
        // Core services
        services.TryAddSingleton<ITendingStore, TendingStore>();
        services.TryAddSingleton<IZenGardenClient, ZenGardenClient>();
        
        // HTTP client for Stone API
        services.AddHttpClient<ZenGardenClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        
        // Service discovery adapter (Priority 100 - highest)
        // This makes Zen Garden the first discovery method for ALL services
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, ZenGardenDiscoveryAdapter>());
        
        // Protocol resolver for "zen-garden:mongodb" connection strings
        services.TryAddSingleton<ZenGardenProtocolResolver>();
        
        // Health contributor
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHealthContributor, ZenGardenHealthContributor>());
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        
        var options = cfg.GetSection(Constants.Configuration.Section)
            .Get<ZenGardenOptions>() ?? new ZenGardenOptions();
        
        if (!options.Enabled)
        {
            module.AddNote("Zen Garden discovery disabled via configuration");
            return;
        }
        
        module.AddNote("Zen Garden discovery enabled (Priority 100)");
        module.AddNote($"Multicast: {Constants.Discovery.MulticastGroup}:{Constants.Discovery.Port}");
        module.AddNote($"Discovery timeout: {options.DiscoveryTimeoutSeconds}s");
        module.AddNote($"Cache TTL: {options.CacheTtlSeconds}s");
        
        // Check tending state
        var tendingPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".zen-garden", ".tending");
        
        if (File.Exists(tendingPath))
        {
            try
            {
                var json = File.ReadAllText(tendingPath);
                var tending = JsonSerializer.Deserialize<TendingState>(json);
                if (tending != null)
                {
                    module.AddNote($"Tended Stone: {tending.StoneName}");
                }
            }
            catch { }
        }
        
        module.AddProviderElection("Infrastructure", "zen-garden",
            new[] { "zen-garden", "aspire", "docker", "localhost" },
            "Package referenced (highest priority)");
    }
}
```

---

## 11. Configuration

```csharp
// Koan.ZenGarden/Infrastructure/Constants.cs
namespace Koan.ZenGarden.Infrastructure;

public static class Constants
{
    public static class Discovery
    {
        public const string MulticastGroup = "239.255.42.99";
        public const int Port = 7184;
        public const string WildcardServiceName = "*";
    }
    
    public static class Api
    {
        public const int MossPort = 7185;
        public const int LanternPort = 7186;
        
        // Moss HTTP API endpoints (per Architecture Reference)
        public const string OfferingsEndpoint = "/api/v1/offerings";
        public const string OfferingByNameEndpoint = "/api/v1/offerings/{0}";
        public const string OfferingsSearchEndpoint = "/api/v1/offerings/search";
        public const string HealthEndpoint = "/health";
    }
    
    public static class Configuration
    {
        public const string Section = "Koan:ZenGarden";
    }
}

// Koan.ZenGarden/Infrastructure/ZenGardenOptions.cs
namespace Koan.ZenGarden.Infrastructure;

public sealed class ZenGardenOptions
{
    /// <summary>Enable/disable Zen Garden discovery</summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>UDP discovery timeout in seconds</summary>
    public int DiscoveryTimeoutSeconds { get; set; } = 3;
    
    /// <summary>Stone cache TTL in seconds</summary>
    public int CacheTtlSeconds { get; set; } = 90;
    
    /// <summary>HTTP request timeout in seconds</summary>
    public int HttpTimeoutSeconds { get; set; } = 30;
    
    /// <summary>Health check timeout in milliseconds</summary>
    public int HealthCheckTimeoutMs { get; set; } = 2000;
    
    /// <summary>Explicit Lantern endpoint (for cross-subnet)</summary>
    public string? LanternEndpoint { get; set; }
    
    /// <summary>Fallback to broadcast if multicast fails</summary>
    public bool EnableBroadcastFallback { get; set; } = true;
}
```

**Configuration example:**

```json
{
  "Koan": {
    "ZenGarden": {
      "Enabled": true,
      "DiscoveryTimeoutSeconds": 3,
      "CacheTtlSeconds": 90,
      "LanternEndpoint": "http://192.168.1.5:7186"
    }
  }
}
```

---

## 12. Boot Telemetry

Example boot output with Zen Garden:

```
[INFO] Koan:boot ──────────────────────────────────────────────────────────
[INFO] Koan:modules zen-garden v1.0.0
[INFO]   • Zen Garden discovery enabled (Priority 100)
[INFO]   • Multicast: 239.255.42.99:7184
[INFO]   • Discovery timeout: 3s
[INFO]   • Tended Stone: stone-topaz-basin
[INFO]   • Provider election: Infrastructure → zen-garden (Package referenced)
[INFO] ────────────────────────────────────────────────────────────────────
[INFO] Koan:modules mongo v1.0.0
[INFO]   • MongoDB discovery handled by autonomous MongoDiscoveryAdapter
[INFO]   • Connection: zen-garden → stone-topaz-basin:27017
[INFO] ────────────────────────────────────────────────────────────────────
[INFO] Koan:health zen-garden → Healthy (Tended: stone-topaz-basin)
[INFO] Koan:health mongo → Healthy (Connected to MongoDB 7.0.4)
```

---

## 13. Error Handling

### Two-Level Caching and Reconnection Strategy

The client uses a **two-level cache** with distinct failure modes:

```
┌─────────────────────────────────────────────────────────────────┐
│  Level 1: Stone Binding (Tending)                               │
│  • Persisted to ~/.zen-garden/.tending                          │
│  • Survives app restarts                                        │
│  • Invalidate: InvalidateStone() → triggers fresh discovery     │
├─────────────────────────────────────────────────────────────────┤
│  Level 2: Offering Cache (In-Memory)                            │
│  • Cached connection URLs per offering                          │
│  • App lifetime, cleared on Stone invalidation                  │
│  • Invalidate: InvalidateOffering("mongodb") → re-search        │
└─────────────────────────────────────────────────────────────────┘
```

**Failure handling:**

```csharp
// Scenario 1: Offering connection fails (e.g., MongoDB container restarted)
try
{
    await _mongoClient.PingAsync();
}
catch (MongoConnectionException)
{
    // Re-search for offering on the same Stone
    _zenGardenClient.InvalidateOffering("mongodb");
    var newService = await _zenGardenClient.FindServiceAsync("mongodb");
    // Reconnect with new connection string...
}

// Scenario 2: Stone/Moss connection fails (e.g., Stone went offline)
try
{
    var service = await _zenGardenClient.GetServiceAsync(stone, "mongodb");
}
catch (HttpRequestException)
{
    // Discover a new Stone entirely
    _zenGardenClient.InvalidateStone();
    var newService = await _zenGardenClient.FindServiceAsync("mongodb");
    // Now connected to a different Stone...
}
```

This pattern ensures:
1. **Zero overhead** on normal operations (both caches hit)
2. **Fast failover** on offering issues (re-search same Stone)
3. **Full recovery** on Stone failure (discover new Stone)

### Error Codes

| Code | Description | Action |
|------|-------------|--------|
| `ZEN_GARDEN_DISABLED` | Package disabled via config | Fall through to other adapters |
| `NO_STONES_DISCOVERED` | UDP discovery found nothing | Fall through, check network |
| `OFFERING_NOT_FOUND` | Offering not on bound Stone | Fall through to other adapters |
| `OFFERING_CONNECTION_LOST` | Runtime connection to offering failed | `InvalidateOffering()`, re-search |
| `STONE_UNREACHABLE` | Moss HTTP API unreachable | `InvalidateStone()`, re-discover |

### Graceful Degradation

```csharp
// In ZenGardenDiscoveryAdapter.DiscoverAsync()
catch (SocketException ex)
{
    _logger.LogDebug("Network error during discovery: {Error}", ex.Message);
    return AdapterDiscoveryResult.Failed(serviceName, $"Network error: {ex.Message}");
    // Result: Other adapters (Docker, localhost) will be tried
}
```

The key principle: **Zen Garden failures never break the application.** The discovery chain continues to Docker Compose, localhost, and other adapters.

---

## 14. Testing Strategy

### 14.1 Unit Tests

```csharp
[Fact]
public async Task FindServiceAsync_ReturnsService_WhenStoneHasOffering()
{
    // Arrange
    var mockHttp = new MockHttpMessageHandler();
    mockHttp.When("*/api/v1/offerings/mongodb")
        .Respond(HttpStatusCode.OK, "application/json", 
            """
            {
              "data": {
                "name": "mongodb",
                "offering": "mongodb",
                "status": "Running",
                "connection": {
                  "hostname": "stone-coral-prairie.local",
                  "ip": "192.168.1.100",
                  "port": 27017,
                  "protocol": "mongodb",
                  "uris": [
                    "mongodb://stone-coral-prairie.local:27017",
                    "mongodb://192.168.1.100:27017"
                  ]
                }
              }
            }
            """);
    
    var client = CreateClient(mockHttp);
    
    // Act
    var result = await client.FindServiceAsync("mongodb");
    
    // Assert
    Assert.NotNull(result);
    Assert.Equal("mongodb://stone-coral-prairie.local:27017", result.ConnectionString);
}

[Fact]
public async Task DiscoveryAdapter_ReturnsSkipped_WhenDisabled()
{
    // Arrange
    var options = new ZenGardenOptions { Enabled = false };
    var adapter = new ZenGardenDiscoveryAdapter(Mock.Of<IZenGardenClient>(), 
        Mock.Of<ILogger<ZenGardenDiscoveryAdapter>>(), options);
    
    // Act
    var result = await adapter.DiscoverAsync(new DiscoveryContext());
    
    // Assert
    Assert.False(result.IsSuccessful);
    Assert.Contains("disabled", result.ErrorMessage);
}
```

### 14.2 Integration Tests

```csharp
[Fact]
[Trait("Category", "Integration")]
public async Task RealDiscovery_FindsMongoDB_WhenGardenRunning()
{
    // Requires actual Zen Garden infrastructure
    var client = new ZenGardenClient(...);
    
    var stones = await client.DiscoverStonesAsync(TimeSpan.FromSeconds(5));
    Assert.NotEmpty(stones);
    
    var mongo = await client.FindServiceAsync("mongodb");
    Assert.NotNull(mongo);
    Assert.StartsWith("mongodb://", mongo.ConnectionString);
}
```

---

## 15. Migration Path

### For Existing Koan Applications

**Step 1:** Add package reference
```bash
dotnet add package Koan.ZenGarden
```

**Step 2:** (Optional) Remove explicit configuration
```diff
// appsettings.json
{
- "ConnectionStrings": {
-   "MongoDB": "mongodb://localhost:27017"
- },
  "Koan": {
    // Zen Garden will discover MongoDB automatically
  }
}
```

**Step 3:** Run application
```bash
dotnet run
# Boot log shows: zen-garden → stone-alpha:27017
```

### Fallback Behavior

If Zen Garden discovery fails (no Stones, service not found), the existing discovery chain continues:

1. ~~Zen Garden~~ (failed)
2. Docker Compose labels ✓
3. Localhost probe ✓
4. Aspire service discovery ✓

No application changes needed for fallback.

---

## 16. Implementation Checklist

### Phase 1: Core Client (Week 1)

- [ ] Create `Koan.ZenGarden` project
- [ ] Implement `Stone`, `Offering`, `ServiceConnection`, `ChirpMessage` models
- [ ] Implement `ZenGardenClient` with UDP discovery and two-level caching
- [ ] Implement parallel discovery (race cached topology vs fresh UDP)
- [ ] Implement `TendingStore` for Stone binding persistence (`~/.zen-garden/.tending`)
- [ ] Implement in-memory offering cache with `InvalidateOffering()`/`InvalidateStone()`
- [ ] Implement Chirp listener for hot-cache topology maintenance
- [ ] Add unit tests for models, client, and cache invalidation

### Phase 2: Koan Integration (Week 2)

- [ ] Implement `ZenGardenDiscoveryAdapter : IServiceDiscoveryAdapter`
- [ ] Implement `ZenGardenProtocolResolver` for `zen-garden:` protocol
- [ ] Implement `ZenGardenHealthContributor`
- [ ] Implement `KoanAutoRegistrar` for auto-registration
- [ ] Bind `ZenGardenOptions` configuration
- [ ] Add integration tests for failure mode handling (offering vs Stone)

### Phase 3: Polish (Week 3)

- [ ] Boot telemetry and provenance reporting
- [ ] Comprehensive error handling
- [ ] Documentation (README.md, TECHNICAL.md)
- [ ] Sample application demonstrating integration
- [ ] Performance testing (discovery latency)
- [ ] Update DATA-0088 ADR status to "Implemented"

### Phase 4: Advanced Features (Future)

- [ ] Lantern integration for cross-subnet discovery
- [ ] Chirp listener for passive topology updates
- [ ] Automatic tending on first successful Stone connection
- [ ] Re-tending on Stone failover (triggers `InvalidateStone()`)
- [ ] Garden topology health dashboard integration
- [ ] Offering pre-warming on startup (resolve commonly used offerings eagerly)

---

## 17. Related Documents

| Document | Purpose |
|----------|---------|
| [driver-specification.md](../../zen-garden/docs/reference/driver-specification.md) | Zen Garden driver spec v2.0 |
| [DATA-0088](../decisions/DATA-0088-adapter-auto-configuration-resolver-pipeline.md) | Adapter auto-configuration ADR |
| [ServiceDiscoveryAdapterBase.cs](../../src/Koan.Core/Orchestration/ServiceDiscoveryAdapterBase.cs) | Base adapter implementation |
| [IServiceDiscoveryAdapter.cs](../../src/Koan.Core/Orchestration/Abstractions/IServiceDiscoveryAdapter.cs) | Adapter interface |
| [MongoDiscoveryAdapter.cs](../../src/Connectors/Data/Mongo/Discovery/MongoDiscoveryAdapter.cs) | Example adapter implementation |
| [adapter-and-orchestration-registration.md](../architecture/adapter-and-orchestration-registration.md) | Registration standards |

---

## 18. Live Test Validation (2026-01-28)

The following was validated against live Stones on the network:

### Discovery Envelope Format
```json
// Request (wrapped in UdpAnnouncement)
{ "type": "discovery_request", "data": { "discover": "moss", "request_id": "...", "requester": "koan-test-script" }}

// Response
{ "type": "discovery_response", "data": { "stone_id": "...", "stone_name": "stone-coral-prairie", "stone_endpoint": "http://192.168.1.135:7185", "moss_version": "0.1.0.202601272024" }}
```

### API Endpoints Validated

| Endpoint | Purpose | Response |
|----------|---------|----------|
| `GET /health` | Stone health check | `{ "status": "healthy", "version": "...", "components": { ... } }` |
| `GET /api/v1/services` | List running services | `{ "data": { "services": [...], "found": true } }` |
| `GET /api/v1/services/{name}` | Get service with connection info | `{ "data": { "ports": { "native": 27017 }, "resources": { ... } } }` |
| `GET /api/v1/offerings` | List all offerings (available + installed) | No connection info |
| `GET /api/v1/offerings/{name}` | Get offering status | No connection info for running services |

### Key Finding: Connection Info
The `/api/v1/services` endpoint (not `/api/v1/offerings`) provides connection information:
```json
{
  "connection": {
    "hostname": "stone-coral-prairie.local",
    "ip": "192.168.1.135",
    "port": 27017,
    "protocol": "tcp",
    "uris": ["tcp://stone-coral-prairie.local:27017", "tcp://192.168.1.135:27017"]
  }
}
```

### Offering Naming
- **Case-insensitive**: `mongodb` (lowercase) is the canonical name
- **No aliases**: `mongo` returns 404, must use `mongodb`

---

## Summary

This proposal provides a **complete, production-ready integration** between Koan Framework and Zen Garden:

1. **Zero-config** - Add package, infrastructure is discovered automatically
2. **Two-level caching** - Stone binding (tending) + offering cache (app lifetime)
3. **Parallel discovery** - Races cached topology against fresh UDP discovery
4. **Distinct failure modes** - Offering failure vs Stone failure handled separately
5. **Full spec compliance** - Implements driver-specification.md v2.0
6. **Graceful degradation** - Falls back to existing adapters (Docker, localhost, Aspire)
7. **Reference = Intent** - Package reference enables discovery
8. **Koan.Core stays clean** - No Zen Garden knowledge in core

The result: **developers add `Koan.ZenGarden` and their app automatically finds MongoDB, Redis, Ollama, and other services running anywhere in their Zen Garden.**
