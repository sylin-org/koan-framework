---
type: RECIPE
recipe: store-and-expose
title: "Store my things and expose them over HTTP"
domain: data
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/store-and-expose.md
gets_you: "A persisted, queryable HTTP API over a model you wrote, with no plumbing in between."
works_if: "Always. This is where most applications start."
costs: "Nothing to operate on the embedded path. A server-backed store adds a process to run."
ingredients:
  - "one | web application bundle | Sylin.Koan.App"
  - "one | entity store, user's choice | Sylin.Koan.Data.Connector.Sqlite, Sylin.Koan.Data.Connector.Json, Sylin.Koan.Data.Connector.InMemory, Sylin.Koan.Data.Connector.Mongo, Sylin.Koan.Data.Connector.Postgres, Sylin.Koan.Data.Connector.SqlServer, Sylin.Koan.Data.Connector.Couchbase, Sylin.Koan.Data.Connector.Cockroach, Sylin.Koan.Data.Connector.Redis"
  - "optional | recoverable deletion | Sylin.Koan.Data.SoftDelete"
  - "optional | move the store later, verifiably | Sylin.Koan.Data.Cutover"
---

# Store my things and expose them over HTTP

An `Entity<T>` and an `EntityController<T>`. The store is elected from the package reference.

## Choosing the store

Pick exactly one unless the application genuinely owns more than one. The axes that actually
discriminate — match these against what the developer described, not against a feature matrix:

| Axis | What to listen for |
|---|---|
| **Process to operate** | "I don't want to run anything" → embedded (SQLite). "We already run Postgres" → use it. |
| **Container available** | Docker or Compose in the conversation makes a server-backed store cheap; without it, it is a new operational burden. |
| **Shape of the data** | Nested, varying documents lean document-store; relational reporting leans SQL. |
| **Scale and topology** | Single node covers far more than people expect. Clustering is a real cost — do not pay it speculatively. |
| **Existing investment** | The store their team already operates usually beats the theoretically better one. |
| **Residency and licensing** | Where data may live, and what the organization is permitted to run. |

Two honest defaults: **SQLite** when nothing in the conversation demands otherwise, and **whatever
they already run** when they already run something. JSON and InMemory are for exploring — say plainly
that InMemory disappears on restart.

Moving the store later is real work but a supported path, so an early choice is not a trap.

## Assembly

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o TodoApi
```

The template is the whole host. Otherwise:

```powershell
dotnet add package Sylin.Koan.App
dotnet add package Sylin.Koan.Data.Connector.Sqlite
```

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

No `DbContext`, repository, schema script, or endpoint mapping. A server-backed store needs its
endpoint and credentials configured under `Koan:Data:Sources`; an embedded one needs nothing.

## Prove it

1. **Behavior** — create, read back, query, and restart the process; assert the data survived.
2. **Composition** — assert the store you intended actually won, via `/.well-known/Koan/facts` or
   `koan.lock.json`. An API that works proves *a* store answered.
3. **Correction** — point it at an unreachable endpoint and assert startup explains the failure
   rather than silently falling back.

## Boundaries

- This is not a security posture. Authorization, validation, and rate limiting are separate.
- Adding a second store never moves existing data.
- Same syntax across stores does not mean identical behavior; filter, paging, and transaction support
  vary by provider.

## Interacts with

**Tenancy.** Retro-fitting isolation onto a populated store is far more expensive than starting with
it. If more than one customer will ever use this, decide before there is data.
