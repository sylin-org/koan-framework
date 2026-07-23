# Koan Framework

Koan is an opinionated .NET 10 meta-framework for agentic, data-driven applications. Application code
states business intent; Koan owns composition, provider negotiation, lifecycle, and explanation.

> **Current line:** Koan 0.20 is a preview. Supported capabilities and packages are explicit in the
> [generated product surface](docs/reference/product-surface.md). Package availability alone is not a
> support promise.

## Build one useful application

Install the public template and create a persisted web API:

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o TodoApi
cd TodoApi
dotnet run
```

Use the URL printed by ASP.NET Core, then create and read a Todo and inspect the resolved application:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/todos `
  -ContentType application/json -Body '{"title":"buy milk"}'
Invoke-RestMethod http://localhost:5000/api/todos
Invoke-RestMethod http://localhost:5000/.well-known/Koan/facts
```

The complete application grammar is small:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
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

```csharp
var todo = await new Todo { Title = "Ship it" }.Save();
var same = await Todo.Get(todo.Id);
var open = await Todo.Query(item => !item.Done);
await todo.Remove();
```

There is no application repository, `DbContext`, schema bootstrap, controller CRUD plumbing, or list
of framework services to register.

## Grow by capability

References make capabilities available. `AddKoan()` compiles their modules once, each capability
elects an eligible provider, and the resolved application explains itself.

| Need | Canonical capability |
|---|---|
| Persist and query business state | [Data](docs/reference/data/index.md) |
| Expose Entities through HTTP | [Web](docs/reference/web/index.md) |
| Authenticate users and isolate tenants | [Identity and isolation](docs/reference/identity/index.md) |
| Run durable work and communicate Entity intent | [Work and communication](docs/reference/communication/index.md) |
| Cache state, store files, and serve media | [State and content](docs/reference/state-content/index.md) |
| Add AI, vector, and search behavior | [Intelligence](docs/reference/ai/index.md) |
| Expose governed tools and resources to agents | [Agents](docs/reference/agents/index.md) |
| Reconcile records into canonical Entities | [Canon](docs/reference/canon/index.md) |
| Test, inspect, and operate the application | [Testing and operations](docs/reference/operations/index.md) |

Adding a capability should add business vocabulary rather than infrastructure ceremony. Provider
differences are declared and negotiated; unsupported intent rejects with a corrective explanation
instead of silently degrading.

## Inspect the resolved application

The same composition decisions appear through:

- startup reporting;
- `/health/live` and `/health/ready`;
- `/.well-known/Koan/facts` for operators and reviewers;
- `koan://facts`, `koan://entities`, and `koan://self` for MCP clients; and
- `koan.lock.json` for referenced-module drift.

These are projections of one runtime model, not competing configuration authorities.

## Choose Koan when

Choose Koan when your application is naturally Entity-centric, you value conventions over repeated
plumbing, and you want data, web, jobs, communication, identity, and agent surfaces to compose through
one inspectable runtime.

Koan 0.20 is not a stable 1.0 compatibility promise. Do not assume an unlisted provider guarantee,
uniform backend cost or behavior, or production readiness merely because a package exists.

## Continue

- [Build the first application](docs/getting-started/quickstart.md)
- [Adopt Koan in an existing application](docs/getting-started/adopt-existing-app.md)
- [Evaluate the architecture](docs/architecture/index.md)
- [Choose a capability pillar](docs/index.md)
- [Run a graduated sample](samples/README.md)
- [Evaluate the supported product surface](docs/reference/product-surface.md)
- [Troubleshoot an application](docs/support/troubleshooting.md)
- [Orient a coding agent](llms.txt)

Architecture decision records preserve historical decisions. For current application behavior, use
the capability pages, package documentation, graduated samples, generated product surface, and
repository-owned tests.
