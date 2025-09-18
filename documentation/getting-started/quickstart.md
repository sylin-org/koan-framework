---
type: GUIDE
domain: core
title: "5-Minute Koan Quickstart"
audience: [developers, architects, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# 5-Minute Koan Quickstart

**Document Type**: GUIDE
**Target Audience**: Developers
**Last Updated**: 2025-01-17
**Framework Version**: v0.2.18+

---

Get a Koan API running in 5 minutes or less.

## Prerequisites

- **.NET 9 SDK** or later
- **IDE**: Visual Studio, VS Code, or JetBrains Rider

## Create Your First Koan API

### 1. Create Project
```bash
mkdir my-koan-app && cd my-koan-app
dotnet new web
```

### 2. Add Packages
```bash
dotnet add package Koan.Core
dotnet add package Koan.Web
dotnet add package Koan.Data.Sqlite
```

### 3. Create Model
```csharp
// Models/Todo.cs
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
}
```

### 4. Create Controller
```csharp
// Controllers/TodosController.cs
[Route("api/[controller]")]
public class TodosController : EntityController<Todo> { }
```

### 5. Update Program.cs
```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Koan services - auto-discovers all referenced modules
builder.Services.AddKoan();

var app = builder.Build();

// Koan.Web automatically wires controllers, health endpoints, etc.
app.Run();
```

### 6. Run
```bash
dotnet run
```

**That's it!** You now have:
- ✅ REST API at `http://localhost:5000/api/todos`
- ✅ Health checks at `http://localhost:5000/api/health`
- ✅ Full CRUD operations (GET, POST, PUT, DELETE)
- ✅ Proper error handling and logging

## Test Your API

```bash
# Create a todo
curl -X POST http://localhost:5000/api/todos \
  -H "Content-Type: application/json" \
  -d '{"title": "Learn Koan", "isCompleted": false}'

# Get all todos
curl http://localhost:5000/api/todos

# Check health
curl http://localhost:5000/api/health
```

## Next Steps

- **[Complete Guide](getting-started.md)** - Full step-by-step walkthrough
- **[Framework Overview](overview.md)** - Architecture and capabilities
- **[Add Authentication](../guides/authentication-setup.md)** - Multi-provider auth setup
- **[Explore Samples](../../../samples/)** - Real-world examples

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+