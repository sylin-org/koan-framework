# Sylin.Koan.App

The one-reference Koan web application: bootstrap the framework, define an Entity, and expose it through an API.
The package composes Koan's foundation and controller-based ASP.NET Core projection at versions proved together.

## Reference

```powershell
dotnet add package Sylin.Koan.App
```

The generated [product surface](../../docs/reference/product-surface.md) owns support maturity and
publication truth for this package.

## Meaningful result

```csharp
// Program.cs
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

```csharp
// Todo.cs
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}
```

```csharp
// TodosController.cs
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

Run the app, use the URL ASP.NET Core prints, then create and read a Todo:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/todos -ContentType application/json -Body '{"title":"buy milk"}'
Invoke-RestMethod http://localhost:5000/api/todos
```

There is no repository, database registration, schema script, endpoint mapping, or framework-specific `Program`
type. `AddKoan()` composes referenced modules; `Entity<Todo>` supplies the data grammar; `EntityController<Todo>`
supplies the REST projection.

## Defaults and composition

The bundle includes `Sylin.Koan`, `Sylin.Koan.Web`, local Events and Transport, and the bounded JSON data provider.
JSON writes local files under `./data`, which makes the first result persistent without an external service.
Startup output and `/.well-known/Koan/facts` report the modules and provider election.

For durable embedded storage, add one reference and change no application code or configuration:

```powershell
dotnet add package Sylin.Koan.Data.Connector.Sqlite
```

SQLite's provider priority replaces the JSON floor and its local default writes `.koan/data/Koan.sqlite`. Explicit
source or Entity routing still wins when the application states a narrower intent.

## Boundaries and corrections

- The JSON floor is for bounded local files, seeds, smoke scenarios, and deliberately small workloads. It does not
  promise provider-bounded streaming, multi-process concurrency, distributed durability, or production backup.
- `EntityController<T>` exposes generic data operations; business actions, authorization, tenancy, validation, and
  public API design remain application decisions.
- Adding an infrastructure package selects capability but does not invent credentials or remote endpoints. When a
  provider cannot derive a safe local target, startup facts and health name the missing or rejected intent.
- This package contains no runtime assembly of its own and intentionally emits no symbol package.

Use `Sylin.Koan.Templates` for the same path as an ordinary `dotnet new` project, or read the
[FirstUse sample](https://github.com/sylin-org/Koan-framework/tree/main/samples/FirstUse) for the cumulative 0.20
preview experience with SQLite, MCP, and shared operator/agent facts.
