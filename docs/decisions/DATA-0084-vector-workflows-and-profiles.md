# DATA-0084: Vector Workflows and Profiles

**Status:** Proposed
**Date:** 2025-10-20
**Scope:** Koan.Data.Vector, Koan.Data.Core, Koan.Core.Options, Koan.TestPipeline

---

## Context

Retrieval-augmented generation features across Koan samples (S5.Recs, S6.SnapVault) rely on the `Vector<TEntity>` facade and provider adapters. While functional, real-world integrations now demand:

- Provider-agnostic flows that coordinate document storage and vector persistence without bespoke client code.
- Declarative retrieval policies (hybrid weights, filters, metadata) that evolve without recompilation.
- First-class observability (quality metrics, run logs) tied to retrieval intent instead of raw provider calls.
- Progressive ergonomics: zero-config defaults for simple scenarios and fluent APIs for advanced pipelines like S7.Meridian.

Current gaps:

- Application teams write direct adapter code when they need metadata, hybrid tuning, or bulk upserts.
- Configuration for alpha/topK lives inside services, making it hard to promote environment overrides.
- Tests stub the vector adapter manually; no reusable plan builder mirrors production behaviour.

## Decision

Introduce **Vector Workflows** and **Vector Profiles** as opinionated orchestration surfaces on top of existing vector adapters.

### 1. VectorWorkflow<T>

- New facade in `Koan.Data.Vector` that composes document persistence + vector operations.
- Aligns with Koan semantics (`Save`, `Delete`, `Query`, `IsAvailable`).
- Supports fluent chaining for metadata, quality gates, fallbacks.
- Automatically emits telemetry via `RunLog` and `Koan.Data.Vector` diagnostics.

```csharp
var workflow = VectorWorkflow<Passage>.For("meridian:evidence");
await workflow.Save(passage, embedding,
    metadata: BuildMetadata(passage),
    ct: ct);

var hits = await workflow.Query(
    vector: queryEmbedding,
    text: query,     // Enables hybrid retrieval
    topK: 12,
    ct: ct);
```

### 2. Vector Profiles

- Strongly-typed descriptions of retrieval intent (default topK, alpha, hybrid toggles, metadata enrichers).
- Declarative registration via `VectorProfiles.Register(...)` or configuration (`Koan:Data:Vector:Profiles`).
- Profile hierarchy mirrors Koan patterns: Entity attribute → Named profile → Global defaults.

```csharp
VectorProfiles.Register(builder => builder
    .For<Passage>("meridian:evidence")
        .TopK(12)
        .Alpha(0.55)
        .EmitMetrics()
        .WithMetadata(p => p["pipeline"] = "narrative"));
```

### 3. Options & Discovery

- `AddKoan()` now wires `IVectorWorkflowRegistry`, `VectorProfileOptions`, and default profile (`TopK=10`, `Alpha=0.5`).
- Profiles can be overridden per environment using standard Koan options binding.
- `VectorWorkflow<T>.For(profile)` resolves lazily using DI; missing profiles fall back to defaults with a warning.

### 4. Observability & Quality

- Workflows emit `VectorRunLog` entries (provider, profile, alpha, latency, TopK, match count).
- Profiles opt-in to quality metric projection feeding `PipelineQualityMetrics`.
- Automatic `IsAvailable` checks ensure degrade-to-DB patterns remain idiomatic.

### 5. Testing Ergonomics

- `Koan.TestPipeline` gains `VectorTestPlan` for profile-aware specs:

```csharp
await VectorTestPlan.For<Passage>(profile: "meridian:evidence")
    .WithVectors(seed)
    .Query(vector, text: "annual revenue")
    .Assert(matches => matches.Should().ContainSingle());
```

## Alternatives Considered

1. **Keep `Vector<TEntity>` only.**
   - Reject: pushes complexity into every app, no declarative knobs, poor observability.
2. **Extend `Vector<TEntity>` with more overloads.**
   - Reject: API explosion, no room for fluent ergonomics, difficult to bind options.
3. **Rely on provider-specific SDKs.**
   - Reject: breaks Koan neutrality, duplicates logic, increases lock-in risk.

## Impact

- **Koan.Data.Vector**: Adds workflow facade, profile registry, options binding, and telemetry.
- **Koan.Data.Core**: `Data<TEntity, TKey>.SaveWithVector` delegates to workflows when available.
- **Samples**: S7.Meridian, S5.Recs, S6.SnapVault adopt `VectorWorkflow<T>.Save/Query`.
- **Docs**: Updated guidance promotes profile-first ergonomics and configuration patterns.
- **Tests**: New vector test plan covers profile discovery + hybrid semantics.

## Rollout Plan

1. Ship workflow + profile primitives behind feature flag (`Koan:Data:Vector:EnableWorkflows`, default `true`).
2. Migrate samples incrementally (Meridian first) and update docs.
3. Expose telemetry in Koan Admin dashboards (profile view, availability checks).
4. Collect provider feedback (Weaviate, Pinecone, Azure AI Search) to ensure metadata compatibility.

## Risks & Mitigations

- **Provider capability gaps:** Some adapters may lack hybrid options. Mitigate via capability flags and graceful fallback to vector-only search.
- **Configuration sprawl:** Profiles centralize advanced knobs; defaults stay minimal to preserve zero-config experience.
- **Breaking changes:** Existing `Vector<TEntity>` API remains; workflows wrap rather than replace.

## Edge Cases

- When no vector provider is registered, workflows throw clear guidance (`VectorWorkflow<Passage>` unavailable) and `IsAvailable` returns false.
- Profiles can require metadata; workflow enforces serialization guard to prevent runtime provider errors.
- Bulk reindex uses `VectorWorkflow<T>.SaveManyAsync` (part of future work) to respect batching semantics.

## References

- DATA-0054 Vector search capability and contracts
- DATA-0060 Vector module split and separation
- DATA-0078 Vector export capabilities
- S5.Recs & S6.SnapVault samples (baseline vector usage)
- S7.Meridian proposal (narrative generator with profile-driven retrieval)
