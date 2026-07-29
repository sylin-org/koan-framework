---
type: EVIDENCE
domain: data
title: "DAC-05 Explore Record"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: Entity, query, bulk, batch, stream, conditional-write, and coordination semantics
---

# DAC-05 Explore Record

**Task:** Make Data the single owner of ordinary Entity identity, query shaping, bulk/batch, conditional-write,
streaming, and coordination semantics while giving adapters exact native execution and receipt seams.

**Application intent:** I use the ordinary Entity verbs once and receive the same identity, visibility, lifecycle,
query, mutation, and cancellation meaning from every selected adapter; when I ask for a stronger guarantee such as an
atomic batch or provider-bounded stream, Koan either proves it or rejects before partial or unbounded work.

**Public expression:** The common surface remains Entity-first:

```csharp
var saved = await new Todo { Title = "Ship" }.Save(ct);
var requested = await Todo.Get(new[] { saved.Id, "missing", saved.Id }, ct);
var page = await Todo.Page(1, 50, todo => !todo.Done, ct);
var batch = await Todo.Batch()
    .Update(saved.Id, todo => todo.Done = true)
    .Save(new BatchOptions(RequireAtomic: true), ct);

await foreach (var todo in Todo.QueryStream(todo => !todo.Done, batchSize: 100, ct))
{
    // consumer-paced, provider-bounded work
}
```

The application references the selected adapter and `Sylin.Koan`, calls `AddKoan()`, and supplies source policy only
when the defaults are not intended. Entity lifecycle, segmentation, mapping, and provider qualification are composed
by referenced modules; no repository, provider class, transaction object, or receipt plumbing enters the common path.

**Guarantee/correction:** Get-many has exactly one ordered slot per requested key, including duplicates and nulls for
missing or invisible records. Data applies residual filter, final sort, page, projection, count, transforms, guards,
and lifecycle in one semantic order. Batch mutations load at commit time and a missing target fails before native
mutation. `RequireAtomic` requires both an advertised and an executable native atomic boundary before callbacks or
provider work; provider-bounded streams require truthful page/filter/order receipts before yielding. A missing seam,
false receipt, unsupported projection, unsafe semantic mass delete, or non-native atomicity request throws a typed,
corrective failure rather than scanning, partially mutating, or weakening the requested guarantee.

**Complete intent surface:** There are no required user actions beyond the Entity expression, ordinary source policy,
and the existing `BatchOptions.RequireAtomic` decision. Execution receipts and mutation outcomes enrich existing result
contracts for callers that inspect them; adapters implement the matching optional/native seams and advertise only the
capabilities their conformance packet proves.

**Public concepts:** `QueryDefinition` remains the one structured query value. `RepositoryQueryResult<TEntity>` is the
adapter execution receipt and gains only the missing filter/count facts needed to validate work. `BatchResult` remains
the batch result and gains atomicity, commit, and per-operation outcome facts. `BatchExecutionCapabilities` describes
what a created native batch can promise before execution. Typed receipt/missing-target exceptions express corrective
failure. No second query builder, unit of work, change tracker, public cursor, or ORM concept is introduced.

**Docs read:**

- `docs/engineering/index.md` — points current contribution work to the canonical engineering owners; relevant as a
  supersession notice only.
- `docs/architecture/principles.md` — establishes Entity-first expression, one semantic owner, compiled hot paths, and
  fail-loud capability honesty; directly governs this slice.
- `docs/toc.yml` — confirms current public documentation teaches Entity access and bounded streaming through one Data
  path; no new public documentation branch is warranted.
- `docs/architecture/data-adapter-development-primer.md` — owns B-01–B-09, G-05–G-06, P-02, and P-04 and requires
  ordered get-many, truthful outcomes/receipts, bounded streams, and real native atomicity.
- `docs/decisions/DATA-0107-provider-bounded-entity-streams.md` — fixes `IAsyncEnumerable<TEntity>` as the only public
  stream and requires provider-applied candidate bounds and complete order before yield.
- `docs/initiatives/data-adapter-conformance/evidence/framework/public-contract.md` — ratifies one Entity/source
  grammar and forbids sequential pseudo-batches or client work hidden by handled/optimized claims.
- `src/Koan.Data.Abstractions/README.md`, `TECHNICAL.md`, `src/Koan.Data.Core/README.md`, and `TECHNICAL.md` — establish
  the current repository/query/facade ownership and must be reconciled with the implemented receipt rules.

**Code read:**

- `src/Koan.Data.Abstractions/IDataRepository.cs`, `IQueryRepository.cs`, `IBatchSet.cs`, `BatchResult.cs`, and
  `IConditionalWriteRepository.cs` — define the deliberately small provider surface, but current batch/count/conditional
  results cannot yet publish every required execution fact.
- `src/Koan.Data.Abstractions/QueryDefinition.cs`, `RepositoryQueryResult.cs`, and `DataCaps.cs` — already carry the
  structured intent and most receipt/capability axes; filter handling and exact count execution are the missing facts.
- `src/Koan.Data.Core/Data.cs` — owns materialized query orchestration and public streaming, but currently trusts an
  impossible pagination receipt after residual planning and can materialize an unbounded residual safety check.
- `src/Koan.Data.Core/RepositoryFacade.cs` — is the correct semantic chokepoint for policy, segmentation, transforms,
  lifecycle, bulk, conditional writes, and batches; current unscoped get-many trusts provider cardinality, lifecycle
  bulk upsert degrades to N single writes, batch mutation silently ignores missing rows, and batch stamps differ from
  single writes.
- `src/Koan.Data.Core/Querying/FilterPushdownCoordinator.cs` and `QueryStreamCoordinator.cs` — are the closest correct
  planning patterns; they centralize residual/page/order behavior but need complete receipt validation and Data-owned
  projection handling.
- `src/Koan.Data.Core/Transactions/TransactionCoordinator.cs`, `ITransactionCoordinator.cs`, and
  `TrackedOperations.cs` — implement useful deferred non-atomic coordination, but incorrectly publish `TxCaps.Local`,
  leak native messages, and leave failed partial commits looking rollbackable.
- `src/Koan.Data.Core/Pipeline/StorageWritePlan.cs` and `IWriteStamp.cs` — compile the warm write path once, while the
  current `ApplyBatch` exclusion contradicts the primer's same-transform rule.
- `tests/Suites/Data/Core/Koan.Tests.Data.Core/Specs/Streaming/EntityStreamingSpec.cs`, pipeline specs, transaction
  specs, and source-policy specs — provide the closest executable patterns and expose where new red-first cases belong.

Explicit constants/options/shared-type searches found the existing `Infrastructure.Constants` stream bounds and
diagnostic reasons, `DataRuntimeOptions`, `BatchOptions`, `BatchResult`, `CountResult`, `DataCommitOutcome`,
`RepositoryQueryResult<TEntity>`, `QueryDefinition`, `DataCaps`, and transaction tokens. There is no existing batch
atomicity/operation-outcome vocabulary, complete query-receipt validator, typed batch-mutation-missing failure, or
Data-owned compiled Entity projection plan.

**Reusing:**

- `RepositoryFacade.Guard` and the DAC-04 immutable source plan as the first write/read boundary.
- `QueryDefinition`, `FilterSplitter`, `InMemoryFilterEvaluator`, `InMemorySorter`, and
  `FilterPushdownCoordinator` as the one query planning/finalization pipeline.
- `RepositoryQueryResult<TEntity>` as the receipt carrier rather than introducing a parallel query result hierarchy.
- `BatchOptions` and `BatchResult` as the user-visible batch decision/result rather than adding a unit-of-work API.
- `DataCaps.Write.AtomicBatch`, `DataCaps.Write.ConditionalReplace`, and provider-bounded paging as claims that must
  agree with their executable seam and receipt.
- `StorageWritePlan` as the compiled, reused identity/timestamp contributor plan.
- The logical transaction coordinator only as explicitly non-atomic deferred coordination.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| `BatchAtomicity`, `BatchExecutionCapabilities`, `BatchOperation`, `BatchItemOutcome`, `BatchItemResult` | `src/Koan.Data.Abstractions/Batch*.cs` | Provider-neutral batch guarantees and outcomes belong beside the existing batch contract. |
| typed query-receipt and deferred-mutation failures | `src/Koan.Data.Abstractions/QueryReceiptRejectedException.cs` and `BatchMutationTargetNotFoundException.cs` | Callers need stable corrective failures without parsing provider text. |
| compiled projection/final receipt validation | `src/Koan.Data.Core/Querying/` | Data owns final shaping and receipt truth; adapters own only native translation/execution. |
| bounded semantic mass-mutation helper | `src/Koan.Data.Core/Querying/` or private facade helpers | The facade must preserve lifecycle/isolation without a full-source materialization. |
| Entity/query/batch/transaction semantic specs | `tests/Suites/Data/Core/Koan.Tests.Data.Core/Specs/Entity`, `Querying`, and `Transactions` | Framework laws are proved without provider-specific algorithms. |

Existing methods to change are `Data.QueryWithCount`, `CountCore`, transaction-aware Entity writes,
`RepositoryFacade.ReadMany`, query/bulk/delete/batch/conditional paths, `FilterPushdownCoordinator.Plan/Finalize`,
`QueryStreamCoordinator.Execute`, batch/result contracts, and transaction capability/failure reporting.

**Coalescence:** Closest pattern: `FilterPushdownCoordinator` plus `RepositoryFacade`. The coordinator already owns
query split/final order and the facade already owns every Entity semantic cross-cut. Repeated mechanics currently live
in `Data.cs`, mass-delete branches, provider batches, and the logical transaction coordinator. Specificity is Data
framework law for ordering, lifecycle, receipt validation, and guarantee selection; native batching, CAS, page
application, error codes, and cleanup stay Family/Adapter concerns. Disposition: keep and extend the two owners; absorb
get-many normalization, bulk preparation, projection, and receipt validation into them; rebuild semantic mass mutation
as bounded work; retain the transaction coordinator only as non-atomic coordination; delete the `TxCaps.Local` false
claim, silent missing-mutation behavior, batch-only timestamp exception, impossible receipt trust, and N-dispatch bulk
lifecycle path. Generic Core is too wide because these are Data semantics. A provider is too narrow because no adapter
may redefine them.

**Ergonomics:** People continue to read `Todo.Get`, `Todo.Query`, `Todo.Batch`, and `Todo.QueryStream` as their business
sentences. `RequireAtomic` remains the only common-path branch that changes a batch guarantee. IntelliSense adds facts
to the result an advanced caller already has, while adapter authors see one capability, one preflight execution seam,
and one receipt to implement. Agents can map intent to guarantee without discovering provider fallbacks, transaction
classes, or lifecycle workarounds.

**Constraints satisfied:**

- No HTTP surface or inline endpoint is introduced.
- No placeholder/scaffold class is planned.
- Stable diagnostic reason identifiers will remain in `Infrastructure.Constants`; guarantee vocabularies are enums,
  not magic strings; no new tunable is required.
- Application examples and transaction preflight use first-class Entity statics.
- Large semantic deletes and streams use capability-qualified bounded pages; unsupported paths reject before reads.
- Public/module docs and initiative evidence will be updated where behavior changes.
- New public/top-level types receive one file each and concern folders retain project structure.

**Risks:** Existing adapters return incomplete query and batch receipts, so this Framework card creates exact seams and
core proofs without editing provider code; SQLite/Mongo gold rewrites and later fleet cards must populate and prove
them. A provider may report success but omit an execution fact after mutation; that is a committed-or-unknown receipt
failure and is never replayable. Nested sparse Entity projection has constructor/object-graph edge cases; Data must
compile a bounded plan and reject an unrepresentable projection before provider work rather than use per-row
reflection or JSON. Deleting while paging can skip rows, so semantic mass deletion must repeatedly consume a bounded
first page under a complete provider order rather than increment offsets.
