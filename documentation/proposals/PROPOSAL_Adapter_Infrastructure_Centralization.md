# PROPOSAL: Adapter Infrastructure Centralization

**Status**: Draft
**Created**: 2025-01-25
**Authors**: Architecture Team

## Problem Statement

During recent build fixes across multiple data adapters (MongoDB, Couchbase, SQL Server, PostgreSQL), significant infrastructure duplication was identified that directly undermines Koan Framework's "Reference = Intent" principle. Each adapter currently reimplements identical patterns for:

- Configuration type conversion (enum/TimeSpan parsing)
- Readiness infrastructure integration
- Query options transformation (Page/PageSize to Skip/Take)
- Boot report generation
- Service discovery integration

This duplication creates:
- **20+ duplication points** across 5 adapters × 4 patterns
- **Maintenance burden** requiring synchronized updates across all adapters
- **Consistency risk** as patterns drift between implementations
- **New provider friction** forcing reimplementation of framework infrastructure
- **Cognitive overhead** for adapter authors focusing on provider-specific logic

## Background

### Current Duplication Evidence

**Configuration Type Conversion** - Repeated in every adapter:
```csharp
var policyStr = Configuration.ReadFirst(config, options.Readiness.Policy.ToString(),
    "Koan:Data:Mongo:Readiness:Policy");
if (Enum.TryParse<ReadinessPolicy>(policyStr, out var policy)) {
    options.Readiness.Policy = policy;
}

var timeoutSecondsStr = Configuration.ReadFirst(config,
    ((int)options.Readiness.Timeout.TotalSeconds).ToString(),
    "Koan:Data:Mongo:Readiness:Timeout");
if (int.TryParse(timeoutSecondsStr, out var timeoutSeconds) && timeoutSeconds > 0) {
    options.Readiness.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
}
```

**Query Options Conversion** - Manual transformation per adapter:
```csharp
if (options is { Page: > 0, PageSize: > 0 }) {
    var skip = (options.Page.Value - 1) * options.PageSize.Value;
    cursor = cursor.Skip(skip);
    cursor = cursor.Limit((int)Math.Min(options.PageSize.Value, maxPageSize));
}
```

**Boot Report Generation** - Nearly identical across auto-registrars:
```csharp
var readinessOptions = Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions());
var configurator = new ProviderOptionsConfigurator(cfg, null, readinessOptions);
var options = new ProviderOptions();
configurator.Configure(options);
report.AddSetting("DefaultPageSize", options.DefaultPageSize.ToString());
```

### Framework Impact

This duplication directly contradicts core framework principles:
- **"Reference = Intent"**: Adding adapter references should provide complete infrastructure
- **Provider Transparency**: Inconsistent implementations break uniform entity behavior
- **Auto-registration**: Common patterns should be inherited, not reimplemented

## Proposed Solution

### 1. Configuration Infrastructure Base Class

**Location**: `src/Koan.Core.Adapters/Configuration/AdapterOptionsConfigurator.cs`

```csharp
public abstract class AdapterOptionsConfigurator<TOptions> : IConfigureOptions<TOptions>
    where TOptions : class, IAdapterOptions
{
    protected IConfiguration Configuration { get; }
    protected ILogger? Logger { get; }
    protected AdaptersReadinessOptions ReadinessDefaults { get; }
    protected abstract string ProviderName { get; }

    protected AdapterOptionsConfigurator(
        IConfiguration config,
        ILogger? logger,
        IOptions<AdaptersReadinessOptions> readiness)
    {
        Configuration = config;
        Logger = logger;
        ReadinessDefaults = readiness.Value;
    }

    public void Configure(TOptions options)
    {
        ConfigureProviderSpecific(options);
        ConfigureReadiness(options.Readiness);
        ConfigurePaging(options);
    }

    protected abstract void ConfigureProviderSpecific(TOptions options);

    protected void ConfigureReadiness(IAdapterReadinessConfiguration readiness)
    {
        var policyStr = Configuration.ReadFirst(readiness.Policy.ToString(),
            $"Koan:Data:{ProviderName}:Readiness:Policy",
            "Koan:Data:Readiness:Policy");

        if (Enum.TryParse<ReadinessPolicy>(policyStr, out var policy))
            readiness.Policy = policy;

        var timeoutSecondsStr = Configuration.ReadFirst(
            ((int)readiness.Timeout.TotalSeconds).ToString(),
            $"Koan:Data:{ProviderName}:Readiness:Timeout",
            "Koan:Data:Readiness:Timeout");

        if (int.TryParse(timeoutSecondsStr, out var timeoutSeconds) && timeoutSeconds > 0)
            readiness.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        else if (readiness.Timeout <= TimeSpan.Zero)
            readiness.Timeout = ReadinessDefaults.DefaultTimeout;

        readiness.EnableReadinessGating = Configuration.Read(
            $"Koan:Data:{ProviderName}:Readiness:EnableReadinessGating",
            readiness.EnableReadinessGating);
    }

    protected void ConfigurePaging(IAdapterOptions options)
    {
        options.DefaultPageSize = Configuration.ReadFirst(options.DefaultPageSize,
            $"Koan:Data:{ProviderName}:DefaultPageSize",
            "Koan:Data:DefaultPageSize");

        options.MaxPageSize = Configuration.ReadFirst(options.MaxPageSize,
            $"Koan:Data:{ProviderName}:MaxPageSize",
            "Koan:Data:MaxPageSize");
    }
}
```

### 2. Common Adapter Options Interface

**Location**: `src/Koan.Core.Adapters/Configuration/IAdapterOptions.cs`

```csharp
public interface IAdapterOptions
{
    IAdapterReadinessConfiguration Readiness { get; }
    int DefaultPageSize { get; set; }
    int MaxPageSize { get; set; }
}
```

### 3. Query Options Extensions

**Location**: `src/Koan.Data.Core/Extensions/QueryExtensions.cs`

```csharp
public static class QueryExtensions
{
    /// <summary>
    /// Converts Page/PageSize options to Skip/Take values for provider-specific queries
    /// </summary>
    public static (int Skip, int Take) ToSkipTake(
        this DataQueryOptions? options,
        int defaultPageSize = 20,
        int maxPageSize = 1000)
    {
        if (options?.Page is null or <= 0 || options.PageSize is null or <= 0)
            return (0, defaultPageSize);

        var safePageSize = Math.Min(options.PageSize.Value, maxPageSize);
        var skip = (options.Page.Value - 1) * safePageSize;
        return (skip, safePageSize);
    }

    /// <summary>
    /// Generic paging application for any provider query type
    /// </summary>
    public static TProviderQuery ApplyPaging<TProviderQuery>(
        this TProviderQuery query,
        DataQueryOptions? options,
        int defaultPageSize,
        int maxPageSize,
        Func<TProviderQuery, int, int, TProviderQuery> applySkipTake)
    {
        var (skip, take) = options.ToSkipTake(defaultPageSize, maxPageSize);
        return applySkipTake(query, skip, take);
    }

    /// <summary>
    /// Provider-agnostic paging guardrails
    /// </summary>
    public static (int DefaultPageSize, int MaxPageSize) GetPagingGuardrails(
        this IAdapterOptions options)
        => (options.DefaultPageSize, options.MaxPageSize);
}
```

### 4. Boot Report Utilities

**Location**: `src/Koan.Core.Adapters/Reporting/AdapterBootReporting.cs`

```csharp
public static class AdapterBootReporting
{
    public static void ReportAdapterConfiguration<TOptions>(
        this BootReport report,
        string moduleName,
        string? moduleVersion,
        TOptions options,
        IConfiguration config,
        Action<BootReport, TOptions>? reportProviderSpecific = null)
        where TOptions : IAdapterOptions
    {
        report.AddModule(moduleName, moduleVersion);

        // Standard adapter capabilities
        report.AddSetting($"{moduleName}:DefaultPageSize", options.DefaultPageSize.ToString());
        report.AddSetting($"{moduleName}:MaxPageSize", options.MaxPageSize.ToString());
        report.AddSetting($"{moduleName}:ReadinessPolicy", options.Readiness.Policy.ToString());
        report.AddSetting($"{moduleName}:ReadinessTimeout",
            options.Readiness.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture));
        report.AddSetting($"{moduleName}:ReadinessGating",
            options.Readiness.EnableReadinessGating.ToString());

        // Provider-specific settings via callback
        reportProviderSpecific?.Invoke(report, options);
    }

    public static TOptions ConfigureForBootReport<TOptions>(
        IConfiguration config,
        Func<IConfiguration, ILogger?, IOptions<AdaptersReadinessOptions>, TOptions> optionsFactory)
        where TOptions : IAdapterOptions
    {
        var readinessOptions = Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions());
        return optionsFactory(config, null, readinessOptions);
    }
}
```

### 5. Service Discovery Base Class

**Location**: `src/Koan.Core.Adapters/ServiceDiscovery/AdapterServiceDiscoveryBase.cs`

```csharp
public abstract class AdapterServiceDiscoveryBase
{
    protected IConfiguration Configuration { get; }
    protected ILogger? Logger { get; }

    protected AdapterServiceDiscoveryBase(IConfiguration config, ILogger? logger = null)
    {
        Configuration = config;
        Logger = logger;
    }

    protected async Task<string> ResolveConnectionStringAsync(
        string serviceName,
        string? explicitConnectionString,
        string? currentConnectionString,
        ServiceDiscoveryOptions discoveryOptions)
    {
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            Logger?.LogInformation("Using explicit connection string for {ServiceName}", serviceName);
            return NormalizeConnectionString(explicitConnectionString);
        }

        if (IsAutoMode(currentConnectionString))
        {
            Logger?.LogInformation("Auto-detection mode for {ServiceName} - using orchestration-aware service discovery", serviceName);
            return await DiscoverServiceConnectionAsync(serviceName, discoveryOptions);
        }

        Logger?.LogInformation("Using pre-configured connection string for {ServiceName}", serviceName);
        return NormalizeConnectionString(currentConnectionString ?? GetDefaultConnectionString());
    }

    protected abstract string NormalizeConnectionString(string connectionString);
    protected abstract string GetDefaultConnectionString();
    protected abstract Task<string> DiscoverServiceConnectionAsync(string serviceName, ServiceDiscoveryOptions options);

    private static bool IsAutoMode(string? connectionString) =>
        string.Equals(connectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(connectionString);

    protected bool IsAutoDetectionDisabled(string providerName) =>
        Configuration.Read($"Koan:Data:{providerName}:DisableAutoDetection", false) ||
        Configuration.Read($"Koan_DATA_{providerName.ToUpperInvariant()}_DISABLE_AUTO_DETECTION", false);
}
```

## Migration Strategy

### Phase 1: Infrastructure Foundation (Week 1)
1. Create base classes and interfaces in `Koan.Core.Adapters`
2. Implement query extensions in `Koan.Data.Core`
3. Add boot reporting utilities
4. **Deliverable**: Infrastructure components with unit tests

### Phase 2: Adapter Migration (Weeks 2-3)
1. **MongoDB** - Migrate first (most complex orchestration patterns)
2. **Couchbase** - Similar NoSQL patterns
3. **SQL adapters** - PostgreSQL, SQL Server, SQLite (similar relational patterns)
4. **Redis** - Simplest migration for validation
5. **Deliverable**: All adapters using centralized infrastructure

### Phase 3: Validation and Documentation (Week 4)
1. Verify provider transparency maintained across all adapters
2. Test auto-registration patterns function correctly
3. Performance validation - ensure no regression
4. Update documentation and adapter development guides
5. **Deliverable**: Validated framework with updated documentation

## Example: MongoDB Migration

### Before (Current State)
```csharp
internal sealed class MongoOptionsConfigurator : IConfigureOptions<MongoOptions>
{
    // 50+ lines of duplicated configuration logic
    // Manual enum parsing, TimeSpan conversion, readiness setup
}
```

### After (Proposed)
```csharp
internal sealed class MongoOptionsConfigurator : AdapterOptionsConfigurator<MongoOptions>
{
    protected override string ProviderName => "Mongo";

    public MongoOptionsConfigurator(IConfiguration config, ILogger<MongoOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readiness)
        : base(config, logger, readiness) { }

    protected override void ConfigureProviderSpecific(MongoOptions options)
    {
        // Only MongoDB-specific configuration logic
        options.ConnectionString = ResolveConnectionString(options.ConnectionString);
        options.Database = Configuration.ReadFirst(options.Database,
            "Koan:Data:Mongo:Database", "Koan:Data:Database");
        // 10-15 lines vs 50+ lines
    }
}
```

## Benefits Analysis

### Immediate Benefits
- **Code Reduction**: ~70% reduction in adapter configuration code
- **Consistency**: Uniform behavior across all adapters
- **Maintainability**: Single point of change for common patterns
- **Testing**: Centralized logic requires fewer test permutations

### Framework Benefits
- **"Reference = Intent"**: Complete infrastructure from package reference
- **Provider Transparency**: Guaranteed consistent entity behavior
- **Auto-registration**: Simplified adapter implementation
- **New Provider Velocity**: Focus on provider-specific value, not framework plumbing

### Enterprise Benefits
- **Reduced Training**: Adapter authors learn patterns once
- **Quality**: Centralized implementations receive more scrutiny
- **Evolution**: Framework improvements automatically benefit all adapters
- **Risk Mitigation**: Less duplication means fewer bugs

## Risks and Mitigations

### Risk: Breaking Changes to Existing Adapters
**Mitigation**: Gradual migration with backward compatibility during transition period

### Risk: Over-abstraction Reducing Flexibility
**Mitigation**: Virtual methods and extension points for provider-specific needs

### Risk: Performance Regression from Additional Abstraction
**Mitigation**: Performance benchmarking during migration, minimal runtime overhead design

### Risk: Complex Base Classes Becoming Hard to Maintain
**Mitigation**: Single Responsibility Principle - each base class handles one concern

## Success Criteria

### Quantitative Metrics
- **Code Reduction**: >60% reduction in adapter configuration line count
- **Duplication Elimination**: Zero repeated configuration/query/reporting patterns
- **New Adapter Velocity**: <50% time to implement new adapter vs current baseline

### Qualitative Metrics
- **Developer Experience**: Simplified adapter development guide
- **Consistency**: Uniform behavior verified by integration tests
- **Maintainability**: Framework changes propagate without manual adapter updates

## Decision Points

### Required Decisions
1. **Final API design** for base classes and interfaces
2. **Migration timeline** and backward compatibility approach
3. **Testing strategy** for gradual migration
4. **Documentation updates** scope and timeline

### Optional Enhancements
- **Code generation** for adapter boilerplate
- **Analyzer rules** to enforce centralized patterns
- **Performance monitoring** integration for adapter infrastructure

## Conclusion

This centralization directly supports Koan Framework's core principle of "Reference = Intent" by eliminating the need for adapter authors to become framework infrastructure experts. It reduces cognitive overhead, ensures consistency, and accelerates new adapter development while maintaining the provider transparency that's central to the framework's value proposition.

The proposed solution transforms adapter development from **infrastructure reimplementation** to **provider-specific value creation**, aligning perfectly with the framework's vision of minimal scaffolding and maximum productivity.

**Recommendation**: Approve for implementation with Phase 1 starting immediately, focusing on the configuration base class as it provides the highest impact with lowest risk.