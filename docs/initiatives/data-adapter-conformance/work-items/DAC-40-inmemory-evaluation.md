---
type: SPEC
domain: data
title: "DAC-40 Rebuild and Certify the InMemory Entity Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: in-progress
last_updated: 2026-07-29
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-29
  status: behavior-green-strict-deferred
  scope: empty-root InMemory Entity adapter rebuild, complete live-in-process conformance, and shared KeyValue regressions
---

# DAC-40 — Rebuild and certify the InMemory Entity adapter

| Field | Value |
|---|---|
| Phase / kind | fleet / break-and-rebuild-certification |
| Depends on | DAC-30 |
| Primer scope | Entity Core, Source Core, KeyValue family, G-09 |
| Production writes | authorized after the exploration gate recorded below |
| Owner | Adapter(InMemory); KeyValue family semantics remain in Data Core |

## Meaningful outcome

Koan has a fast dockerless Entity sentinel whose persistence semantics are real within one host: writes snapshot values,
reads are detached, awaited mutations are visible, source and partition isolation are exact, state is bounded, and no
claim implies durability, streaming, provider indexes, or atomicity that process memory cannot supply.

## Exploration gate

**Task:** Replace the InMemory Entity adapter from an empty implementation root, retaining only stable provider identity
and the already-proven KeyValue family contract, then certify its complete observable surface.

**Application intent:** Use ordinary Entity persistence in a fast, explicitly ephemeral Koan host without files,
containers, network services, or provider-specific setup.

**Public expression:** Reference the connector, call `AddKoan()`, and use ordinary Entity verbs:

```powershell
dotnet add package Sylin.Koan.Data.Connector.InMemory
```

```csharp
builder.Services.AddKoan();

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

var saved = await new Todo { Title = "Ship" }.Save();
var same = await Todo.Get(saved.Id);
```

Explicit source placement remains the standard source decision:

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": { "Adapter": "inmemory" }
      }
    }
  }
}
```

**Guarantee/correction:** Each awaited write stores a detached Entity snapshot; every point read and query materializes a
fresh detached value. Unsaved caller mutation cannot alter stored state. Values are visible within the same host,
source, Entity root, and partition, and disappear with the host. Physical maps and External lifecycle reject because
there is no provider-owned physical store. Required atomic batches, provider-bounded streams, durability, or native
plans reject before partial work instead of being simulated.

**Complete intent surface:** Package reference, ordinary Koan bootstrap, and an Entity model are sufficient. Optional
Source and partition contexts retain their provider-neutral meanings. There is no connector registration, options type,
storage handle, reset API, DTO, external service, or settling loop.

**Public concepts:** `inmemory` is the only provider-specific concept and expresses deliberate host-ephemeral placement.
Source, partition, access, lifecycle, query, batch, and Entity verbs remain provider-neutral decisions.

**Docs read:**

- `docs/architecture/principles.md` — Entity is the public center, adapters own mechanics only, hot paths consume frozen
  decisions, and process-static state is forbidden.
- `docs/architecture/data-adapter-development-primer.md` — snapshots, truthful receipts, source policy, isolation,
  bounded work, corrective declines, and the greenfield boundary are acceptance law.
- `docs/decisions/DATA-0107-provider-bounded-entity-streams.md` — resident InMemory scans do not earn
  provider-bounded streaming.
- Current DAC-40/README/TECHNICAL — useful public expression and explicit declines, but audit-only language and passing
  legacy tests do not prove an empty-root rebuild.

**Code read:**

- `KeyValueStore<TEntity,TKey>` and `KvRecord<TEntity>` — the capability-family owner for queries, managed-field guards,
  batches, instructions, and G-09; the adapter should implement only six storage primitives and capability facts.
- Current factory/store/repository/features/module — harvested failure modes: caller references are retained on writes
  and returned on reads, the old root was only incrementally patched, capacity uses a poisoned over-limit `Lazy`, and
  comments/history outweigh the four provider mechanics.
- Current 53-case connector suite and AODB base — proves CRUD, query, sorting, policy, source/partition isolation,
  instructions, and current claims, but lacks detached snapshot proof.
- JSON Entity materialization contract — `EntityJsonSerialization` is the existing root/variant-safe snapshot codec and
  avoids inventing a second Entity serialization interpretation.

**Reusing:**

- `KeyValueStore<TEntity,TKey>`, `KvRecord<TEntity>`, Entity root materialization, source registry/policy, Entity facade,
  naming, module activation, capabilities, current tests, and stable provider identity `inmemory`/`memory`.
- No current InMemory execution file or control flow is retained merely because it exists.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| compact provider constants | `Infrastructure/Constants.cs` | Stable provider/boot identities and the one host-state bound. |
| bounded host state | `Runtime/InMemoryState.cs` | Own one finite source/root/partition map with lock-free hot lookup and serialized snapshots. |
| thin repository | `Runtime/InMemoryRepository.cs` | Implement only KeyValue storage primitives and detached materialization. |
| executable claims | `Runtime/InMemoryFeatures.cs` | Keep factory and repository capability truth identical. |
| plan/policy factory | `InMemoryAdapterFactory.cs` | Resolve the source, reject impossible map/External decisions, and create the repository. |
| compact module | `Initialization/InMemoryDataModule.cs` | Register one host-owned state and the factory; project the same facts. |
| snapshot acceptance cases | connector InMemory test project | Prove original/read/query mutation cannot bypass Save. |
| instruction-first docs | InMemory `README.md` and `TECHNICAL.md` | State the shortest result, guarantees, limits, and owners without history. |

**Coalescence:** The closest pattern is `KeyValueStore<TEntity,TKey>`. Its decision owner is Data Core, its consumers are
InMemory/JSON/Redis, its state is repository-local immutable capability truth, and its hot cost is scan/filter/batch
orchestration already required by each family member. Keep that family seam. Rebuild every InMemory-owned execution
file. The one adapter owner is `InMemoryState` for bytes/lifetime and `InMemoryRepository` for primitive translation;
moving snapshots into Core would force serialized persistence semantics onto JSON/Redis, while leaving them in the
factory would mix composition and I/O. Delete root-level `InMemoryDataStore.cs` and `InMemoryRepository.cs`; no bridge,
wrapper, fallback, or alternate path remains.

**Ergonomics:** Human code remains package + `AddKoan()` + Entity. IntelliSense exposes no provider service or options.
An agent sees one placement token and ordinary Entity verbs. Cognitive branches are only optional Source/partition and
explicit declined guarantees.

**Constraints satisfied:**

- Entity-first access; no public repository, state, codec, DTO, or provider API.
- No HTTP surface or inline endpoint.
- Stable identifiers live in constants; no tunable option exists.
- Host state is bounded and owned by DI; no process-static cache.
- InMemory `AllStream`/`QueryStream` remain corrective declines under DATA-0107.
- No placeholder, compatibility layer, hidden fallback, or commented scaffold.
- README/TECHNICAL and the initiative ledger move with executable behavior.

**Risks:** Entity snapshotting adds serialization allocation to an adapter valued for speed; use the existing
root/variant-safe codec, serialize only at the storage boundary, and measure the warm path. The KeyValue family returns
the caller object from `Upsert` by contract, so acceptance must prove stored state rather than object identity. Complex
non-serializable object graphs must fail correctively on write, as every real persistence adapter would.

Standing user authorization permits implementation after this gate. It does not relax empty-root, conformance, or
strict-packet requirements.

## Execute

1. Delete the complete InMemory production implementation root and create only the owners above.
2. Add detached original/read/query snapshot cases before implementing the replacement.
3. Run the connector ledger, shared Data/Core/KeyValue regressions, host lifecycle/soak, and full solution gates.
4. Keep strict packet absence visible; do not create a local certification substitute.

## Verification

- InMemory Entity ledger: 56/56 passed with zero skips, including CRUD/query/sort/batch/instructions, all advertised
  isolation modes, source and partition routing, managed-field guards, fail-fast policy, detached nested graphs,
  polymorphic root/variant round trips, and the exact 4,096-store host ceiling.
- Snapshot semantics: original, point-read, and query-returned object mutation stayed invisible until an explicit save;
  every read materialized a fresh root-aware Entity value.
- Shared family regressions: Data Core 471/471, JSON 28/28, and live Redis 17/17 against its pinned Docker fixture.
- Repository: full `Koan.sln` build succeeded with zero warnings/errors; package quality returned 93 packages, 0 repair,
  10 review, and 83 structurally ready; product surface returned 43 claims and 93 packages and its generated outputs
  are current; public-doc truth passed; docs lint returned 0 errors and 1,476 baseline warnings.
- Initiative validation retains only the pre-existing stale DAC-11/DAC-51/DAC-53/DAC-54/DAC-56 dependency rows,
  pre-DAC-30 lane rule, and missing historical DAC-09/DAC-10 progress rows; DAC-40 introduces no validator mismatch.
- Strict evidence remains deferred because the shared versioned packet generator is absent. No local substitute was
  created and no missing packet is counted as green.

## Definition of done

- [x] One empty-root implementation passes every advertised Entity/Source/KeyValue claim.
- [x] Unsaved mutation of original, point-read, or query-returned objects cannot change stored state.
- [x] Host/source/root/partition state is isolated, finite, and discarded with the host.
- [x] Physical maps, External lifecycle, streaming, durability, native plans, and atomic batch remain corrective declines.
- [ ] Strict Forge has a complete versioned packet or remains explicitly deferred.

## Stop conditions

Process-static state, retained caller references, unbounded store growth, a second query/batch owner, hidden streaming,
weakened source policy, a compatibility bridge, or a fabricated durability/atomicity claim blocks certification.
