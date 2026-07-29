---
type: REFERENCE
domain: data
title: "DAC-50 Vector Conformance Control Plane Exploration"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: pre-implementation ownership, placement, and ergonomics record
---

# DAC-50 exploration

**Task:** Project the ratified Vector contract into the existing Data conformance protocol and strict runner without
changing provider production behavior or creating a second catalog.

**Application intent:** Declare one vector space and then save, retrieve, search, synchronize, and clear through one
portable Koan surface whose claims mean the same thing on every provider.

**Public expression:**

```csharp
koan.Data.Source("Semantic").Vector<Document>(space => space
    .Name("documents")
    .Dimensions(1536)
    .Metric(VectorMetric.Cosine)
    .Visibility(VectorVisibility.Session));

var result = await Vector<Document>.Search(
    embedding,
    query => query.Top(12).Where(filter).AtLeast(.82),
    ct);
```

The application references the Vector package and one provider, calls `AddKoan()`, and supplies provider configuration
or an explicitly selected source. No conformance package or provider-native object appears in the application path.

**Guarantee/correction:** The expression binds one immutable source/space plan; regular results expose finite
higher-is-closer similarity plus honest exact/approximate execution; Session writes are visible after await. Unsupported
query clauses, unsafe lifecycle actions, incompatible shape, and unproved translation fail before provider I/O with a
corrective typed failure. Conformance skips, missing infrastructure, and missing LIVE evidence remain non-green.

**Complete intent surface:** Reference Vector plus one adapter, call `AddKoan()`, declare the source/space once, and use
`Vector<TEntity>` terminals. Eventual visibility, filters, hybrid search, named spaces, continuation, bulk, atomic batch,
export, managed lifecycle, isolation, and entity/vector coordination are explicit earned branches only.

**Public concepts:** Source/space expresses routing and immutable shape; Metric defines normalization; Visibility chooses
Session or Eventual; the seven query clauses express independent search decisions; Similarity and Execution state the
portable result guarantee; Clear deletes while Sync establishes visibility. Every concept maps to a ratified business
decision or corrective guarantee.

**Docs read:**

- `docs/engineering/index.md` establishes repository engineering and documentation conventions; applicable globally.
- `docs/architecture/principles.md` requires provider transparency, explicit boundaries, and explainable behavior.
- `docs/architecture/data-adapter-development-primer.md` is the sole 105-cell/39-profile semantic authority after DAC-49.
- `docs/decisions/DATA-0110-compact-data-adapter-language.md` freezes the compact provider-neutral grammar.
- `work-items/DAC-49` and `work-items/DAC-50` establish ratification, allowed paths, and the no-provider-change boundary.

**Code read:**

- `Vector.cs` is the current facade; its positional query, `Score`, dictionary get-many, `TotalKind`, and destructive
  `Flush` are evidence of later contract work, not authority for this tooling card.
- `IVectorSearchRepository.cs`, `VectorQueryOptions.cs`, `VectorQueryResult.cs`, and `VectorCaps.cs` define current
  provider contracts and capability facts; DAC-50 observes them without changing public semantics.
- `VectorAodbConformanceSpecsBase.cs` is the existing exact-row Forge boundary and the correct home for explicit
  provider proof seams.
- `DataConformanceCatalog`, `DataConformanceManifest`, `DataConformancePacket`, and
  `DataAdapterConformanceSpecs` already own generated truth, claim selection, evidence, verdicts, and executable cells.
- `forge-verify.ps1` is the sole discovery and strict process boundary; it already treats skips and missing packets as
  non-green.

**Reusing:** The primer parser, embedded catalog, manifest builder, packet compiler/validator, strict exit taxonomy,
exact AODB row keys, runtime `CapabilitySet`, existing Vector capabilities, and current adapter fixtures already exist.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| 12 Vector profile constants and Vector capability projection | `src/Koan.Testing/Conformance/` | extend the one claim owner without a Vector-owned semantic catalog |
| explicit classification for the legacy streaming-result token | `DataConformanceManifest` | the ratified regular result is buffered; an incompatible advertised token must fail structurally |
| V-01–V-24 provider proof seams | `VectorAodbConformanceSpecsBase.cs` | one existing inherited fixture exposes every ratified cell; unimplemented seams skip loudly and remain non-green |
| 105-cell/39-profile parser cardinality and V row support | `scripts/forge-verify.ps1` | keep the one repository runner aligned with the primer |
| Vector protocol mutation/coverage tests | `Koan.Testing.Tests` | prove mapping, fingerprint invalidation, false claims, and unavailable evidence behavior |
| regenerated embedded projection | `src/Koan.Testing/Conformance/data-conformance-catalog.json` | deterministic build artifact from the primer, never a second authority |

**Coalescence:** Closest pattern: the DAC-03 Data protocol plus `VectorAodbConformanceSpecsBase`. The semantic owner is
Framework conformance; consumers are adapter certification projects; state is immutable test metadata; no application
hot path is involved. Keep the packet/verdict machinery, absorb Vector profiles and capability projection into it,
rebuild the Vector AODB boundary around exact V rows, and delete hard-coded 81-cell assumptions. Do not add a Vector
catalog, a second runner, or a runtime conformance hierarchy. `Koan.Testing` is the one wider correct owner; Core is too
wide because this is executable test law, while a provider or TestKit-local manifest is too narrow and would duplicate
claim semantics.

**Ergonomics:** Adapter certification authors declare one manifest and inherit one suite. Profile constants are
IntelliSense-discoverable. Runtime capability projection is explicit through `FromVector`; incompatible legacy claims
fail with a correction. Ordinary applications see none of this machinery.

**Constraints satisfied:**

- No HTTP surface or inline endpoint is involved.
- No application data access is added; the later runtime implementation remains Entity/Vector-first.
- Stable identifiers stay in the generated catalog or centralized conformance constants.
- No large-data operation is introduced; V-19 requires provider-bounded export evidence.
- README/TECHNICAL and initiative records change with protocol behavior.
- No provider production code changes in DAC-50.

**Risks:** Existing adapter capability declarations over-claim some ratified semantics, notably InMemory streaming
results. DAC-50 must report these as RED/DEFER without correcting providers. The public runtime still contradicts parts
of the ratified annex; those changes belong to the shared Framework cards after the runner is stable.
