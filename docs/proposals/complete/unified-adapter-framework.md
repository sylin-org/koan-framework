# Unified Adapter Framework Proposal

**Status**: Implemented
**Implementation Evidence**: The Koan.Core.Adapters package, base adapter classes, capability system, and orchestration bridge outlined here now ship in `src/Koan.Core.Adapters`.
**Author**: Technical Analysis
**Date**: 2024-09-22
**Version**: 1.0

## Executive Summary

**Problem**: Current adapter implementations across the Koan framework suffer from 75-80% code duplication, inconsistent patterns, and poor developer experience. Each adapter (Ollama, PostgreSQL, MongoDB, Redis, etc.) reimplements similar boilerplate for configuration, health checks, capability reporting, and bootstrap integration.

**Solution**: Create a unified adapter framework that reduces implementation complexity from 150+ lines to ~40 lines per adapter while providing consistent DX patterns across all service categories.

**Impact**:
- 75-80% reduction in adapter implementation code
- Unified configuration and capability patterns
- Standardized health reporting and bootstrap integration
- Consistent error handling and logging

---

## Problem Statement

### Current State Analysis

**1. Code Duplication Crisis**
- `OllamaAdapter`: 151 lines of boilerplate + AI-specific logic
- `PostgresAdapterFactory`: 168 lines of database connection logic
- `MongoAdapterFactory`: 145 lines of similar database patterns
- `RedisHealthContributor`: 85 lines of health check logic

**2. Repetitive Patterns**
```csharp
// Configuration retrieval (repeated in every adapter)
var connectionString = configuration.GetConnectionString("ServiceName");
var options = configuration.GetSection("Koan:Services:ServiceName").Get<ServiceOptions>();
var enabled = options?.Enabled ?? true;

// Health check implementation (repeated in every adapter)
public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
{
    try { /* service-specific check */ }
    catch (Exception ex) { return HealthCheckResult.Unhealthy(ex.Message); }
}

// Bootstrap reporting (inconsistent across adapters)
public BootstrapReport GetBootstrapReport()
{
    return new BootstrapReport { ServiceType = ServiceType.Database, /* ... */ };
}
```

**3. Identified Inconsistencies**
- Configuration section naming varies: `"Koan:AI:Ollama"` vs `"Koan:Services:Postgres"`
- Health check timeout handling differs across services
- Bootstrap reporting uses different metadata structures
- Error handling patterns are inconsistent

### Architecture Issues

**1. Separation of Concerns Violations**
- Adding adapter framework to `Koan.Core` would create dependency inversion
- Core package should remain dependency-free
- Mixing orchestration metadata with runtime concerns

**2. Missing Developer Experience**
- No scaffolding or templates for new adapters
- Configuration patterns not documented/enforced
- Health check requirements vary by service type
- Bootstrap metadata structure is unclear

---

## Proposed Solution

### Architecture Principles

**1. Developer Experience First**
- Reduce adapter implementation to essential business logic only
- Provide smart defaults for common patterns
- Template-driven code generation where possible

**2. Two-Layer Architecture**
```
Orchestration Layer (KoanService)     Runtime Layer (KoanAdapter)
├─ Service discovery metadata        ├─ Runtime capabilities
├─ Container orchestration           ├─ Health monitoring
├─ Dependency provisioning           ├─ Configuration management
└─ Development tooling               └─ Bootstrap reporting
```

**3. Capability-Based Programming**
```csharp
// Runtime querying of adapter capabilities
var ollama = adapters.GetService<IOllamaAdapter>();
if (ollama.Capabilities.SupportsStreaming) {
    await ollama.StreamResponseAsync(prompt);
} else {
    await ollama.GetResponseAsync(prompt);
}
```

### Package Structure

**New Package: Koan.Core.Adapters**
```xml
<PackageReference Include="Koan.Orchestration.Abstractions" Version="*" />
<PackageReference Include="Koan.Data.Abstractions" Version="*" />
<PackageReference Include="Microsoft.Extensions.Options" Version="9.0.8" />
<PackageReference Include="Microsoft.Extensions.Configuration" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.0" />
```

**Contents:**
- `IKoanAdapter` - Unified adapter interface
- `BaseKoanAdapter<T>` - Template method pattern base class
- `AdapterCapabilities` - Capability declaration system
- `OrchestrationRuntimeBridge` - Two-layer coordination
- Configuration helpers and health check templates

### Core Interface Design

```csharp
public interface IKoanAdapter : IHealthContributor
{
    ServiceType ServiceType { get; }
    string AdapterId { get; }
    string? DisplayName { get; }
    AdapterCapabilities Capabilities { get; }
    Task<BootstrapReport> GetBootstrapReportAsync(CancellationToken cancellationToken = default);
}

public abstract class BaseKoanAdapter<TOptions> : IKoanAdapter
    where TOptions : class, new()
{
    // Template method pattern with DX-focused helpers
    protected TOptions GetOptions() => Configuration.GetOptions<TOptions>(ServiceType);
    protected string GetConnectionString(string? name = null) => Configuration.GetConnectionString(name ?? ServiceType.ToString());
    protected bool IsEnabled() => GetOptions().Enabled;

    // Abstract methods for service-specific implementation
    protected abstract Task<bool> CheckServiceHealthAsync(CancellationToken cancellationToken);
    protected abstract AdapterCapabilities GetCapabilities();
}

public class AdapterCapabilities
{
    public bool SupportsStreaming { get; init; }
    public bool SupportsBatching { get; init; }
    public string[] SupportedProtocols { get; init; } = [];
    public QueryCapabilities QueryCapabilities { get; init; } = QueryCapabilities.None;
    public Dictionary<string, object> CustomCapabilities { get; init; } = new();
}
```

---

## Implementation Plan

### Phase 1: Foundation Package (Week 1-2)

**1.1 Create Koan.Core.Adapters Package**
- Set up project structure with proper dependencies
- Ensure SoC compliance (no reverse dependencies to Koan.Core)

**1.2 Implement Core Interfaces**
- `IKoanAdapter` - Unified adapter contract
- `AdapterCapabilities` - Capability declaration system
- `BootstrapReport` - Standardized bootstrap metadata

**1.3 Create BaseKoanAdapter Template**
- Configuration helpers (`GetOptions<T>()`, `GetConnectionString()`)
- Health check template with timeout handling
- Bootstrap reporting with service metadata
- Error handling and logging patterns

### Phase 2: Orchestration Bridge (Week 3)

**2.1 OrchestrationRuntimeBridge**
- Coordinate between KoanService (orchestration) and KoanAdapter (runtime)
- Aggregate capability metadata across layers
- Provide unified service discovery interface

**2.2 Integration Patterns**
- Auto-registration discovery for IKoanAdapter implementations
- Health check registration with ASP.NET Core
- Bootstrap reporting integration with existing BootReport system

### Phase 3: Proof of Concept (Week 4)

**3.1 OllamaAdapter Refactoring**
```csharp
[KoanService(ServiceKind.AI, "ollama", "Ollama AI",
    ContainerImage = "ollama/ollama", DefaultPorts = new[] { 11434 })]
public class OllamaAdapter : BaseKoanAdapter<OllamaOptions>, IOllamaAdapter
{
    public override ServiceType ServiceType => ServiceType.ArtificialIntelligence;

    protected override async Task<bool> CheckServiceHealthAsync(CancellationToken cancellationToken)
    {
        var response = await HttpClient.GetAsync("/api/tags", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    protected override AdapterCapabilities GetCapabilities()
    {
        return new AdapterCapabilities
        {
            SupportsStreaming = true,
            SupportedModels = GetAvailableModels(),
            QueryCapabilities = QueryCapabilities.None
        };
    }

    // Only AI-specific business logic remains (~40 lines vs 151 original)
    public async Task<string> GenerateResponseAsync(string prompt) { /* ... */ }
}
```

**3.2 Validation**
- Verify 75%+ code reduction achieved
- Test health check integration
- Validate bootstrap reporting
- Confirm capability querying works

### Phase 4: Adapter Migration (Week 5-7)

**4.1 Data Adapters**
- PostgresAdapterFactory → PostgresAdapter
- MongoAdapterFactory → MongoAdapter
- Maintain backward compatibility with IAdapterFactory

**4.2 Infrastructure Adapters**
- Redis health contributors → RedisAdapter
- Message queue adapters → unified messaging patterns

**4.3 Migration Validation**
- Integration tests for each migrated adapter
- Performance regression testing
- Backward compatibility verification

### Phase 5: Developer Experience (Week 8-9)

**5.1 Template System**
- `AdapterTemplateManager` for scaffolding new adapters
- Service-type-specific templates (Database, AI, Vector, Messaging)
- Code generation from service metadata

**5.2 Configuration Standardization**
- Unified section naming: `"Koan:Services:{ServiceName}"`
- Standard option patterns across all adapters
- Environment-specific configuration helpers

**5.3 Documentation**
- Migration guides for existing adapters
- New adapter development guide
- API documentation for unified interfaces

### Phase 6: Testing & Validation (Week 10)

**6.1 Comprehensive Testing**
- Unit tests for adapter framework
- Integration tests for all migrated adapters
- Performance benchmarks vs original implementations

**6.2 Quality Assurance**
- Code coverage analysis
- Static code analysis
- Security review of new interfaces

---

## Expected Benefits

### Quantitative Improvements

**Code Reduction Metrics:**
- **OllamaAdapter**: 151 lines → 40 lines (74% reduction)
- **PostgresAdapter**: ~168 lines → ~45 lines (73% reduction)
- **Average reduction**: 75-80% across all adapters

**Developer Productivity:**
- **Time to implement new adapter**: 2-3 hours → 30-45 minutes
- **Configuration complexity**: Manual → Template-driven
- **Health check implementation**: Manual → Declarative
- **Bootstrap integration**: Ad-hoc → Automatic

### Qualitative Improvements

**Maintainability:**
- Unified error handling across all adapters
- Consistent logging patterns with structured data
- Standardized capability discovery for runtime queries
- Template-based updates can propagate to all adapters

**Developer Experience:**
- Clear scaffolding path for new adapters
- Consistent configuration patterns
- Automated health check and bootstrap integration
- Runtime capability querying eliminates guesswork

---

## Risk Assessment

### Technical Risks

**1. Breaking Changes to Existing Adapters**
- **Risk Level**: Medium
- **Impact**: Existing service integrations may break during migration
- **Mitigation**:
  - Maintain backward compatibility interfaces
  - Phased migration approach with feature flags
  - Comprehensive integration testing for each adapter

**2. Dependency Management Complexity**
- **Risk Level**: Low
- **Impact**: New package dependencies could create version conflicts
- **Mitigation**:
  - Minimal dependency footprint
  - Align with existing framework versions
  - Dependency analysis across all Koan packages

**3. Performance Overhead**
- **Risk Level**: Low
- **Impact**: Abstract base classes could introduce performance penalties
- **Mitigation**:
  - Benchmark critical paths during development
  - Optimize hot paths in template implementations
  - Performance regression testing for adapter operations

### Implementation Risks

**1. Incomplete Migration Coverage**
- **Risk Level**: Medium
- **Impact**: Some adapters may not fit the unified pattern
- **Mitigation**:
  - Comprehensive analysis of all existing adapters before finalizing interface
  - Provide escape hatches for edge cases
  - Document patterns that don't fit and reasons why

**2. Developer Adoption Resistance**
- **Risk Level**: Low
- **Impact**: Developers may prefer existing manual patterns
- **Mitigation**:
  - Clear migration guides with before/after examples
  - Demonstrate quantifiable DX improvements
  - Provide comprehensive documentation and support

### Architectural Risks

**1. SoC Violations**
- **Risk Level**: Medium
- **Impact**: Could introduce dependency inversions or architectural debt
- **Mitigation**:
  - Strict package boundaries with dependency analysis
  - Architecture decision records for key boundaries
  - Regular architecture reviews during implementation

**2. Over-Engineering**
- **Risk Level**: Medium
- **Impact**: Framework becomes too complex for simple adapter use cases
- **Mitigation**:
  - Progressive complexity model - simple cases remain simple
  - Validate framework with minimal adapter implementations
  - Regular complexity assessments against original goals

---

## Success Criteria

### Quantitative Measures
- [ ] **70%+ reduction** in adapter implementation lines of code
- [ ] **50%+ reduction** in time to implement new adapters
- [ ] **100% backward compatibility** with existing adapter interfaces
- [ ] **Zero performance regression** in adapter operations

### Qualitative Measures
- [ ] **Consistent configuration patterns** across all service types
- [ ] **Unified health check and bootstrap reporting** experience
- [ ] **Positive developer feedback** on DX improvements
- [ ] **Framework patterns easily understood** and adopted by new developers

### Technical Deliverables
- [ ] **Koan.Core.Adapters package** with unified interfaces and base classes
- [ ] **Migration guide** for converting existing adapters to unified framework
- [ ] **Template system** for generating new adapters from service metadata
- [ ] **Comprehensive test coverage** (>90%) for adapter framework components
- [ ] **Updated documentation** reflecting new patterns and best practices

---

## Timeline Summary

| Phase | Duration | Key Deliverables |
|-------|----------|------------------|
| **Phase 1: Foundation** | Week 1-2 | Koan.Core.Adapters package, core interfaces |
| **Phase 2: Bridge** | Week 3 | OrchestrationRuntimeBridge, integration patterns |
| **Phase 3: Proof of Concept** | Week 4 | OllamaAdapter refactoring, validation |
| **Phase 4: Migration** | Week 5-7 | All existing adapters migrated to unified framework |
| **Phase 5: Developer Experience** | Week 8-9 | Template system, documentation, guides |
| **Phase 6: Testing & Validation** | Week 10 | Comprehensive testing, quality assurance |

**Total Duration**: 10 weeks
**Key Milestone**: Phase 3 completion validates the entire approach

---

## Decision Points

### Critical Design Decisions Required

**1. Configuration Section Standardization**
- **Options**:
  - A: Maintain existing varied patterns (`"Koan:AI:Ollama"`, `"Koan:Services:Postgres"`)
  - B: Standardize on single pattern (`"Koan:Services:{ServiceName}"`)
- **Recommendation**: Option B for consistency, with migration path for existing configurations

**2. Backward Compatibility Scope**
- **Options**:
  - A: Full backward compatibility with wrapper interfaces
  - B: Breaking changes with comprehensive migration guide
- **Recommendation**: Option A to minimize disruption during initial rollout

**3. Template Complexity Level**
- **Options**:
  - A: Simple template with minimal abstractions
  - B: Rich template system with extensive code generation
- **Recommendation**: Start with Option A, evolve to B based on adoption feedback

### Implementation Approach

**1. Migration Strategy**
- **Parallel Implementation**: Maintain both old and new adapter patterns during transition
- **Feature Flags**: Allow runtime switching between implementations
- **Gradual Rollout**: Migrate adapters one by one with thorough testing

**2. Testing Strategy**
- **Compatibility Testing**: Ensure migrated adapters behave identically to originals
- **Performance Testing**: Benchmark adapter operations before and after migration
- **Integration Testing**: Validate adapter framework with real service dependencies

---

## Conclusion

The unified adapter framework addresses critical code duplication and developer experience issues in the current Koan architecture. By implementing a template method pattern with service-specific capabilities, we can achieve 75-80% code reduction while improving consistency and maintainability.

**Key Success Factors:**
1. **Separation of Concerns**: Keep orchestration and runtime concerns properly separated
2. **Progressive Migration**: Maintain backward compatibility during transition
3. **Developer Focus**: Prioritize DX improvements over architectural purity
4. **Thorough Testing**: Validate every aspect of the migration

**The Path Forward:**
This proposal provides a concrete roadmap for eliminating adapter code duplication while establishing consistent patterns for future development. The phased approach minimizes risk while delivering incremental value at each stage.

Success depends on careful implementation planning, comprehensive testing, and maintaining developer productivity throughout the transition period.

---

## Appendix

### A. Current Adapter Analysis

**Detailed Code Analysis:**
- OllamaAdapter: 151 lines (health check: 23 lines, config: 18 lines, bootstrap: 15 lines)
- PostgresAdapterFactory: 168 lines (connection: 31 lines, health: 27 lines, options: 22 lines)
- MongoAdapterFactory: 145 lines (similar patterns to Postgres)
- RedisHealthContributor: 85 lines (focused on health checks only)

**Common Pattern Extraction:**
- Configuration retrieval: ~20-25 lines per adapter
- Health check implementation: ~25-30 lines per adapter
- Bootstrap reporting: ~15-20 lines per adapter
- Error handling and logging: ~10-15 lines per adapter

**Specific Code Analysis Discoveries:**

**1. OllamaAdapter Pattern Analysis (`src/Koan.AI.Connector.Ollama/OllamaAdapter.cs`)**
```csharp
// Configuration Duplication (18 lines across multiple methods)
private static OllamaOptions GetOllamaOptions(IConfiguration configuration)
{
    var section = configuration.GetSection("Koan:AI:Ollama");
    var options = section.Get<OllamaOptions>() ?? new OllamaOptions();
    // Additional validation and default setting logic...
}

// Health Check Boilerplate (23 lines)
public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
{
    try
    {
        var healthCheckTimeout = TimeSpan.FromMilliseconds(450);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(healthCheckTimeout);

        var response = await _httpClient.GetAsync("/api/tags", cts.Token);
        // Response validation and result construction...
    }
    catch (Exception ex) { return HealthCheckResult.Unhealthy(ex.Message, ex); }
}
```

**2. PostgreSQL Adapter Factory Pattern (`src/Koan.Data.Connector.Postgres/PostgresAdapterFactory.cs`)**
```csharp
[KoanService(ServiceKind.Database, shortCode: "postgres", name: "PostgreSQL",
    ContainerImage = "postgres", DefaultTag = "16", DefaultPorts = new[] { 5432 },
    Capabilities = new[] { "protocol=postgres" })]
public class PostgresAdapterFactory : IAdapterFactory<IPostgresDataAdapter>
{
    // Service discovery and orchestration metadata (25 lines)
    public async Task<ServiceInstanceResult> DiscoverServiceInstanceAsync(...)
    {
        var options = CreateOllamaDiscoveryOptions();
        var result = await _serviceDiscovery.DiscoverServiceAsync("postgres", options, cancellationToken);
        // Connection string construction and validation...
    }

    // Connection management patterns (31 lines)
    private async Task<IPostgresDataAdapter> CreateAdapterAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        // Connection pooling, timeout configuration, error handling...
    }
}
```

**3. Mongo Adapter Factory Analysis (`src/Koan.Data.Connector.Mongo/MongoAdapterFactory.cs`)**
- Nearly identical structure to PostgreSQL (145 lines vs 168 lines)
- Same service discovery patterns with different connection string format
- Duplicate health check implementation with MongoDB-specific client calls
- Same orchestration metadata attribute pattern with different service parameters

**4. Redis Health Contributor (`src/Koan.Data.Connector.Redis/RedisHealthContributor.cs`)**
```csharp
public class RedisHealthContributor : IHealthContributor
{
    // Minimal health check focused implementation (85 lines total)
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        try
        {
            await _redis.Database.PingAsync();
            return HealthCheckResult.Healthy($"Redis connection is healthy");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Redis connection failed: {ex.Message}", ex);
        }
    }
}
```

**5. Configuration Pattern Inconsistencies Discovered:**
- **OllamaAdapter**: Uses `"Koan:AI:Ollama"` configuration section
- **PostgreSQL**: Uses `"Koan:Services:Postgres"` and Infrastructure.Constants.Configuration.ServicesRoot
- **MongoDB**: Uses `"Koan:Data:Mongo"` configuration section
- **Redis**: Uses `"Koan:Cache:Redis"` configuration section

**6. Service Discovery Integration Patterns:**
- All data adapters implement `OrchestrationAwareServiceDiscovery` pattern
- Consistent `ServiceDiscoveryOptions` usage with different health check paths
- Common timeout handling (450ms) across service health checks
- Legacy environment variable support in discovery services

**7. Bootstrap Reporting Inconsistencies:**
- Some adapters integrate with `BootReport` system, others don't
- Metadata structure varies: some use structured objects, others use string arrays
- Service type categorization inconsistent: `ServiceType.Database` vs `ServiceType.ArtificialIntelligence`

### B. Technology Stack

**Dependencies:**
- .NET 9.0 (aligning with existing framework)
- Microsoft.Extensions.* packages (DI, Configuration, Options, Health, Logging)
- Koan.Orchestration.Abstractions (ServiceType, metadata)
- Koan.Data.Abstractions (QueryCapabilities)

**Tools:**
- Source generators for template code generation
- Roslyn analyzers for enforcing adapter patterns
- Benchmarking tools for performance validation

### C. Migration Checklist

**Pre-Migration:**
- [ ] Analyze all existing adapters for pattern compatibility
- [ ] Define capability model for each service type
- [ ] Create backward compatibility interfaces
- [ ] Set up comprehensive test suite

**During Migration:**
- [ ] Migrate one adapter type at a time (AI → Database → Infrastructure)
- [ ] Validate each migration with integration tests
- [ ] Monitor performance metrics continuously
- [ ] Gather developer feedback at each stage

**Post-Migration:**
- [ ] Remove deprecated adapter implementations (after grace period)
- [ ] Update all documentation and examples
- [ ] Conduct developer training sessions
- [ ] Establish maintenance procedures for adapter framework
