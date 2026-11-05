# Sylin.Koan.Data.Core

Data access core for Koan: common primitives, options, and helpers used by relational/document/vector providers and apps.

- Target framework: net10.0
- License: Apache-2.0

## Capabilities

- Entity contracts and helpers for aggregate storage
- Options and conventions shared across data adapters
- Data-owned adapter option binding for readiness and default paging
- Immutable named-source lifecycle/access policy enforced before readiness or provider work
- Separate host-owned reachability, declared-shape validation, and authorized provisioning stages
- Source-only inspection and registered reads without an Entity repository shim
- Bounded provider-neutral `RecordSet` materialization and compiled ordinal DTO projection
- Compact provider-neutral aggregate mapping with host-scoped compiled plans and shared physical encodings
- Selection-aware health semantics for data connectors
- Support for paging, streaming, and batching semantics (see references)

## Install (minimal setup)

```powershell
dotnet add package Sylin.Koan.Data.Core
```

## Usage - quick examples

Constrained generated endpoints automatically use Data's insert-only terminal when their visible read
cannot find the submitted identity. The elected adapter must support native atomic insertion; no
application registration is required. Existing `Save` remains an upsert. A conflicting
insert exposes no prior row and runs no completion lifecycle. This does not make authorized updates
conditional on their earlier read or provide atomic mixed bulk writes.

- Prefer first-class model statics for top-level data access in your app models:
  - `Item.All(ct)`
  - `Item.Query(predicate, ct)`
  - `Item.FirstPage(size, ct)` and `Item.Page(pageNumber, pageSize, ct)`
  - `Item.QueryStream(predicate, ct)`
- List-returning `All`/`Query` operations preserve null count intent, even with sort and pagination. Use
  `QueryWithCount` when the caller needs a total; it defaults to the optimized count strategy unless the
  query selects another strategy explicitly.
- `AllStream`/`QueryStream` lazily compose numbered pages only when the selected adapter proves provider-bounded
  paging and complete ordering. Otherwise they reject correctively before query/yield; there is no materializing
  fallback. `batchSize` bounds Koan-visible candidates, not opaque driver buffers. No public cursor or resume-token
  API exists.
- If a first-class static isn’t available, you can fall back to the generic facade (second-class): `Data<TEntity, TKey>.Query(...)`.

Host-owned persistence policy composes beside the normal zero-configuration bootstrap:

```csharp
services.AddKoan(() =>
    Item.Lifecycle.BeforeUpsert(context =>
        context.Current.Price < 0
            ? context.Cancel("Price cannot be negative.", "item.price")
            : context.Proceed()));
```

The same Lifecycle boundary governs Entity/Data calls and generated REST/MCP entity operations. See
`/docs/reference/data/entity-lifecycle.md` for phases, bulk/transaction semantics, and deliberate
bypasses.

Use a named source when the application needs to inspect or query an external system without adopting Entity
persistence. The connector supplies the final native binding leaf; the application declares the business name,
parameters, lane, and bounds once:

```csharp
services.AddKoan(koan =>
{
    koan.Data.Source("LegacyErp")
        .Query("orders.recent", query => query
            .Lane("Reports")
            .Sql("select ... where CREATED_UTC >= @since")
            .Parameter<DateTimeOffset>("since")
            .MaxRecords(500));
});

var source = Data.Source("LegacyErp");
var description = source.Describe();
var explanation = source.Explain("orders.recent");
var diagnosis = await source.Doctor(ct);
var recent = await source.Query("orders.recent", new { since }, ct);
var orders = recent.Project<RecentOrder>();
var containers = await source.Inspect().Containers(50, ct: ct);
```

The runtime call cannot change the provider payload, source, or read lane. Opaque bindings require a
provider-enforced read lane. Inspection uses neutral containers rather than schemas/tables, and `RecordSet` preserves
duplicate fields, missing/null, nested values, explicit bounds, and honest completion. `Describe` and `Explain` are
pure and never activate a client; `Doctor` is explicit, bounded, non-mutating, and returns stable corrective findings.

Map an Entity to an external record shape in the same source declaration:

```csharp
services.AddKoan(koan =>
{
    koan.Data.Source("LegacyErp").Map<Customer>(map => map
        .Container("dbo", "CUSTOMER")
        .Key(customer => customer.Id).Name("CUSTOMER_NO")
        .Property(customer => customer.Name.Full).Name("DISPLAY_NM")
        .Property(customer => customer.Profile).Object("PROFILE_JSON"));
});
```

`Name` is a scalar physical value, `Object` keeps one logical subtree together, and `Path("NAME_DATA", "full")`
places a flat logical value inside a structured physical value. `Key(...).Parts(...)`, `Generated`, `ReadOnly`, and
`Codec` remain explicit only when needed.

When insert/update distinction matters, ask for it explicitly; ordinary `Save` stays the low-ceremony default:

```csharp
var result = await item.SaveWithOutcome(ct);
// result.Outcome is Inserted or Updated; result.CommitOutcome is Committed.
```

This stronger path is available only when the selected adapter proves exact native mutation outcomes. Bulk writes
prepare the complete Entity set, dispatch once, and reject an inexact affected-count receipt without replay.

Child relationships are strict by default. Native and in-memory providers execute directly; a
scan-backed provider fails with a corrective `RelationshipQueryRejectedException` unless the call
chooses a finite budget:

```csharp
var children = await todo.GetChildren<TodoItem>(
    RelationshipQueryPolicy.Bounded(maxCandidates: 1_000, maxResults: 200), ct);
```

This policy bounds candidates before rows escape and never returns a partial relationship.

Load every declared direct edge with the same `Relatives` operation for one Entity, a finite
selection, or a provider-bounded stream. Model and key types are inferred:

```csharp
var graph = await todo.Relatives(ct);
var graphs = await todos.Where(todo => !todo.IsCompleted).Relatives(ct);

await foreach (var current in Todo.QueryStream(todo => !todo.IsCompleted).Relatives(ct))
{
    // current.Entity, current.Parents, current.Children
}
```

Finite and stream forms preserve source order and multiplicity. Parent edges use batched keyed reads;
child edges retain the same strict or explicitly bounded negotiation and runtime facts as
`GetChildren<TChild>`.

Required Entity/Data operations without a usable Koan host throw `KoanHostContextException`. Its
`Failure`, `Operation`, and `RequiredService` properties distinguish an absent host, a disposed host,
and a host where the Data module was not composed.

`EntityContext` is deliberately Data-specific: it scopes source, adapter, partition, cache, and
transaction routing. It stores that state in Core's logical-flow `KoanContext`, but it is not the
generic API for tenancy, subjects, or other module-owned axes. Those modules own their business-facing
facades and register durable carriage independently through `Koan.Core.Context`.

Named sources default to `Managed + ReadWrite`. Integrate an existing system without granting Koan
shape or data mutation authority by declaring the two independent ceilings once:

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

Entity, batch, instruction, and Direct paths consume the same immutable source plan. `ReadOnly`
blocks data writes; `External` independently blocks create/alter/drop/repair. Forbidden and opaque
effects reject before lifecycle callbacks, readiness, resource creation, or provider I/O. Provider
credentials remain the security boundary. A `Read` label on SQL is the caller's word, so SQLite and
PostgreSQL run Read instructions on their raw-read lane, where the engine refuses a write. Opaque
instructions, like raw queries, fail closed under any active managed or read scope (soft delete, data
axes, request row filters), and `DataInstructions.Clear` runs as `DeleteAll` (refused for soft-delete
entities, DATA-0129).

For a synchronous console process, `new ServiceCollection().StartKoan()` starts a standard .NET
Generic Host and returns its active provider facade. The caller owns it; use
`using var app = (IDisposable)services.StartKoan()` so disposal stops hosted capabilities and releases
the ambient Koan host binding. ASP.NET Core and workers continue to use their native host builder with
`AddKoan()`.

## Boundaries and failures

- Data Core is the Entity runtime and provider-election owner; it does not provide storage by itself. Reference a
  connector or use the JSON floor carried by a Koan entry bundle.
- Its dependency on Cache Abstractions is inert contract vocabulary. Referencing Data Core does not activate caching.
- Stream-shaped Entity APIs run only when the elected provider proves bounded paging. Unsupported providers reject
  before yielding instead of hiding whole-source materialization.
- Query lifecycle callbacks observe only the final visible rows after residual filtering, sort/page completion, and
  projection; discarded provider candidates never escape through Lifecycle.
- Atomic batches and exact per-item outcomes are negotiated execution seams. `RequireAtomic` rejects before deferred
  mutation loads or callbacks when the selected adapter cannot prove it.
- `Batch().Delete(id)` deletes only a row the caller's tenant, axis, soft-delete or other read scope can see, exactly
  like `Remove(id)`. An id outside the scope is not deleted and its item reports `Missing` (DATA-0124).
- On adapters that declare a conditional delete (SQLite, PostgreSQL, MongoDB, InMemory), a scoped `Remove`, bulk
  remove or batch delete runs as one store statement carrying the scope, so a row that leaves the scope mid-flight is
  not removed. Elsewhere the delete is checked first and is not atomic; `DataAxis.Explain<T>()` says which (DATA-0156).
- `Copy`, `Move` and `Mirror` compare the physical places their endpoints reach, not how they are written. A `Move` or
  `Mirror` whose endpoints resolve to one container is refused before any read; a `Copy` onto itself writes nothing
  and says so in its warnings (DATA-0125).
- Transfers carry rows exactly by default: stored values and OnSave timestamps arrive unchanged, and no Lifecycle or
  write stamp runs for the transfer's reads and writes. Tenancy, policy, isolation and receipts still apply.
  `.Fidelity(TransferFidelity.Semantic)` writes each row as a Save instead (DATA-0149).
- Ambient `EntityContext.Transaction` is deferred coordination. On one route whose provider declares
  `write.atomicCommit` (SQLite) its commit is one native transaction across entity types; elsewhere it is sequential
  dispatch, a failed commit can leave its completed prefix durable, and rollback discards only undispatched work.
  `TransactionGuarantee.Atomic` makes the native transaction a requirement that is refused where it cannot be met
  (DATA-0158). For one Entity root, `BatchOptions(RequireAtomic: true)` is the per-root all-or-nothing seam.
- `EntityContext.Hold` holds that native transaction from the start of the scope instead of opening it at commit, on a
  route that declares `write.heldTransaction` (SQLite), and is refused elsewhere (DATA-0166). The body reads its own
  writes, a write acts when it is issued, and other writers wait, in another process too.
- `command.Send(ct)` runs an `ICommand<TResult>` in a hold of its own, in this flow, and answers with an `Outcome<TResult>`; a route
  with no holds refuses it (DATA-0167). `command.Send(clock, ct)` runs that hold by a `TimeProvider` the sender names for this send; every
  other hold runs by the host's registered `TimeProvider`, or the system's (DATA-0169).
- Relationship expansion is direct-edge and budgeted. It is not recursive graph traversal, snapshot isolation, or a
  promise that scan-backed providers can execute without an explicit candidate limit.
- `EntityContext` owns Data routing dimensions and the clock a hold runs by (`EntityContext.Clock`); tenancy, subject, and other
  semantic axes are contributed and enforced by their owning modules.
- Canonical `PatchPayload` is provider-neutral. Web/MCP projections own protocol parsing and normalization before
  the operation reaches Data.
- Required Entity operations without a live composed host throw `KoanHostContextException` with the missing
  operation/service correction.
- A business operation is never treated as a missing-shape probe, provisioned from exception text, and replayed.
  Adapters translate native types/codes through the Data failure-classification seam.
- Registered reads are uncached and never replayed. Unknown effects, missing/unenforced lanes, parameter drift,
  scalar cardinality drift, extra result channels, and active segmentation reject before unsafe exposure.
- Direct typed ADO queries share `RecordSet`'s compiled ordinal projection; they do not serialize dictionaries to JSON.

## Customization

- Configuration and advanced usage are documented in
  [TECHNICAL.md](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.Data.Core/TECHNICAL.md).

## References

- [Data access patterns](https://github.com/sylin-org/koan-framework/blob/main/docs/guides/data/entity-access-and-streaming.md)
- [Provider-bounded Entity streams](https://github.com/sylin-org/koan-framework/blob/main/docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Engineering guardrails](https://github.com/sylin-org/koan-framework/blob/main/docs/engineering/README.md)
- Repo: https://github.com/sylin-org/koan-framework

## Enum storage contract

Default Entity storage preserves enum names as strings, including nullable values, nested values, collections,
and declared EnumMember aliases. Unnamed numeric values fail instead of silently changing the storage format.
Queries use the same spelling. Ordinary enum ordering uses declared ordinal ranks in native expressions while
the stored value stays a string; native ordering of arbitrary Flags combinations rejects correctively.
An explicit external mapping codec remains responsible for its declared physical representation.
Rows or columns that hold numeric enum values are not rewritten automatically; they require an explicit
migration to the string representation.
## Counterpart reads

Use `Entity.AllWithCount(QueryDefinition.All.Where(Filter.SameIdIn<T>(policy, "")))`
when content must have an eligible same-ID counterpart in the default partition. Set the
content partition on QueryDefinition. Ordinary lambda and JSON query overloads AND their
predicate with the structured filter. QueryStream accepts QueryDefinition for the same
provider-bounded path. Both operands retain Data source and isolation guards; unsupported
native shapes reject. Count and page share predicate meaning, not a transactional snapshot.

Explicit empty partitions select default in structured reads and positional Entity Get/GetMany
helpers; null retains the ambient partition. The caller's scope is restored after execution.

## JSON collection round trips

Entity JSON hydration and standard typed Web input share additive collection construction from
Core.Json. JSON arrays reconstruct concrete collections with a public constructor and `Add(T)`;
constructor-seeded values require `Clear()`. Persistence-only private-setter restoration and family
discriminators remain in Data and are not enabled for request binding.

## Guarded replacement

`await replacement.ReplaceIf(stored => stored.Revision == expected, partition: locale, ct: ct)`
replaces an existing row only when its native guard matches. False means missing or conflict; it
never inserts; Data dispatches once. Existing bounded failed-compare retries remain adapter behavior.
Null partition inherits, empty selects default. The guard is captured when
called, before asynchronous preparation. Ordinary `Save` retains upsert behavior.

Use a qualified connector and an ordinary persisted revision field. Unsupported native predicates,
variants and active managed/read scopes refuse. BeforeUpsert may run on conflict; AfterUpsert runs
only after success. A cancellation, cache error or lifecycle exception after dispatch does not imply
rollback. This protects one destination row, not equality with another document at commit time.

## Deferred transaction writes

Operations enlisted in a Koan transaction are deferred, not executed inline. Each tracked write
restores the source, adapter, and partition captured when it was enlisted; an explicitly selected
default partition clears that axis instead of inheriting the commit caller's scope. Commit runs the
tracked operations in call order and returns a Task. When they all target one route whose provider
declares `write.atomicCommit`, they run inside one native transaction: a failure rolls it back and the
`TransactionException` reports `NotCommitted` with zero completed operations. Otherwise they run
sequentially, and a dispatched-operation failure carries a `TransactionException` with completed count,
unknown commit outcome, and retry/replay dispositions. `EntityContext.Transaction(name,
TransactionGuarantee.Atomic)` requires the native transaction and refuses, when the operation is
tracked, anything that cannot join it (DATA-0158). Coordination never claims atomicity across stores,
and executing a tracked operation suppresses the surrounding transaction to prevent recursive enlistment.

## Send a write as a value

A write that decides from what it reads is a command, and it answers with what happened (DATA-0167):

```csharp
public sealed record Reply(string TopicId, string Body) : ICommand<Post>
{
    public static readonly Reason NoTopic = Reason.Absent("No Topic has this Id.");
    public static readonly Reason Locked = Reason.Gated("locked", "This topic is locked, so it takes no reply.");
    public static readonly Reason NeedsBody = Reason.Invalid("A Post's body holds at least one visible character.");

    public async Task<Outcome<Post>> Run(CancellationToken ct)
    {
        if (await Post.Get(TopicId, ct) is not { } topic) return new Outcome<Post>.Refused(NoTopic);
        if (topic.Locked) return new Outcome<Post>.Refused(Locked);
        if (string.IsNullOrWhiteSpace(Body)) throw new Refusal(NeedsBody);
        var post = await new Post { ParentId = topic.Id, Body = Body }.Save(ct);
        await new Event { Seq = await Event.Log.Next(ct), PostId = post.Id }.Save(ct);
        return new Outcome<Post>.Done(post);
    }
}

var outcome = await new Reply(topicId, "hi").Send(ct);
```

- It runs in a hold of its own, in this flow, so in its sender's partition, source and tenant. Commands take turns in arrival order, with every other
  hold of the store. Only `Done` commits; every other outcome, a `Refusal` thrown from below it and any other throw leave nothing. A role change joins it
  (SEC-0036). `Commit` and `Rollback` refuse inside it.
- `Outcome<T>` is `Done`, `Refused` or `Failed`. A `Reason` declares its kind once, where it is defined: `Reason.Invalid(text)`, or `Reason.Invalid(field, text)`
  naming the wire field it concerns, `Reason.Absent(text)`, `Reason.Gated(text)` or `Reason.Gated(word, text)`, and `Reason.Conflict(text)` or
  `Reason.Conflict(word, text)`. Every reason has a word a client acts on, its kind's where it names none. Equality is the record's, and a string is
  never a reason (ARCH-0143, ARCH-0145). `throw new Refusal(reason)` from below a command reaches its sender the same way. `Failed.Landed` says `NotDispatched` (it never ran: the
  sender gave up, the store's busy timeout ran out, the route cannot hold, or a scope is open around the send), `NotCommitted` (nothing landed) or `Unknown` (the native commit
  itself failed). A command that fails throws; only `Send` says `Failed`.
- The token ends the wait and the command, and the outcome says what happened. Nothing cuts a commit short. A command that never returns hangs its own
  sender, and `HoldLimit` frees the store.
- A write is sent by whoever asks for it, in their own flow, and a read starts none (DATA-0168).
- `Sequence.Of<Event>(e => e.Seq)` names a column under `[Index(Unique = true)]`, and `Next(ct)` numbers it with no gap in what commits, across
  processes too. It reads the top of the column at its first use in a hold, and refuses outside one, and over an entity a read scope such as soft
  delete hides rows of.
- `Head(ct)` is the highest number that has committed, with every number below it committed too (DATA-0169). The first ask reads the store; after it
  the host answers from memory, and each hold that commits raises it once, before its sender is answered. `WaitPast(seen, bound, ct)` waits for the head
  to pass `seen`, for at most a bound it must give, and answers the head then: a commit of this process wakes it at once, and at its bound it reads the
  store, so another process's commit reaches it within one bound. A head nobody waits on learns of no other process's commit. Both refuse inside a hold,
  where `Highest` is the read, and wherever the column is read from anything but the entity's default store: a source, an adapter, a partition, a
  container a storage-name particle names, or an entity with an equality field such as a tenant's.
- A command reads the time from `EntityContext.Clock`, the clock its hold runs by; outside a hold it is the host's `TimeProvider`. The `[Timestamp]`
  stamp stays on the system clock.
- Work that must follow a command's commit registers `EntityContext.UncommittedScope.AfterCommit` from inside it. It runs only if the command commits, once the
  turn has passed on and the heads are raised, and before the sender is answered, so it can send. A fact other code acts on is an Entity event, which waits for
  the commit too.

## Hold a transaction from the start of a scope

Deferred writes cannot be read back, cannot report an outcome, and leave a read inside the scope free to be overtaken.
A scope that needs those holds the native transaction from its start instead (DATA-0166):

```csharp
using (await EntityContext.Hold("reply", ct))
{
    var topic = await Topic.Get(id, ct);
    var post = await new Post { TopicId = topic!.Id, Sequence = topic.Posts + 1 }.Save(ct);
    var stored = await Post.Get(post.Id, ct);   // its own write
    await EntityContext.Commit(ct);
}
```

Every read and write of the route from that flow runs inside it, including batches, conditional writes and SQL
instructions that a deferred scope refuses; a lifecycle handler runs and a unique index answers at the save, a failed batch
or bulk save leaves nothing of itself while the hold goes on, and a receipt reports `Inserted` or `Updated` with `NotCommitted`, because only
`Commit` commits. Any other writer of the store waits,
in another process too, for at most the store's busy timeout; a hold that waits that long throws `TransactionException`
with `NotCommitted`. Disposal without a commit rolls back. A hold keeps every other writer out, so keep it short:
past `TransactionOptions.HoldLimit` (20 seconds) it rolls back and refuses the rest of its scope. It covers the one route
the ambient context names; a write elsewhere refuses, and a read elsewhere sees committed state. A Direct write, a vector write and a
transfer refuse inside it; a named read on a lane keeps its own connection and sees committed state.

## Insert without replacement

```csharp
var result = await candidate.Insert(partition: "", ct: ct);
// Or: await Work.Insert(candidate, partition: "", ct: ct).
```

Use ordinary `AddKoan()` with a qualified connector. `Insert` returns the existing
`MutationResult<TEntity,TKey>`: Inserted/Committed includes the application entity and assigned key;
Conflict/NotCommitted includes the submitted key and no entity. A conflict leaves the existing row
unchanged, including a row hidden from an earlier read. BeforeUpsert can run on conflict with no Prior;
only verified insertion runs AfterUpsert. Data does not retry.

Null partition inherits the current context; empty selects default; a named value selects that
partition. Ordinary single-entity `Save(partition: "")` also selects default and remains an upsert.
The caller's scope is restored on completion, refusal or cancellation.

Mongo qualifies acknowledged managed `_id` insertion with an assigned non-default key; SQLite also
supports its qualified generated-key mapping; InMemory provides host-local atomic insertion.
Unsupported providers, identity mappings and deferred coordination refuse. Other unique constraints,
ambiguous commits and completion failures remain exceptions; an exception does not prove rollback.
Choose recovery according to the business operation, without assuming a failed await means no row.

Family root and inherited variant statics return the root-typed receipt. A string-key variant receiver
returns its exact typed receipt; a root call still preserves a registered runtime variant and the
family's shared identity. This does not expand the generated companion's point-Get grammar.
