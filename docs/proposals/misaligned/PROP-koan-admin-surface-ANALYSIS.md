# Architectural Analysis: Koan Admin Surface Implementation Gaps

**Status**: Analysis
**Date**: 2025-10-12
**Analyst**: Senior Systems Architecture Review
**Related**: PROP-koan-admin-surface.md

---

## Executive Summary

This analysis evaluates the gap between PROP-koan-admin-surface's aspirational architecture and the current codebase implementation state. The proposal represents a **future-state vision** rather than an implementation guide, with approximately **60% of required infrastructure present but fragmented** across the codebase.

### Critical Finding

Significant reusable infrastructure exists (`BootReport`, `IArtifactExporter`, capability detection) but remains **isolated and unexposed**. The proposal's "LaunchKit" concept requires less net-new development and more **unification of existing capabilities** behind a coherent admin surface.

### Evidence of Incomplete Implementation

- `src/Koan.Web.Admin/` contains empty directories (Controllers, Services, Models, Options, Initialization)
- Test specifications exist at `tests/Suites/Web/Koan.Web.Admin.Tests/` expecting endpoints that don't exist
- No `.csproj` file exists for `Koan.Web.Admin`—only build artifacts from a removed/incomplete implementation
- No console admin module exists in any form

---

## Proposal Objectives (Recap)

**Primary Goals from Proposal:**
1. Turnkey visibility into Koan runtime capabilities via console and web surfaces
2. Ready-made configuration bundle exports (Docker Compose, Aspire, appsettings)
3. Leverage existing auto-discovery and adapter validation pipelines
4. Safe-by-default activation with explicit environment gating
5. Configurable prefix strategy (`/.koan/admin` default)

**Target Deliverables:**
- `Koan.Console.Admin` - interactive console takeover UI
- `Koan.Web.Admin` - ASP.NET dashboard with downloadable bundles
- Shared services: `CapabilitiesSurveyor`, `LaunchKitGenerator`, `ManifestPublisher`, `AdminAuthorizationFilter`

---

## Infrastructure Audit: What Exists vs. What's Missing

### ✅ Solid Foundations (Available for Leverage)

#### 1. BootReport Infrastructure
**Location**: `src/Koan.Core/Hosting/Bootstrap/BootReport.cs:7-197`

**Current Capabilities:**
- Comprehensive module version tracking
- Decision logging (ConnectionAttempt, ProviderElection, Discovery)
- Structured boot reporting with ANSI-formatted output
- Settings and notes collection per module

**Limitations:**
- Console-only output format (`ToString()` method)
- No JSON serialization for HTTP consumption
- Not persisted beyond startup for runtime access
- No HTTP endpoint exposure

**Usage Example:**
```csharp
public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
{
    report.AddModule(ModuleName, ModuleVersion);
    report.AddProviderElection("storage", "postgresql", ["mongodb", "redis"], "explicit config");
    report.AddConnectionAttempt("postgresql", connectionString, success: true);
}
```

#### 2. Artifact Export Infrastructure
**Location**: `src/Koan.Orchestration.Abstractions/Abstractions/IArtifactExporter.cs:5-11`

**Current Capabilities:**
- `IArtifactExporter` abstraction for pluggable exporters
- `ComposeExporter` fully implemented at `src/Connectors/Orchestration/Renderers/Compose/ComposeExporter.cs:11`
- Supports multiple profiles (Local, CI, Staging, Prod)
- Auto-discovery of container mounts via reflection
- Volume management with profile-specific strategies

**Limitations:**
- Requires manual `Plan` object construction
- No runtime introspection to build plans automatically
- No HTTP endpoint for download functionality
- No integration with admin surfaces

**Usage Example:**
```csharp
var exporter = new ComposeExporter();
var plan = new Plan { Services = [...] };  // Manual construction required
await exporter.GenerateAsync(plan, Profile.Local, "./docker-compose.yml");
```

#### 3. Capability Detection
**Location**: `src/Koan.Data.Core/Data.cs` (QueryCaps/WriteCaps)

**Current Capabilities:**
- Per-adapter capability flags (`QueryCapabilities`, `WriteCapabilities`)
- Automatic fallback when provider lacks features
- Individual adapter tests verify capabilities

**Limitations:**
- No unified service to survey all adapters
- Capability information scattered across adapter implementations
- No HTTP endpoint to query runtime capabilities
- No health status integration

#### 4. Environment Detection
**Location**: `KoanEnv` usage throughout codebase

**Current Capabilities:**
- `IsDevelopment`, `InContainer`, `AllowMagicInProduction` flags
- Consistent usage across adapters for environment-specific behavior

**Limitations:**
- No admin-specific authorization integration
- Not wired to any access control filters

#### 5. Auto-Registration Pattern
**Location**: Multiple `KoanAutoRegistrar` implementations

**Current State:**
- Well-established pattern across 10+ modules
- Consistent `IKoanAutoRegistrar` interface
- Automatic service registration via assembly scanning

**Strength**: Admin modules can follow established pattern.

#### 6. Health Check Infrastructure
**Location**: `src/Koan.Recipe.Observability/ObservabilityRecipe.cs:19-24`

**Current Capabilities:**
- Basic ASP.NET Core health checks registered
- Health endpoints defined in `KoanWebConstants.Routes`

**Limitations:**
- Not extended with adapter-specific health checks
- No aggregated health status API

---

### ❌ Critical Missing Components

#### 1. Configuration Options Model
**Expected Location**: `Koan.Admin.Core/Options/KoanAdminOptions.cs` (does not exist)

**Impact**: No binding target for configuration, no validation surface

**Required Properties:**
```csharp
public sealed class KoanAdminOptions
{
    public bool Enabled { get; set; }
    public string PathPrefix { get; set; } = ".";  // dot-prefixed default
    public bool ExposeManifest { get; set; } = true;
    public bool AllowInProduction { get; set; } = false;
    public bool DestructiveOps { get; set; } = false;
    public bool EnableConsoleUi { get; set; }
    public bool EnableWeb { get; set; }

    public AdminAuthorizationOptions Authorization { get; set; } = new();
    public LaunchKitGenerationOptions Generate { get; set; } = new();
    public AdminLoggingOptions Logging { get; set; } = new();
}
```

**Evidence**: `grep "KoanAdminOptions"` returns only proposal document.

#### 2. Route Constants
**Location**: `src/Koan.Web/Infrastructure/KoanWebConstants.cs:48-60`

**Gap**: Admin routes not defined despite tests expecting them

**Tests Expect:**
```csharp
await _fixture.HttpGetAsync("/admin/entities");      // Line: AdminControllerSpec.cs:17
await _fixture.HttpGetAsync("/admin/models");        // Line: AdminControllerSpec.cs:27
await _fixture.HttpGetAsync("/admin/backup");        // Line: BackupEndpointSpec.cs:17
await _fixture.HttpGetAsync("/admin/auth/roles");    // Line: AuthAndRolesSpec.cs:17
```

**Proposal Specifies:**
```
Default: /.koan/admin
Configurable: /_koan/admin, /-koan/admin, /koan/admin
```

**Misalignment**: Tests use unprefixed `/admin` but proposal requires `/.koan/admin` with dot-prefix strategy.

#### 3. LaunchKit Generator Service
**Expected**: Service bridging runtime introspection → Plan → IArtifactExporter

**Required Functionality:**
- Introspect registered data adapters from DI container
- Build `Plan` object from BootReport + discovery results
- Invoke appropriate exporter (Compose, Aspire, etc.)
- Return serialized artifact for download

**Current Workaround**: Manual `Plan` construction by caller

#### 4. Capabilities Surveyor Service
**Expected**: Unified service enumerating all adapter capabilities

**Required Functionality:**
- Enumerate `IKoanRepository<,>` registrations from DI
- Query each adapter's `QueryCaps` and `WriteCaps`
- Collect health status and connection state
- Return aggregated snapshot

**Current State**: Capabilities exist per-adapter but no enumeration service

#### 5. Manifest Publisher Service
**Expected**: Generate `/.koan/manifest.json` discovery document

**Required Contract:**
```json
{
  "version": "1.0",
  "openapi": "/swagger/v1/swagger.json",
  "health": "/health",
  "admin": "/.koan/admin",
  "modules": {
    "web": true,
    "data": true,
    "ai": false,
    "messaging": true
  }
}
```

**Current State**: No implementation exists

#### 6. Authorization Filter
**Expected**: Middleware enforcing environment gating and policies

**Required Gates:**
1. Admin must be enabled via configuration
2. Production requires explicit `AllowInProduction` opt-in
3. Authorization policy enforcement (if configured)
4. Network allowlist support
5. Destructive operations double-gating

**Current State**: No authorization filter exists

#### 7. Admin Controllers and Endpoints
**Expected Location**: `src/Koan.Web.Admin/Controllers/` (directory exists but empty)

**Required Endpoints:**
- `GET /.koan/admin/boot-report` → BootReport JSON
- `GET /.koan/admin/capabilities` → Capabilities snapshot
- `GET /.koan/admin/launchkit/{format}?profile={profile}` → Artifact download
- `GET /.koan/manifest.json` → Discovery manifest
- `GET /.koan/admin/entities` → Entity model list (from tests)
- `GET /.koan/admin/models` → Schema information (from tests)

**Current State**: No controllers exist

#### 8. Console Admin Module
**Expected**: `Koan.Console.Admin` assembly with ANSI UI

**Current State**: Does not exist in any form

**Complexity Factors:**
- ANSI rendering cross-platform compatibility
- Stdin/stdout control conflicts with Koan CLI
- Graceful degradation for non-ANSI terminals
- Log streaming redaction pipeline integration

---

## Detailed Misalignment Analysis

### 1. Phantom Implementation Architecture

**Evidence:**
- Empty directories: `src/Koan.Web.Admin/{Controllers,Services,Models,Options,Initialization}`
- Test files exist but implementation missing
- No `.csproj` file (only build artifacts in obj/bin)

**Root Cause**: Test-driven design debt—tests written before implementation and never completed.

**Architectural Concern**: Tests encode assumptions that contradict the proposal:

| Test Expectation | Proposal Specification | Misalignment |
|------------------|------------------------|--------------|
| `/admin/entities` | `/.koan/admin/entities` | Missing dot-prefix |
| No auth gating | `AdminAuthorizationFilter` required | Security gap |
| Hard-coded routes | Configurable `PathPrefix` | Inflexible |

**Recommendation:**
- Delete orphaned tests OR update to match proposal routes
- Remove empty directory structure to avoid confusion
- Start fresh with proper implementation following proposal

**Location References:**
- Tests: `tests/Suites/Web/Koan.Web.Admin.Tests/Specs/*.cs`
- Empty dirs: `src/Koan.Web.Admin/`

---

### 2. Route Namespace Fragmentation

**Current State** (`KoanWebConstants.Routes`):
```csharp
// Health
public const string HealthBase = "health";              // Line 50
public const string ApiHealth = "/api/health";          // Line 53

// Well-known
public const string WellKnownBase = ".well-known/Koan"; // Line 56
```

**Missing**:
```csharp
// Admin (SHOULD BE ADDED)
public static class Admin
{
    public const string DefaultPrefix = ".";
    public const string BaseNamespace = "koan";
    public const string AdminSuffix = "admin";

    public static string GetBasePath(string prefix = DefaultPrefix)
        => $"/{prefix}{BaseNamespace}/{AdminSuffix}";

    public const string Entities = "entities";
    public const string Models = "models";
    public const string Capabilities = "capabilities";
    public const string LaunchKit = "launchkit";
    public const string BootReport = "boot-report";
}
```

**Architectural Concern**:
- Dot-prefix strategy (`/.koan/`) not reflected in framework constants
- Routing collision risk with application controllers
- Discoverability issues for proxy configurations
- No startup validation for route conflicts

**Impact**:
- Developers may accidentally create conflicting routes
- Proxy configurations may block dot-prefixed paths
- No consistent way to reference admin routes

**Recommendation**:
1. Add admin route constants to `KoanWebConstants`
2. Implement startup collision detection
3. Emit warning if `PathPrefix = "."` used outside Development
4. Provide proxy configuration snippets in documentation

**Location**: `src/Koan.Web/Infrastructure/KoanWebConstants.cs:48-100`

---

### 3. BootReport Exposure Gap

**Current Limitation**:
```csharp
public override string ToString() => FormatWithKoanStyle(options);  // Console-only
```

**Location**: `src/Koan.Core/Hosting/Bootstrap/BootReport.cs:80-88`

**Gaps**:
1. No JSON serialization
2. Not persisted to DI container for runtime access
3. No HTTP endpoint
4. Startup-only telemetry (not accessible post-startup)

**Proposal Expectation**: "Overview panel showing active modules, environment snapshot"

**Recommendation**:

**Step 1: Add JSON Serialization**
```csharp
public sealed class BootReportDto
{
    public string FrameworkVersion { get; set; } = "";
    public List<BootModuleDto> Modules { get; set; } = new();
    public List<DecisionDto> Decisions { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

public BootReportDto ToDto() => new()
{
    FrameworkVersion = _modules.FirstOrDefault(m => m.Name.Contains("Core")).Version ?? "unknown",
    Modules = _modules.Select(m => new BootModuleDto
    {
        Name = m.Name,
        Version = m.Version,
        Settings = m.Settings.Where(s => !s.Secret).Select(s => new { s.Key, s.Value }).ToList(),
        Notes = m.Notes
    }).ToList(),
    Decisions = _decisions.Select(d => new DecisionDto
    {
        Type = d.Type.ToString(),
        Category = d.Category,
        Decision = d.Decision,
        Reason = d.Reason,
        Alternatives = d.Alternatives
    }).ToList(),
    GeneratedAt = DateTime.UtcNow
};
```

**Step 2: Persist to DI Container**
```csharp
// In Koan.Core startup (where BootReport is created)
var bootReport = new BootReport();
// ... populate via registrars ...
services.AddSingleton(bootReport);  // Make available to admin surface
```

**Step 3: Expose via HTTP**
```csharp
// In Koan.Web.Admin controller
[HttpGet("/.koan/admin/boot-report")]
[AdminAuthorization]
public IActionResult GetBootReport([FromServices] BootReport report)
    => Ok(report.ToDto());
```

**Security Consideration**: Redact sensitive settings even in admin surface (already handled by `isSecret` flag).

---

### 4. Fragmented Export Infrastructure

**Current State**: `IArtifactExporter` and `ComposeExporter` exist but isolated

**Location**:
- Interface: `src/Koan.Orchestration.Abstractions/Abstractions/IArtifactExporter.cs:5-11`
- Implementation: `src/Connectors/Orchestration/Renderers/Compose/ComposeExporter.cs:11-412`

**Gap**: No runtime introspection to build `Plan` objects

**Current Usage Requires**:
```csharp
var plan = new Plan
{
    Services = new List<ServiceSpec>
    {
        new ServiceSpec
        {
            Id = "postgres",
            Image = "postgres:16-alpine",
            Env = new Dictionary<string, string> { ["POSTGRES_PASSWORD"] = "dev" },
            Ports = [(5432, 5432)],
            // ... manual construction
        }
    }
};
await exporter.GenerateAsync(plan, Profile.Local, "./docker-compose.yml");
```

**Proposal Expects**: "LaunchKit Downloads — Downloadable bundles generated by the LaunchKit function from the live configuration (detected providers, connection hints, set routing)"

**Missing Component**: Bridge between runtime state and Plan construction

**Recommendation**:

```csharp
// Koan.Admin.Core/Services/LaunchKitGenerator.cs
public sealed class LaunchKitGenerator
{
    private readonly IEnumerable<IArtifactExporter> _exporters;
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LaunchKitGenerator> _logger;

    public async Task<byte[]> GenerateAsync(string format, Profile profile, CancellationToken ct)
    {
        // Find appropriate exporter
        var exporter = _exporters.FirstOrDefault(e => e.Supports(format));
        if (exporter == null)
            throw new NotSupportedException($"Format '{format}' not supported. Available: {string.Join(", ", _exporters.Select(e => e.Id))}");

        // Build plan from runtime introspection
        var plan = await BuildPlanFromRuntimeAsync(profile, ct);

        // Generate to temp file
        using var tempFile = Path.GetTempFileName();
        await exporter.GenerateAsync(plan, profile, tempFile, ct);

        // Read and return
        var content = await File.ReadAllBytesAsync(tempFile, ct);
        _logger.LogInformation("Generated {Format} bundle for {Profile} profile ({Size} bytes)",
            format, profile, content.Length);
        return content;
    }

    private async Task<Plan> BuildPlanFromRuntimeAsync(Profile profile, CancellationToken ct)
    {
        var services = new List<ServiceSpec>();

        // 1. Detect data adapters from configuration
        var dataConnectionStrings = _configuration.GetSection("Koan:Data:ConnectionStrings")
            .GetChildren()
            .ToDictionary(x => x.Key, x => x.Value);

        foreach (var (name, connectionString) in dataConnectionStrings)
        {
            if (string.IsNullOrEmpty(connectionString)) continue;

            var serviceSpec = InferServiceFromConnectionString(name, connectionString, profile);
            if (serviceSpec != null)
                services.Add(serviceSpec);
        }

        // 2. Detect vector stores
        var vectorProviders = _configuration.GetSection("Koan:AI:Vector")
            .GetChildren();
        foreach (var provider in vectorProviders)
        {
            var serviceSpec = InferVectorService(provider, profile);
            if (serviceSpec != null)
                services.Add(serviceSpec);
        }

        // 3. Detect messaging brokers
        var messagingConnectionString = _configuration["Koan:Messaging:ConnectionString"];
        if (!string.IsNullOrEmpty(messagingConnectionString))
        {
            var serviceSpec = InferMessagingService(messagingConnectionString, profile);
            if (serviceSpec != null)
                services.Add(serviceSpec);
        }

        // 4. Add application service
        services.Add(new ServiceSpec
        {
            Id = "api",
            Image = "app:latest",  // Will be overridden by build context in ComposeExporter
            Ports = [(5000, 8080)],
            DependsOn = services.Select(s => s.Id).ToList(),
            Env = BuildAppEnvironmentVariables(services)
        });

        return new Plan { Services = services };
    }

    private ServiceSpec? InferServiceFromConnectionString(string name, string connectionString, Profile profile)
    {
        // Parse connection string to determine provider type
        if (connectionString.Contains("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return new ServiceSpec
            {
                Id = "postgres",
                Image = "postgres:16-alpine",
                Env = new Dictionary<string, string>
                {
                    ["POSTGRES_PASSWORD"] = profile == Profile.Prod ? "${POSTGRES_PASSWORD}" : "dev",
                    ["POSTGRES_USER"] = "koan",
                    ["POSTGRES_DB"] = "koandb"
                },
                Ports = [(5432, 5432)],
                Health = new HealthSpec
                {
                    HttpEndpoint = "http://localhost:5432",  // TCP probe
                    Interval = TimeSpan.FromSeconds(10),
                    Timeout = TimeSpan.FromSeconds(5),
                    Retries = 3
                }
            };
        }

        if (connectionString.Contains("mongodb", StringComparison.OrdinalIgnoreCase))
        {
            return new ServiceSpec
            {
                Id = "mongodb",
                Image = "mongo:7",
                Env = new Dictionary<string, string>
                {
                    ["MONGO_INITDB_ROOT_USERNAME"] = "koan",
                    ["MONGO_INITDB_ROOT_PASSWORD"] = profile == Profile.Prod ? "${MONGO_PASSWORD}" : "dev"
                },
                Ports = [(27017, 27017)]
            };
        }

        // Add similar logic for Redis, SQL Server, etc.

        return null;
    }

    private ServiceSpec? InferVectorService(IConfigurationSection provider, Profile profile)
    {
        var providerName = provider.Key.ToLowerInvariant();

        if (providerName.Contains("weaviate"))
        {
            return new ServiceSpec
            {
                Id = "weaviate",
                Image = "semitechnologies/weaviate:1.24.1",
                Env = new Dictionary<string, string>
                {
                    ["AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED"] = "true",
                    ["PERSISTENCE_DATA_PATH"] = "/var/lib/weaviate",
                    ["QUERY_DEFAULTS_LIMIT"] = "25",
                    ["DEFAULT_VECTORIZER_MODULE"] = "none"
                },
                Ports = [(8090, 8080)]
            };
        }

        // Add Qdrant, Milvus, Pinecone (external), etc.

        return null;
    }

    private ServiceSpec? InferMessagingService(string connectionString, Profile profile)
    {
        if (connectionString.Contains("redis", StringComparison.OrdinalIgnoreCase))
        {
            return new ServiceSpec
            {
                Id = "redis",
                Image = "redis:7-alpine",
                Ports = [(6379, 6379)]
            };
        }

        return null;
    }

    private Dictionary<string, string> BuildAppEnvironmentVariables(List<ServiceSpec> dependencies)
    {
        var env = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_HTTP_PORTS"] = "8080"
        };

        // Add connection strings based on detected services
        if (dependencies.Any(s => s.Id == "postgres"))
        {
            env["Koan__Data__ConnectionStrings__default"] =
                "Host=postgres;Port=5432;Database=koandb;Username=koan;Password=dev";
        }

        if (dependencies.Any(s => s.Id == "mongodb"))
        {
            env["Koan__Data__ConnectionStrings__mongo"] =
                "mongodb://koan:dev@mongodb:27017/koandb?authSource=admin";
        }

        if (dependencies.Any(s => s.Id == "weaviate"))
        {
            env["Koan__AI__Vector__Weaviate__Endpoint"] = "http://weaviate:8080";
        }

        if (dependencies.Any(s => s.Id == "redis"))
        {
            env["Koan__Messaging__ConnectionString"] = "redis:6379";
            env["Koan__Cache__ConnectionString"] = "redis:6379";
        }

        return env;
    }
}
```

**Controller Integration**:
```csharp
[HttpGet("/.koan/admin/launchkit/{format}")]
[AdminAuthorization]
public async Task<IActionResult> DownloadLaunchKit(
    string format,
    [FromQuery] string profile = "Local",
    [FromServices] LaunchKitGenerator generator,
    CancellationToken ct)
{
    if (!Enum.TryParse<Profile>(profile, ignoreCase: true, out var profileEnum))
        return BadRequest($"Invalid profile: {profile}. Valid: {string.Join(", ", Enum.GetNames<Profile>())}");

    try
    {
        var content = await generator.GenerateAsync(format, profileEnum, ct);
        var filename = $"koan-{format}-{profile.ToLowerInvariant()}.{GetExtension(format)}";

        return File(content, "application/octet-stream", filename);
    }
    catch (NotSupportedException ex)
    {
        return BadRequest(ex.Message);
    }
}

private static string GetExtension(string format) => format.ToLowerInvariant() switch
{
    "compose" => "yml",
    "aspire" => "json",
    _ => "txt"
};
```

**Impact**: Enables one-click download of infrastructure bundles reflecting actual runtime configuration.

---

### 5. Missing Capabilities Surveyor

**Current State**: Capability detection exists per-adapter but no unified enumeration

**Evidence**:
- `Data<T,K>.QueryCaps` available at `src/Koan.Data.Core/Data.cs`
- Individual capability tests at `tests/Suites/Data/Connector.*/Specs/Capabilities/`
- No service to collect capabilities across all registered adapters

**Proposal Expectation**: "Providers Health panel visualizing adapter status, capability flags (QueryCaps, WriteCaps), schema guard results, and vector index checks"

**Recommendation**:

```csharp
// Koan.Admin.Core/Services/CapabilitiesSurveyor.cs
public sealed class CapabilitiesSurveyor
{
    private readonly IServiceProvider _services;
    private readonly ILogger<CapabilitiesSurveyor> _logger;

    public async Task<CapabilitiesSnapshot> SurveyAsync(CancellationToken ct = default)
    {
        var snapshot = new CapabilitiesSnapshot
        {
            SurveyedAt = DateTime.UtcNow
        };

        // 1. Enumerate data adapters
        snapshot.DataAdapters = await SurveyDataAdaptersAsync(ct);

        // 2. Enumerate vector stores
        snapshot.VectorStores = await SurveyVectorStoresAsync(ct);

        // 3. Enumerate messaging brokers
        snapshot.MessageBrokers = await SurveyMessageBrokersAsync(ct);

        // 4. Enumerate cache providers
        snapshot.CacheProviders = await SurveyCacheProvidersAsync(ct);

        return snapshot;
    }

    private async Task<List<DataAdapterInfo>> SurveyDataAdaptersAsync(CancellationToken ct)
    {
        var adapters = new List<DataAdapterInfo>();

        // Use reflection to find all IKoanRepository<,> registrations
        var repositoryType = typeof(IKoanRepository<,>);
        var registrations = _services.GetType()
            .GetMethod("GetService")
            ?.Invoke(_services, new object[] { repositoryType });

        // For each registration, query capabilities
        // This is complex due to generic type parameters - simplified here

        // Alternative: Use marker interfaces or explicit registration tracking

        return adapters;
    }

    private async Task<List<VectorStoreInfo>> SurveyVectorStoresAsync(CancellationToken ct)
    {
        var stores = new List<VectorStoreInfo>();

        // Attempt to resolve known vector store types
        var weaviateRepo = _services.GetService<IVectorSearchRepository<object, string>>();
        if (weaviateRepo != null)
        {
            stores.Add(new VectorStoreInfo
            {
                Provider = "Weaviate",
                Status = await CheckHealthAsync(weaviateRepo, ct),
                Capabilities = new VectorCapabilitiesInfo
                {
                    SupportsFilters = true,
                    SupportsHybridSearch = true,
                    SupportsExport = true  // DATA-0078 capability
                }
            });
        }

        return stores;
    }

    private async Task<List<MessageBrokerInfo>> SurveyMessageBrokersAsync(CancellationToken ct)
    {
        // Check for Redis Inbox, RabbitMQ, etc.
        return new List<MessageBrokerInfo>();
    }

    private async Task<List<CacheProviderInfo>> SurveyCacheProvidersAsync(CancellationToken ct)
    {
        // Check for Redis Cache, Memory Cache, etc.
        return new List<CacheProviderInfo>();
    }

    private async Task<HealthStatus> CheckHealthAsync(object adapter, CancellationToken ct)
    {
        // Attempt to call a health check method if available
        // Or perform a simple connectivity test
        return HealthStatus.Healthy;
    }
}

public sealed record CapabilitiesSnapshot
{
    public DateTime SurveyedAt { get; init; }
    public List<DataAdapterInfo> DataAdapters { get; set; } = new();
    public List<VectorStoreInfo> VectorStores { get; set; } = new();
    public List<MessageBrokerInfo> MessageBrokers { get; set; } = new();
    public List<CacheProviderInfo> CacheProviders { get; set; } = new();
}

public sealed record DataAdapterInfo
{
    public string Provider { get; init; } = "";
    public string SetName { get; init; } = "";
    public HealthStatus Status { get; init; }
    public QueryCapabilities QueryCaps { get; init; }
    public WriteCapabilities WriteCaps { get; init; }
    public string? ConnectionString { get; init; }  // Redacted
}

public sealed record VectorStoreInfo
{
    public string Provider { get; init; } = "";
    public HealthStatus Status { get; init; }
    public VectorCapabilitiesInfo Capabilities { get; init; } = new();
    public int IndexCount { get; init; }
}

public sealed record VectorCapabilitiesInfo
{
    public bool SupportsFilters { get; init; }
    public bool SupportsHybridSearch { get; init; }
    public bool SupportsExport { get; init; }
}

public sealed record MessageBrokerInfo
{
    public string Provider { get; init; } = "";
    public HealthStatus Status { get; init; }
    public int QueueCount { get; init; }
}

public sealed record CacheProviderInfo
{
    public string Provider { get; init; } = "";
    public HealthStatus Status { get; init; }
}

public enum HealthStatus
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy
}
```

**Controller Integration**:
```csharp
[HttpGet("/.koan/admin/capabilities")]
[AdminAuthorization]
public async Task<IActionResult> GetCapabilities(
    [FromServices] CapabilitiesSurveyor surveyor,
    CancellationToken ct)
{
    var snapshot = await surveyor.SurveyAsync(ct);
    return Ok(snapshot);
}
```

**Challenge**: Generic repository enumeration is complex. Consider:
1. Explicit registration tracking (maintain list of adapters during auto-registration)
2. Marker interfaces for discoverable services
3. Convention-based discovery (scan for `*Repository` services)

---

### 6. Absent Manifest Publisher

**Proposal Specification**:
```json
{
  "version": "1.0",
  "openapi": "/swagger/v1/swagger.json",
  "health": "/health",
  "admin": "/.koan/admin",
  "modules": {
    "web": true,
    "data": true,
    "ai": false,
    "messaging": true
  }
}
```

**Expected Endpoints**:
- `/.koan/manifest.json` (primary)
- `/.well-known/koan` (alias)

**Current State**: No implementation exists

**Architectural Concern**: Discovery manifest is cross-cutting infrastructure benefiting **all Koan services**, not just admin. Should be framework-level capability.

**Recommendation**:

```csharp
// Koan.Web/Services/ManifestPublisher.cs
public sealed class ManifestPublisher
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly IOptions<KoanAdminOptions> _adminOptions;

    public KoanManifest Generate()
    {
        var manifest = new KoanManifest
        {
            Version = "1.0",
            GeneratedAt = DateTime.UtcNow
        };

        // Detect OpenAPI endpoint
        var hasSwagger = _services.GetService<ISwaggerProvider>() != null;
        if (hasSwagger)
            manifest.OpenApi = "/swagger/v1/swagger.json";

        // Health endpoint
        manifest.Health = "/health";

        // Admin endpoint (if enabled)
        if (_adminOptions.Value.Enabled)
        {
            var basePath = KoanWebConstants.Routes.Admin.GetBasePath(_adminOptions.Value.PathPrefix);
            manifest.Admin = basePath;
        }

        // Detect active modules
        var bootReport = _services.GetService<BootReport>();
        if (bootReport != null)
        {
            var modules = bootReport.GetModules();
            manifest.Modules = new ModuleFlags
            {
                Web = modules.Any(m => m.Name.Contains("Web")),
                Data = modules.Any(m => m.Name.Contains("Data")),
                Ai = modules.Any(m => m.Name.Contains("AI")),
                Messaging = modules.Any(m => m.Name.Contains("Messaging") || m.Name.Contains("Inbox")),
                Vector = modules.Any(m => m.Name.Contains("Vector")),
                Cache = modules.Any(m => m.Name.Contains("Cache"))
            };
        }

        return manifest;
    }
}

public sealed record KoanManifest
{
    public string Version { get; init; } = "1.0";
    public DateTime GeneratedAt { get; init; }
    public string? OpenApi { get; set; }
    public string? Health { get; set; }
    public string? Admin { get; set; }
    public ModuleFlags Modules { get; set; } = new();
}

public sealed record ModuleFlags
{
    public bool Web { get; init; }
    public bool Data { get; init; }
    public bool Ai { get; init; }
    public bool Messaging { get; init; }
    public bool Vector { get; init; }
    public bool Cache { get; init; }
}
```

**Auto-Registration**:
```csharp
// In Koan.Web registrar
services.AddSingleton<ManifestPublisher>();

// In Koan.Web startup (MapKoanEndpoints extension)
app.MapGet("/.koan/manifest.json", (ManifestPublisher publisher)
    => Results.Json(publisher.Generate()))
    .WithName("KoanManifest")
    .WithTags("Discovery");

// Alias endpoint
app.MapGet("/.well-known/koan", (ManifestPublisher publisher)
    => Results.Json(publisher.Generate()))
    .WithName("KoanWellKnown")
    .WithTags("Discovery");
```

**Security**: Manifest should be publicly accessible (non-sensitive discovery info). Admin surface URLs are expected to have their own authorization.

---

### 7. Missing Authorization Filter

**Proposal Requirements**:
1. Default disabled in production
2. Require `AllowInProduction` flag for staging/production
3. Authorization policy enforcement
4. Network allowlist support
5. Destructive operations double-gating

**Current State**: No authorization filter exists

**Security Impact**: HIGH - Admin surface without auth would expose sensitive runtime info

**Recommendation**:

```csharp
// Koan.Web.Admin/Filters/AdminAuthorizationAttribute.cs
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminAuthorizationAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var options = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<KoanAdminOptions>>().Value;
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILogger<AdminAuthorizationAttribute>>();

        // Gate 1: Admin must be enabled
        if (!options.Enabled)
        {
            logger.LogWarning("Admin surface access denied: not enabled");
            context.Result = new NotFoundResult();
            return;
        }

        // Gate 2: Production requires explicit opt-in
        if (KoanEnv.IsProduction && !options.AllowInProduction)
        {
            logger.LogWarning("Admin surface access denied: production opt-in required");
            context.Result = new StatusCodeResult(403);
            return;
        }

        // Gate 3: Network allowlist (if configured)
        if (options.Authorization.AllowedNetworks?.Any() == true)
        {
            var remoteIp = context.HttpContext.Connection.RemoteIpAddress;
            if (remoteIp == null || !IsIpAllowed(remoteIp, options.Authorization.AllowedNetworks))
            {
                logger.LogWarning("Admin surface access denied: IP {Ip} not in allowlist", remoteIp);
                context.Result = new StatusCodeResult(403);
                return;
            }
        }

        // Gate 4: Authorization policy (if configured)
        if (!string.IsNullOrEmpty(options.Authorization.Policy))
        {
            var authService = context.HttpContext.RequestServices
                .GetRequiredService<IAuthorizationService>();

            var result = await authService.AuthorizeAsync(
                context.HttpContext.User,
                options.Authorization.Policy);

            if (!result.Succeeded)
            {
                logger.LogWarning("Admin surface access denied: policy {Policy} failed",
                    options.Authorization.Policy);
                context.Result = new ForbidResult();
                return;
            }
        }

        // All gates passed
        logger.LogInformation("Admin surface access granted to {User} from {Ip}",
            context.HttpContext.User.Identity?.Name ?? "anonymous",
            context.HttpContext.Connection.RemoteIpAddress);
    }

    private static bool IsIpAllowed(IPAddress ip, string[] allowedNetworks)
    {
        foreach (var network in allowedNetworks)
        {
            if (network.Contains('/'))
            {
                // CIDR notation: 10.20.0.0/16
                if (IpInCidrRange(ip, network))
                    return true;
            }
            else if (IPAddress.TryParse(network, out var allowedIp))
            {
                // Single IP
                if (ip.Equals(allowedIp))
                    return true;
            }
        }
        return false;
    }

    private static bool IpInCidrRange(IPAddress ip, string cidr)
    {
        // Implement CIDR range checking
        // Libraries like IPNetwork2 can help, or implement manually
        return true; // Placeholder
    }
}
```

**Destructive Operations Gate**:
```csharp
[AttributeUsage(AttributeTargets.Method)]
public sealed class DestructiveOperationAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var options = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<KoanAdminOptions>>().Value;
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILogger<DestructiveOperationAttribute>>();

        if (!options.DestructiveOps)
        {
            logger.LogWarning("Destructive operation denied: not enabled");
            context.Result = new ObjectResult(new { error = "Destructive operations not enabled" })
            {
                StatusCode = 403
            };
            return;
        }

        // Require re-confirmation via header
        if (!context.HttpContext.Request.Headers.TryGetValue("X-Confirm-Destructive", out var confirmation)
            || confirmation != "yes")
        {
            logger.LogWarning("Destructive operation denied: missing confirmation header");
            context.Result = new ObjectResult(new
            {
                error = "Destructive operation requires X-Confirm-Destructive: yes header"
            })
            {
                StatusCode = 400
            };
            return;
        }

        logger.LogWarning("Destructive operation authorized by {User}",
            context.HttpContext.User.Identity?.Name ?? "anonymous");

        await next();
    }
}
```

**Usage**:
```csharp
[HttpPost("/.koan/admin/fixtures/purge")]
[AdminAuthorization]
[DestructiveOperation]
public async Task<IActionResult> PurgeFixtures([FromServices] IFixtureService fixtures)
{
    await fixtures.PurgeAllAsync();
    return Ok(new { message = "Fixtures purged" });
}
```

---

### 8. Test Suite Contradictions

**Orphaned Tests** at `tests/Suites/Web/Koan.Web.Admin.Tests/Specs/`:

| Test File | Expected Endpoint | Proposal Endpoint | Status |
|-----------|-------------------|-------------------|--------|
| AdminControllerSpec.cs:17 | `/admin/entities` | `/.koan/admin/entities` | ❌ Mismatch |
| AdminControllerSpec.cs:27 | `/admin/models` | `/.koan/admin/models` | ❌ Mismatch |
| BackupEndpointSpec.cs:17 | `/admin/backup` | `/.koan/admin/backup` | ❌ Mismatch |
| AuthAndRolesSpec.cs:17 | `/admin/auth/roles` | `/.koan/admin/auth/roles` | ❌ Mismatch |

**Test Fixture** at `tests/Suites/Web/Koan.Web.Admin.Tests/Support/WebAdminTestPipelineFixture.cs:7`:
```csharp
public WebAdminTestPipelineFixture() : base("web-admin")
{
    // Additional setup for web admin HTTP pipeline
}
```

**Architectural Concern**: Tests exist without implementation, encoding assumptions that contradict proposal.

**Impact**:
- Tests will fail when implementation follows proposal
- Misleading signal that implementation exists
- Conflicting route expectations

**Recommendation**:

**Option 1: Delete Orphaned Tests** (preferred for clean slate)
```bash
rm -rf tests/Suites/Web/Koan.Web.Admin.Tests/
```

**Option 2: Update Tests to Match Proposal**
```csharp
// Update all test endpoints
await _fixture.HttpGetAsync("/.koan/admin/entities");
await _fixture.HttpGetAsync("/.koan/admin/models");
await _fixture.HttpGetAsync("/.koan/admin/backup");
await _fixture.HttpGetAsync("/.koan/admin/auth/roles");

// Add authorization headers if needed
var response = await _fixture.HttpGetAsync("/.koan/admin/entities",
    headers: new Dictionary<string, string>
    {
        ["Authorization"] = "Bearer test-token"
    });
```

**Option 3: Preserve Tests as Integration Targets**
- Keep tests as specification of required endpoints
- Implement controllers to satisfy tests
- Accept route mismatch as intentional deviation from proposal

**Recommendation**: Choose Option 1 (delete) to avoid confusion. Write new tests alongside implementation.

---

### 9. Sample Application Integration Gap

**Proposal Statement**: "Samples like S7.ContentPlatform or S13.DocMind do not reference admin modules"

**Verified**: No sample references `Koan.Web.Admin` or `Koan.Console.Admin`

**Impact**:
- No reference implementation for developers
- No onboarding path
- No validation that admin surface works in real applications

**Recommendation**: Update **S13.DocMind** as reference implementation

**Step 1: Add Project Reference**
```xml
<!-- samples/S13.DocMind/API/S13.DocMind.API.csproj -->
<ItemGroup>
  <ProjectReference Include="../../../src/Koan.Web.Admin/Koan.Web.Admin.csproj" />
</ItemGroup>
```

**Step 2: Configuration**
```jsonc
// samples/S13.DocMind/API/appsettings.Development.json
{
  "Koan": {
    "Admin": {
      "Enabled": true,
      "PathPrefix": ".",
      "ExposeManifest": true,
      "Generate": {
        "ComposeProfiles": ["Local", "CI"],
        "OpenApiClients": ["csharp", "typescript"]
      },
      "Logging": {
        "IncludeCategories": ["Koan.*", "DocMind.*"],
        "RedactKeys": ["password", "secret", "key"]
      }
    }
  }
}
```

**Step 3: Documentation**
```markdown
<!-- samples/S13.DocMind/README.md -->

## Admin Surface

The DocMind API includes the Koan Admin Surface for development convenience.

### Access Admin Dashboard

Navigate to: `https://localhost:5001/.koan/admin`

### Download LaunchKit Bundles

- **Docker Compose (Local)**: `https://localhost:5001/.koan/admin/launchkit/compose?profile=Local`
- **Docker Compose (CI)**: `https://localhost:5001/.koan/admin/launchkit/compose?profile=CI`

### View Runtime Capabilities

- **Boot Report**: `https://localhost:5001/.koan/admin/boot-report`
- **Capabilities**: `https://localhost:5001/.koan/admin/capabilities`
- **Discovery Manifest**: `https://localhost:5001/.koan/manifest.json`

### Security

The admin surface is **only enabled in Development** by default. To enable in other environments:

```json
{
  "Koan": {
    "Admin": {
      "Enabled": true,
      "AllowInProduction": true,  // Required for production
      "Authorization": {
        "Policy": "RequireAdminRole"
      }
    }
  }
}
```
```

**Expected Workflow**:
1. Developer clones repo
2. Navigates to `samples/S13.DocMind/API`
3. Runs `dotnet run`
4. Opens `https://localhost:5001/.koan/admin`
5. Downloads Compose file
6. Runs `docker compose up` to spin up dependencies
7. Service connects to infrastructure successfully

**Success Metric**: Developer goes from clone → running stack in under 5 minutes.

---

### 10. Console Admin Non-Existence

**Proposal Scope**: Console takeover UI with ANSI rendering

**Current State**: Does not exist in any form

**Complexity Factors**:
1. **ANSI Rendering**: Cross-platform terminal compatibility
2. **Stdin Control**: Conflicts with Koan CLI hosting
3. **Graceful Degradation**: Fallback for non-ANSI terminals
4. **Log Streaming**: Integration with redaction pipeline
5. **UI Framework**: ANSI-safe TUI library selection

**Architectural Concern**: Console admin is **significantly more complex** than web admin for **less value**:
- Web admin accessible from any device (phone, tablet, remote machine)
- Console admin requires terminal access to server
- Web admin supports richer UI (charts, tables, downloads)
- Console admin limited by terminal constraints

**Market Validation**: Most admin tools prioritize web over console (Kubernetes Dashboard, Grafana, Portainer)

**Recommendation**: **Defer console implementation to Phase 6** (optional)

**Priority Justification**:
- Web admin delivers 80% of value with 40% of complexity
- Sample integration proves concept with web admin alone
- Console admin can follow if user demand materializes

**Alternative**: If console experience is critical, consider:
1. **Simple CLI commands** instead of takeover UI: `koan admin boot-report`, `koan admin capabilities`
2. **Terminal browser** pointing to web admin: Auto-open `https://localhost:5001/.koan/admin` in terminal browser
3. **JSON output** for scripting: `koan admin boot-report --json | jq .`

---

## Prioritized Implementation Roadmap

### Phase 1: Foundation (Week 1)

**Goal**: Unblock development with configuration model and security gates

**Deliverables**:
1. ✅ Create `KoanAdminOptions` class with validation
2. ✅ Add admin route constants to `KoanWebConstants.Routes.Admin`
3. ✅ Implement `AdminAuthorizationAttribute` filter
4. ✅ Implement `DestructiveOperationAttribute` filter
5. ✅ Delete orphaned test specs (or update to match proposal routes)
6. ✅ Remove empty `Koan.Web.Admin` directories (start fresh)

**Acceptance Criteria**:
- Options model binds from `Koan:Admin` configuration section
- Validation throws on invalid `PathPrefix` values
- Authorization filter blocks access when `Enabled = false`
- Authorization filter blocks production access without `AllowInProduction`
- Constants provide consistent route references

**Estimated Effort**: 2-3 days

---

### Phase 2: Core Services (Week 2)

**Goal**: Enable functionality with reusable services

**Deliverables**:
1. ✅ Add `BootReport.ToDto()` JSON serialization
2. ✅ Register `BootReport` as singleton in DI
3. ✅ Implement `CapabilitiesSurveyor` service
4. ✅ Implement `LaunchKitGenerator` service
5. ✅ Implement `ManifestPublisher` service
6. ✅ Add unit tests for each service

**Acceptance Criteria**:
- `BootReport` can be injected and serialized to JSON
- `CapabilitiesSurveyor` enumerates registered adapters
- `LaunchKitGenerator` builds Plan from runtime configuration
- `LaunchKitGenerator` invokes appropriate exporter (Compose)
- `ManifestPublisher` generates valid discovery manifest

**Estimated Effort**: 4-5 days

---

### Phase 3: Web Admin MVP (Week 3)

**Goal**: Prove value with working admin surface

**Deliverables**:
1. ✅ Create `Koan.Web.Admin.csproj` with proper dependencies
2. ✅ Implement admin controllers with endpoints:
   - `GET /.koan/admin/boot-report`
   - `GET /.koan/admin/capabilities`
   - `GET /.koan/admin/launchkit/{format}?profile={profile}`
   - `GET /.koan/manifest.json`
3. ✅ Implement `IKoanAutoRegistrar` for auto-wiring
4. ✅ Add minimal HTML landing page (or JSON-only API)
5. ✅ Apply `[AdminAuthorization]` to all endpoints
6. ✅ Add integration tests

**Acceptance Criteria**:
- Admin endpoints respond with correct JSON
- Authorization gates enforced on all endpoints
- LaunchKit downloads return valid Compose files
- Manifest reflects runtime module state
- Startup logs show admin module registration

**Estimated Effort**: 5-6 days

---

### Phase 4: Sample Integration & Documentation (Week 4)

**Goal**: Enable developer onboarding with reference implementation

**Deliverables**:
1. ✅ Update S13.DocMind to reference `Koan.Web.Admin`
2. ✅ Add admin configuration to DocMind appsettings
3. ✅ Document admin surface usage in DocMind README
4. ✅ Write admin surface reference guide
5. ✅ Write security checklist
6. ✅ Write LaunchKit workflow guide
7. ✅ Create ADR `WEB-Admin-0001` for routing/prefix strategy

**Acceptance Criteria**:
- Developer can clone repo and access admin dashboard in under 5 minutes
- Documentation covers all endpoints and configuration options
- Security checklist addresses production deployment concerns
- ADR formalizes prefix strategy and route namespace decisions

**Estimated Effort**: 3-4 days

---

### Phase 5: Polish & Production-Ready (Week 5-6)

**Goal**: Feature-complete admin surface

**Deliverables**:
1. ✅ Web UI assets (React/Vue SPA or Razor Pages)
2. ✅ Additional endpoints (entities, models, auth/roles, backup)
3. ✅ Destructive operations with double-gating
4. ✅ OTEL instrumentation for admin actions
5. ✅ Integration tests for prefix overrides, policy enforcement
6. ✅ Load testing for LaunchKit generation under concurrency

**Acceptance Criteria**:
- UI provides rich visualization of capabilities and boot report
- LaunchKit download handles concurrent requests
- Destructive operations require explicit confirmation
- OTEL spans track all admin actions
- Tests cover all edge cases from proposal

**Estimated Effort**: 8-10 days

---

### Phase 6: Console Admin (Optional - Future)

**Goal**: Console parity with web admin

**Deliverables**:
1. ✅ Console takeover UI with ANSI rendering
2. ✅ Koan CLI integration (detect `--admin-console` flag)
3. ✅ Cross-platform testing (Windows/Linux/macOS)
4. ✅ Log streaming with redaction
5. ✅ Parity panels (overview, providers, launchkit, logs)

**Acceptance Criteria**:
- Console UI renders correctly on all platforms
- Koan CLI launches console admin when flag provided
- Log streaming respects redaction configuration
- Graceful degradation for non-ANSI terminals

**Estimated Effort**: 10-12 days

**Decision Point**: Evaluate after Phase 4. If sample integration proves value and user demand exists, proceed. Otherwise, defer indefinitely.

---

## Strategic Concerns

### Concern 1: Scope Creep Risk

**Analysis**: Proposal conflates three distinct capabilities:
1. Runtime introspection (BootReport, capabilities)
2. Configuration generation (LaunchKit/Compose/Aspire)
3. Admin UI (dashboards, diagnostics)

**Risk**: Attempting all three simultaneously leads to incomplete implementation (as evidenced by current state).

**Mitigation Strategy**:
- **Ship incrementally**: Phases 1-2 deliver introspection without UI
- **API-first approach**: Phase 3 delivers JSON endpoints before HTML UI
- **Prove value early**: Phase 4 validates concept before investing in polish

**Success Metric**: Each phase delivers working functionality, not just scaffolding.

---

### Concern 2: Maintenance Burden

**Analysis**: Admin surfaces introduce ongoing maintenance:
- UI framework updates (React/Vue version bumps)
- Security audits for authorization logic
- Compatibility with new adapter types (vector stores, caches)
- Prefix strategy edge cases (proxy configurations)
- Documentation drift as framework evolves

**Estimated Annual Maintenance**: 40-60 hours

**Mitigation Strategy**:
- Keep UI minimal (JSON API + basic HTML)
- Use framework-agnostic patterns (vanilla JS over SPA frameworks)
- Leverage existing Koan patterns (EntityController-style APIs)
- Automated tests prevent regression
- ADR documents design rationale for future maintainers

**Cost-Benefit Analysis**:
- **Cost**: Initial 4-6 weeks + 40-60 hours/year maintenance
- **Benefit**: Reduced onboarding time (hours → minutes), consistent infra generation, runtime visibility

**Verdict**: Maintenance burden justified if developer experience improvement materializes.

---

### Concern 3: LaunchKit Value Proposition

**Question**: Does "one-click Compose generation" justify the complexity?

**Arguments Against**:
- Developers typically customize generated configs (secrets, resource limits)
- Generated configs may drift from production reality
- Teams already have CI/CD pipelines generating configs
- Infrastructure-as-Code tools (Terraform, Pulumi) are more production-appropriate

**Arguments For**:
- Valuable for **initial setup** and **local development**
- Reduces onboarding friction for new team members
- Ensures consistent provider configuration across environments
- Eliminates "works on my machine" issues from mismatched connection strings
- Validates that runtime config matches infrastructure expectations

**Competitor Analysis**:
- **Aspire Dashboard**: Generates Compose and Kubernetes manifests
- **Docker Desktop**: Generates Compose from running containers
- **Portainer**: Generates stack files from templates

**Recommendation**: Position LaunchKit as **starter templates** not **production automation**

**Documentation Guidance**:
```markdown
## LaunchKit Usage

LaunchKit generates infrastructure bundles for **local development and testing**.

### Local Development
1. Download Compose file: `/.koan/admin/launchkit/compose?profile=Local`
2. Run `docker compose up`
3. Service auto-connects to infrastructure

### CI/CD Pipelines
1. Download Compose file: `/.koan/admin/launchkit/compose?profile=CI`
2. Use as baseline for pipeline configuration
3. **Customize** secrets, resource limits, and scaling settings

### Production
**Do not use generated configs directly in production.** Instead:
1. Use generated config as reference
2. Migrate to infrastructure-as-code tools (Terraform, Pulumi)
3. Implement proper secret management (Vault, Key Vault)
4. Configure observability and monitoring
```

**Expected Customizations**:
- Replace `password: "dev"` with secret references
- Add resource limits (CPU, memory)
- Configure backup strategies
- Add monitoring/logging sidecars
- Adjust network policies

**Success Metric**: Developer can go from generated config → working local stack in under 5 minutes.

---

### Concern 4: Proxy Compatibility

**Proposal Risk**: Dot-prefixed routes (`/.koan/admin`) may be blocked by reverse proxies

**Common Proxy Behaviors**:
- **Nginx**: Blocks `location ~ /\\.` by default (dotfile protection)
- **Apache**: May require `AllowOverride` directives
- **Cloud Load Balancers**: Often strip dot-prefixed paths

**Mitigation Strategies**:

**1. Configurable Prefix** (implemented in proposal)
```json
{
  "Koan": {
    "Admin": {
      "PathPrefix": "_"  // Uses /_koan/admin instead
    }
  }
}
```

**2. Documentation with Proxy Configs**
```nginx
# Nginx: Allow Koan admin surface
location ~ ^/\.koan {
    proxy_pass http://localhost:5000;
    # ... other settings
}
```

```apache
# Apache: Allow Koan admin surface
<Location ~ "^/\.koan">
    Require all granted
</Location>
```

**3. Startup Warning**
```csharp
if (options.PathPrefix == "." && !KoanEnv.IsDevelopment)
{
    _logger.LogWarning(
        "Admin surface using dot-prefix (/.koan) outside Development. " +
        "Reverse proxies may block this path. Consider PathPrefix=\"_\" or \"-\".");
}
```

**4. Well-Known Alternative**
```csharp
// Always expose alternative route
app.MapGet("/.well-known/koan/admin", () => Results.Redirect("/.koan/admin"));
```

**Testing Strategy**: Integration tests should verify all prefix options work correctly.

---

### Concern 5: Security Implications

**Attack Surface Analysis**:

**1. Information Disclosure**
- **Risk**: Admin endpoints leak internal architecture
- **Mitigation**: Authorization filter required on all endpoints
- **Severity**: HIGH

**2. Denial of Service**
- **Risk**: LaunchKit generation CPU-intensive
- **Mitigation**: Rate limiting on download endpoints
- **Severity**: MEDIUM

**3. Configuration Injection**
- **Risk**: Malicious config could corrupt generated artifacts
- **Mitigation**: Validate all configuration inputs before Plan construction
- **Severity**: MEDIUM

**4. Destructive Operations**
- **Risk**: Accidental data loss from purge operations
- **Mitigation**: Double-gating with explicit confirmation header
- **Severity**: HIGH

**5. CSRF**
- **Risk**: Cross-site requests to destructive operations
- **Mitigation**: Require anti-CSRF token for POST/DELETE operations
- **Severity**: MEDIUM

**Security Checklist**:
- [ ] All admin endpoints require `[AdminAuthorization]`
- [ ] Production access requires `AllowInProduction = true`
- [ ] Authorization policy enforced (if configured)
- [ ] Network allowlist enforced (if configured)
- [ ] Rate limiting on LaunchKit downloads
- [ ] Anti-CSRF tokens on destructive operations
- [ ] Input validation on all configuration reads
- [ ] Connection strings redacted in all responses
- [ ] OTEL instrumentation for audit trail
- [ ] Startup warnings for insecure configurations

**Penetration Testing**: Phase 5 should include security review.

---

## Conclusion

The Koan Admin Surface proposal describes a **sound architectural vision** with approximately **60% of infrastructure already present** in the codebase. The primary gaps are:

1. **Configuration model** (`KoanAdminOptions`)
2. **Unified services** (`LaunchKitGenerator`, `CapabilitiesSurveyor`, `ManifestPublisher`)
3. **HTTP endpoints** exposing existing capabilities
4. **Authorization filters** enforcing security gates
5. **Sample integration** demonstrating usage

**Recommended Path**: Incremental 6-phase implementation, prioritizing API-only endpoints (Phases 1-3) before investing in UI polish (Phase 5) or console admin (Phase 6).

**Critical Success Factor**: Phase 4 sample integration proves developer experience improvement. If developers can go from clone → running stack in under 5 minutes, the admin surface delivers on its promise.

**Avoid**:
- Building UI before API is proven
- Attempting console + web admin simultaneously
- Scope creep into "full database admin tool" territory
- Production deployment without thorough security review

**Next Steps**:
1. Review this analysis with architecture team
2. Approve phased roadmap and priorities
3. Create ADR `WEB-Admin-0001` formalizing routing/prefix strategy
4. Begin Phase 1 implementation
5. Establish success metrics for each phase gate

---

**Document Version**: 1.0
**Last Updated**: 2025-10-12
**Next Review**: After Phase 3 completion
