---
type: REF
domain: data
title: "Entity Data Foundation"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-15
  status: verified
  scope: Entity data grammar, local-provider roles, negotiation, and current support boundary
---

# Entity data foundation

## Current contract

Koan's data foundation is `Entity<T>` plus deterministic provider negotiation. The common application
path needs no repository, `DbContext`, provider registration, or schema bootstrap code:

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

var saved = await new Todo { Title = "Ship the meaningful step" }.Save();
var same = await Todo.Get(saved.Id);
var open = await Todo.Query(todo => !todo.Done);
await saved.Remove();
```

Entity persistence, query, and SQLite belong to the supported 0.20 foundation. That guarantee does not extend to
every available provider: use the [generated product surface](../product-surface.md) for the exact boundary.
Public-feed publication and observation follow the final package-only proof; the source checkout and staged
candidate exercise the same application contract today.

## Smallest durable application path

The maintained Level-1 application references:

- `Sylin.Koan.App` for Core, Entity data, the automatic JSON floor, and controller-based Web composition;
- `Sylin.Koan.Data.Connector.Sqlite` for the selected durable local provider; and
- only the capability packages the application actually needs.

`AddKoan()` is the complete registration call. Referencing SQLite makes it available; FirstUse also
pins its business Entity to SQLite so adding another connector cannot silently move its data.

[`samples/FirstUse`](../../../samples/FirstUse/README.md) is the executable contract. It proves SQLite
create/read/query, REST, startup facts, readiness, a checked-in composition lockfile, and agent/operator
inspection from source and from a staged package-only clean room.

## Local provider roles

The providers below are deliberately not described as interchangeable.

| Provider | Role in the foundation | Current evidence | Explicit limits |
|---|---|---|---|
| SQLite | Durable local/single-node application path | connector 35/35; FirstUse 8-step and GoldenJourney 11-step source/package proofs | No claim for multi-node writes, every transaction shape, production migration policy, or remote-database behavior. |
| InMemory | Fast conformance oracle and ephemeral test/development store | connector 56/56; Koan.Testing 12 passed with 3 capability/trait skips | Process-local and non-durable; never a production persistence claim. |
| JSON | Automatic zero-infrastructure floor carried by `Sylin.Koan` | connector 21/21, including selection-aware readiness and persistence safety | File-backed, limited concurrency, and not the durable V1 application proof. |

PostgreSQL, SQL Server, and MongoDB are supported networked extensions outside that local foundation.
Their real provider suites cover the capabilities each adapter declares, including Entity CRUD/query,
batch, filtering, paging/streaming, source routing, health, field transforms, and isolation. Their
first-publication package consumers prove normal `AddKoan()` selection and Entity save/get/query
against the selected service. Each requires its reachable database and retains its documented schema,
ordering, streaming, query-subset, consistency, and operational limits.

Couchbase, Redis, and other providers remain valuable lower-maturity extensions. Each needs its own
current conformance, operations, packaging, and compatibility evidence; another provider's promotion
does not confer support on a sibling.

## Reference is availability; negotiation selects

Provider selection follows one order:

1. an explicit `EntityContext` source;
2. a database-axis route;
3. an explicit `EntityContext` adapter override;
4. `[SourceAdapter]` or `[DataAdapter]` on the Entity;
5. `Koan:Data:Sources:Default:Adapter`; then
6. the highest-priority referenced provider.

Configured intent is fail-loud. If the selected adapter was not referenced, Koan reports
`adapter-unavailable`, lists referenced choices, and names the configuration/package correction. It
does not substitute a different provider. Availability alone is inspectable but does not make an
unused connector a readiness dependency.

Use explicit selection when business durability must not change as references grow:

```csharp
[DataAdapter("sqlite")]
public sealed class Approval : Entity<Approval>
{
    public string Subject { get; set; } = "";
}
```

Or configure the application default:

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": { "Adapter": "sqlite" }
      },
      "Sqlite": {
        "ConnectionString": "Data Source=./data/app.db"
      }
    }
  }
}
```

## Query and cost honesty

One Entity grammar does not mean every provider has the same physical behavior. Capability facts
describe whether filtering is native, bounded residual work, or in-memory. Provider-specific tests
earn provider-specific claims.

Pagination intent belongs to the caller, not the adapter. `Product.All()` requests the complete
visible set; an adapter does not invent a default limit. `Product.Page(page, size)` and an explicitly
paged `QueryDefinition` carry an exact page request to the selected provider. Consumer boundaries
such as Web may apply documented defaults and safety bounds, but they compile that policy into an
explicit page before Data executes it.

- Use `Get`, `Query`, `FirstPage`/`Page`, `AllStream`, `Save`, and `Remove` as the common vocabulary.
- Prefer explicit paging when the result can grow; use Entity streams only when the selected adapter
  advertises `DataCaps.Query.ProviderBoundedPaging`.
- Inspect `Data<TEntity, string>.Capabilities` before relying on optional bulk, transaction, filter,
  or isolation behavior.
- Unsupported guarantees reject explicitly; Koan does not silently claim backend parity.

The low-level `Data<TEntity,TKey>` facade, direct provider instructions, and raw access remain expert
escape hatches. They do not replace Entity statics in ordinary business code.

## Optional semantics and recovery

Reference optional Data packages only when their business meaning applies:

- `Sylin.Koan.Data.SoftDelete` lets `[SoftDelete]` Entities retain ordinary `Remove()` grammar while hiding rows
  through one Data axis. `T.WithDeleted()` is a type-targeted recycle-bin scope; `.Restore()` and `.HardDelete()` are
  the explicit recovery and purge verbs. It supplies no generic HTTP workflow or authorization bypass.
- `Sylin.Koan.Data.Backup` supplies one DI-owned, single-Entity archive/recovery round trip through Koan Storage.
  Create requires provider-bounded paging; restore validates the complete archive before its first batched upsert.
  It does not claim whole-application coordination, encryption, retention, schema migration, or transactional restore.

Both packages compose through the application's existing `AddKoan()` call. See their package-owned README and
technical contracts for the complete operation surface and limits.

## Testing the contract

Reference `Sylin.Koan.Testing` and add one class per Entity:

```csharp
public sealed class TodoConformance : EntityConformanceSpecs<Todo>
{
    protected override Todo NewValid() => new() { Title = "A valid business example" };
}
```

Koan owns the real host, temporary storage, Entity partition, and async-flow host binding. Independent
conformance classes can use normal xUnit scheduling. Trait/capability batteries skip explicitly when
they do not apply; a skip is absence of evidence, not provider certification.

## Inspect and recover

- Read startup's data election and composition summary first.
- Use `/health/ready` for aggregate dependency readiness and `/health/live` for process liveness.
- Read `/.well-known/Koan/facts` as an operator or `koan://facts` as an MCP client; both project the
  same redacted schema-1 decisions.
- Review `koan.lock.json` for referenced-module drift. Runtime facts add the provider actually elected.

## Not in this foundation boundary

- universal provider parity or production certification;
- schema migrations/upgrades, rollback, or long-term compatibility guarantees;
- cross-provider or distributed transactions;
- tenancy, concurrency control, audit, recovery, SLOs, or security posture;
- vector/AI storage semantics; or
- an assumption that adding a provider reference changes existing Entity routing safely without an
  explicit election review.

Those capabilities graduate in later rings with their own evidence.

## Related contracts

- [Entity Semantics Contract](../../architecture/entity-semantics-contract.md)
- [Adapter diagnostics](adapter-diagnostics.md)
- [Entity access and streaming](../../guides/data/entity-access-and-streaming.md)
- [Testing your app](../../guides/testing-your-app.md)
- [Current capability evidence](../../initiatives/koan-v1/CAPABILITIES.md)
