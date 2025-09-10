# 5-Minute Sora Quickstart

Get a Sora API running in 5 minutes or less.

## Prerequisites
- .NET 9 SDK

## Create Your First Sora API

### 1. Create Project
```bash
mkdir my-sora-app && cd my-sora-app
dotnet new web
```

### 2. Add Packages
```bash
dotnet add package Sora.Core
dotnet add package Sora.Web
dotnet add package Sora.Data.Sqlite
```

### 3. Create Model
```csharp
// Models/Todo.cs
using Sora.Core;

public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsDone { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
```

### 4. Create Controller
```csharp
// Controllers/TodosController.cs
using Microsoft.AspNetCore.Mvc;
using Sora.Web;

[Route("api/[controller]")]
public class TodosController : EntityController<Todo> { }
```

### 5. Update Program.cs
```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Sora services
builder.Services.AddSora();

var app = builder.Build();

// Configure pipeline
app.MapControllers();
await app.RunAsync();
```

### 6. Run
```bash
dotnet run
```

**That's it!** You now have:
- REST API at `https://localhost:5001/api/todos`
- Health checks at `https://localhost:5001/health`
- Swagger UI at `https://localhost:5001/swagger` (development)

## Test Your API

```bash
# Create a todo
curl -X POST https://localhost:5001/api/todos \
  -H "Content-Type: application/json" \
  -d '{"title": "Learn Sora", "isDone": false}'

# Get all todos
curl https://localhost:5001/api/todos
```

## Next Steps

- **Complete Guide**: [Getting Started Guide](reference/getting-started.md)
- **Framework Overview**: [Framework Overview](reference/framework-overview.md)
- **Add Authentication**: [Authentication Guide](reference/pillars/authentication.md)
- **Explore Samples**: Check the `samples/` directory