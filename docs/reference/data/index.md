---
type: REF
domain: data
title: "Entity Data Foundation"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
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

This is a pre-1.0 candidate boundary, not a blanket production-support claim. Build from source today;
the public 0.17.0 package set is not coherent. The staged package closure proves the intended path but
has not been published and observed.

## Smallest durable application path

The maintained Level-1 application references:

- `Sylin.Koan.App` for Core, Entity data, the JSON fallback, and controller-based Web composition;
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
| InMemory | Fast conformance oracle and ephemeral test/development store | connector 55/55; Koan.Testing 12 passed with 3 capability/trait skips | Process-local and non-durable; never a production persistence claim. |
| JSON | Zero-infrastructure fallback carried by `Sylin.Koan` | connector 14/14; selection-aware readiness 19/19 | File-backed, limited concurrency, and not the durable V1 application proof. |

Postgres, SQL Server, MongoDB, Couchbase, Redis, and other providers are valuable extensions. Each
needs its own current conformance, operations, packaging, and compatibility evidence; their existence
does not expand this foundation boundary.

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

- Use `Get`, `Query`, `FirstPage`/`Page`, `AllStream`, `Save`, and `Remove` as the common vocabulary.
- Prefer explicit paging when the result can grow; use Entity streams only when the selected adapter
  advertises `DataCaps.Query.ProviderBoundedPaging`.
- Inspect `Data<TEntity, string>.Capabilities` before relying on optional bulk, transaction, filter,
  or isolation behavior.
- Unsupported guarantees reject explicitly; Koan does not silently claim backend parity.

The low-level `Data<TEntity,TKey>` facade, direct provider instructions, and raw access remain expert
escape hatches. They do not replace Entity statics in ordinary business code.

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
- Use `/health/ready` for aggregate dependency readiness; `/api/health` is only a shallow compatibility
  up-check.
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
