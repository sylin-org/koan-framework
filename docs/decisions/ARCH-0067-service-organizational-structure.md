---
id: ARCH-0067
slug: service-organizational-structure
domain: Architecture
status: Accepted
date: 2025-11-02
title: Service Mesh Organizational Structure and Naming Clarity
---

## Contract

- **Inputs**: Service infrastructure projects (`Koan.Services`, `Koan.Services.Abstractions`) and service implementation projects (`Koan.Services.Translation`, `Koan.Services.Translation.Container`) requiring organizational restructuring.
- **Outputs**: Hierarchical `src/Services/` folder structure mirroring `src/Connectors/` pattern, `ServiceMesh` naming for infrastructure (`Koan.ServiceMesh`), and clear separation between framework infrastructure and service implementations.
- **Error Modes**: Mixed directory depths causing build path confusion, inconsistent project references, stale Docker contexts, orphaned configuration keys, and namespace collisions.
- **Success Criteria**: All service projects build successfully, Docker images use correct paths, solution structure is intuitive, ServiceMesh terminology clarifies technical role, and future services can be added to `src/Services/{ServiceName}/` without cluttering the src root.

## Context

Koan Framework's service mesh infrastructure (`Koan.Services`) and service implementations (`Koan.Services.Translation`) resided at the `src/` root level, creating organizational inconsistency:

1. **Inconsistent with Connectors Pattern**: `src/Connectors/` follows `Connectors/{Domain}/{Implementation}` hierarchy, while services were flat at root level
2. **Scalability Issues**: Adding 5-10+ services (Inbox, Email, Notifications, etc.) would clutter `src/` with dozens of projects
3. **Naming Ambiguity**: Plural "Services" suggests multiple implementations rather than framework infrastructure (compare: `Koan.Core`, `Koan.Data.Core`, not `Koan.Cores`)
4. **Unclear Separation**: No visual distinction between framework infrastructure (service mesh, discovery, execution) and actual service implementations (Translation, future services)

The framework's greenfield status enabled a breaking reorganization without migration debt from external dependents.

## Decision

**Accepted**: Restructure service organization with:

1. **ServiceMesh Naming** for framework infrastructure (clarifies technical role):
   - `Koan.Services` → `Koan.ServiceMesh`
   - `Koan.Services.Abstractions` → `Koan.ServiceMesh.Abstractions`

2. **Hierarchical Service Folder**:
   ```
   src/
     Koan.ServiceMesh/                    # Service mesh infrastructure (discovery, routing, execution)
     Koan.ServiceMesh.Abstractions/       # Service mesh contracts
     Services/                            # Service implementations folder
       Translation/
         Koan.Service.Translation/        # Service implementations stay in Koan.Service.*
         Koan.Service.Translation.Container/
       {Future services}/
   ```

3. **Clear Naming Distinction**:
   - Infrastructure: `Koan.ServiceMesh.*` (mesh, discovery, load balancing)
   - Implementations: `Koan.Service.{ServiceName}` (actual business services)

4. **Configuration Updates**:
   - Logging: `Koan.ServiceMesh` namespace
   - Service config: `Koan:Service:{ServiceName}:*` (unchanged for service-specific settings)

### Rationale

- **Technical Accuracy**: "ServiceMesh" correctly describes UDP multicast discovery, load balancing, health monitoring
- **Industry Alignment**: Familiar terminology (compare: Istio, Linkerd, Consul) while clarifying this is application-level integration
- **Code Consistency**: Internal types (`IKoanServiceMesh`, `KoanServiceMesh`) already use this terminology
- **Clear Separation**: Infrastructure (`ServiceMesh`) vs implementations (`Service.Translation`)
- **Scales Cleanly**: 50+ services won't clutter `src/` root
- **Visual Hierarchy**: Organizational structure matches conceptual architecture

## Options Considered

| Option                                      | Outcome                       | Evaluation                                                                                                    |
| ------------------------------------------- | ----------------------------- | ------------------------------------------------------------------------------------------------------------- |
| Keep flat structure at src root              | Minimal disruption            | **Rejected.** Doesn't scale. 10 services = 30+ projects cluttering src root.                                  |
| Use `Koan.Services.*` with docs clarifying   | Preserve naming, add comments | **Rejected.** Naming ambiguity persists. Doesn't solve scalability or organizational issues.                  |
| Rename to `Koan.Service` (singular)          | Concise infrastructure naming | **Rejected.** Still ambiguous - is it "the service framework" or "a service"? Doesn't clarify technical role. |
| **Hierarchical + `Koan.ServiceMesh` naming** | Technical accuracy + hierarchy| **Accepted.** Mirrors Connectors, scales indefinitely, clarifies mesh infrastructure vs service implementations.|

## Implementation Guidelines

### Phase 1: Infrastructure Rename (Core Framework)

1. **Project Structure**:
   ```bash
   cp -r src/Koan.Services src/Koan.ServiceMesh && rm -rf src/Koan.Services
   cp -r src/Koan.Services.Abstractions src/Koan.ServiceMesh.Abstractions && rm -rf src/Koan.Services.Abstractions
   ```

2. **Project Files**: Rename `.csproj` files to match new project names (`Koan.ServiceMesh.csproj`, `Koan.ServiceMesh.Abstractions.csproj`)

3. **Namespace Updates**:
   - Replace `namespace Koan.Service.*` → `namespace Koan.ServiceMesh.*`
   - Update `using Koan.Service.*` → `using Koan.ServiceMesh.*` (infrastructure only)
   - Update `"Koan.Service"` string literals in module names to `"Koan.ServiceMesh"`
   - Update pillar manifest namespace prefixes

4. **Logging Configuration**:
   - Update appsettings.json: `"Koan.Service":` → `"Koan.ServiceMesh":`
   - Service-specific config keys (`Koan:Service:{ServiceName}:*`) remain unchanged

### Phase 2: Service Implementation Relocation

1. **Create Hierarchy**:
   ```bash
   mkdir -p src/Services/Translation
   ```

2. **Move Service Projects**:
   ```bash
   mv src/Koan.Services.Translation src/Services/Translation/Koan.Service.Translation
   mv src/Koan.Services.Translation.Container src/Services/Translation/Koan.Service.Translation.Container
   ```

3. **Update Project References**: Adjust relative paths in `.csproj` files:
   ```xml
   <!-- Before -->
   <ProjectReference Include="..\Koan.Services\Koan.Services.csproj" />

   <!-- After (infrastructure references) -->
   <ProjectReference Include="..\..\..\Koan.ServiceMesh\Koan.ServiceMesh.csproj" />
   <ProjectReference Include="..\..\..\Koan.ServiceMesh.Abstractions\Koan.ServiceMesh.Abstractions.csproj" />
   ```

4. **Using Statements**: Update service implementations to import from ServiceMesh:
   ```csharp
   // Update in Translation service
   using Koan.ServiceMesh.Abstractions;
   using Koan.ServiceMesh.Execution;
   ```

5. **Docker Context Updates**:
   - Dockerfile build paths: `src/Koan.Services.Translation.Container` → `src/Services/Translation/Koan.Service.Translation.Container`
   - Entrypoint DLL names: `Koan.Services.Translation.Container.dll` → `Koan.Service.Translation.Container.dll`

5. **Solution File**: Update project paths in `.sln` (usually handled automatically by IDE or manual edit)

### Phase 3: Validation

1. **Build Verification**:
   ```bash
   dotnet build src/Koan.ServiceMesh/Koan.ServiceMesh.csproj
   dotnet build src/Services/Translation/Koan.Service.Translation/Koan.Service.Translation.csproj
   dotnet build src/Services/Translation/Koan.Service.Translation.Container/Koan.Service.Translation.Container.csproj
   ```

2. **Docker Build**: Verify Dockerfile paths resolve correctly from repository root
3. **Configuration Testing**: Ensure appsettings keys resolve correctly at runtime
4. **Sample Projects**: Update and verify samples that reference service projects

## Consequences

### Positive

- **Consistent Organization**: Services mirror Connectors pattern
- **Infinite Scalability**: Add 50+ services without src root pollution
- **Clear Separation**: Visual distinction between framework (src root) and implementations (src/Services/)
- **Semantic Clarity**: Singular naming aligns with framework conventions
- **Improved Navigation**: Developers intuitively find "translation service" in `src/Services/Translation/`

### Tradeoffs

- **Breaking Change**: All service references require path updates
- **Large Refactor**: Touches ~100+ files across projects, configs, Docker, samples
- **Temporary Disruption**: IDE and build caches may need clearing post-migration
- **Migration Effort**: 2-4 hours for comprehensive update and validation

### Neutral

- **No API Changes**: Service mesh APIs, attributes, execution patterns remain identical
- **Configuration Compatible**: New config keys coexist with fallback logic if needed

## Follow-ups

1. **Future Services**: Apply this pattern for Inbox, Email, Notifications services
2. **Documentation Update**: Reflect new structure in architecture diagrams and service development guides
3. **Template Updates**: Update service scaffolding templates to use new structure
4. **CI/CD Paths**: Verify build pipeline paths reference new locations

## References

- [ARCH-0056 – Koan.Canon Pillar Renaming](./ARCH-0056-koan-canon-pillar-renaming.md) (Similar large-scale rename)
- [DX-0028 – Service Project Naming and Conventions](./DX-0028-service-project-naming-and-conventions.md)
- [ARCH-0049 – Unified Service Metadata and Discovery](./ARCH-0049-unified-service-metadata-and-discovery.md)
- Implementation PR: Migration completed 2025-11-02
