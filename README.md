**Koan**

**Write with intent. Koan makes it real.**

[![Release](https://github.com/sylin-org/koan-framework/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/sylin-org/koan-framework/actions/workflows/release.yml)
[![NuGet](https://img.shields.io/nuget/v/Sylin.Koan.App.svg?label=NuGet&color=004880)](https://www.nuget.org/packages/Sylin.Koan.App)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)

Koan is an opinionated .NET meta-framework for agentic, data-driven applications.

A package reference expresses intent. `AddKoan()` composes the referenced capabilities. Entities give application code a consistent vocabulary for persistence, APIs, background work, and intelligence.

Your application describes its business. Koan owns composition, provider selection, infrastructure lifecycle, and the explanation of what was selected.

**Declare what your application knows. Declare how the world reaches it.**

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

With Koan’s web foundation and SQLite connector, this becomes a persisted, queryable HTTP API at `/api/todos`.

**Make one.**

You need the .NET 10 SDK.

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o TodoApi
cd TodoApi
dotnet run -- --urls http://localhost:5000
```

In another shell, create a Todo and read it back:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/todos `
  -ContentType application/json -Body '{"title":"Ship something useful"}'

Invoke-RestMethod http://localhost:5000/api/todos
```

Stop the application, start it again, and repeat the GET. Your Todo is still there.

The template references Koan’s application bundle and SQLite connector. Its host is ordinary ASP.NET Core with one composition call:

```csharp
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

var app = builder.Build();
await app.RunAsync();
```

Application code works directly with the Entity:

```csharp
var todo = await new Todo { Title = "Ship it" }.Save();

var open = await Todo.Query(item => !item.Done);
```

Use ordinary ASP.NET Core controllers and services for application-specific behavior.

Already have an application? [Bring Koan into an existing ASP.NET Core application](docs/getting-started/adopt-existing-app.md).

**Let the idea grow.**

Today it is a local SQLite API. Tomorrow it might need semantic search, tenant isolation, background processing, files, or an agent interface.

Add what the application needs. **The code keeps saying `Todo`.**

| You want to… | Explore… |
|---|---|
| Store, query, relate, or move business data | [Data capabilities](docs/capabilities/data.md) |
| Search by meaning or work with models | [AI capabilities](docs/capabilities/ai.md) |
| Run background work or exchange events and records | [Work and integration](docs/capabilities/work.md) |
| Establish identity, access rules, tenancy, or field protection | [Trust and isolation](docs/capabilities/trust.md) |
| Cache data, accept files, or produce media derivatives | [State and content](docs/capabilities/state.md) |
| Let agents discover and use application operations | [Agent surfaces](docs/capabilities/agents.md) |
| Reconcile arrivals from several sources | [Trusted records](docs/capabilities/records.md) |
| Verify, observe, and deploy the application | [Operations](docs/capabilities/operations.md) |

For example, after composing Entity embedding integration, an embedding runtime, and a vector store, semantic search uses the Entity’s vocabulary:

```csharp
var matches = await Todo.Ai.Search(
    "something quick to finish before lunch",
    search => search.Top(5));
```

The [semantic-search capability](docs/capabilities/ai/semantic-search.md) carries the complete setup, indexing declaration, model constraints, and working recipe.

Provider choices remain meaningful. Query support, durability, transactions, and operating requirements follow the selected capability and implementation.

**Agent, meet Todo.**

Add Koan’s MCP package:

```powershell
dotnet add package Sylin.Koan.Mcp
```

Opt the existing Entity into an agent-visible surface:

```diff
+using Koan.Mcp;
+
+[McpEntity(Name = "Todo", Description = "Work the team intends to finish")]
 public sealed class Todo : Entity<Todo>
```

Koan projects applicable Entity operations into MCP tools and resources. Generated operations use the application’s configured Entity access rules and persistence policies.

Choose the client transport and access policy through the [MCP recipe](docs/recipes/let-an-agent-use-my-app.md). Local STDIO and remote Streamable HTTP are supported; the HTTP transport requires explicit enablement.

The declaration selects the model to expose. Authorization determines what a caller may do.

**Agents can also help you build it.**

Koan supplies workflows for building, extending, repairing, explaining, and upgrading applications.

Start with a business outcome:

> Add semantic search over task titles. Keep the existing HTTP routes.

Or request an explanation:

> Why was this data provider selected?

The workflows direct agents to inspect the application, retrieve the relevant capability guidance, use exact packages and APIs, and verify the resulting behavior.

The same documentation serves developers and agents:

```text
Business outcome
  → capability and constraints
  → exact packages and deployment choices
  → working recipe
  → observable proof
```

The [capability tree](docs/capabilities/index.md) routes from broad requirements to actionable guidance. Recipes provide installation, configuration, code, and verification. Installed packages carry their own version-matched documentation, and the coding skill includes dated capability snapshots for restricted or offline environments.

Read-only explanation has its own workflow, allowing an agent to inspect evidence and explain behavior before changes are made.

[Explore Koan’s agent workflows](docs/guides/agent-skills.md).

**Shared expertise can become an application foundation.**

Koan’s bundle and module model provides building blocks for teams maintaining an internal platform.

Platform engineers can package approved capabilities and compatible versions. Domain engineers can contribute shared contracts, lifecycle policies, and business operations. Feature developers can build upon those decisions.

An organization’s purchasing foundation, for example, could bring together its persistence choices, tenancy conventions, diagnostics, and purchasing policies.

Those decisions become versioned, reviewable, and testable.

For teams with varied experience, this gives new contributors established patterns, experienced developers reusable foundations, architects executable conventions, and coding agents a consistent application vocabulary.

Bundles select capabilities. Modules and application boundaries enforce rules. The organization owns its shared contracts, extension points, and compatibility promises.

**Understand what the application composed.**

When behavior comes from referenced capabilities, its origin should be inspectable.

| Evidence | What it tells you |
|---|---|
| `koan.lock.json` | Referenced-module composition recorded at build time |
| Startup output | Runtime selections and dependency failures |
| `/.well-known/Koan/facts` | Redacted runtime composition decisions |
| `koan://facts` | The corresponding evidence through MCP |
| `/health/live` | Process liveness |
| `/health/ready` | Required dependency readiness |

For the application above:

```powershell
Invoke-RestMethod http://localhost:5000/.well-known/Koan/facts
```

Build-time composition and runtime selections answer different questions. Together with behavior checks, they help developers and agents understand what the application actually does.

**Choose a foundation appropriate to your application.**

Koan fits business APIs, internal tools, knowledge applications, integration services, operational portals, and product backends. Its composition model also supports organizations maintaining shared foundations across several teams.

Capabilities and connectors have individual maturity assessments. Use the [evaluated product surface](docs/reference/product-surface.md) when choosing a production baseline.

Continue with:

- [Build your first application](docs/getting-started/quickstart.md)
- [Explore capabilities](docs/capabilities/index.md)
- [Find a working recipe](docs/recipes/index.md)
- [Compose a whole solution](docs/capabilities/solutions.md)
- [Run complete applications](samples/README.md)
- [Understand the architecture](docs/architecture/index.md)
- [Find exact packages](docs/reference/capability-map.md)
- [Navigate documentation as an agent](llms.txt)

Koan is licensed under [Apache 2.0](LICENSE).

[Contributing](CONTRIBUTING.md) · [Support](SUPPORT.md) · [Security](SECURITY.md) · [Code of Conduct](CODE_OF_CONDUCT.md)
