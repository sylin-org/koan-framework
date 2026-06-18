---
name: koan-quickstart
description: Zero to first Koan app in under 10 minutes — new project, Program.cs, first Entity<T> + EntityController<T> CRUD API, switching the data provider. S0/S1 starter patterns, learning Koan basics, prototypes and proofs of concept.
pillar: core
status: current
last_validated: 2026-06-18
---

# Koan Quickstart

## Trigger this skill when you see

- "new Koan project", "getting started", "first app", "hello world", "scaffold", "from scratch"
- A bare `Program.cs` with `builder.Services.AddKoan()` and nothing else
- A first `Entity<T>` being declared, or someone unsure how to save/read it
- `EntityController<T>` for the first auto-CRUD endpoint
- "how do I expose an API", "where's the repository", "how do I switch to Postgres"
- Prototypes, proofs of concept, demos, `samples/S0.*` / `samples/S1.*` references

## Core principle

**Reference = Intent, Entity-first.** Add `Koan.*` packages, call `AddKoan()` once, declare an `Entity<T>` — there is no repository to define, no DI to wire, no registration. The static/instance verbs (`Get` / `Query` / `Save` / `Remove`) are the whole data surface; GUID v7 ids are assigned automatically on first save.

<!-- validate -->
```csharp
using Koan.Data.Core;        // Save/Query/Remove verbs
using Koan.Data.Core.Model;  // Entity<T> base

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
}
```

Then use it anywhere — no repository, no DI, no registration:

```csharp
var todo = await new Todo { Title = "First task" }.Save();   // GUID v7 Id auto-assigned
var one = await Todo.Get(todo.Id);                           // Task<Todo?>
var open = await Todo.Query(t => !t.Completed);              // Task<IReadOnlyList<Todo>>
todo.Completed = true;
await todo.Save();                                           // upsert
await todo.Remove();                                         // delete
```

## 10-minute first app

**1. Create the project + add references** (each reference is intent — see the table below):

```bash
dotnet new web -n MyKoanApp && cd MyKoanApp
dotnet add package Koan.Core
dotnet add package Koan.Data.Core
dotnet add package Koan.Data.Connector.Json   # dev-friendly file store
dotnet add package Koan.Web                    # EntityController<T>
```

**2. `Program.cs` — one call wires the whole framework:**

```csharp
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();          // discovers every referenced Koan module
var app = builder.Build();
app.MapControllers();
app.Run();
```

**3. Declare the entity** — the canonical pattern above (`Todo : Entity<Todo>`).

**4. Expose CRUD** — derive a controller; full GET/POST/PUT/DELETE/PATCH + query/paging come for free:

```csharp
using Koan.Web.Controllers;          // EntityController<T> lives here
using Microsoft.AspNetCore.Mvc;

[Route("api/[controller]")]
public sealed class TodosController : EntityController<Todo> { }   // body intentionally empty
```

**5. Run:** `dotnet run`, then `curl http://localhost:5000/api/todos`. Done — auto GUID v7 ids, JSON storage, zero config.

## Reference = Intent activation

| Add this reference | Effect |
|---|---|
| `Koan.Core` | Bootstrap + discovery (`AddKoan()`) |
| `Koan.Data.Core` | Entity facade — `Get` / `Query` / `Save` / `Remove`, GUID v7 ids |
| `Koan.Data.Connector.Json` | JSON file store (great for dev) — the default when it's the only adapter |
| `Koan.Web` | `EntityController<T>` auto-CRUD + query/paging/patch |
| swap `Json` → `Koan.Data.Connector.Postgres` | Same entity code, Postgres-backed. **Provider transparency** — no code change |

## Relationships: use the primitive, don't hand-roll nav

Don't write `Task<User?> GetUser() => User.Get(UserId)` by hand. Declare the foreign key with `[Parent(typeof(T))]` and let the framework resolve the graph (DATA-0072):

```csharp
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Data.Core.Extensions;     // .Relatives()

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";

    [Parent(typeof(User))]
    public string UserId { get; set; } = "";
}

var graph = await todo.GetRelatives();        // single entity → its parents
var graphs = await todos.Relatives<Todo, string>();   // batch (no N+1)
```

The full relationship/aggregate story (batch loading, multi-parent, lifecycle hooks) lives in **koan-data-modeling** and **koan-relationships**.

## Switch to a real database (no code change)

```bash
dotnet add package Koan.Data.Connector.Postgres
```

```jsonc
// appsettings.json
{ "Koan": { "Data": { "Sources": { "Default": {
  "Adapter": "postgres",
  "ConnectionString": "Host=localhost;Database=myapp;Username=koan;Password=dev"
}}}}}
```

Entity code, controllers, and verbs are untouched — that's provider transparency. See **koan-multi-provider**.

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `interface ITodoRepository` / `IRepository<Todo>` hand-rolled | Delete it — `Todo.Get/Query/Save/Remove` are the surface (Entity-first). |
| A repository injected into a controller/service to do CRUD | Call the entity verbs directly; no repository abstraction exists to inject. |
| `services.AddSingleton<...>()` / manual `Add*()` for a referenced Koan module | Just reference the package — `AddKoan()` discovers and registers it (Reference = Intent). |
| Custom `DbContext` / EF mapping for a Koan entity | `Entity<T>` is the model; the adapter is chosen by reference + config. |
| `using Koan.Web;` for `EntityController<T>` | `using Koan.Web.Controllers;` — that's the real namespace. |
| Hand-written `GetUser() => User.Get(UserId)` nav helpers | `[Parent(typeof(User))]` + `entity.GetRelatives()` / `entity.Relatives()`. |
| Returning `List<T>` from a query and asserting `.Count` shape | `Query` returns `IReadOnlyList<T>`; treat it as read-only. |
| Manually assigning `Id = Guid.NewGuid()` before `Save()` | Leave `Id` unset — GUID v7 is assigned on first save. |

## Escape hatches

- **Need raw SQL / a provider-specific call?** `IDataService.Direct(...)` (in `Koan.Data.Core`) drops to the adapter — but writes there bypass the cache decorator (see koan-caching for the out-of-band evict).
- **Need a custom route or payload shape?** Override or add actions on the `EntityController<T>` subclass, or write a plain MVC controller — koan-api-building covers transformers, auth policies, custom routes.
- **Boot not behaving?** `AddKoan()` emits a structured boot report; koan-debugging walks reading it.
- **Timestamps:** they are **not** automatic — declare a `DateTimeOffset` property with `[Timestamp]` (set-once) or `[Timestamp(OnSave = true)]` (every save).

## See also

- [Reference card: data.md](../../../docs/reference/cards/data.md) — one-screen Data pillar map
- [Getting started: quickstart](../../../docs/getting-started/quickstart.md) — the long-form walkthrough
- [Getting started: overview](../../../docs/getting-started/overview.md) — framework mental model
- Sample: [`samples/S0.ConsoleJsonRepo/`](../../../samples/S0.ConsoleJsonRepo) — minimal console + JSON store
- Sample: [`samples/S1.Web/`](../../../samples/S1.Web) — web app with `EntityController<T>` + `[Parent]` relationships
- [DATA-0072 — explicit-type parent relationship attribute](../../../docs/decisions/DATA-0072-parent-relationship-attribute-explicit-type.md)
