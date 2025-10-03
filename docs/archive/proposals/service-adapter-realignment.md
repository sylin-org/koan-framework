# Service Adapter Realignment Proposal

**Version:** 1.0
**Date:** 2025-01-27
**Status:** Draft

## Executive Summary

This proposal outlines a comprehensive realignment of the Koan Framework's service discovery architecture to achieve true Separation of Concerns, eliminate hardcoded provider dependencies from core orchestration, and establish autonomous adapter patterns that maintain the framework's "Reference = Intent" philosophy.

### Current State Problems

- **Architectural Violation**: `Koan.Core.Orchestration` contains hardcoded MongoDB, Ollama, Weaviate-specific logic
- **SoC Breakdown**: Core orchestration layer knows provider implementation details
- **Coupling Issues**: Adding new providers requires modifying core framework code
- **Scattered Responsibilities**: Health checks and discovery logic distributed inconsistently

### Proposed Solution

- **Pure Delegation Model**: Orchestrator coordinates, adapters perform autonomously
- **Provider Autonomy**: Each adapter handles its own discovery, health validation, and connection logic
- **Auto-Registration**: Maintains "Reference = Intent" via `KoanAutoRegistrar` patterns
- **Zero Core Modifications**: New providers add without touching core orchestration

---

## Core Architecture Principles

### 1. Orchestrator Responsibilities (Coordination Only)

```
┌─────────────────────────────────────────┐
│        Service Discovery Coordinator    │
│  • Route requests to adapters          │
│  • Aggregate results                   │
│  • Cache discovery outcomes            │
│  • Handle adapter registration         │
│  • NO provider-specific knowledge      │
└─────────────────────────────────────────┘
                    │
                 Delegates to
                    │
┌─────────────────────────────────────────┐
│         Service Discovery Adapters      │
│  • Read own KoanServiceAttribute       │
│  • Implement discovery strategies      │
│  • Perform health validation           │
│  • Build connection strings            │
│  • Make autonomous decisions           │
└─────────────────────────────────────────┘
```

### 2. Communication Pattern

```
Orchestrator: "mongo adapter, discover yourself"
MongoAdapter: "I tried container DNS, explicit config, localhost. I'm using mongodb://mongo:27017"

Orchestrator: "ollama adapter, discover yourself"
OllamaAdapter: "I found myself at http://ollama:11434, validated all required models"
```

### 3. Framework Compliance

- **Reference = Intent**: Adding `Koan.Data.Connector.Mongo` automatically enables MongoDB discovery
- **Auto-Registration**: `KoanAutoRegistrar` registers both data and discovery capabilities
- **Attribute-Driven**: `KoanServiceAttribute` provides orchestration hints
- **Environment-Aware**: Adapters respect `OrchestrationMode` for discovery strategies

---

## Core Abstractions Design

### Interface Hierarchy

```csharp
// File: src/Koan.Core/Orchestration/Abstractions/IServiceDiscoveryAdapter.cs
namespace Koan.Core.Orchestration.Abstractions;

/// <summary>
/// Contract for autonomous service discovery adapters.
/// Each service adapter implements this to handle its own discovery process.
/// </summary>
public interface IServiceDiscoveryAdapter
{
    /// <summary>Primary service identifier (e.g., "mongo", "ollama")</summary>
    string ServiceName { get; }

    /// <summary>Alternative identifiers this adapter handles (e.g., ["mongodb"] for mongo)</summary>
    string[] Aliases { get; }

    /// <summary>Adapter priority for service name conflicts (higher wins)</summary>
    int Priority { get; }

    /// <summary>
    /// Autonomous discovery - adapter reads its own KoanServiceAttribute,
    /// tries discovery strategies, validates health, and decides what to use.
    /// </summary>
    Task<ServiceDiscoveryResult> DiscoverAsync(
        DiscoveryContext context,
        CancellationToken cancellationToken = default);
}

// File: src/Koan.Core/Orchestration/Abstractions/IServiceDiscoveryCoordinator.cs
/// <summary>
/// Pure coordination layer - delegates to registered adapters without provider knowledge.
/// </summary>
public interface IServiceDiscoveryCoordinator
{
    /// <summary>Delegate discovery to registered adapter for service name</summary>
    Task<ServiceDiscoveryResult> DiscoverServiceAsync(
        string serviceName,
        DiscoveryContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>Get all registered adapters (for diagnostics)</summary>
    IServiceDiscoveryAdapter[] GetRegisteredAdapters();
}
```

### Supporting Types

```csharp
// File: src/Koan.Core/Orchestration/Models/DiscoveryContext.cs
/// <summary>Environment and configuration context for discovery</summary>
public sealed record DiscoveryContext
{
    public OrchestrationMode OrchestrationMode { get; init; } = OrchestrationMode.SelfOrchestrated;
    public IConfiguration Configuration { get; init; } = null!;
    public bool RequireHealthValidation { get; init; } = true;
    public TimeSpan HealthCheckTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxRetryAttempts { get; init; } = 2;
    public IDictionary<string, object>? Parameters { get; init; }
}

// File: src/Koan.Core/Orchestration/Models/ServiceDiscoveryResult.cs
/// <summary>Result of autonomous adapter discovery</summary>
public sealed record ServiceDiscoveryResult
{
    public string ServiceName { get; init; } = "";
    public string ServiceUrl { get; init; } = "";
    public bool IsSuccessful { get; init; }
    public bool IsHealthy { get; init; }
    public string DiscoveryMethod { get; init; } = "";
    public string? ErrorMessage { get; init; }
    public IDictionary<string, object>? Metadata { get; init; }
    public DateTime DiscoveredAt { get; init; } = DateTime.UtcNow;

    // Factory methods
    public static ServiceDiscoveryResult Success(string serviceName, string serviceUrl, string method, bool isHealthy = true) =>
        new() { ServiceName = serviceName, ServiceUrl = serviceUrl, DiscoveryMethod = method, IsSuccessful = true, IsHealthy = isHealthy };

    public static ServiceDiscoveryResult Failed(string serviceName, string error) =>
        new() { ServiceName = serviceName, IsSuccessful = false, ErrorMessage = error };

    public static ServiceDiscoveryResult NoAdapter(string serviceName) =>
        Failed(serviceName, $"No discovery adapter registered for service '{serviceName}'");
}

// File: src/Koan.Core/Orchestration/Models/DiscoveryCandidate.cs
/// <summary>Internal adapter use - represents a discovery attempt</summary>
internal sealed record DiscoveryCandidate(string Url, string Method, int Priority = 1);
```

---

## Implementation Specifications

### Core Coordinator Implementation

```csharp
// File: src/Koan.Core/Orchestration/ServiceDiscoveryCoordinator.cs
namespace Koan.Core.Orchestration;

/// <summary>
/// Pure delegation coordinator - routes to adapters, aggregates results.
/// Zero provider-specific knowledge.
/// </summary>
internal sealed class ServiceDiscoveryCoordinator : IServiceDiscoveryCoordinator
{
    private readonly ConcurrentDictionary<string, IServiceDiscoveryAdapter> _adapters = new();
    private readonly ILogger<ServiceDiscoveryCoordinator> _logger;

    public ServiceDiscoveryCoordinator(
        IEnumerable<IServiceDiscoveryAdapter> adapters,
        ILogger<ServiceDiscoveryCoordinator> logger)
    {
        _logger = logger;
        RegisterAdapters(adapters);
    }

    public async Task<ServiceDiscoveryResult> DiscoverServiceAsync(
        string serviceName,
        DiscoveryContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (!_adapters.TryGetValue(serviceName.ToLowerInvariant(), out var adapter))
        {
            _logger.LogWarning("No discovery adapter registered for service: {ServiceName}", serviceName);
            return ServiceDiscoveryResult.NoAdapter(serviceName);
        }

        context ??= new DiscoveryContext();

        _logger.LogDebug("Delegating discovery of {ServiceName} to {AdapterType}",
            serviceName, adapter.GetType().Name);

        try
        {
            // Pure delegation - "Adapter, discover yourself"
            var result = await adapter.DiscoverAsync(context, cancellationToken);

            _logger.LogInformation("Service {ServiceName} discovery result: {IsSuccessful} -> {ServiceUrl}",
                serviceName, result.IsSuccessful, result.ServiceUrl);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discovery adapter {AdapterType} failed for service {ServiceName}",
                adapter.GetType().Name, serviceName);
            return ServiceDiscoveryResult.Failed(serviceName, $"Adapter exception: {ex.Message}");
        }
    }

    public IServiceDiscoveryAdapter[] GetRegisteredAdapters() =>
        _adapters.Values.ToArray();

    private void RegisterAdapters(IEnumerable<IServiceDiscoveryAdapter> adapters)
    {
        foreach (var adapter in adapters.OrderByDescending(a => a.Priority))
        {
            RegisterAdapter(adapter);
        }
    }

    private void RegisterAdapter(IServiceDiscoveryAdapter adapter)
    {
        var serviceNames = new[] { adapter.ServiceName }.Concat(adapter.Aliases);

        foreach (var serviceName in serviceNames)
        {
            var key = serviceName.ToLowerInvariant();
            _adapters.AddOrUpdate(key, adapter, (_, existing) =>
                adapter.Priority > existing.Priority ? adapter : existing);

            _logger.LogInformation("Registered discovery adapter: {ServiceName} -> {AdapterType} (priority: {Priority})",
                serviceName, adapter.GetType().Name, adapter.Priority);
        }
    }
}
```

### Base Adapter Implementation

```csharp
// File: src/Koan.Core/Orchestration/ServiceDiscoveryAdapterBase.cs
namespace Koan.Core.Orchestration;

/// <summary>
/// Base implementation providing common discovery patterns.
/// Adapters can inherit this or implement IServiceDiscoveryAdapter directly.
/// </summary>
public abstract class ServiceDiscoveryAdapterBase : IServiceDiscoveryAdapter
{
    protected readonly IConfiguration _configuration;
    protected readonly ILogger _logger;

    protected ServiceDiscoveryAdapterBase(IConfiguration configuration, ILogger logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public abstract string ServiceName { get; }
    public virtual string[] Aliases => Array.Empty<string>();
    public virtual int Priority => 10;

    public async Task<ServiceDiscoveryResult> DiscoverAsync(
        DiscoveryContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("{ServiceName} adapter starting autonomous discovery", ServiceName);

        // Get our own KoanServiceAttribute
        var attribute = GetServiceAttribute();
        if (attribute == null)
        {
            return ServiceDiscoveryResult.Failed(ServiceName, "No KoanServiceAttribute found on adapter factory");
        }

        // Build discovery candidates based on orchestration mode
        var candidates = BuildDiscoveryCandidates(attribute, context);

        // Try each candidate until one succeeds
        foreach (var candidate in candidates.OrderBy(c => c.Priority))
        {
            _logger.LogDebug("{ServiceName} trying discovery method: {Method} -> {Url}",
                ServiceName, candidate.Method, candidate.Url);

            if (await ValidateCandidate(candidate.Url, context, cancellationToken))
            {
                _logger.LogInformation("{ServiceName} adapter decided: {Url} via {Method}",
                    ServiceName, candidate.Url, candidate.Method);

                return ServiceDiscoveryResult.Success(ServiceName, candidate.Url, candidate.Method, true);
            }
        }

        _logger.LogWarning("{ServiceName} adapter failed all discovery attempts", ServiceName);
        return ServiceDiscoveryResult.Failed(ServiceName, "All discovery methods failed");
    }

    /// <summary>Override to specify which factory type contains KoanServiceAttribute</summary>
    protected abstract Type GetFactoryType();

    /// <summary>Override to implement service-specific health validation</summary>
    protected abstract Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken);

    /// <summary>Override to customize discovery candidate generation</summary>
    protected virtual IEnumerable<DiscoveryCandidate> BuildDiscoveryCandidates(KoanServiceAttribute attribute, DiscoveryContext context)
    {
        return context.OrchestrationMode switch
        {
            OrchestrationMode.Container => new[]
            {
                new DiscoveryCandidate(BuildServiceUrl(attribute.Scheme, attribute.Host, attribute.EndpointPort), "container-dns", 1),
                new DiscoveryCandidate(ReadExplicitConfiguration(), "explicit-config", 2),
                new DiscoveryCandidate(BuildServiceUrl(attribute.LocalScheme, attribute.LocalHost, attribute.LocalPort), "host-fallback", 3)
            },
            OrchestrationMode.AspireManaged => new[]
            {
                new DiscoveryCandidate(ReadAspireServiceDiscovery(), "aspire-discovery", 1),
                new DiscoveryCandidate(ReadExplicitConfiguration(), "explicit-config", 2)
            },
            _ => new[]
            {
                new DiscoveryCandidate(ReadExplicitConfiguration(), "explicit-config", 1),
                new DiscoveryCandidate(BuildServiceUrl(attribute.LocalScheme, attribute.LocalHost, attribute.LocalPort), "localhost", 2)
            }
        }.Where(c => !string.IsNullOrWhiteSpace(c.Url));
    }

    private KoanServiceAttribute? GetServiceAttribute() =>
        GetFactoryType().GetCustomAttribute<KoanServiceAttribute>();

    private string? BuildServiceUrl(string? scheme, string? host, int port) =>
        string.IsNullOrWhiteSpace(scheme) || string.IsNullOrWhiteSpace(host) ? null : $"{scheme}://{host}:{port}";

    private async Task<bool> ValidateCandidate(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        if (!context.RequireHealthValidation) return true;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(context.HealthCheckTimeout);

            return await ValidateServiceHealth(serviceUrl, context, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("{ServiceName} health check timed out for {Url}", ServiceName, serviceUrl);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("{ServiceName} health check failed for {Url}: {Error}", ServiceName, serviceUrl, ex.Message);
            return false;
        }
    }

    /// <summary>Override to customize configuration reading</summary>
    protected virtual string? ReadExplicitConfiguration() => null;

    /// <summary>Override to implement Aspire service discovery</summary>
    protected virtual string? ReadAspireServiceDiscovery() => null;
}
```

---

## Adapter Implementation Patterns

### MongoDB Discovery Adapter

```csharp
// File: src/Koan.Data.Connector.Mongo/Discovery/MongoDiscoveryAdapter.cs
namespace Koan.Data.Connector.Mongo.Discovery;

/// <summary>
/// MongoDB autonomous discovery adapter.
/// Reads KoanServiceAttribute from MongoAdapterFactory and handles MongoDB-specific discovery.
/// </summary>
internal sealed class MongoDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "mongo";
    public override string[] Aliases => new[] { "mongodb" };

    public MongoDiscoveryAdapter(IConfiguration configuration, ILogger<MongoDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    protected override Type GetFactoryType() => typeof(MongoAdapterFactory);

    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            var settings = MongoClientSettings.FromConnectionString(serviceUrl);
            settings.ServerSelectionTimeout = context.HealthCheckTimeout;

            var client = new MongoClient(settings);
            await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1), cancellationToken: cancellationToken);

            _logger.LogDebug("MongoDB health check passed for {Url}", serviceUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("MongoDB health check failed for {Url}: {Error}", serviceUrl, ex.Message);
            return false;
        }
    }

    protected override string? ReadExplicitConfiguration()
    {
        return _configuration.GetConnectionString("MongoDB") ??
               _configuration["Koan:Data:Mongo:ConnectionString"] ??
               _configuration["Koan:Data:ConnectionString"];
    }

    protected override IEnumerable<DiscoveryCandidate> BuildDiscoveryCandidates(KoanServiceAttribute attribute, DiscoveryContext context)
    {
        // MongoDB-specific candidate building with auth parameters
        var baseUrl = $"{attribute.Scheme ?? "mongodb"}://{attribute.Host}:{attribute.EndpointPort}";
        var localUrl = $"{attribute.LocalScheme ?? "mongodb"}://{attribute.LocalHost}:{attribute.LocalPort}";

        // Apply MongoDB-specific connection parameters if provided
        if (context.Parameters != null)
        {
            baseUrl = ApplyMongoConnectionParameters(baseUrl, context.Parameters);
            localUrl = ApplyMongoConnectionParameters(localUrl, context.Parameters);
        }

        return context.OrchestrationMode switch
        {
            OrchestrationMode.Container => new[]
            {
                new DiscoveryCandidate(baseUrl, "container-dns", 1),
                new DiscoveryCandidate(ReadExplicitConfiguration(), "explicit-config", 2),
                new DiscoveryCandidate(localUrl, "host-fallback", 3)
            },
            _ => base.BuildDiscoveryCandidates(attribute, context)
        }.Where(c => !string.IsNullOrWhiteSpace(c.Url));
    }

    private string ApplyMongoConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        var uri = new Uri(baseUrl);
        var auth = "";
        var database = "";

        if (parameters.TryGetValue("username", out var username) &&
            parameters.TryGetValue("password", out var password))
        {
            auth = $"{username}:{password}@";
        }

        if (parameters.TryGetValue("database", out var db))
        {
            database = $"/{db}";
        }

        return $"{uri.Scheme}://{auth}{uri.Host}:{uri.Port}{database}";
    }
}

// File: src/Koan.Data.Connector.Mongo/Initialization/KoanAutoRegistrar.cs
public sealed class KoanAutoRegistrar : IKoanInitializer
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Existing MongoDB data adapter registration
        services.Configure<MongoOptions>(configuration.GetSection("Koan:Data:Mongo"));
        services.AddSingleton<MongoClientProvider>();
        services.AddScoped(typeof(IDataRepository<,>), typeof(MongoRepository<,>));

        // NEW: Register MongoDB discovery adapter
        services.AddSingleton<IServiceDiscoveryAdapter, MongoDiscoveryAdapter>();
    }
}
```

### Ollama Discovery Adapter

```csharp
// File: src/Koan.AI.Connector.Ollama/Discovery/OllamaDiscoveryAdapter.cs
namespace Koan.AI.Connector.Ollama.Discovery;

/// <summary>
/// Ollama autonomous discovery adapter with model validation.
/// </summary>
internal sealed class OllamaDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "ollama";
    public override string[] Aliases => Array.Empty<string>();

    public OllamaDiscoveryAdapter(IConfiguration configuration, ILogger<OllamaDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    protected override Type GetFactoryType() => typeof(OllamaAdapter);

    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = context.HealthCheckTimeout };

            // Basic connectivity check
            var response = await client.GetAsync($"{serviceUrl}/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode) return false;

            // Ollama-specific: Validate required models if configured
            var requiredModels = _configuration.GetSection("Koan:Ai:Ollama:RequiredModels").Get<string[]>();
            if (requiredModels?.Length > 0)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var hasAllModels = requiredModels.All(model => content.Contains($"\"{model}\""));

                if (!hasAllModels)
                {
                    _logger.LogDebug("Ollama at {Url} missing required models: {Models}",
                        serviceUrl, string.Join(", ", requiredModels));
                    return false;
                }

                _logger.LogDebug("Ollama at {Url} has all required models: {Models}",
                    serviceUrl, string.Join(", ", requiredModels));
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Ollama health check failed for {Url}: {Error}", serviceUrl, ex.Message);
            return false;
        }
    }

    protected override string? ReadExplicitConfiguration()
    {
        return _configuration["Koan:Ai:Ollama:BaseUrl"] ??
               _configuration["Koan:Ai:BaseUrl"];
    }

    protected override IEnumerable<DiscoveryCandidate> BuildDiscoveryCandidates(KoanServiceAttribute attribute, DiscoveryContext context)
    {
        // Check for environment variable hints (existing Ollama pattern)
        var envUrls = Environment.GetEnvironmentVariable("Koan_AI_OLLAMA_URLS");
        var envCandidates = string.IsNullOrWhiteSpace(envUrls)
            ? Enumerable.Empty<DiscoveryCandidate>()
            : envUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(url => new DiscoveryCandidate(url.Trim(), "environment-urls", 0));

        return envCandidates.Concat(base.BuildDiscoveryCandidates(attribute, context));
    }
}
```

### PostgreSQL Discovery Adapter

```csharp
// File: src/Koan.Data.Connector.Postgres/Discovery/PostgresDiscoveryAdapter.cs
namespace Koan.Data.Connector.Postgres.Discovery;

/// <summary>
/// PostgreSQL autonomous discovery adapter.
/// </summary>
internal sealed class PostgresDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "postgres";
    public override string[] Aliases => new[] { "postgresql" };

    public PostgresDiscoveryAdapter(IConfiguration configuration, ILogger<PostgresDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    protected override Type GetFactoryType() => typeof(PostgresAdapterFactory);

    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new NpgsqlConnection(serviceUrl);
            await connection.OpenAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("PostgreSQL health check failed for {Url}: {Error}", serviceUrl, ex.Message);
            return false;
        }
    }

    protected override string? ReadExplicitConfiguration()
    {
        return _configuration.GetConnectionString("PostgreSQL") ??
               _configuration["Koan:Data:Postgres:ConnectionString"] ??
               _configuration["Koan:Data:ConnectionString"];
    }

    protected override IEnumerable<DiscoveryCandidate> BuildDiscoveryCandidates(KoanServiceAttribute attribute, DiscoveryContext context)
    {
        // PostgreSQL-specific connection string building
        var candidates = base.BuildDiscoveryCandidates(attribute, context).ToList();

        // Apply PostgreSQL-specific parameters if provided
        if (context.Parameters != null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(candidates[i].Url))
                {
                    candidates[i] = candidates[i] with
                    {
                        Url = ApplyPostgresConnectionParameters(candidates[i].Url, context.Parameters)
                    };
                }
            }
        }

        return candidates;
    }

    private string ApplyPostgresConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        var builder = new NpgsqlConnectionStringBuilder(baseUrl);

        if (parameters.TryGetValue("database", out var database))
            builder.Database = database.ToString();
        if (parameters.TryGetValue("username", out var username))
            builder.Username = username.ToString();
        if (parameters.TryGetValue("password", out var password))
            builder.Password = password.ToString();

        return builder.ConnectionString;
    }
}
```

---

## Refactored Core Orchestration Infrastructure

### Updated OrchestrationAwareServiceDiscovery

```csharp
// File: src/Koan.Core/Orchestration/OrchestrationAwareServiceDiscovery.cs
namespace Koan.Core.Orchestration;

/// <summary>
/// Maintains existing interface for backward compatibility but delegates to coordinator.
/// </summary>
public sealed class OrchestrationAwareServiceDiscovery : IOrchestrationAwareServiceDiscovery
{
    private readonly IServiceDiscoveryCoordinator _coordinator;
    private readonly OrchestrationMode _orchestrationMode;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrchestrationAwareServiceDiscovery> _logger;

    public OrchestrationAwareServiceDiscovery(
        IServiceDiscoveryCoordinator coordinator,
        IConfiguration configuration,
        ILogger<OrchestrationAwareServiceDiscovery> logger)
    {
        _coordinator = coordinator;
        _configuration = configuration;
        _logger = logger;
        _orchestrationMode = KoanEnv.OrchestrationMode;
    }

    public async Task<ServiceDiscoveryResult> DiscoverServiceAsync(
        string serviceName,
        ServiceDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var context = new DiscoveryContext
        {
            OrchestrationMode = _orchestrationMode,
            Configuration = _configuration,
            RequireHealthValidation = options?.HealthCheck?.Required ?? true,
            HealthCheckTimeout = options?.HealthCheck?.Timeout ?? TimeSpan.FromSeconds(5),
            Parameters = ExtractParametersFromOptions(options)
        };

        return await _coordinator.DiscoverServiceAsync(serviceName, context, cancellationToken);
    }

    // Backward compatibility - converts old ServiceDiscoveryOptions to new DiscoveryContext
    private IDictionary<string, object>? ExtractParametersFromOptions(ServiceDiscoveryOptions? options)
    {
        if (options == null) return null;

        var parameters = new Dictionary<string, object>();

        // Extract common parameters from legacy options structure
        if (options.UrlHints != null)
        {
            // This is a bridge during migration - new adapters won't use UrlHints
            parameters["urlHints"] = options.UrlHints;
        }

        return parameters.Count > 0 ? parameters : null;
    }
}
```

### Eliminated ServiceDiscoveryExtensions Hardcoded Methods

```csharp
// File: src/Koan.Core/Orchestration/ServiceDiscoveryExtensions.cs
namespace Koan.Core.Orchestration;

/// <summary>
/// Generic service discovery extension methods.
/// NO provider-specific knowledge - pure delegation to adapters.
/// </summary>
public static class ServiceDiscoveryExtensions
{
    /// <summary>
    /// Discover any service by name - delegates to registered adapter.
    /// </summary>
    public static async Task<string> DiscoverServiceUrlAsync(
        this IOrchestrationAwareServiceDiscovery discovery,
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var result = await discovery.DiscoverServiceAsync(serviceName, cancellationToken: cancellationToken);

        if (!result.IsSuccessful)
            throw new InvalidOperationException($"Failed to discover service '{serviceName}': {result.ErrorMessage}");

        return result.ServiceUrl;
    }

    /// <summary>
    /// Discover service with connection parameters - adapter handles parameter application.
    /// </summary>
    public static async Task<string> DiscoverServiceWithParametersAsync(
        this IOrchestrationAwareServiceDiscovery discovery,
        string serviceName,
        IDictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        var options = new ServiceDiscoveryOptions(); // Bridge during migration
        var result = await discovery.DiscoverServiceAsync(serviceName, options, cancellationToken);

        if (!result.IsSuccessful)
            throw new InvalidOperationException($"Failed to discover service '{serviceName}': {result.ErrorMessage}");

        return result.ServiceUrl;
    }

    /// <summary>
    /// Quick helper for connection string resolution with retry.
    /// </summary>
    public static string ResolveConnectionString(
        this IOrchestrationAwareServiceDiscovery discovery,
        string serviceName,
        OrchestrationConnectionHints hints)
    {
        // Legacy method maintained for existing code - delegates to adapters
        var task = discovery.DiscoverServiceUrlAsync(serviceName);
        return task.GetAwaiter().GetResult();
    }
}

// REMOVED: All hardcoded provider methods
// - ForMongoDB()
// - ForOllama()
// - ForWeaviate()
// - ForRabbitMQ()
// - ForVault()
// - BuildConnectionString() switch statements
```

---

## Migration Strategy and Timeline

### Phase 1: Foundation Infrastructure (Week 1-2)

#### Tasks:

1. **Create Core Abstractions**

   - `IServiceDiscoveryAdapter` interface
   - `IServiceDiscoveryCoordinator` interface
   - `ServiceDiscoveryCoordinator` implementation
   - `ServiceDiscoveryAdapterBase` base class
   - Supporting models (`DiscoveryContext`, `ServiceDiscoveryResult`, etc.)

2. **Update Core Auto-Registration**
   - Modify `OrchestrationAutoRegistrar` to register coordinator
   - Add adapter auto-registration infrastructure
   - Maintain backward compatibility with existing `IOrchestrationAwareServiceDiscovery`

#### Files Created:

```
src/Koan.Core/Orchestration/Abstractions/IServiceDiscoveryAdapter.cs
src/Koan.Core/Orchestration/Abstractions/IServiceDiscoveryCoordinator.cs
src/Koan.Core/Orchestration/ServiceDiscoveryCoordinator.cs
src/Koan.Core/Orchestration/ServiceDiscoveryAdapterBase.cs
src/Koan.Core/Orchestration/Models/DiscoveryContext.cs
src/Koan.Core/Orchestration/Models/ServiceDiscoveryResult.cs
src/Koan.Core/Orchestration/Models/DiscoveryCandidate.cs
```

#### Agent Assignments:

- **Koan-framework-specialist**: Review abstractions for framework compliance
- **Koan-bootstrap-specialist**: Design auto-registration patterns

### Phase 2: Adapter Implementations (Week 3-4)

#### Tasks:

1. **Database Adapters**

   - `MongoDiscoveryAdapter` with MongoDB-specific health checks
   - `PostgresDiscoveryAdapter` with PostgreSQL connection validation
   - `SqliteDiscoveryAdapter` for file-based discovery

2. **AI Service Adapters**

   - `OllamaDiscoveryAdapter` with model validation
   - `OpenAIDiscoveryAdapter` for API key-based services

3. **Infrastructure Service Adapters**
   - `WeaviateDiscoveryAdapter` for vector database
   - `RabbitMQDiscoveryAdapter` for message queues
   - `VaultDiscoveryAdapter` for secrets management

#### Agent Assignments:

- **Koan-data-architect**: Implement database adapter discovery patterns
- **Koan-ai-gateway-integrator**: Implement AI service discovery patterns
- **Koan-orchestration-devops**: Implement infrastructure service patterns

### Phase 3: Core Refactoring (Week 5)

#### Tasks:

1. **Remove Hardcoded Logic**

   - Eliminate provider-specific methods from `ServiceDiscoveryExtensions`
   - Remove hardcoded connection string building
   - Update `OrchestrationAwareServiceDiscovery` to use coordinator

2. **Configurator Updates**
   - Update `MongoOptionsConfigurator` to use discovery coordinator
   - Update other provider configurators to use new patterns
   - Maintain existing configuration contracts

#### Files Modified:

```
src/Koan.Core/Orchestration/ServiceDiscoveryExtensions.cs (major cleanup)
src/Koan.Core/Orchestration/OrchestrationAwareServiceDiscovery.cs (delegation)
src/Koan.Data.Connector.Mongo/MongoOptionsConfigurator.cs (use coordinator)
src/Koan.Data.Connector.Postgres/PostgresOptionsConfigurator.cs (use coordinator)
```

#### Agent Assignments:

- **Koan-framework-specialist**: Ensure compliance during refactoring
- **Koan-config-guardian**: Validate configuration compatibility

### Phase 4: Testing and Validation (Week 6)

#### Tasks:

1. **Unit Testing**

   - Test each adapter in isolation
   - Test coordinator delegation logic
   - Test backward compatibility

2. **Integration Testing**

   - Test full discovery workflows
   - Test container vs local vs Aspire scenarios
   - Test health check validations

3. **Regression Testing**
   - Validate existing applications continue working
   - Test S5.Recs and other samples
   - Performance validation

#### Agent Assignments:

- **Koan-developer-experience-enhancer**: Test developer workflows
- **Koan-performance-optimizer**: Validate performance characteristics

---

## Usage Patterns and Examples

### Basic Service Discovery

```csharp
// Application code - no changes required
public class DataService
{
    private readonly IOrchestrationAwareServiceDiscovery _discovery;

    public DataService(IOrchestrationAwareServiceDiscovery discovery)
    {
        _discovery = discovery;
    }

    public async Task InitializeAsync()
    {
        // Orchestrator delegates to MongoDiscoveryAdapter
        var mongoUrl = await _discovery.DiscoverServiceUrlAsync("mongo");

        // Orchestrator delegates to OllamaDiscoveryAdapter
        var ollamaUrl = await _discovery.DiscoverServiceUrlAsync("ollama");

        Console.WriteLine($"Using MongoDB: {mongoUrl}");
        Console.WriteLine($"Using Ollama: {ollamaUrl}");
    }
}
```

### Provider Configuration Integration

```csharp
// MongoOptionsConfigurator - updated to use coordinator
public class MongoOptionsConfigurator : AdapterOptionsConfigurator<MongoOptions>
{
    private readonly IServiceDiscoveryCoordinator _coordinator;

    protected override void ConfigureProviderSpecific(MongoOptions options)
    {
        if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            Logger?.LogInformation("Auto-detection mode - delegating to MongoDB discovery adapter");

            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                Configuration = Configuration,
                Parameters = BuildMongoParameters(options)
            };

            // "MongoDB adapter, discover yourself"
            var result = _coordinator.DiscoverServiceAsync("mongo", context)
                                   .GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                // "I did, and I'm using this connection string"
                options.ConnectionString = result.ServiceUrl;
                Logger?.LogInformation("MongoDB adapter decided: {ConnectionString}", result.ServiceUrl);
            }
            else
            {
                Logger?.LogError("MongoDB discovery failed: {Error}", result.ErrorMessage);
                throw new InvalidOperationException($"MongoDB discovery failed: {result.ErrorMessage}");
            }
        }
    }

    private IDictionary<string, object> BuildMongoParameters(MongoOptions options)
    {
        var parameters = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(options.Database))
            parameters["database"] = options.Database;

        // Add auth parameters if configured
        var username = Configuration["Koan:Data:Mongo:Username"];
        var password = Configuration["Koan:Data:Mongo:Password"];

        if (!string.IsNullOrWhiteSpace(username))
        {
            parameters["username"] = username;
            parameters["password"] = password ?? "";
        }

        return parameters;
    }
}
```

### Custom Adapter Implementation

```csharp
// Example: Redis discovery adapter
public sealed class RedisDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "redis";
    public override string[] Aliases => Array.Empty<string>();

    public RedisDiscoveryAdapter(IConfiguration configuration, ILogger<RedisDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    protected override Type GetFactoryType() => typeof(RedisAdapterFactory);

    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            var redis = ConnectionMultiplexer.Connect(serviceUrl);
            await redis.GetDatabase().PingAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    protected override string? ReadExplicitConfiguration()
    {
        return _configuration.GetConnectionString("Redis") ??
               _configuration["Koan:Data:Redis:ConnectionString"];
    }
}

// Auto-registration in Redis package
public sealed class KoanAutoRegistrar : IKoanInitializer
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register Redis data capabilities
        services.AddStackExchangeRedisCache(options => { /* config */ });

        // Register Redis discovery capabilities
        services.AddSingleton<IServiceDiscoveryAdapter, RedisDiscoveryAdapter>();
    }
}
```

### Testing Patterns

```csharp
// Unit testing individual adapters
[Test]
public async Task MongoAdapter_ContainerMode_ReturnsContainerDns()
{
    // Arrange
    var config = new ConfigurationBuilder().Build();
    var logger = new Mock<ILogger<MongoDiscoveryAdapter>>();
    var adapter = new MongoDiscoveryAdapter(config, logger.Object);

    var context = new DiscoveryContext
    {
        OrchestrationMode = OrchestrationMode.Container,
        Configuration = config,
        RequireHealthValidation = false
    };

    // Act
    var result = await adapter.DiscoverAsync(context);

    // Assert
    Assert.That(result.IsSuccessful, Is.True);
    Assert.That(result.ServiceUrl, Is.EqualTo("mongodb://mongo:27017"));
    Assert.That(result.DiscoveryMethod, Is.EqualTo("container-dns"));
}

// Integration testing coordinator
[Test]
public async Task Coordinator_WithRegisteredAdapter_DelegatesToAdapter()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddSingleton<IServiceDiscoveryAdapter, MongoDiscoveryAdapter>();
    services.AddSingleton<IServiceDiscoveryCoordinator, ServiceDiscoveryCoordinator>();

    var provider = services.BuildServiceProvider();
    var coordinator = provider.GetRequiredService<IServiceDiscoveryCoordinator>();

    // Act
    var result = await coordinator.DiscoverServiceAsync("mongo", new DiscoveryContext());

    // Assert
    Assert.That(result.IsSuccessful, Is.True);
}
```

---

## Backward Compatibility Strategy

### Legacy Method Preservation

```csharp
// File: src/Koan.Core/Orchestration/LegacyServiceDiscoveryExtensions.cs
namespace Koan.Core.Orchestration;

/// <summary>
/// Temporary backward compatibility layer for deprecated methods.
/// Mark as obsolete and remove in next major version.
/// </summary>
public static class LegacyServiceDiscoveryExtensions
{
    [Obsolete("Use DiscoverServiceUrlAsync('mongo') instead. Will be removed in v2.0")]
    public static ServiceDiscoveryOptions ForMongoDB(
        string? databaseName = null,
        string? username = null,
        string? password = null)
    {
        // Bridge to new adapter-based approach
        var parameters = new Dictionary<string, object>();
        if (databaseName != null) parameters["database"] = databaseName;
        if (username != null) parameters["username"] = username;
        if (password != null) parameters["password"] = password;

        return new ServiceDiscoveryOptions
        {
            // Legacy structure maintained during transition
            ExplicitConfigurationSections = new[]
            {
                "Koan:Data:Mongo",
                "Koan:Data",
                "ConnectionStrings"
            }
        };
    }

    [Obsolete("Use DiscoverServiceUrlAsync('ollama') instead. Will be removed in v2.0")]
    public static ServiceDiscoveryOptions ForOllama()
    {
        return new ServiceDiscoveryOptions
        {
            ExplicitConfigurationSections = new[]
            {
                "Koan:Ai:Ollama",
                "Koan:Ai"
            }
        };
    }

    // Similar obsolete methods for other providers...
}
```

### Configuration Compatibility

```csharp
// Existing configuration continues to work
{
  "Koan": {
    "Data": {
      "Mongo": {
        "ConnectionString": "auto",  // Triggers discovery
        "Database": "MyApp"
      }
    },
    "Ai": {
      "Ollama": {
        "BaseUrl": "http://custom-ollama:11434",  // Explicit config
        "RequiredModels": ["all-minilm", "llama2"]
      }
    }
  }
}
```

### Migration Guide for Applications

```csharp
// BEFORE (deprecated but still works)
var mongoOptions = ServiceDiscoveryExtensions.ForMongoDB("mydb", "user", "pass");
var result = await discovery.DiscoverServiceAsync("mongo", mongoOptions);

// AFTER (recommended)
var mongoUrl = await discovery.DiscoverServiceUrlAsync("mongo");

// OR with parameters
var parameters = new Dictionary<string, object>
{
    ["database"] = "mydb",
    ["username"] = "user",
    ["password"] = "pass"
};
var mongoUrl = await discovery.DiscoverServiceWithParametersAsync("mongo", parameters);
```

---

## Quality Assurance and Testing

### Automated Testing Strategy

#### 1. Unit Tests per Adapter

```csharp
// Test structure for each adapter
[TestFixture]
public class MongoDiscoveryAdapterTests
{
    [Test] public async Task ContainerMode_ValidService_ReturnsContainerDns() { }
    [Test] public async Task LocalMode_ValidService_ReturnsLocalhost() { }
    [Test] public async Task HealthCheckDisabled_SkipsValidation() { }
    [Test] public async Task HealthCheckEnabled_ValidatesConnection() { }
    [Test] public async Task ExplicitConfig_OverridesAutoDiscovery() { }
    [Test] public async Task AllMethodsFail_ReturnsFailureResult() { }
}
```

#### 2. Integration Tests

```csharp
[TestFixture]
public class ServiceDiscoveryIntegrationTests
{
    [Test] public async Task Coordinator_MultipleAdapters_RoutesCorrectly() { }
    [Test] public async Task BackwardCompatibility_LegacyMethods_StillWork() { }
    [Test] public async Task ConfiguratorIntegration_AutoMode_UsesDiscovery() { }
    [Test] public async Task ContainerEnvironment_FullStack_DiscoversAllServices() { }
}
```

#### 3. Performance Tests

```csharp
[TestFixture]
public class ServiceDiscoveryPerformanceTests
{
    [Test] public async Task DiscoveryLatency_UnderLoad_MeetsThresholds() { }
    [Test] public async Task ConcurrentDiscovery_MultipleServices_ScalesLinearly() { }
    [Test] public async Task MemoryUsage_ExtendedOperation_RemainsStable() { }
}
```

### Regression Prevention

#### 1. Contract Tests

- Ensure `IOrchestrationAwareServiceDiscovery` interface unchanged
- Validate existing configurator behavior preserved
- Test backward compatibility with legacy methods

#### 2. Sample Application Validation

- S5.Recs continues to work without changes
- All existing samples start successfully
- Docker Compose orchestration works correctly

#### 3. Configuration Compatibility Tests

- All existing `appsettings.json` formats work
- Environment variable overrides function correctly
- Connection string resolution maintains behavior

---

## Documentation and Knowledge Transfer

### Developer Documentation Updates

#### 1. Service Discovery Guide

````markdown
# Service Discovery in Koan Framework

## Overview

Service discovery is handled by autonomous adapters that discover themselves.

## Usage

```csharp
// Discover any service
var mongoUrl = await discovery.DiscoverServiceUrlAsync("mongo");
var ollamaUrl = await discovery.DiscoverServiceUrlAsync("ollama");
```
````

## Adding New Services

1. Implement `IServiceDiscoveryAdapter`
2. Register in `KoanAutoRegistrar`
3. Service automatically discoverable

````

#### 2. Adapter Development Guide
```markdown
# Creating Discovery Adapters

## Base Implementation
Inherit from `ServiceDiscoveryAdapterBase` for common patterns:

```csharp
public class MyServiceAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "myservice";
    protected override Type GetFactoryType() => typeof(MyServiceAdapterFactory);
    protected override async Task<bool> ValidateServiceHealth(...) { /* implementation */ }
}
````

## Auto-Registration

```csharp
public sealed class KoanAutoRegistrar : IKoanInitializer
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IServiceDiscoveryAdapter, MyServiceAdapter>();
    }
}
```

````

#### 3. Migration Guide
```markdown
# Migrating from Legacy Discovery

## Code Changes
- Replace `ServiceDiscoveryExtensions.ForMongoDB()` with `DiscoverServiceUrlAsync("mongo")`
- No configuration changes required
- Gradual migration supported via compatibility layer

## Timeline
- v1.x: Legacy methods marked `[Obsolete]`
- v2.0: Legacy methods removed
````

### Architecture Decision Records

#### ADR-001: Autonomous Adapter Pattern

```markdown
# ADR-001: Autonomous Service Discovery Adapters

## Status: Accepted

## Context

Core orchestration layer contained hardcoded provider-specific logic, violating SoC.

## Decision

Implement autonomous adapter pattern where:

- Orchestrator coordinates ("Adapter, discover")
- Adapters perform ("I did, using X")
- Zero provider knowledge in core

## Consequences

- True separation of concerns
- Provider autonomy
- Framework extensibility

* Migration complexity
* Interface proliferation
```

---

## Risk Mitigation and Contingency Plans

### High Risk Areas

#### 1. Backward Compatibility Break

**Risk**: Existing applications fail after migration
**Mitigation**:

- Maintain `IOrchestrationAwareServiceDiscovery` interface
- Provide compatibility layer for legacy methods
- Gradual deprecation over multiple versions

#### 2. Performance Regression

**Risk**: New abstraction layer adds latency
**Mitigation**:

- Benchmark current performance
- Optimize adapter registration and lookup
- Cache discovery results per session
- Performance gates in CI/CD

#### 3. Configuration Complexity

**Risk**: New patterns confuse developers
**Mitigation**:

- Maintain existing configuration contracts
- Provide migration examples
- Clear documentation with before/after examples
- Interactive migration tool

### Contingency Plans

#### Rollback Strategy

1. **Phase-by-phase rollback**: Each phase can be independently reverted
2. **Feature flags**: Control new vs legacy discovery per service
3. **Configuration override**: Fallback to explicit connection strings
4. **Legacy compatibility**: Keep old methods until v2.0

#### Incremental Deployment

1. **Service-by-service migration**: Start with non-critical services
2. **Environment isolation**: Test in dev before staging/production
3. **Canary releases**: Gradual rollout with monitoring
4. **Circuit breaker**: Automatic fallback on failure thresholds

---

## Success Metrics and Validation Criteria

### Technical Metrics

- **SoC Compliance**: Zero provider-specific code in `Koan.Core.Orchestration`
- **Performance**: Discovery latency within 5% of current baseline
- **Memory**: No memory leaks during extended operation
- **Test Coverage**: >95% coverage for all new adapters

### Framework Metrics

- **Extensibility**: New adapter added without core modifications
- **Maintainability**: Provider logic isolated and testable
- **Consistency**: All adapters follow identical patterns
- **Documentation**: Complete guides for adapter development

### Business Metrics

- **Developer Experience**: Reduced cognitive load for service setup
- **Framework Adoption**: Easier integration for new service types
- **Maintenance Cost**: Reduced cross-team coordination for service changes
- **Time to Market**: Faster addition of new service integrations

---

## Implementation Roadmap Summary

### Week 1-2: Foundation

- Core abstractions and interfaces
- Coordinator implementation
- Auto-registration infrastructure
- **Deliverable**: Working coordinator with sample adapter

### Week 3-4: Adapter Implementation

- All existing service adapters migrated
- Provider-specific discovery logic extracted
- Health check implementations
- **Deliverable**: Complete adapter ecosystem

### Week 5: Core Refactoring

- Remove hardcoded logic from core
- Update configurators to use coordinator
- Backward compatibility layer
- **Deliverable**: Clean core orchestration layer

### Week 6: Validation

- Comprehensive testing
- Performance validation
- Documentation updates
- **Deliverable**: Production-ready system

### Post-Release: Evolution

- Monitor adoption patterns
- Gather developer feedback
- Iterative improvements
- Plan legacy method removal for v2.0

---

This proposal establishes a foundation for true service adapter autonomy while maintaining the Koan Framework's core principles of simplicity, extensibility, and developer experience. The phased approach ensures minimal disruption while achieving architectural excellence.

