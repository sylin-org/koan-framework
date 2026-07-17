# Koan Framework

Koan is an opinionated .NET 10 meta-framework for agentic, data-driven applications: the “Ruby on
Rails for agentic .NET.” It is designed to move from V0 to V1 in meaningful small steps—application
code states business intent while the framework owns composition, backend negotiation, lifecycle,
and explanation.

> **Status:** pre-1.0 and source-first. Packages use independent NBGV versions; there is no single
> framework package version. The current coherent package wave is proved locally but has not yet been
> published and observed from public feeds. Use this checkout for the supported first-use path and
> consult the [generated product surface](docs/reference/product-surface.md) for evidence-backed maturity.

## Reach a meaningful result

```powershell
git clone https://github.com/sylin-org/koan-framework
cd koan-framework
dotnet run --project samples/FirstUse
```

In another shell, create a business request and inspect what composed it:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/approvals `
  -ContentType application/json -Body '{"subject":"Approve supplier invoice"}'
Invoke-RestMethod http://localhost:5000/api/approvals
Invoke-RestMethod http://localhost:5000/.well-known/Koan/facts
```

Use the URL printed by the application if it differs. The complete walkthrough is in
[`samples/FirstUse`](samples/FirstUse/README.md).

## The application grammar

The normal web host is four lines:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

A model and its HTTP surface remain business-shaped:

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

That expression gains Entity persistence, query, paging, controller conventions, health, structured
startup reporting, and runtime facts from referenced capabilities. There is no application repository,
`DbContext`, schema bootstrap, controller CRUD plumbing, or framework service-registration list.

```csharp
var todo = await new Todo { Title = "Ship the meaningful step" }.Save();
var same = await Todo.Get(todo.Id);
var open = await Todo.Query(item => !item.Done);
await todo.Remove();
```

## Grow by intent

References make capabilities available. `AddKoan()` compiles their modules once; the relevant pillar
elects providers and reports the result. Adding a capability should add business vocabulary, not
infrastructure ceremony.

Examples:

- `[Cacheable]` adds Entity cache semantics.
- `IKoanJob<T>` makes an Entity durable work with progress and schedules.
- `[Embedding]` adds semantic indexing and search when AI/vector providers are present.
- `[McpEntity]` projects a governed Entity surface to agents.
- Entity Events and Transport are local-first; adding an eligible connector can transparently extend
  the communication mesh without changing the application terminal.

```csharp
await order.Events.Raise<OrderApproved>(ct); // something happened to this order
await order.Transport.Send(ct);              // distribute an isolated copy of its current state
```

The same terminals lift pointwise over Entity collections and lazy streams. Backend differences are
negotiated, not hidden: configured intent either resolves to an eligible provider or fails with a
corrective explanation.

## Inspect what happened

When Koan is working well, the same resolved composition is visible through:

- the startup report;
- `/health/live` and `/health/ready`;
- `/.well-known/Koan/facts` for operators and reviewers;
- `koan://facts`, `koan://entities`, and `koan://self` for MCP clients; and
- `koan.lock.json` for referenced-module drift.

These are projections of runtime decisions, not separate configuration authorities.

## Choose Koan when

Choose Koan when the application is naturally Entity-centric, you value conventions over repeated
plumbing, and you want data, jobs, communication, web, and agent surfaces to compose through one
inspectable runtime.

Do not choose it when you need a stable 1.0 compatibility promise today, require a publicly certified
package-only install, need an unsupported provider guarantee, or want direct control of every ORM,
transport, and hosting mechanism. Koan rejects false backend parity; package existence is not a support
claim.

## Read next

- [Quickstart](docs/getting-started/quickstart.md)
- [Golden path](docs/getting-started/overview.md)
- [Graduated samples](samples/README.md)
- [Product constitution](docs/architecture/product-constitution.md)
- [Entity semantics contract](docs/architecture/entity-semantics-contract.md)
- [Current capability and package surface](docs/reference/product-surface.md)
- [Troubleshooting](docs/support/troubleshooting.md)

Architecture decision records are retained as dated decisions. For current product behavior, prefer
the pages above, executable samples, generated product surface, and source/tests.
