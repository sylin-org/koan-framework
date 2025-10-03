---
type: PROPOSAL
domain: orchestration
title: "Koan-Aspire Integration: Distributed Resource Registration via KoanAutoRegistrar"
audience: [architects, developers]
date: 2025-01-20
status: proposed
---

# Koan-Aspire Integration Analysis

**Document Type**: PROPOSAL
**Target Audience**: Enterprise Architects, Framework Developers
**Date**: 2025-01-20
**Status**: Proposed for Evaluation

---

## Executive Summary

This document analyzes the feasibility and strategic implications of integrating Koan Framework with .NET Aspire through a distributed resource registration pattern leveraging the existing `KoanAutoRegistrar` infrastructure.

**Key Finding**: The existing `KoanAutoRegistrar` pattern provides an ideal foundation for distributed Aspire integration that enhances rather than compromises Koan's architectural principles.

**Strategic Recommendation**: Pursue selective integration through extending `KoanAutoRegistrar` with optional `IKoanAspireRegistrar` interface.

---

## Background Context

### Current Koan Orchestration Approach
- **Distributed Service Declaration**: Services self-describe orchestration needs via attributes
- **Multi-Provider Support**: Docker and Podman with automatic detection
- **Profile-Driven Deployment**: Different behaviors for local/ci/staging/prod environments
- **CLI-Driven Workflow**: `Koan up`, `Koan export compose`, etc.

### .NET Aspire Approach
- **Centralized Orchestration**: Single AppHost defines entire application topology
- **Microsoft Ecosystem Integration**: Azure-native deployments, Visual Studio tooling
- **Rich Development Experience**: Built-in dashboard, observability, service discovery
- **Broad Integration Library**: 20+ official integrations

### The Integration Opportunity

The key insight is that every Koan module already implements the `KoanAutoRegistrar` pattern:

```csharp
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public void Initialize(IServiceCollection services) { /* DI registration */ }
    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env) { /* Boot reporting */ }
}
```

This pattern can be extended to support distributed Aspire resource registration while preserving Koan's philosophical approach.

---

## Comparative Analysis

### .NET Aspire Strengths
- **Official Microsoft Support** and roadmap alignment
- **Azure Integration** without building cloud connectors
- **Rich Development Tooling** and debugging experience
- **Established Ecosystem** with proven adoption patterns
- **Production-Ready Deployment** to Kubernetes and cloud platforms

### .NET Aspire Limitations
- **Centralized Configuration** becomes unwieldy at scale
- **Microsoft Ecosystem Lock-in** limits multi-provider flexibility
- **Limited Service Ownership** patterns for distributed teams
- **Configuration Explosion** when managing many services

### Koan.Orchestration Strengths
- **Distributed Service Ownership** aligned with enterprise patterns
- **Provider Flexibility** with Docker/Podman support
- **Framework Integration** following "Reference = Intent" philosophy
- **Windows-First Development** experience
- **Override-Friendly** local development workflow

### Koan.Orchestration Limitations
- **Framework Dependency** - not usable outside Koan ecosystem
- **Limited Adoption** compared to Microsoft-backed solutions
- **Feature Gaps** in development tooling and observability
- **Unproven at Enterprise Scale** without large production deployments

---

## Integration Feasibility Assessment

### Technical Feasibility: ✅ HIGHLY FEASIBLE

The integration can be achieved through extending the existing `KoanAutoRegistrar` pattern:

```csharp
public interface IKoanAspireRegistrar
{
    void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env);
}

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar, IKoanAspireRegistrar
{
    // Existing DI registration (unchanged)
    public void Initialize(IServiceCollection services) { /* Existing logic */ }

    // Existing boot reporting (unchanged)
    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env) { /* Existing logic */ }

    // NEW: Self-registers Aspire resources
    public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
    {
        var postgres = builder.AddPostgres("postgres")
            .WithDataVolume()
            .WithEnvironment("POSTGRES_DB", cfg["Database:Name"] ?? "Koan");
    }
}
```

### Architectural Compatibility: 🟡 MOSTLY COMPATIBLE

**Enhances Core Principles**:
- ✅ **"Reference = Intent"**: Package reference now enables DI + Aspire resource registration
- ✅ **Self-Reporting Infrastructure**: Services self-describe both DI and orchestration needs
- ✅ **Auto-Registration Patterns**: Natural extension of existing auto-registration philosophy

**Philosophical Shifts**:
- 🟡 **Multi-Provider Transparency**: Loses Docker/Podman flexibility, gains Azure/cloud options
- 🟡 **Framework Integration**: Koan becomes "pattern provider" rather than "full-stack framework"

**Preserved Patterns**:
- ✅ **Entity-First Development**: Completely unaffected by orchestration changes
- ✅ **Framework-Native Integration**: Enhanced through Aspire resource registration

### Strategic Positioning: ✅ DIFFERENTIATING

This approach would create a **unique market position**:
- **No other framework** offers distributed, self-describing Aspire resource registration
- **Enterprise-friendly** service ownership patterns for Aspire development
- **Best of both worlds**: Koan's patterns + Aspire's ecosystem

---

## Implementation Architecture

### Distributed Resource Registration Pattern

```csharp
// AppHost/Program.cs (generated or template)
var builder = DistributedApplication.CreateBuilder(args);

// Koan's distributed discovery automatically finds and registers all modules
builder.AddKoanDiscoveredResources();

var app = builder.Build();
app.Run();
```

### Discovery Implementation

```csharp
public static class KoanAspireExtensions
{
    public static IDistributedApplicationBuilder AddKoanDiscoveredResources(
        this IDistributedApplicationBuilder builder)
    {
        var assemblies = KoanAssemblyDiscovery.GetKoanAssemblies();

        foreach (var assembly in assemblies)
        {
            var registrarType = assembly.GetTypes()
                .FirstOrDefault(t => t.Name == "KoanAutoRegistrar" &&
                               t.GetInterface(nameof(IKoanAspireRegistrar)) != null);

            if (registrarType != null)
            {
                var registrar = (IKoanAspireRegistrar)Activator.CreateInstance(registrarType);
                registrar.RegisterAspireResources(builder, builder.Configuration, builder.Environment);
            }
        }

        return builder;
    }
}
```

### Example Module Implementations

**Postgres Module**:
```csharp
public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
{
    var pgOptions = new PostgresOptions();
    new PostgresOptionsConfigurator(cfg).Configure(pgOptions);

    builder.AddPostgres("postgres")
        .WithDataVolume()
        .WithEnvironment("POSTGRES_DB", pgOptions.Database ?? "Koan")
        .WithConnectionString(pgOptions.ConnectionString);
}
```

**Redis Module**:
```csharp
public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
{
    var redisOptions = new RedisOptions();
    new RedisOptionsConfigurator(cfg).Configure(redisOptions);

    builder.AddRedis("redis")
        .WithDataVolume()
        .WithEnvironment("REDIS_PASSWORD", redisOptions.Password ?? "");
}
```

### CLI Integration

```bash
# New export target alongside existing Compose export
Koan export aspire --out ./AppHost/

# Traditional workflows remain available
Koan export compose --profile local
Koan up --engine docker
```

---

## Risk Assessment

### High-Risk Areas

**1. Resource Naming Conflicts**
- **Risk**: Multiple modules registering resources with same names
- **Mitigation**: Use consistent naming conventions from Koan's service discovery

**2. Service Dependency Management**
- **Risk**: Complex inter-service references across distributed registrations
- **Mitigation**: Implement resource reference patterns and validation

**3. Configuration Complexity**
- **Risk**: Mapping Koan configuration patterns to Aspire resource configuration
- **Mitigation**: Reuse existing Koan configuration configurators

### Medium-Risk Areas

**4. Development Workflow Changes**
- **Risk**: Developers need to learn both Koan and Aspire patterns
- **Mitigation**: Maintain existing Koan workflows alongside new Aspire options

**5. Ecosystem Dependency**
- **Risk**: Tight coupling to Aspire's evolution and Microsoft's roadmap
- **Mitigation**: Maintain Compose export as alternative deployment path

### Low-Risk Areas

**6. Framework Integration Impact**
- **Risk**: Minimal - Entity patterns and core framework remain unchanged
- **Mitigation**: Not required - integration is additive, not replacing

---

## Strategic Implications

### What Koan Would Gain

**Immediate Benefits**:
- Access to Microsoft's Aspire investment and ecosystem
- Azure integration without building cloud connectors
- Visual Studio tooling and development experience
- Community adoption path through .NET ecosystem

**Long-term Benefits**:
- Unique positioning as "enterprise patterns for Aspire"
- Reduced development burden for orchestration infrastructure
- Production deployment patterns already solved

### What Koan Would Preserve

**Core Value Propositions**:
- Entity-first development patterns remain unchanged
- "Reference = Intent" philosophy actually enhanced
- Auto-registration patterns naturally extended
- Framework-native integration maintained

### Strategic Positioning

**Instead of "Koan vs Aspire"**, this becomes **"Koan-enhanced Aspire"**:
- Superior way to use Aspire with enterprise service ownership patterns
- Distributed, self-describing resource registration (unique in market)
- Framework-consistent development experience
- Multi-deployment flexibility (Compose for local, Aspire for cloud)

---

## Recommendations

### Phase 1: Proof of Concept (Recommended)
- Extend `IKoanAutoRegistrar` with optional `IKoanAspireRegistrar`
- Implement for 2-3 core modules (Postgres, Redis, basic app)
- Create `Koan export aspire` command
- Validate technical feasibility and developer experience

### Phase 2: Full Integration (If POC Successful)
- Implement `IKoanAspireRegistrar` across all infrastructure modules
- Add Aspire dashboard integration during development
- Optimize resource dependencies and service references

### Phase 3: Market Positioning (Long-term)
- Market as "Enterprise patterns for Aspire development"
- Build community around pattern-driven Aspire usage
- Establish as preferred approach for large-scale Aspire deployments

### Alternative: Maintain Competitive Position
If integration proves problematic, double down on Koan.Orchestration's unique differentiators:
- Enterprise service ownership patterns
- True multi-provider flexibility (Docker + Podman + future providers)
- Export to multiple targets (Compose, Nomad, pure Kubernetes)

---

## Conclusion

The `KoanAutoRegistrar` pattern provides an architecturally sound foundation for Koan-Aspire integration that enhances rather than compromises Koan's core principles.

**Technical Verdict**: Highly feasible through distributed resource registration
**Architectural Verdict**: Compatible with enhancement of core principles
**Strategic Verdict**: Differentiating approach with unique market positioning

**Recommendation**: Proceed with Phase 1 proof of concept to validate technical approach and developer experience.

This integration would position Koan as the premier framework for enterprise-scale Aspire development while preserving the architectural integrity that makes Koan unique.

---

**Next Steps**:
1. Create technical specification for `IKoanAspireRegistrar` interface
2. Implement proof of concept with Postgres and Redis modules
3. Validate approach with sample application deployment
4. Gather feedback from development team on developer experience