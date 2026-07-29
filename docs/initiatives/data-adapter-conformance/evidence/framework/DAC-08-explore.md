---
type: EVIDENCE
domain: data
title: "DAC-08 exploration — Diagnostics, claims, lifecycle, and performance"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: pre-implementation placement and ergonomics record
---

# DAC-08 exploration — Diagnostics, claims, lifecycle, and performance

**Task:** Make one executable adapter claim set and one frozen source decision visible through pure diagnostics,
runtime facts, health, failures, TestKit applicability, and benchmark evidence without exposing restricted data or
creating a second execution interpretation.

**Application intent:** “Show me exactly what this source will do and why before I run it, then verify it safely
without changing it.”

**Public expression:**

```csharp
var source = Data.Source("LegacyErp");
var description = source.Describe();
var explanation = source.Explain("orders.recent");
var diagnosis = await source.Doctor(ct);
```

`Describe` is a pure projection of the route, policy, capabilities, claims, mappings, and named operations already
compiled for execution. `Explain` is a pure projection of a named operation's effect, binding, bounds, support, and
expected division of work. `Doctor` performs only adapter-declared non-mutating checks. None of the three surfaces
requires credentials, driver objects, provider vocabulary, or diagnostic configuration from the application.

**Guarantee/correction:** Description, explanation, execution, runtime facts, health, public failures, and TestKit
consume the same immutable decision and claim identifiers. Pure diagnostics do not create an integration, acquire a
connection, perform readiness work, reflect over application types, or invoke a provider. Doctor never provisions or
repairs shape. An adapter without a safe probe reports `Unavailable` with a stable correction instead of guessing an
operation. Caller cancellation and Framework timeout remain distinct. Unproved capabilities are absent. Public
surfaces exclude credentials, native payloads, parameters, business values, entity and tenant identifiers, raw
driver messages, and provider runtime objects. Exact native type/code evidence is bounded and restricted, referenced
publicly only by an opaque identifier.

**Complete intent surface:** Reference Data Core and the selected connector; configure a source; optionally declare
maps and named operations; call `Data.Source(name)` and then `Describe`, `Explain`, or `Doctor`. Adapter authors
declare claims on the adapter factory, provide a pure source descriptor where Source Integration semantics differ,
and optionally implement a non-mutating doctor. TestKit derives applicability from that same declaration. Benchmark
fixtures pin provider version and runner, then compare provider-relative cold/warm observations; the Framework does
not publish universal latency thresholds.

**Public concepts:** `DataClaimSet` is the executable declaration and `DataClaimProfiles` is the sole Data
capability-to-profile projection. `DataSourceDescription`, `DataSourceExplanation`, and `DataSourceDiagnosis` are
small redacted projections. `DataSourceIntegrationDescriptor` is the pure factory-owned source description and
`IDataSourceDoctor` is the optional active seam. `DataFailure` exposes a stable taxonomy, correction, retry/commit
facts, and opaque evidence reference. Benchmark observations report elapsed time, allocations, provider dispatches,
and provider work against an identified fixture.

**Docs read:**

- `docs/engineering/index.md` redirects to the active contributor workbooks; relevant as the required entry.
- `docs/architecture/principles.md` requires business-intent APIs, one composition kernel, compile-once execution,
  semantic honesty, fact/health agreement, one current path, and host isolation; directly governing.
- `docs/architecture/data-adapter-development-primer.md` §§1.5, 4–10 and G/H/P rows define diagnostics purity,
  readiness/failure boundaries, provider-relative measurement, lifecycle/resource proof, redaction, and hot paths;
  normative.
- `docs/decisions/DATA-0110-compact-data-adapter-language.md` fixes compact provider-neutral vocabulary; normative.
- `evidence/framework/public-contract.md` and `consumer-contract.cs` require immutable decision identities shared by
  Describe/Explain/receipts/facts/health/errors and forbid public restricted values; normative acceptance fixtures.
- DAC-08 and DAC-09 identify the Framework correction and independent gate.

**Code read:**

- `DataDiagnostics` owns current source configuration, participation, and plan observations, but its dictionaries are
  unbounded and its projection does not carry executable claim identities.
- `DataCompositionFacts` and `DataAdapterHealthContributorBase` are the closest public fact and health projectors.
  Health currently emits exception messages and neither surface consumes one claim owner.
- `DataCaps`/`CapabilitySet` already provide executable capability declarations. `DataConformanceManifest` separately
  owns a duplicate Testing-only capability/profile map, so runtime and certification can drift.
- `DataFailure` has the correct kind, commit, retry, replay, and context axes, but accepts arbitrary public messages
  and arbitrary public facts. `IDataFailureClassifier` already requires exact type/code classification and forbids
  message parsing; no bounded restricted evidence store exists.
- `DataSourceIntegrationService` currently creates a source integration while resolving `Data.Source`, which makes a
  pure description impossible and leaves factory-created integrations without explicit host disposal.
- `DataOperationCatalog` and `IDataMappingPlans` already own frozen host-scoped operation and mapping decisions. They
  are inputs to diagnostics, not concepts to duplicate.
- `DataSourceReadinessCoordinator` already has bounded host-scoped, single-flight reachability, validation, and
  provisioning stages with caller-detached cancellation. Its identities and outcomes must be projected, not
  reimplemented.
- `KoanCompositionScope` and `AppHost` provide the composition-time and current-host mechanics needed to move mutable
  static registration ledgers behind host singleton implementations while preserving terse static facades.
- `ManagedFieldRegistry`, `OperationOverrideRegistry`, `StorageNameParticleRegistry`,
  `StorageWriteContributorRegistry`, `DatabaseRouteRegistry`, and the axis field-owner ledger hold mutable
  process-static composition decisions. `FieldPathResolver` and `StorageNameGenerator` have unbounded static caches.
  These are cross-host and soak risks; type-only structural caches are candidates for weak ownership.

**Reusing:** immutable `DataSourcePlan`, `DataOperationPlan`, `MappingPlan`, `CapabilitySet`, readiness stage records,
`IKoanRuntimeFactRecorder`, `KoanFactDescriptor`, health contributor conventions, `KoanCompositionScope`, `AppHost`,
Framework option binding, and deterministic Testing packet/catalog generation. Their decisions are projected rather
than copied.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| claim declarations, profile projection, source diagnostic records, doctor contracts, restricted evidence vocabulary | `src/Koan.Data.Abstractions/Diagnostics/**` | Runtime, adapters, and TestKit require inert provider-neutral meaning without referencing Data Core or Testing. |
| diagnostic compiler/service, bounded evidence store, lazy integration ownership, fact/health projections | `src/Koan.Data.Core/Diagnostics/**` and existing Source Integration owners | Pure and active paths need one host-owned orchestrator over the already frozen execution plans. |
| bounds, stable codes, and option registration | existing Data Core constants/options/registration owners | Capacities and public corrections must be centralized and configurable. |
| shared claim projection plus scenario/benchmark observations | `src/Koan.Testing/Conformance/**` | Test applicability and provider-relative evidence must consume runtime claims rather than maintain a private truth. |
| diagnostic/privacy/lifecycle/performance oracles | Data Core and Testing suites | Purity, exact identity, two-host isolation, disposal, bounds, redaction, and warm-path work need executable proof. |

**Coalescence:** The source plan remains the route/policy identity owner, the operation catalog remains the named
operation owner, the mapping service remains the mapping owner, and the readiness coordinator remains the lifecycle
owner. Diagnostics is only a redacted projection over those values. Capability-to-profile projection moves from
Testing to Abstractions so runtime and TestKit share it. Factory claims extend the existing adapter factory seam with
a default empty declaration instead of adding a competing registry. Source resolution becomes lazy so pure
diagnostics can use a factory's inert source descriptor without activation. Existing static application facades may
remain as syntax, but mutable contents move to current-host services; no second compatibility ledger is introduced.

**Ergonomics:** `Describe`, `Explain`, and `Doctor` are the complete vocabulary: inspect the decision, explain one
declared action, verify safe operation. Results use Framework concepts—source, lifecycle, access, claim, operation,
mapping, effect, bounds, support, status, correction—not tables, collections, SQL, pipelines, or driver types. Stable
identifiers connect an operator observation to a verifier without disclosing the physical value that produced it.
Adapters implement declarations and receipts, not a parallel diagnostics object model.

**Constraints satisfied:**

- No connector behavior is written by DAC-08; default seams preserve compilation until provider replacement cards.
- Describe and Explain are pure and cannot activate Source Integration.
- Doctor is explicitly non-mutating and cannot call provisioning.
- Claims are executable, absent by default, and flow through one projection.
- Public failure and diagnostic DTOs cannot contain arbitrary fact bags or native messages.
- Host/client/integration/pool/cache ownership is explicit, bounded, isolated, and disposable.
- Performance results are provider-relative observations with pinned fixture identity, not universal promises.
- Every repeated Framework mechanic has one owner; provider syntax/error recognition remains adapter-owned.

**Risks:** Moving mature static registration ledgers behind host ownership touches broad composition tests and may
surface callers that register outside `AddKoan`. Compatibility must preserve call shape without retaining a
process-global fallback. Some pure type metadata caches may be harmless and faster than rebuilding; replace them with
weak/bounded ownership only where key lifetime or user-controlled cardinality makes growth observable. No provider
may be credited for fault, restart, durability, isolation, or performance profiles until its later native fixture
returns the exact evidence required by the shared scenario module.
