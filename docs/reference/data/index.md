---
type: REF
domain: data
title: "Persist and query business state"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: verified
  scope: Entity grammar, provider choice, query cost, relationships, lifecycle, and correction paths
---

# Persist and query business state

Use Koan Data when an application needs to store, find, relate, page, or process business objects.
Application code works with its own Entity types; the referenced connector owns the physical store.
There is no repository, `DbContext`, provider-registration method, or schema bootstrap in the ordinary
application path.

## Smallest useful result

Add the application bundle and one durable provider:

```powershell
dotnet add package Sylin.Koan.App
dotnet add package Sylin.Koan.Data.Connector.Sqlite
```

Keep the normal Koan bootstrap:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

var app = builder.Build();
await app.RunAsync();
```

Then model and use business state directly:

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

var todo = await new Todo { Title = "Ship one useful thing" }.Save(ct);
var same = await Todo.Get(todo.Id, ct);
var open = await Todo.Query(item => !item.Done, ct);
await todo.Remove(ct);
```

Referencing SQLite makes the connector available. `AddKoan()` discovers it, elects it, creates the
default local schema on first use, and exposes the same Entity operations to application code,
generated REST controllers, and generated MCP tools.

## The application vocabulary

| Need | Use | Cost signal |
|---|---|---|
| One known Entity | `Todo.Get(id, ct)` | Keyed lookup |
| A filtered, bounded result | `Todo.Query(predicate, ct)` | Materialized result; provider execution varies |
| One UI or API page | `Todo.FirstPage(size, ct)` or `Todo.Page(number, size, ct)` | Caller supplies the bound |
| A large sequential workload | `Todo.AllStream(...)` or `Todo.QueryStream(...)` | Only on a provider that proves bounded paging |
| Create or update | `entity.Save(ct)` | One logical write |
| Delete | `entity.Remove(ct)` | One logical removal |
| A finite batch | `Todo.UpsertMany(items, ct)` | Atomicity depends on the provider capability |

Use `Entity<T, TKey>` when the identifier is not the default string key. The lower-level
`Data<TEntity, TKey>` facade, direct provider instructions, and raw access are expert escape hatches,
not an application architecture requirement.

## Choose a provider by business need

Reference only the connectors the application can actually use. The
[product surface](../product-surface.md) is the authority for current maturity; each connector README
owns its setup and backend-specific limits.

| Need | Connector | Important boundary |
|---|---|---|
| Durable local or single-node state | `Sylin.Koan.Data.Connector.Sqlite` | The simplest durable application path |
| Process-local tests or ephemeral work | `Sylin.Koan.Data.Connector.InMemory` | Non-durable; not a production persistence claim |
| File-backed, zero-infrastructure state | JSON provider included by the application bundle | Limited concurrency; not the durable foundation path |
| PostgreSQL | `Sylin.Koan.Data.Connector.Postgres` | External service and provider-owned schema/operations apply |
| SQL Server | `Sylin.Koan.Data.Connector.SqlServer` | External service and provider-owned schema/operations apply |
| MongoDB | `Sylin.Koan.Data.Connector.Mongo` | Document-store query and consistency limits apply |
| Couchbase | `Sylin.Koan.Data.Connector.Couchbase` | Bucket, query-service, and consistency limits apply |
| CockroachDB | `Sylin.Koan.Data.Connector.Cockroach` | Cockroach-specific routing and schema policy apply |
| Redis-backed keyed state | `Sylin.Koan.Data.Connector.Redis` | Queries may scan; Entity streams reject |

Search and vector stores solve different needs. Start from the [AI pillar](../ai/index.md) for
embedding/vector retrieval and from each search connector's package documentation for indexed search.
Do not infer that every Data connector has identical query, transaction, ordering, or consistency
semantics merely because the Entity vocabulary is stable.

## Pin important routing

When more than one connector is referenced, make durability intent explicit on the Entity:

```csharp
[DataAdapter("sqlite")]
public sealed class Approval : Entity<Approval>
{
    public string Subject { get; set; } = "";
}
```

Or choose the application default in configuration:

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": {
          "Adapter": "sqlite",
          "ConnectionString": "Data Source=./data/app.db"
        }
      }
    }
  }
}
```

Explicit intent fails loudly. If the selected adapter is not referenced, Koan reports
`adapter-unavailable`, lists the available choices, and names the package or configuration correction.
It does not silently move the Entity to another store.

## Relationships stay in the model

Declare a direct edge with `[Parent]`, then navigate it without introducing a repository layer:

```csharp
public sealed class Order : Entity<Order>
{
    [Parent(typeof(Customer))]
    public string CustomerId { get; set; } = "";
}

var customer = await order.GetParent<Customer>(ct);
var orders = await customer.GetChildren<Order>(ct);
```

Koan rejects an unbounded scan-backed relationship unless the call explicitly supplies a finite
candidate/result budget. Direct-edge navigation is not recursive graph traversal or snapshot
isolation.

## Add write policy only when the business needs it

The parameterless `AddKoan()` remains complete. Compose host-owned persistence rules only for a real
business invariant:

```csharp
builder.Services.AddKoan(() =>
    Product.Lifecycle.BeforeUpsert(context =>
        context.Current.Price < 0
            ? context.Cancel("Price cannot be negative.", "product.price")
            : context.Proceed()));
```

Lifecycle policy applies at the common Entity/Data boundary, so ordinary Entity calls and generated
protocol surfaces have the same persistence meaning. See [Entity lifecycle](entity-lifecycle.md) for
phases, prior values, protected fields, bulk behavior, transactions, and deliberate bypasses.

## Query and streaming honesty

A shared vocabulary does not hide physical cost:

- Prefer an explicit page when a request result can grow.
- Use `All()` only when fully materializing the visible set is intentional.
- Use Entity streams only when the elected connector advertises provider-bounded paging.
- Treat a capability rejection as a design correction, not a reason to add a silent full-set fallback.
- Inspect `Data<TEntity, string>.Capabilities` before relying on optional bulk, transaction, filter,
  or isolation behavior.

The [Entity access and streaming guide](../../guides/data/entity-access-and-streaming.md) owns the
qualified-provider matrix, ordering floor, cancellation behavior, offset-paging consistency limits,
and corrective exception.

## Verify the application contract

`Sylin.Koan.Testing` can exercise the same host and Entity surface used by the application:

```csharp
public sealed class TodoConformance : EntityConformanceSpecs<Todo>
{
    protected override Todo NewValid() => new() { Title = "A valid example" };
}
```

Capability-specific batteries skip when their prerequisite is absent. A skip means the test did not
produce evidence; it is not provider certification. External connectors still require reachable
services and provider-specific tests for the guarantees the application relies on.

## Inspect and correct

- Read startup's Data election and composition summary first.
- Use `/health/ready` for participating dependency readiness and `/health/live` for process liveness.
- Read `/.well-known/Koan/facts` or `koan://facts` for the same redacted runtime decisions.
- Review `koan.lock.json` for referenced-module drift; runtime facts add the connector actually elected.
- If an available connector is unused, it remains non-critical and must not open a connection merely
  because its package was referenced.

[Adapter diagnostics and readiness](adapter-diagnostics.md) owns the operator and connector-author
contract for availability, participation, health, route identity, and redaction.

## Add only the semantics you need

- `Sylin.Koan.Data.SoftDelete` keeps ordinary `Remove()` grammar while adding explicit recycle-bin,
  restore, and purge behavior for opted-in Entities.

The package owns its complete API and limitations in its README and technical companion. Data Backup
is shelved and is not a greenfield 0.20 capability; it does not belong in the application path above.

## Continue by task

- [Entity lifecycle](entity-lifecycle.md) — enforce persistence invariants.
- [Entity access and streaming](../../guides/data/entity-access-and-streaming.md) — process large sets
  without disguising provider cost.
- [Adapter diagnostics and readiness](adapter-diagnostics.md) — operate or author a connector.
- [Testing your app](../../guides/testing-your-app.md) — prove the application-facing contract.
- [`FirstUse`](../../../samples/FirstUse/README.md) — run the smallest durable application path.

## Deliberate limits

Koan Data does not promise universal provider parity, cross-provider transactions, recursive graph
loading, production migration policy, backup/SLO/security posture, or snapshot-safe streaming. Those
guarantees belong to a selected provider, another capability pillar, or the application and must be
stated and tested there.
