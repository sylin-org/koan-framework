---
type: EVIDENCE
domain: data
title: "DAC-04 exploration — source policy, readiness, and failures"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: pre-implementation placement and guardrail record
---

# DAC-04 exploration — source policy, readiness, and failures

**Task:** Compile source lifecycle and access into one immutable host plan, enforce it before every observable data effect, separate readiness from provisioning, and establish a stable failure-classification seam without changing provider behavior.

**Application intent:** “Use this named source as an external read-only system, and guarantee that no Koan path can mutate its data or physical shape.”

**Public expression:** Reference the selected adapter, call the ordinary `AddKoan()` composition path, configure the named source once, then use normal Entity or Direct reads:

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "LegacyErp": {
          "Adapter": "sqlserver",
          "ConnectionString": "<provider-enforced read-only route>",
          "StorageLifecycle": "External",
          "Access": "ReadOnly"
        }
      }
    }
  }
}
```

```csharp
using (EntityContext.Use(source: "LegacyErp"))
{
    var customer = await Customer.Get(id, ct);
}
```

The application must still supply a compatible provider package, an existing target, valid credentials, and—because the framework guard is not a security boundary—provider-enforced read authority. Direct reads inherit the same named-source ceiling; a literal connection override never widens it.

**Guarantee/correction:** `Managed + ReadWrite` remains the default. `ReadOnly` rejects data writes and storage administration; `External` rejects storage creation, alteration, repair, and removal while still permitting mapped data writes when access is `ReadWrite`. A forbidden or unclassified effect throws a stable policy exception before lifecycle callbacks, readiness, resource creation, cache/provider work, or I/O, naming the source, attempted effect, current ceiling, and correction without exposing credentials.

**Complete intent surface:** Beyond adapter reference, ordinary Koan composition, the two source settings, provider-enforced credentials, and existing storage for an external source, no additional user action is required. Entity, batch, instruction, Direct, transaction, inspection, named-operation, and provider-extension paths must consume the same ceiling; this slice implements the currently available Entity, batch, instruction, and Direct chokepoints and records later-surface proof against the same plan.

**Public concepts:**

- `StorageLifecycle` expresses whether Koan owns physical-shape mutation (`Managed`) or must preserve an existing shape (`External`).
- `DataSourceAccess` expresses the independent data-mutation decision (`ReadWrite` or `ReadOnly`).
- `DataOperationEffect` carries proven operation intent (`Read`, `Write`, `SchemaOrAdmin`, or fail-closed `Unknown`) without SQL/message heuristics.
- `DataSourcePlan` is the immutable resolved source ceiling shared by execution and diagnostics; applications configure it but do not construct it on the common path.
- `DataSourcePolicyException` is the corrective, redacted fail-fast result of violating that ceiling.
- The failure kind, commit outcome, retry disposition, replay disposition, and classifier seam exist because adapters must translate provider facts while Data retains application-facing semantics.

**Docs read:**

- `README.md` — establishes Entity-first application language and absence of repository ceremony; directly relevant to keeping source policy declarative.
- `docs/engineering/index.md` — points contributors to the current engineering owners; relevant as a superseded compatibility pointer only.
- `docs/architecture/principles.md` — establishes one owner per semantic decision, immutable composition, thin hot paths, and corrective failure; directly controlling.
- `docs/toc.yml` — establishes the current documentation curriculum; no new public guide node is required for this foundation slice.
- `docs/architecture/data-adapter-development-primer.md` — ratifies the four source-policy cells, monotonic narrowing, readiness stages, and failure/replay matrix; normative for this work.
- `docs/initiatives/data-adapter-conformance/work-items/DAC-04-source-policy-routing-readiness-failures.md` — constrains ownership and production-write scope; directly controlling.

**Code read:**

- `src/Koan.Data.Core/DataSourceRegistry.cs` — currently discovers mutable string settings and owns the named-source lifetime; relevant and the correct plan compiler/cache owner.
- `src/Koan.Data.Core/DataService.cs` — elects the provider and constructs the one unavoidable Entity facade; relevant insertion point for the resolved plan.
- `src/Koan.Data.Core/RepositoryFacade.cs` — currently gates cancellation, segmentation, guards, and readiness but has no effect/policy ceiling; relevant hot-path chokepoint.
- `src/Koan.Data.Core/Direct/DirectSession.cs` and `DirectTransaction.cs` — open provider resources and accept raw execution paths independently of Entity; relevant alternate path that must not elevate policy.
- `src/Koan.Data.Core/Adapters/DataAdapterReadinessExtensions.cs` and `Document/DocumentStore.cs` — currently classify missing shape from message text, provision, and replay a business operation; relevant superseded path to delete.
- `src/Koan.Core/Infrastructure/Singleflight.cs` — process-static, caller-token-owned in-flight coalescing; similar mechanics but wrong source-policy owner and host lifetime.
- `src/Koan.Core/Adapters/ReadinessStateManager.cs` and `IAdapterReadiness.cs` — provider participation/readiness state, not target-shape provisioning; retained as a separate concern.
- `src/Koan.Data.Core/Document/OnceGate.cs` — document-instance one-time schema gate whose failure is not cached; useful lesson, but too narrow and caller-cancellation-coupled for shared source stages.
- `src/Koan.Data.Abstractions/Instructions/Instruction.cs`, `DataInstructions.cs`, and `RelationalInstructions.cs` — stable instruction identities already exist, but instruction effect does not; relevant exact classification boundary.

**Reusing:**

- `DataSourceRegistry` named-source discovery and case-insensitive lookup.
- `AdapterResolver` election and `AdapterConnectionResolver` normalized connection precedence.
- `RepositoryFacade` as the Entity/batch semantic chokepoint.
- Existing exact instruction identifiers; effect is mapped by identity, never by prefix or payload text.
- `Task.WaitAsync` for caller cancellation that detaches from host-owned shared work.
- Existing `IAdapterReadiness` participation state remains provider reachability/health rather than becoming provisioning authority.

Explicit searches found no existing `StorageLifecycle`, access-policy, operation-effect, stable data-failure/outcome, retry-disposition, or replay-disposition contract. `ConfigurationConstants`, `DirectOptions`, instruction constants, and the source registry already exist. The new source-policy values are typed decisions rather than tunable options, so no new `*Options` type is appropriate.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| `StorageLifecycle`, `DataSourceAccess`, `DataOperationEffect` | `src/Koan.Data.Abstractions/Sources/` | Provider-neutral public policy vocabulary belongs to Data contracts. |
| `DataSourcePlan`, `DataSourcePolicyException` | `src/Koan.Data.Abstractions/Sources/` | All execution surfaces and adapters may consume the same immutable, redacted ceiling. |
| Failure/outcome/retry/replay records, enums, and `IDataFailureClassifier` | `src/Koan.Data.Abstractions/Failures/` | Data owns stable application-facing semantics; adapters own only native translation. |
| `InstructionEffect` exact classifier | `src/Koan.Data.Abstractions/Instructions/` | Stable instruction identities and their proven effects share the contract owner. |
| Source configuration keys | `src/Koan.Data.Core/Infrastructure/ConfigurationConstants.cs` | Stable configuration identifiers remain centralized. |
| Source plan compilation/cache | `src/Koan.Data.Core/DataSourceRegistry.cs` | The registry owns named source definitions for one host and can freeze resolved election/route facts once. |
| Host-owned stage coalescer | `src/Koan.Data.Core/Readiness/DataSourceReadinessCoordinator.cs` | Data—not Core or adapters—owns the distinct reachability, validation, and policy-authorized provisioning stages. |
| Source-policy and readiness facts | `tests/Suites/Data/Core/Koan.Tests.Data.Core/Specs/Sources/` | Framework policy is proven independently of any provider implementation. |

Existing methods to change are the `RepositoryFacade` constructor/guard calls, `DataService` plan handoff, Direct resource-opening boundary, source discovery/registration, and DocumentStore readiness wrapper. `DataAdapterReadinessExtensions` is deleted.

**Coalescence:** Closest pattern: `RepositoryFacade.Guard` plus `Document.OnceGate`. The facade currently owns application-facing Entity semantics for a host-composed repository, while OnceGate and the global `Singleflight` repeat only pieces of readiness mechanics at incompatible lifetimes. Specificity is Data framework law. Disposition: keep election/routing and the facade; absorb source-policy enforcement and host-owned stage coalescing into Data; rebuild readiness stages around explicit intent and host cancellation; delete message-text classification and business-operation provision/replay. `DataSourceRegistry` is the one plan owner because it already owns named source configuration per host; generic Core is too wide because storage lifecycle is Data meaning, while a family or adapter is too narrow because every path must obey it.

**Ergonomics:** Humans configure two independent words—`StorageLifecycle` and `Access`—once and keep writing ordinary Entity code. IntelliSense exposes a small four-value effect vocabulary only to framework/adapter authors; application code does not branch on infrastructure. The coding model reads from intent to guarantee without repository, readiness, or provider-classifier ceremony. Defaults preserve the zero-configuration path. Exact instruction identity and typed effects eliminate hidden text rules and make unsupported/opaque execution fail at the declaration boundary.

**Constraints satisfied:**

- No HTTP surface or inline endpoints are introduced.
- No placeholder/scaffold classes are planned.
- Stable configuration and instruction identifiers are centralized; policy values are enums rather than magic strings.
- Application examples remain Entity-first; Direct is treated only as an expert alternate path.
- No large-data access is introduced.
- The primer/work card and initiative evidence remain the documentation owners; current public docs will be updated only where the implemented contract changes their truth.
- One public/top-level type will live per new file, grouped in concern folders.

**Risks:** Existing providers may still create storage inside their own `EnsureReady` or native connection-open path. DAC-04 can prevent forbidden framework dispatch and delete shared replay without smuggling provider behavior into this slice; each gold-reference rewrite must prove a genuinely non-creating external route. Raw Direct `Execute` and transaction creation do not carry a proven effect today, so constrained sources must fail closed; widening that surface requires the later registered-operation contract rather than SQL text inference. Existing production transaction/provider-extension seams may lack a source-plan handoff; any provider behavior change needed to close those rows will be recorded for its owning later card.
