# Koan

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

Underneath, it's still ASP.NET Core. Reach for a normal controller or service whenever you want
one. Want to look behind the magic? Koan tells you what it chose and why.

## Go further

- [Build your first Koan application](docs/getting-started/quickstart.md)
- [Bring Koan into an existing ASP.NET Core application](docs/getting-started/adopt-existing-app.md)
- [Run complete applications](samples/README.md)
- [Build for agents](docs/reference/agents/index.md)
- [Understand the architecture](docs/architecture/index.md)
- [See what works today](docs/reference/what-works.md)

> Koan 0.20 is a .NET 10 preview.
