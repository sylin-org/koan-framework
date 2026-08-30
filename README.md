<div align="center">

# Koan

**Write with intent. Koan makes it real.**

[![Release](https://github.com/sylin-org/koan-framework/actions/workflows/release.yml/badge.svg)](https://github.com/sylin-org/koan-framework/actions/workflows/release.yml)
[![NuGet](https://img.shields.io/nuget/v/Sylin.Koan.App.svg?label=NuGet&color=004880)](https://www.nuget.org/packages/Sylin.Koan.App)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)

An opinionated .NET meta-framework for agentic, data-driven applications.
A package reference is the intent — referencing a capability makes it available, and
`AddKoan()` composes everything referenced, once.

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org) — see the [code signing policy](CODE_SIGNING_POLICY.md).

</div>

---

## Write with intent. Koan makes it real.

Declare what your application knows. Declare how the world reaches it.

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

**Entity. Controller. Done.**

Run it and `/api/todos` is a persisted, queryable HTTP API.

No `DbContext`. No repository. No schema script. No CRUD service. No endpoint mapping.

## Make one

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o TodoApi
cd TodoApi
dotnet run -- --urls http://localhost:5000
```

Create a Todo. Read it back. It survives the restart.

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/todos `
  -ContentType application/json -Body '{"title":"buy milk"}'
Invoke-RestMethod http://localhost:5000/api/todos
```

## Agent, meet Todo.

Add Koan's MCP package and one declaration:

```powershell
dotnet add package Sylin.Koan.Mcp
```

```diff
+using Koan.Mcp;
+
+[McpEntity(Name = "Todo", Description = "Work the team intends to finish")]
 public sealed class Todo : Entity<Todo>
```

Now an MCP client can discover and work with the same `Todo`, through the same model and access
rules as the rest of your application.

No second domain model. No mirrored service. No handwritten tool handlers.

## Let the idea grow

Today it is a local SQLite API. Tomorrow it can use Postgres, run durable work, publish events,
search semantically, serve media, or collaborate with an agent.

Add only what the application needs. **The code keeps saying `Todo`.**

| You add | You get | Surface on your model |
|---|---|---|
| `Sylin.Koan.Data.Connector.Sqlite` / `.Postgres` / `.Mongo` / `.SqlServer` | Durable, restart-surviving persistence — provider negotiated, not coded | nothing changes |
| `Sylin.Koan.Jobs` | Durable background work: retries, schedules, chains, multi-node | `todo.Job.Submit()` · `Todo.Jobs.Schedule(...)` |
| `Sylin.Koan.Mcp` | MCP clients work your model under the same access rules | `[McpEntity]` |
| `Sylin.Koan.Communication` (+ `.Connector.RabbitMq`) | Entity events and transport, local-first | `todo.Events.Raise(...)` |
| `Sylin.Koan.Canon` | Multi-source record reconciliation with staged review | `Person.Canon.OnIntake(...)` |
| `Sylin.Koan.Tenancy` | Tenant-scoped everything, carried ambiently | nothing changes |
| `Sylin.Koan.Cache` / `.Storage` | Layered caching; local or S3 media | `[CachePolicy]` |
| `Sylin.Koan.AI` (+ `.Connector.Ollama` / `.LlamaCpp` / `.Onnx`) | Embeddings, semantic search, local models | `[Embedding]` |

Underneath, it's still ASP.NET Core. Reach for a normal controller or service whenever you want
one. Want to look behind the magic? Koan tells you what it chose and why — every boot publishes
its composition decisions at `/.well-known/Koan/facts`, and `koan.lock.json` records them for
code review.

## Go further

- [Build your first Koan application](docs/getting-started/quickstart.md)
- [Bring Koan into an existing ASP.NET Core application](docs/getting-started/adopt-existing-app.md)
- [Run complete applications](samples/README.md)
- [Build for agents](docs/reference/agents/index.md)
- [Understand the architecture](docs/architecture/index.md)
- [See what works today](docs/reference/what-works.md)
- [Capability map — every package, assessed](docs/reference/capability-map.md)
- [Agent retrieval map (llms.txt)](llms.txt)
- [Code signing policy](CODE_SIGNING_POLICY.md)

## Community

- [Contributing](CONTRIBUTING.md) — laws of the tree, first issue guidance, PR expectations
- [Security](SECURITY.md) — how to report vulnerabilities
- [Support](SUPPORT.md) — where to ask questions
- [Code of Conduct](CODE_OF_CONDUCT.md)

> Koan 1.x is the .NET 10 stabilization train. Every active package ships on it.
