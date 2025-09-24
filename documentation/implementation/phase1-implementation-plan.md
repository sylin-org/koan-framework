---
type: IMPLEMENTATION_PLAN
domain: orchestration
title: "Phase 1: Koan-Aspire Foundation and Proof of Concept Implementation Plan"
audience: [developers, technical-leads]
date: 2025-01-20
status: active
phase: 1
---

# Phase 1: Foundation and Proof of Concept Implementation Plan

**Phase Duration**: 3-4 weeks
**Start Date**: 2025-01-20
**Status**: ACTIVE (Architecture Approved)
**Team**: Framework Development Team

---

## Phase 1 Objectives

- ✅ Validate technical feasibility of distributed Aspire registration
- ✅ Establish core interface and extension patterns
- ✅ Implement basic integration with 2-3 key modules
- ✅ Create initial CLI export capability

## Success Criteria

**Technical**:
- [ ] Successfully generate and run AppHost with Postgres + Redis + sample app
- [ ] All resources appear correctly in Aspire dashboard
- [ ] Application can connect to and use database resources
- [ ] CLI export generates valid, runnable AppHost project
- [ ] Zero regression in existing Koan functionality

**Architectural**:
- [ ] Interface design aligns with existing Koan patterns
- [ ] Discovery mechanism reliable and performant
- [ ] Configuration integration seamless with existing patterns
- [ ] Error handling comprehensive and user-friendly

---

## Week 1-2: Core Interface Implementation

### Task 1.1: Define IKoanAspireRegistrar Interface
**Duration**: 2 days
**Assignee**: Senior Framework Developer
**Dependencies**: None

**Deliverables**:
- [ ] `IKoanAspireRegistrar` interface in `Koan.Core`
- [ ] XML documentation with usage examples
- [ ] Unit tests for interface contract validation
- [ ] Integration with existing `IKoanAutoRegistrar` pattern

**Implementation**:
```csharp
// File: src/Koan.Core/IKoanAspireRegistrar.cs
namespace Koan.Core;

/// <summary>
/// Optional interface for KoanAutoRegistrar implementations to provide
/// distributed Aspire resource registration capabilities.
/// </summary>
public interface IKoanAspireRegistrar
{
    void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env);
    int Priority => 1000;
    bool ShouldRegister(IConfiguration cfg, IHostEnvironment env) => true;
}
```

**Acceptance Criteria**:
- [ ] Interface compiles and integrates with existing `IKoanAutoRegistrar`
- [ ] Documentation covers all method parameters and usage patterns
- [ ] Unit tests validate interface contract requirements
- [ ] Code review approved by framework team

### Task 1.2: Create Discovery and Registration Infrastructure
**Duration**: 3 days
**Assignee**: Senior Framework Developer
**Dependencies**: Task 1.1 (IKoanAspireRegistrar interface)

**Deliverables**:
- [ ] Assembly discovery mechanism for Aspire-enabled modules
- [ ] Priority-based registration system
- [ ] Error handling and validation infrastructure
- [ ] Performance optimization for discovery process

**Key Components**:
```csharp
// KoanAspireExtensions.AddKoanDiscoveredResources()
// KoanAssemblyDiscovery.GetKoanAssemblies()
// Resource registration with error handling and validation
```

**Acceptance Criteria**:
- [ ] Discovery finds all assemblies with IKoanAspireRegistrar implementations
- [ ] Registration respects priority ordering and conditional logic
- [ ] Error handling provides clear messages for common failure scenarios
- [ ] Performance benchmarks meet acceptable startup time targets

### Task 1.3: Create New Orchestration.Aspire Package
**Duration**: 2 days
**Assignee**: DevOps Engineer + Framework Developer
**Dependencies**: Task 1.2 (Discovery infrastructure)

**Deliverables**:
- [ ] `Koan.Orchestration.Aspire` project with proper dependencies
- [ ] NuGet package configuration and metadata
- [ ] Extension methods and integration helpers
- [ ] Package documentation and README

**Package Structure**:
```
src/Koan.Orchestration.Aspire/
├── Koan.Orchestration.Aspire.csproj
├── Extensions/
│   └── KoanAspireExtensions.cs
├── Discovery/
│   └── KoanAssemblyDiscovery.cs
├── README.md
└── Properties/
    └── AssemblyInfo.cs
```

**Acceptance Criteria**:
- [ ] Package references correct Aspire dependencies
- [ ] Extension methods work with standard Aspire AppHost projects
- [ ] Package can be installed via NuGet in test projects
- [ ] Documentation covers installation and basic usage

---

## Week 2-3: Core Module Implementation

### Task 1.4: Implement Postgres Module Integration
**Duration**: 2 days
**Assignee**: Data Platform Developer
**Dependencies**: Task 1.3 (Orchestration.Aspire package)

**Deliverables**:
- [ ] Extend `Koan.Data.Postgres` KoanAutoRegistrar with `IKoanAspireRegistrar`
- [ ] Map PostgresOptions to Aspire postgres resource configuration
- [ ] Add conditional registration based on environment and configuration
- [ ] Create integration tests for postgres resource registration

**Implementation**:
```csharp
// In Koan.Data.Postgres/Initialization/KoanAutoRegistrar.cs
public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
{
    var options = new PostgresOptions();
    new PostgresOptionsConfigurator(cfg).Configure(options);

    var postgres = builder.AddPostgres("postgres", port: 5432)
        .WithDataVolume()
        .WithEnvironment("POSTGRES_DB", options.Database ?? "Koan")
        .WithEnvironment("POSTGRES_USER", options.Username ?? "postgres");
}
```

**Acceptance Criteria**:
- [ ] Postgres resource appears in Aspire dashboard
- [ ] Application can connect using generated connection string
- [ ] Configuration mapping works correctly for all PostgresOptions
- [ ] Integration tests pass in CI/CD pipeline

### Task 1.5: Implement Redis Module Integration
**Duration**: 2 days
**Assignee**: Data Platform Developer
**Dependencies**: Task 1.4 (Postgres implementation pattern)

**Deliverables**:
- [ ] Extend `Koan.Data.Redis` KoanAutoRegistrar with `IKoanAspireRegistrar`
- [ ] Map RedisOptions to Aspire redis resource configuration
- [ ] Handle password and database configuration mapping
- [ ] Add integration tests for redis resource registration

**Implementation**:
```csharp
// In Koan.Data.Redis/Initialization/KoanAutoRegistrar.cs
public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
{
    var options = new RedisOptions();
    new RedisOptionsConfigurator(cfg).Configure(options);

    var redis = builder.AddRedis("redis", port: 6379)
        .WithDataVolume()
        .WithEnvironment("REDIS_PASSWORD", options.Password ?? "");
}
```

**Acceptance Criteria**:
- [ ] Redis resource appears in Aspire dashboard
- [ ] Application can connect and perform basic operations
- [ ] Password and database configuration respected
- [ ] Integration tests validate all configuration scenarios

### Task 1.6: Create Sample Application Module
**Duration**: 3 days
**Assignee**: Web Platform Developer
**Dependencies**: Tasks 1.4, 1.5 (Database modules)

**Deliverables**:
- [ ] Sample web application project for testing
- [ ] Application KoanAutoRegistrar with resource references
- [ ] Test resource dependency injection and configuration
- [ ] Validate end-to-end application startup with Aspire

**Sample Application Structure**:
```
samples/KoanAspireIntegration/
├── KoanAspireIntegration.csproj
├── Program.cs
├── Controllers/
│   └── HealthController.cs
├── Models/
│   └── Todo.cs
├── Initialization/
│   └── KoanAutoRegistrar.cs
└── appsettings.json
```

**Acceptance Criteria**:
- [ ] Application starts successfully with Aspire AppHost
- [ ] Can read/write data to both Postgres and Redis
- [ ] Health checks work correctly in Aspire dashboard
- [ ] Resource dependencies injected and functional

---

## Week 3-4: CLI Integration

### Task 1.7: Extend Koan CLI with Aspire Export
**Duration**: 3 days
**Assignee**: CLI/Tooling Developer
**Dependencies**: Task 1.6 (Sample application validation)

**Deliverables**:
- [ ] Add `export aspire` command to existing Koan.Orchestration.Cli
- [ ] Create AppHost project template generation
- [ ] Generate Program.cs with KoanDiscoveredResources call
- [ ] Add command-line options for output path and profile

**CLI Command Structure**:
```bash
Koan export aspire [options]
  --out <path>           Output directory for AppHost project (default: ./AppHost)
  --profile <profile>    Environment profile (local|ci|staging|prod)
  --template <template>  AppHost template variant (default|minimal|advanced)
  --provider <provider>  Preferred container provider (auto|docker|podman)
```

**Acceptance Criteria**:
- [ ] Command generates valid AppHost project that compiles
- [ ] Generated Program.cs includes proper KoanDiscoveredResources call
- [ ] CLI help documentation accurate and complete
- [ ] Integration with existing CLI infrastructure seamless

### Task 1.8: Create End-to-End Validation
**Duration**: 2 days
**Assignee**: QA Engineer + Framework Developer
**Dependencies**: Task 1.7 (CLI export command)

**Deliverables**:
- [ ] Generate AppHost project using CLI
- [ ] Test startup and resource registration
- [ ] Validate Aspire dashboard integration
- [ ] Document developer workflow and troubleshooting

**Validation Scenarios**:
1. **CLI Export**: `Koan export aspire` generates working AppHost
2. **Resource Registration**: All modules register correctly
3. **Application Startup**: Sample app starts and connects to resources
4. **Aspire Dashboard**: Resources visible with correct status
5. **Provider Selection**: Docker and Podman scenarios tested

**Acceptance Criteria**:
- [ ] End-to-end workflow documented with screenshots
- [ ] All validation scenarios pass consistently
- [ ] Performance benchmarks within acceptable ranges
- [ ] Troubleshooting guide covers common issues

---

## Resource Allocation

### Team Members
- **Senior Framework Developer** (40h): Interface design, discovery infrastructure
- **Data Platform Developer** (32h): Postgres and Redis module integration
- **Web Platform Developer** (24h): Sample application and validation
- **CLI/Tooling Developer** (24h): CLI export command implementation
- **DevOps Engineer** (16h): Package creation, CI/CD integration
- **QA Engineer** (16h): End-to-end validation and testing

### Infrastructure Requirements
- **Development Environment**: .NET 8+, Docker Desktop or Podman
- **CI/CD Pipeline**: Integration test runners with container support
- **Test Accounts**: Azure subscription for cloud deployment validation (optional)

---

## Risk Mitigation

### Technical Risks

**Risk**: Aspire API incompatibility or breaking changes
- **Mitigation**: Pin to specific Aspire version, monitor release notes
- **Contingency**: Implement compatibility layer or version detection

**Risk**: Complex resource dependency issues
- **Mitigation**: Start with simple scenarios, add complexity incrementally
- **Contingency**: Implement resource dependency validation and clear error messages

**Risk**: Performance impact on application startup
- **Mitigation**: Benchmark against current orchestration, optimize discovery
- **Contingency**: Implement lazy loading and optional registration patterns

### Process Risks

**Risk**: Team unfamiliarity with Aspire patterns
- **Mitigation**: Training sessions, pair programming, documentation review
- **Contingency**: Adjust timeline and bring in external Aspire expertise

**Risk**: Scope creep beyond Phase 1 objectives
- **Mitigation**: Strict adherence to Phase 1 deliverables, defer enhancements
- **Contingency**: Re-scope to minimum viable integration for evaluation

---

## Phase 1 Deliverables Summary

### Code Deliverables
- [ ] `IKoanAspireRegistrar` interface in `Koan.Core`
- [ ] `Koan.Orchestration.Aspire` NuGet package
- [ ] Postgres module Aspire integration
- [ ] Redis module Aspire integration
- [ ] Sample application with end-to-end integration
- [ ] `Koan export aspire` CLI command

### Documentation Deliverables
- [ ] Interface documentation and usage examples
- [ ] Module integration guide for developers
- [ ] CLI command reference and examples
- [ ] End-to-end developer workflow guide
- [ ] Troubleshooting guide for common issues

### Testing Deliverables
- [ ] Unit tests for interface contracts
- [ ] Integration tests for module registrations
- [ ] End-to-end validation test suite
- [ ] Performance benchmarks and regression tests
- [ ] CI/CD pipeline integration

---

## Phase 1 Exit Criteria

### Must-Have (Blocking Phase 2)
- [ ] All core deliverables completed and tested
- [ ] Zero regression in existing Koan functionality
- [ ] End-to-end workflow validated and documented
- [ ] Architecture review confirms approach viability

### Should-Have (Address before Phase 2)
- [ ] Performance benchmarks meet targets
- [ ] Error handling comprehensive and user-friendly
- [ ] Documentation complete and reviewed
- [ ] Team comfortable with implementation patterns

### Nice-to-Have (Optional for Phase 1)
- [ ] Additional module integrations beyond Postgres/Redis
- [ ] Advanced CLI options and configuration
- [ ] Community feedback integration
- [ ] Azure deployment validation

---

**Next Phase**: Phase 2 - Infrastructure Module Coverage (2-3 weeks)
**Phase 2 Kickoff**: Contingent on Phase 1 success criteria being met

This implementation plan provides clear structure and accountability for delivering a functional proof of concept that validates the Koan-Aspire integration approach.