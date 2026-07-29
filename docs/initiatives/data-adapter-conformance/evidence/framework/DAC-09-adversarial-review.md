---
type: EVIDENCE
domain: data
title: "DAC-09 independent adversarial Framework review"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: red
  scope: independent source, architecture, security, performance, inventory, Forge, and focused-test review
---

# DAC-09 independent adversarial Framework review

## Verdict

**RED. DAC-10 and DAC-20 remain blocked.**

The candidate does not satisfy the Framework contract. This review found independently reproducible policy bypasses,
ambiguous replay, hidden unbounded work, claim-identity drift, mutable source decisions, unsafe public diagnostics, and
privacy/failure leaks. The required sealed DAC-08 source checkpoint is also absent, so the candidate cannot be reproduced
as a clean certification input even if the behavioral findings were fixed.

This was a read-only production review. No production file was changed. The only repository write made by this reviewer
is this evidence file.

## Reviewed authority and candidate identity

The review read the complete DAC-09 card, charter, acceptance rules, primer, responsibility map, public contract,
NOW/PROGRESS state, and DAC-04 through DAC-08 exploration and verification evidence before inspecting source. Prior PASS
conclusions were treated as claims to falsify, not as authority. SQLite and MongoDB were sampled only to verify whether
they consume shared seams; their legacy implementations were not used to define the target behavior.

The inspected working tree was on `agent/polymorphic-entity-root-persistence`, HEAD
`86c18819cf03160c20a001d91f3bd2f257fd1a0d`, ahead of its remote by two commits, with the DAC-04--08 production and test
candidate spread across a large set of modified and untracked files. `git diff --name-only` reported 95 tracked changes
under the inspected Data source/test roots alone.

### Reproducibility stop: DEFER

DAC-09 requires replay of the sealed DAC-08 source checkpoint in a disposable clean worktree. The only checkpoint in
`artifacts/data-adapter-conformance/checkpoints/` is `dac00-current.json`. It is based on
`86c18819cf03160c20a001d91f3bd2f257fd1a0d`, records source fingerprint
`e35b9f8eb9b6e4ea6f49e1bdcb29710ab18cd6af026236f0135db2a4e94820df`, and inventories 283 initiative/document files;
it does not bind the current Framework/Family production and test changes. No DAC-08 bundle, resultant commit, or
production-source fingerprint exists. The Framework identity/dependency packet also remains the DAC-01-era packet.

This is independently **DEFER** for the certification-input prerequisite. The substantive RED findings below determine
the overall verdict.

## Gate results

| Gate | Result | Reproduction |
|---|---|---|
| DAC-01 public-surface audit | PASS | `New-FrameworkSurfaceAudit.ps1`: 27 projects, 638 public types, 2,509 public members, zero parse errors |
| Dynamic surface map | PASS | `New-FrameworkSurfaceMap.ps1`: 52 surfaces, 3,147 public entries, 417 critic matches |
| Framework scorecard regeneration | **RED** | `New-FrameworkScorecard.ps1` aborts: `No surface maps primer cell V-01.` |
| Forge catalog | PASS | `pwsh scripts/forge-verify.ps1 -CatalogOnly`: 105 cells, 39 profiles, fingerprint `e8f8aa55e2765824ff05890db9dceb514d8a47625921134de01257bdb13c079c` |
| Strict SQLite record Forge | **RED** | Five behavior failures (`B-01`, `B-09`, and three `G-09` cases); `docs/initiatives/data-adapter-conformance/evidence/sqlite/conformance.json` is absent |
| Focused Framework suite | **RED** | 102 pass / 9 fail / 111 total; every failure is in `EntityTransferDslSpec`, rejecting missing filter/count receipts |
| Clean checkpoint replay | **DEFER** | No sealed DAC-08 production checkpoint exists |
| Full solution/docs build | Not claimed | Stopped after decisive structural, behavioral, and source REDs; a successful build could not change this verdict |

The focused command was:

```powershell
dotnet test tests/Suites/Data/Core/Koan.Tests.Data.Core/Koan.Tests.Data.Core.csproj --no-restore --filter "FullyQualifiedName~SourcePolicySpec|FullyQualifiedName~SourceReadinessCoordinatorSpec|FullyQualifiedName~SourceIntegrationSpec|FullyQualifiedName~DataDiagnosticsConformanceSpec|FullyQualifiedName~EntityExecutionSemanticsSpec|FullyQualifiedName~DirectDataAccessSpec|FullyQualifiedName~CrossAdapterTransactionsSpec|FullyQualifiedName~TransactionErrorHandlingSpec|FullyQualifiedName~EntityTransferDslSpec"
```

## Actionable findings

### F1 — CRITICAL: Direct and string-instruction surfaces can misclassify opaque mutations as reads

Rows: C-01, C-04, C-05, F-05, F-06, F-11, H-06.

- `DirectSession.Scalar`, `Query`, and `Query<T>` demand `DataOperationEffect.Read` solely from the result-shaped API used,
  then dispatch the caller's arbitrary SQL (`src/Koan.Data.Core/Direct/DirectSession.cs:93`, `:109`, `:137`, `:146`,
  `:163`). `DirectTransaction` repeats the same rule (`src/Koan.Data.Core/Direct/DirectTransaction.cs:43`, `:65`, `:87`).
  A statement with a returned scalar/rowset, a side-effecting function, or a multi-statement command can therefore execute
  under a Read ceiling. The source policy is not an effective access boundary.
- Both public string execution helpers infer effect from `TrimStart().StartsWith("select ")` rather than an explicit
  registered operation/effect (`src/Koan.Data.Core/DataServiceExecuteExtensions.cs:15` and
  `src/Koan.Data.Relational/Extensions/DataServiceExecuteExtensions.cs:14`). The primer explicitly forbids text-prefix
  effect inference.

The repair belongs in Framework: remove or fail-close the ambiguous string overload, require an explicit effect/binding,
and route Direct, instruction, and transaction dispatch through the same compiled operation plan before any provider work.

### F2 — CRITICAL: Framework replays after exceptions that do not prove non-dispatch

Rows: B-03, B-04, B-08, C-04, G-04, H-06, P-04.

- `RepositoryFacade.DeleteAll` dispatches native `DataInstructions.Clear`, catches any `NotSupportedException`, and then
  performs bounded semantic deletion (`src/Koan.Data.Core/RepositoryFacade.cs:963`, `:973-976`). A provider may mutate
  before throwing that exception. Framework then replays the operation without a receipt proving non-dispatch.
- Transfer predicate reads likewise catch `NotSupportedException` after `Data.Query`, then execute `Data.All` and filter
  locally (`src/Koan.Data.Core/Transfers/EntityTransferBuilderBase.cs:79`, `:90-94`). This both replays after an ambiguous
  exception and silently changes the execution plan.

Fallback selection must occur before dispatch from a frozen capability/plan. Once dispatch is possible, failures must
carry explicit dispatch and outcome metadata and must never trigger replay.

### F3 — HIGH: transfer is an unbounded hidden full-scan workflow and is currently behaviorally red

Rows: B-08, C-04, G-04, P-02, P-04.

`FetchEntities` loads all source entities when no predicate is present and as the unsupported-query fallback, converts
the full result to lists, recompiles the predicate on each run, applies it a second time even after a provider query, and
applies `QueryShaper` in memory (`EntityTransferBuilderBase.cs:92-113`). `BatchSizeValue` is applied only later to writes
(`:137-142`), so it is not a source-memory or candidate-work bound. The focused suite confirms this surface is not even
internally green: 9/9 transfer cases selected failed strict filter/count receipt validation.

Transfer needs provider-bounded source paging/candidates, an explicit pre-dispatch fallback decision, receipt validation,
and a compiled/cached predicate or mapping plan. A warning after hidden client work is not conformance.

### F4 — HIGH: source decisions are mutable after composition and host state is not bounded

Rows: A-02, C-04, G-08, P-01, P-03.

- `DataSourceRegistry.RegisterSource` is public, described for runtime scenarios, overwrites an existing source, and
  invalidates cached plans with no composition freeze (`src/Koan.Data.Core/DataSourceRegistry.cs:141`, `:153-157`). Existing
  repositories retain the old plan while subsequent resolution can use the replacement. This permits a split-brain host
  rather than one frozen route/policy decision.
- `_sources` and `_plans` are unbounded dictionaries (`DataSourceRegistry.cs:37-39`). `DataService` adds unbounded
  repository and variant caches (`src/Koan.Data.Core/DataService.cs:18-19`). Repository creation uses `TryGetValue`, creates
  provider state, then assigns (`:63`, `:76`, `:102`) rather than atomic single-flight publication, so concurrent first
  use can create duplicate/lost provider resources.
- Each source replacement appends the old lazy source to an unbounded `_retired` list that is drained only at host
  disposal (`src/Koan.Data.Core/SourceIntegration/Runtime/DataSourceIntegrationService.cs:12`, `:97-98`, `:122-126`).

Freeze the registry at composition, bound every admitted key space, reject unbounded runtime names/replacements, and use
single-flight publication with deterministic loser disposal.

### F5 — HIGH: the claimed one executable claim identity does not exist for SQLite or MongoDB

Rows: A-01, H-01, H-04, P-06.

`IAdapterFactory.DescribeClaims` has an empty default (`src/Koan.Data.Abstractions/IAdapterFactory.cs:38`). The only
production references to `DescribeClaims` are that default and the Framework call in `DataClaimSet`; no connector
overrides it. Therefore runtime `DataClaimSet.Describe` publishes only Framework `SourceCore` for every current adapter
(`src/Koan.Data.Abstractions/Diagnostics/DataClaimSet.cs:25`).

Meanwhile SQLite declares an independent repository `CapabilitySet`
(`src/Connectors/Data/Sqlite/SqliteRepository.cs:46-60`), Mongo declares another in
`src/Connectors/Data/Mongo/MongoDocumentStore.cs:98-105`, `DataConformanceManifest.Builder` still accepts both
`CapabilitySet` and `DataClaimSet` (`src/Koan.Testing/Conformance/DataConformanceManifest.cs:101`, `:110`), and the record
TestKit selects applicability from `DataCaps.Describe` (`tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/AodbConformanceSpecsBase.cs:81-95`).

Runtime descriptions/health can consequently publish Source Core while TestKit and repository introspection advertise
query, bulk, isolation, or atomicity profiles. SQLite and MongoDB do not yet consume a single shared authoring seam, so
neither gold card has a compile-ready claim contract.

### F6 — HIGH: an alternate public Explain path activates adapter construction and leaks raw error text

Rows: H-01, H-02, H-05, P-01.

The new `Data.Source(...).Describe/Explain` path is lazy, but it is not the only public Explain path. `DataAxis.Explain`
reflectively invokes `IDataService.GetScopeDiagnostics` (`src/Koan.Data.Core/Axes/DataAxis.cs:111-120`). That method calls
the selected adapter's `factory.Create<TEntity,TKey>` and intentionally does not cache it
(`src/Koan.Data.Core/DataService.cs:122-131`). For Mongo this resolves or creates source-specific provider/client state
(`src/Connectors/Data/Mongo/MongoAdapterFactory.cs:62-65`, `:106-109`). On failure, `DataAxis.Explain` embeds
`actual.Message` into its public explanation (`DataAxis.cs:122-125`).

Thus public diagnostics can activate provider resources and expose provider/native messages despite the canonical
responsibility map explicitly naming both as invalid. All public Describe/Explain variants must project the same inert,
redacted plan without repository/provider construction.

### F7 — HIGH: warm Direct and materialization paths bypass the compiled/bounded plan

Rows: D-05, D-06, D-07, P-02, P-03, P-04.

- Entity-backed Direct scans every loaded assembly and all loadable types on every resolution
  (`DirectSession.cs:190-196`), reflects and closes execution methods on each call (`:118-122`, `:218-221`), and enumerates
  all connection factories during route resolution and again during connection creation (`:394-395`, `:429-432`).
- The reflected Execute/Scalar entity path substitutes `default(CancellationToken)` for the caller token
  (`DirectSession.cs:221`).
- Typed Direct materialization sets both byte limits to `long.MaxValue` in sessions and transactions
  (`DirectSession.cs:167-171`, `DirectTransaction.cs:104`). A row cap alone does not bound large values or total bytes.
- Patch execution serializes entities through `JObject.FromObject` and a default JSON serializer instead of the compiled
  mapping plan (`src/Koan.Data.Core/Patch/PatchOpsExecutor.cs:38`, `:54`, `:112-116`).
- Writable-property DTO projection silently skips missing fields rather than raising the promised corrective failure
  (`src/Koan.Data.Abstractions/Records/RecordProjector.cs:43-48`).

These are Framework-owned copies/bypasses of mapping, projection, and resource-bound rules. They must consume the same
host plan as registered operations and provider inspection.

### F8 — HIGH: public failure/log paths expose business identifiers and native exception objects

Rows: H-05, H-06.

Deferred transaction logging records entity IDs for scalar and vector writes/deletes
(`src/Koan.Data.Core/Transactions/TransactionCoordinator.cs:62-65`, `:81-84`, `:104-109`, `:124-127`). Failure logs pass
the entire native exception (`:184`, `:231`, `:300`), rollback includes `ex.Message` in the public message (`:235-238`),
and public `TransactionException` constructors retain the raw exception as `InnerException`
(`src/Koan.Data.Core/Transactions/ITransactionCoordinator.cs:102-112`). This bypasses the new bounded restricted-evidence
store and stable public failure vocabulary.

Additionally, `DataSourcePlan` calls itself redacted but publicly exposes arbitrary adapter `Settings`
(`src/Koan.Data.Abstractions/Sources/DataSourcePlan.cs:63`) through public `DataSourceRegistry.GetPlan`
(`src/Koan.Data.Core/DataSourceRegistry.cs:164`). Non-Framework configuration children are copied wholesale, so a custom
credential/token setting can become public plan data even though the raw connection string is hashed.

General logs and public exceptions must exclude entity/tenant/business values and native exception/message material;
exact native evidence belongs only in the bounded restricted store. Public plans need an allowlisted, redacted projection.

## Negative-search disposition

- No mutable process-static host decision was found in the new readiness/diagnostic implementations; the material
  state failures are host-owned but unbounded or mutable after composition.
- `Data.Source(...).Describe/Explain` itself does not access the lazy `Integration` value. The public `DataAxis.Explain`
  bypass above prevents a Framework-wide PASS.
- Doctor activation is explicit and the reviewed Framework tests cover cancellation/timeout. Non-mutation remains a
  provider LIVE obligation; this review found no mechanism that could make an arbitrary provider Doctor implementation
  non-mutating by construction.
- SQLite and MongoDB share route/naming helpers, but their missing `DescribeClaims` implementations and duplicate legacy
  capability declarations prove that the common seam is not yet sufficient for gold authoring.

## Required bounded remediation cards

DAC-09 forbids production fixes, so this review creates no implementation changes. The RED should be split into bounded
work before another certification attempt:

1. **Framework source freeze and bounded ownership:** freeze source declaration at composition, bound registry/repository/
   integration caches, add single-flight creation and replacement rejection tests.
2. **Framework operation/effect chokepoint:** eliminate SQL-prefix inference; require explicit effect/binding for Direct,
   instructions, and transaction operations; prove policy rejection before route/resource/callback work.
3. **Framework no-replay and bounded transfer:** remove post-dispatch `NotSupportedException` fallbacks; introduce explicit
   dispatch/outcome receipts and provider-bounded transfer paging.
4. **Framework claim identity:** make `DataClaimSet` the sole adapter declaration consumed by runtime, health, TestKit, and
   packets; delete or mechanically bridge independent `CapabilitySet` selection; require SQLite/Mongo compile proofs.
5. **Framework diagnostic/privacy boundary:** make every public Describe/Explain inert; remove raw messages, exceptions,
   identifiers, and arbitrary settings from public projections/logs.
6. **Framework compiled mapping/warm path:** route Direct, patch, transfer, and DTO projection through bounded compiled
   plans; cache finite reflection decisions; restore caller cancellation and byte/value limits.
7. **Certification tooling/identity:** repair V-cell surface mapping, seal a complete production/test checkpoint, replay it
   in a clean disposable tree, then rerun strict Forge, focused/full tests, solution build, and docs examples.

Until all seven cards are independently green and a replayable packet exists, Framework certification and SQLite/MongoDB
gold authoring remain blocked.
