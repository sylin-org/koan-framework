---
id: ARCH-0055
slug: koan-aspire-integration-approval
domain: Architecture
status: approved
date: 2025-01-20
title: Koan-Aspire Integration via Distributed Resource Registration
---

## Context

Koan Framework requires enhanced orchestration capabilities to compete with .NET Aspire while maintaining its unique architectural principles. Analysis revealed that .NET Aspire's native multi-provider support (Docker + Podman via `ASPIRE_CONTAINER_RUNTIME`) creates an opportunity for beneficial integration rather than competitive positioning.

## Decision

**APPROVED**: Implement Koan-Aspire integration through distributed resource registration via extending the existing `KoanAutoRegistrar` pattern.

### Core Architecture

**Interface Extension**:
```csharp
public interface IKoanAspireRegistrar
{
    void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env);
    int Priority => 1000;
    bool ShouldRegister(IConfiguration cfg, IHostEnvironment env) => true;
}

// Each module extends existing pattern
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar, IKoanAspireRegistrar
{
    public void Initialize(IServiceCollection services) { /* Existing DI */ }
    public void RegisterAspireResources(IDistributedApplicationBuilder builder) { /* NEW */ }
}
```

**Discovery and Registration**:
```csharp
// AppHost automatically discovers and registers all Koan modules
var builder = DistributedApplication.CreateBuilder(args);
builder.AddKoanDiscoveredResources(); // Scans assemblies for IKoanAspireRegistrar
```

## Strategic Rationale

### Architectural Alignment
- ✅ **Enhances "Reference = Intent"**: Package reference enables DI + orchestration
- ✅ **Preserves Entity-First Development**: No impact on Entity<T> patterns
- ✅ **Extends Auto-Registration**: Natural evolution of existing pattern
- ✅ **Improves Multi-Provider Support**: Koan intelligence + Aspire runtime

### Competitive Positioning
- **Unique Market Position**: Only framework offering distributed Aspire resource registration
- **Enterprise Value**: Service ownership patterns for large-scale Aspire development
- **Enhanced Provider Selection**: Superior Docker/Podman detection + Aspire's native support
- **Deployment Flexibility**: Compose for local, Aspire for cloud, optimal provider selection

### Technical Benefits
- **Zero Regression**: Existing Koan functionality preserved
- **Ecosystem Access**: Microsoft Aspire tooling, dashboard, Azure integration
- **Reduced Infrastructure Burden**: Leverage Aspire's container orchestration
- **Enhanced Windows Experience**: Koan's Windows-first + Aspire's multi-platform support

## Implementation Approach

### Phase 1: Foundation and Proof of Concept (3-4 weeks)
- Core interface implementation (`IKoanAspireRegistrar`)
- Discovery and registration infrastructure
- Postgres + Redis module integration
- Basic CLI export capability

### Phase 2: Infrastructure Module Coverage (2-3 weeks)
- All core data providers (MongoDB, SQL Server, etc.)
- Advanced configuration and health check integration
- Dependency management and validation

### Phase 3: Application Services and Advanced Features (2-3 weeks)
- AI providers (Ollama), web services, messaging
- Custom resource types and external integrations
- Multi-environment support and performance optimization

### Phase 4: Polish and Production Readiness (1-2 weeks)
- Comprehensive documentation and migration guides
- Quality assurance and release preparation
- Community engagement and feedback integration

## Success Criteria

### Minimum Viability
- [ ] Zero regression in existing Koan functionality
- [ ] Clean integration with framework patterns
- [ ] Postgres + Redis + sample app working end-to-end with Aspire
- [ ] CLI generates valid, runnable AppHost projects

### Excellence Targets
- [ ] Superior provider selection compared to vanilla Aspire
- [ ] Unique "enterprise Aspire" market positioning established
- [ ] Community adoption through enhanced developer experience
- [ ] Azure deployment capabilities fully functional

## Risk Mitigation

### Technical Risks
- **Aspire API Changes**: Pin versions, monitor roadmap, maintain compatibility layers
- **Complex Dependencies**: Robust validation, clear error messages, extensive testing
- **Performance Impact**: Benchmarking, optimization, lazy loading patterns

### Strategic Risks
- **Developer Adoption**: Excellent documentation, migration guides, community support
- **Ecosystem Dependency**: Maintain Compose export as alternative, avoid Aspire lock-in
- **Competitive Response**: Focus on unique distributed patterns, enterprise value

## Consequences

### Positive
- Access to Microsoft's Aspire ecosystem and investment
- Unique market positioning as "enterprise patterns for Aspire"
- Reduced orchestration infrastructure development burden
- Enhanced Windows and multi-provider development experience

### Tradeoffs
- Additional complexity in framework architecture
- Dependency on Microsoft's Aspire roadmap and stability
- Need for team upskilling on Aspire patterns and tooling
- Maintenance of dual orchestration approaches (Compose + Aspire)

### Neutral
- Entity patterns and core framework unchanged
- Existing workflows preserved alongside new capabilities
- Optional adoption - teams can continue with current approaches

## Follow-ups

- ORCH-0001: Technical specification for IKoanAspireRegistrar interface implementation
- ORCH-0002: Discovery infrastructure design and assembly scanning patterns
- ORCH-0003: CLI integration for `Koan export aspire` command
- ORCH-0004: Module-by-module integration plan and timeline
- DOCS-0001: Developer migration guide from Compose to Aspire workflows

## References

- [Integration Analysis](../proposals/koan-aspire-integration-analysis.md)
- [Technical Specification](../proposals/koan-aspire-technical-specification.md)
- [Implementation Roadmap](../proposals/koan-aspire-implementation-roadmap.md)
- [Architecture Review Framework](../proposals/koan-aspire-architecture-review.md)
- [Microsoft Learn - .NET Aspire Multi-Provider Support](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/setup-tooling)

---

This decision establishes Koan's strategic direction toward enhanced orchestration capabilities while preserving architectural integrity and creating unique market differentiation in the .NET Aspire ecosystem.