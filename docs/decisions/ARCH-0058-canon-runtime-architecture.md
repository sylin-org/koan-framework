# ARCH-0058: Canon Runtime Architecture

## Status
**Accepted** (2025-Q4)

## Context

### Legacy Canon Problems

The legacy Koan.Canon implementation exhibited several architectural flaws that impeded maintainability and developer experience:

1. **Transport Coupling**: Core canonization logic depended on Koan.Messaging and queue naming conventions, preventing inline or synchronous usage scenarios.

2. **Monolithic Orchestration**: `CanonOrchestratorBase` mixed intake, validation, aggregation, policy, projection, and messaging acknowledgments in a 800+ line background service with no clear separation of concerns.

3. **Reflection Brittleness**: Heavy use of reflection-based registries (`CanonRegistry`, `CanonMessagingInitializer`) created runtime discovery issues and hindered testability.

4. **Developer Experience Friction**: Teams had to craft transport envelopes instead of simply calling `entity.Canonize(origin)`, adding cognitive overhead for basic operations.

5. **Operational Gaps**: Replay, projection rebuild, and healing queue features were placeholders or missing entirely.

6. **Storage Proliferation**: Multiple storage artifacts (`KeyIndex<T>`, `IdentityLink<T>`, various tracking tables) created complexity without clear separation of concerns.

### Greenfield Opportunity

Given the framework's greenfield stance, a clean-slate rebuild was feasible without backward compatibility constraints. The redesign needed to:

- Preserve `CanonEntity<T>` as the semantic opt-in pattern
- Eliminate transport dependencies from core canonization logic
- Provide a deterministic, composable pipeline with precise transformation hooks
- Support both inline and distributed processing models
- Deliver first-class operational tooling (replay, rebuild, diagnostics)
- Minimize storage footprint while maintaining capability

## Decision

### Architecture Overview

Canon runtime consolidates into **two packages** with clear separation:

1. **`Koan.Canon.Domain`** - Domain model, runtime orchestration, pipeline contracts
2. **`Koan.Canon.Web`** - HTTP controllers, discovery endpoints, admin operations

This replaces the originally proposed 5-package split (Domain, Metadata, Engine, Runtime, Transport) which was deemed premature fragmentation.

### Domain Model Decisions

#### Unified State Projection

**Decision**: Consolidate lifecycle, readiness, and consumer signals into a single immutable `CanonState` record.

**Rationale**:
- Reduces cognitive load (one state object vs. three separate properties)
- Enables atomic state transitions via `ApplyState(transform)`
- Facilitates state merging in distributed scenarios
- Signals provide extensible key/value pairs for downstream hints

**Implementation**:
```csharp
public sealed record CanonState
{
    public CanonLifecycle Lifecycle { get; init; }      // Active, Superseded, Archived, Withdrawn
    public CanonReadiness Readiness { get; init; }      // Complete, PendingEnrichment, RequiresManualReview, etc.
    public IReadOnlyDictionary<string, string?> Signals { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
```

**Alternatives Considered**:
- Separate properties on `CanonEntity<T>` → Rejected due to synchronization complexity
- Mutable state object → Rejected to enforce immutable pipeline transformations

#### Simplified Storage Model

**Decision**: Use three storage primitives instead of proliferating entity types:

1. **`CanonEntity<T>`** - The canonical record with embedded `CanonMetadata`
2. **`CanonIndex`** - Shared aggregation/external ID lookup across all entity types
3. **`CanonStage<T>`** - Optional staging for deferred processing

**Rationale**:
- Eliminates per-model index tables (`KeyIndex<T>`, `IdentityLink<T>`)
- Centralizes identity resolution queries (single index scan vs. multiple joins)
- Metadata embedded in entity avoids separate metadata table and foreign key overhead
- Staging is opt-in (most pipelines run inline)

**Alternatives Considered**:
- Separate metadata table → Rejected due to join overhead and orphan risk
- Per-model index tables → Rejected due to maintenance burden and query complexity

#### CanonStatus → CanonLifecycle Rename

**Decision**: Replace `CanonStatus` enum with `CanonLifecycle` to clarify semantic intent.

**Rationale**:
- "Status" is overloaded (could mean processing status, health, etc.)
- "Lifecycle" clearly indicates progression through entity lifetime
- Maintains backward compatibility via `[Obsolete]` alias

### Pipeline Design Decisions

#### Unified Contributor Pattern

**Decision**: Use single `ICanonPipelineContributor<TModel>` interface instead of phase-specific interfaces (`IIntakeStep`, `IValidationStep`, etc.).

**Rationale**:
- **Simplicity**: One interface to learn vs. six phase-specific contracts
- **Composability**: Contributors can evolve across phases without interface changes
- **Flexibility**: Phase assignment via property (`Phase { get; }`) allows dynamic registration
- **Consistency**: Matches Koan's minimal scaffolding philosophy

**Original Proposal** specified granular interfaces:
```csharp
IIntakeStep      → BeforeIntake, AfterIntake
IValidationStep  → Validate, OnValid, OnInvalid
IAggregationStep → OnSelectAggregationKey, OnResolveCanonicalId, OnConflict
// etc.
```

**Implemented Design**:
```csharp
public interface ICanonPipelineContributor<TModel>
{
    CanonPipelinePhase Phase { get; }
    ValueTask<CanonizationEvent?> ExecuteAsync(CanonPipelineContext<TModel> context, CancellationToken ct);
}
```

**Trade-off**: Less compile-time safety on hook names, but significantly lower cognitive overhead and easier testing.

#### Six-Phase Pipeline

**Decision**: Standardize on six deterministic phases executed in order:

1. **Intake** - Origin assignment, metadata initialization
2. **Validation** - Data quality, schema checks, business rules
3. **Aggregation** - Identity resolution, external ID mapping, conflict detection
4. **Policy** - Attribute selection, versioning, compliance
5. **Projection** - Canonical view generation, lineage tracking
6. **Distribution** - Downstream notification, event publishing

**Rationale**:
- Matches enterprise MDM (Master Data Management) best practices
- Clear separation of concerns (validation ≠ aggregation ≠ policy)
- Enables granular observability (per-phase metrics and tracing)
- Deterministic ordering prevents contributor ambiguity

**Alternatives Considered**:
- Dynamic phase registration → Rejected due to unpredictable execution order
- Combined validation + policy phase → Rejected to maintain separation of data quality vs. governance

### Runtime API Decisions

#### No "Async" Suffix

**Decision**: Methods return `Task<T>` or `IAsyncEnumerable<T>` but use clean names (`Canonize`, `RebuildViews`, `Replay`).

**Rationale**:
- Aligns with Koan framework conventions
- Modern C# guidance deprecates `Async` suffix for async methods
- Return type (`Task`) makes async nature obvious

**Consistency Check**: Framework-wide pattern (e.g., `Entity<T>.Save()` not `SaveAsync()`)

#### Extension Methods for DX

**Decision**: Provide `entity.Canonize(services)` extension methods while retaining `runtime.Canonize(entity)` as primary API.

**Rationale**:
- Enables entity-centric workflows: `var result = await customer.Canonize(services)`
- Mirrors `entity.Save()` pattern developers already know
- Extensions delegate to `ICanonRuntime` (no logic duplication)

**Implementation**:
```csharp
public static Task<CanonizationResult<T>> Canonize<T>(this T entity, IServiceProvider services, CanonizationOptions? options = null)
{
    var runtime = services.GetRequiredService<ICanonRuntime>();
    return runtime.Canonize(entity, options);
}
```

### Persistence Strategy Decisions

#### Default Delegation to Entity Statics

**Decision**: `DefaultCanonPersistence` delegates to `entity.Save()` and `stage.Save()` rather than implementing custom DAL.

**Rationale**:
- **Provider Transparency**: Leverages Koan's existing multi-provider abstractions
- **Zero Configuration**: Works with SQL, MongoDB, JSON stores out-of-box
- **Testability**: Mock `ICanonPersistence` for unit tests, use real providers for integration tests
- **Consistency**: Same entity patterns everywhere (no canon-specific data access)

**Custom Persistence**: Teams can implement `ICanonPersistence` for event sourcing, CQRS, or domain-specific requirements.

**Alternatives Considered**:
- Custom repository layer → Rejected to avoid breaking provider transparency
- Direct DbContext injection → Rejected to maintain entity-first patterns

### Web Integration Decisions

#### Generic Controllers with Auto-Discovery

**Decision**: Generate `CanonEntitiesController<T>` per canonical entity via assembly scanning + `GenericControllers.AddGenericController`.

**Rationale**:
- **Zero Configuration**: Add `Koan.Canon.Web` reference, controllers appear automatically
- **Consistent Routes**: `/api/canon/{kebab-case-slug}` pattern (e.g., `/api/canon/customer-canon`)
- **Inheritance**: Controllers inherit `EntityController<T>` for standard CRUD, override writes to invoke `ICanonRuntime`

**Discovery Endpoint**: `/api/canon/models` returns catalog of registered entities with pipeline metadata for client tooling.

**Alternatives Considered**:
- Manual controller registration → Rejected due to maintenance burden
- Convention-based routing (attribute-free) → Rejected for explicitness

#### Request Options from Headers/Query

**Decision**: Parse canonization options from HTTP headers (`X-Canon-Origin`) and query parameters (`?tag.foo=bar`).

**Rationale**:
- **Flexibility**: Clients choose header or query based on preference
- **Discoverability**: Query params visible in Swagger/OpenAPI
- **Standards Compliance**: Custom headers prefixed with `X-Canon-`

**Parsing Rules**:
- `X-Canon-Origin` or `?origin=` → `CanonizationOptions.Origin`
- `?forceRebuild=true` → `CanonizationOptions.ForceRebuild`
- `?tag.key=value` → `CanonizationOptions.Tags["key"] = "value"`
- Later values override earlier (query overrides headers)

## Consequences

### Positive

1. **Minimal Cognitive Load**: Two packages, one contributor interface, unified state model
2. **Provider Transparency Preserved**: Delegates to `Entity<T>.Save()`, works across all Koan data providers
3. **Testability**: Mock `ICanonPersistence`, inject fake contributors, assert state transitions
4. **Operational Maturity**: Replay, rebuild, observer APIs are first-class (not placeholders)
5. **Developer Experience**: `entity.Canonize(services)` reads naturally, minimal boilerplate
6. **Extensibility**: Contribute to any phase without framework changes via `ICanonPipelineContributor<T>`

### Negative

1. **Learning Curve**: Metadata model is rich (external IDs, lineage, policies, tags) - requires time to master
2. **Index Management**: Shared `CanonIndex` table requires understanding aggregation key design
3. **State Model Complexity**: `CanonState` combines lifecycle + readiness + signals - developers must learn when to use each dimension
4. **No Granular Hooks**: Unified contributor pattern sacrifices compile-time safety of phase-specific contracts

### Neutral (Trade-offs)

1. **Package Consolidation**: Two packages simpler to maintain than five, but less separation of concerns
2. **Staging Opt-in**: Most pipelines run inline (simpler), but distributed scenarios require explicit configuration
3. **Synchronous by Default**: Inline processing is default, async/distributed requires `CanonStageBehavior.StageOnly`

### Edge Cases

#### Concurrency Violations
**Problem**: Multiple workers process same entity simultaneously.
**Mitigation**: Stage transitions check `UpdatedAt` for optimistic concurrency; contributors must be idempotent.

#### Provider Capability Gaps
**Problem**: Data provider lacks streaming support.
**Mitigation**: Runtime detects via `QueryCaps`, falls back to paging, logs degraded performance warning.

#### Partial Stage Failures
**Problem**: Contributor mutates state then throws exception.
**Mitigation**: Use compensating actions or park entity with `CanonizationOutcome.Parked` for manual remediation.

#### Schema Evolution
**Problem**: Metadata schema changes, old entities incompatible.
**Mitigation**: Version metadata via tags (`schema:version`), implement upgrade contributors that detect old schema and migrate in-place.

#### Large Payloads
**Problem**: Entity exceeds memory limits for in-process handling.
**Mitigation**: Intake contributor streams to blob storage, injects pointer via metadata tag (`blob:uri`), downstream contributors load as needed.

#### State Merge Conflicts
**Problem**: Concurrent updates from different sources create conflicting metadata.
**Mitigation**: `CanonState.Merge(incoming, preferIncoming)` applies deterministic rules; readiness/lifecycle transitions are monotonic.

## Alternatives Considered

### Five-Package Architecture (Rejected)

**Proposal**: Separate packages for Domain, Metadata, Engine, Runtime, Transport.

**Rejection Rationale**:
- Premature fragmentation for current scope
- Increased dependency management overhead
- No clear versioning benefit (packages evolve together)
- Can split later if needed (YAGNI principle)

### Phase-Specific Contributor Interfaces (Rejected)

**Proposal**: `IIntakeStep`, `IValidationStep`, etc. with granular methods.

**Rejection Rationale**:
- Six interfaces to learn vs. one
- Contributors often span multiple hooks (validation + policy)
- Breaking changes when adding new hooks
- Unified pattern more composable and flexible

### Separate Metadata Table (Rejected)

**Proposal**: Store `CanonMetadata` in separate table with foreign key to canonical entity.

**Rejection Rationale**:
- Join overhead on every query
- Orphan risk (entity deleted but metadata remains)
- Breaks entity-first pattern (metadata is integral to canonical record)

### Transport-First Design (Rejected)

**Proposal**: Keep canonization coupled to messaging, expose inline API as adapter.

**Rejection Rationale**:
- Violates separation of concerns
- Prevents unit testing without messaging infrastructure
- Limits deployment flexibility (can't use in serverless functions, CLI tools)
- Higher cognitive load (must understand queues to canonize inline)

## Migration Impact

### Breaking Changes from Legacy

1. **`CanonStatus` → `CanonLifecycle`**: Enum renamed (backward compatible via `[Obsolete]` alias)
2. **Metadata Structure**: Legacy separate tables consolidated into `CanonMetadata` embedded in entity
3. **Pipeline Hooks**: Replace orchestrator overrides with `ICanonPipelineContributor<T>` implementations
4. **Persistence**: Replace custom DAL with `ICanonPersistence` or use `DefaultCanonPersistence`

### Migration Path

See [canon-runtime-migration.md](../architecture/canon-runtime-migration.md) for milestone-based cutover plan.

**Key Milestones**:
- **M1**: Runtime core implementation (✅ Complete)
- **M2**: Web + API surfaces (✅ Complete)
- **M3**: Adapter modernization (In Progress)
- **M4**: Sample migration (Pending)
- **M5**: Operational cutover (Pending)

## Implementation Notes

### Package Layout

```
src/Koan.Canon.Domain/
├── Model/              # CanonEntity, CanonState, CanonIndex, CanonStage, CanonValueObject
├── Metadata/           # CanonMetadata, CanonLineage, CanonExternalId, CanonSourceAttribution
├── Runtime/            # ICanonRuntime, contributors, pipeline, persistence, builders
└── README.md           # [DELETED - See SPEC-canon-runtime.md]

src/Koan.Canon.Web/
├── Catalog/            # Model discovery and routing
├── Controllers/        # Generic entity controllers, discovery, admin
├── Infrastructure/     # Constants, helpers
├── Initialization/     # Auto-registrar
└── README.md           # [DELETED - See SPEC-canon-runtime.md]

docs/specifications/
└── SPEC-canon-runtime.md  # Single source of truth

docs/architecture/
└── canon-runtime-migration.md  # Milestone tracking (living document)

docs/decisions/
└── ARCH-0058-canon-runtime-architecture.md  # This ADR
```

### Testing Strategy

**Unit Tests** (`tests/Koan.Canon.Domain.Tests/`):
- Domain model behavior (state transitions, metadata operations)
- Pipeline execution with mock contributors
- Persistence delegation verification

**Integration Tests** (`tests/Koan.Canon.Web.Tests/`):
- Controller request/response flows
- Option parsing from headers/query
- Discovery endpoint accuracy

**Sample Validation** (`samples/S8.Canon/`):
- End-to-end canonization workflows
- Multi-stage pipeline demonstrations
- Real provider integration (MongoDB, SQL)

## References

### Superseded Documents
- `PROP-canon-overhaul-2.md` - Original proposal (historical reference)
- All `README.md` and `TECHNICAL.md` files in Canon packages (deleted, superseded by spec)

### Active Documentation
- [SPEC-canon-runtime.md](../specifications/SPEC-canon-runtime.md) - Authoritative specification
- [canon-runtime-migration.md](../architecture/canon-runtime-migration.md) - Migration tracking

### Related ADRs
- [DATA-0061](DATA-0061-data-access-pagination-and-streaming.md) - Streaming patterns
- [ARCH-0040](ARCH-0040-config-and-constants-naming.md) - Naming conventions

### Code Locations
- Implementation: `src/Koan.Canon.Domain/`, `src/Koan.Canon.Web/`
- Tests: `tests/Koan.Canon.Domain.Tests/`, `tests/Koan.Canon.Web.Tests/`
- Sample: `samples/S8.Canon/`

---

**Decision Date**: 2025-Q4
**Decision Makers**: Enterprise Architecture Team
**Review Cycle**: Annually or on breaking change proposals
