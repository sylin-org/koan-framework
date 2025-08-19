# Welcome to Sora!

Building backend services shouldn't feel like assembling a puzzle with missing pieces. Sora is designed to feel natural—like having a conversation with your code rather than wrestling with it.

## What makes Sora different?

**Start simple, grow smart**  
Your first API can be three files. When you need CQRS, messaging, or advanced patterns, they're there—but they don't get in your way until you're ready.

**Familiar, but better**  
Controllers work like you expect. Configuration follows .NET conventions. No magic, no surprises—just the good parts of what you already know, refined.

**Batteries included, assembly optional**  
Health checks, OpenAPI docs, flexible data access, and message handling all work out of the box. Use what you need, ignore the rest.

## See it in action

Let's experience what Sora feels like rather than just reading about it.

### Prerequisites
- .NET 9 SDK  
- Your favorite code editor

You'll create a tiny API from scratch below—no cloning required.

## Your first Sora API

Now let's build one from scratch and expose a tiny entity so you can see model data flowing end-to-end:

```bash
# Start fresh
dotnet new webapi -n MyFirstSora
cd MyFirstSora

# Add Sora (start simple)
dotnet add package Sora.Core
dotnet add package Sora.Web
dotnet add package Sora.Data.Sqlite
```

Open `Program.cs` and replace everything with:

```csharp
using Sora.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSora();

var app = builder.Build();
app.UseSora();
app.Run();
```

Create `Todo.cs`:

```csharp
using Sora.Domain;

namespace MyFirstSora;

public class Todo : Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}
```

Note: `Entity<T>` gives you an `Id` automatically. If you prefer to define it yourself (e.g., a GUID or custom string), you can add an `Id` property; Sora’s identity manager will fill it when missing.

Create `Controllers/TodosController.cs` to expose your entity via the generic controller:

```csharp
using Microsoft.AspNetCore.Mvc;
using MyFirstSora;
using Sora.Web.Controllers;

namespace MyFirstSora.Controllers;

[Route("api/[controller]")]
public class TodosController : EntityController<Todo> { }
```

Run it:

```bash
dotnet run
```

Try your new endpoints:

```bash
# list items
curl http://localhost:5000/api/todos

# add one
curl -X POST http://localhost:5000/api/todos \
    -H "Content-Type: application/json" \
    -d '{"title":"Ship a Sora service","isDone":false}'

# fetch by id
curl http://localhost:5000/api/todos/<returned-id>
```

**That's it.** Health checks, clean routing—and real model data—all working.

If your app is running right now, click to see it working:

- [http://localhost:5000/api/todos](http://localhost:5000/api/todos)
- [http://localhost:5000/swagger](http://localhost:5000/swagger)

### What just happened?
- The generic `EntityController<T>` exposed CRUD endpoints at `api/todos` with zero boilerplate.

### Oh, but now I want to point it to my real SQLite database!

Add a minimal connection string in `Program.cs` (before `var app = builder.Build();`):

```csharp
using Sora.Data.Sqlite; // at the top

// ...after AddSora()
builder.Services.Configure<SqliteOptions>(o =>
    o.ConnectionString = "Data Source=C:\\data\\myapp.db");
```
You can also set it via configuration:
- appsettings.json: `{"ConnectionStrings":{"sqlite":"Data Source=C:\\data\\myapp.db"}}`
- or environment: `ConnectionStrings__sqlite=Data Source=C:\\data\\myapp.db`

## Add Swagger (when you're ready)

To get interactive API docs:
```bash
dotnet add package Sora.Web.Swagger
```

Update `Program.cs`:

```csharp
using Sora.Web.Swagger;

// Swagger services are auto-registered when the package is referenced.
// UI is enabled automatically in Development; you can also call UseSoraSwagger() explicitly if you want to force it in Production.
app.UseSora();
app.Run();
```

Now open [http://localhost:5000/swagger](http://localhost:5000/swagger) (in Development) to explore and try your endpoints.

## What you just experienced

**Minimal ceremony:** Two small files, zero configuration files, and you have a working API with docs.

**Notice what you don't see:** complex startup code, configuration sprawl, or boilerplate controllers. It's just clean, focused code doing exactly what it says.

**Room to grow:** When you need data persistence, add `Sora.Data.Sqlite`. When you need messaging, add `Sora.Messaging.Core`. Each piece integrates naturally.

**Predictable patterns:** Controllers are controllers. Configuration follows .NET conventions. No magical discovery or hidden behavior.


Welcome to building with Sora. We think you'll like it here.

---

## Next: [Quick Start Part 2](02-quickstart-part-2.md)

When you’re ready to add another database and copy data between stores.