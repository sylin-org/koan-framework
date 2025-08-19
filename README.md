# Sora Framework

**Stop fighting your framework. Start building.**

Sora is a modern .NET framework that gets out of your way and lets you focus on what matters—your application logic. No complex configuration, no hidden magic, just clean, predictable code that works the way you expect.

## The Problem We Solve

Most frameworks force you to choose: either simple but limited, or powerful but complex. Sora gives you both—start with a three-file API, scale to enterprise patterns when you need them.

```csharp
// This is a complete, working API with persistence
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSora();

var app = builder.Build();
app.UseSora();
app.Run();
```

## Why Developers Choose Sora

🚀 **Zero to API in minutes** — Real CRUD endpoints with just `EntityController<T>`  
🔧 **Escape hatches everywhere** — Drop to raw SQL, custom controllers, or provider-specific features  
📦 **Modular architecture** — Add JSON, SQLite, MongoDB, or messaging as your needs grow  
✅ **Production ready** — Health checks, OpenAPI docs, and observability built-in  
🎯 **Predictable** — Convention over configuration, but configuration always wins  

## Core Philosophy

- **Start simple, grow smart** — Begin with basics, add complexity only when needed
- **Familiarity first** — Uses patterns you already know (Controllers, DI, EF-style entities)
- **Developer experience** — Clear error messages, helpful defaults, minimal friction
- **Flexibility** — Multiple data providers, pluggable components, custom implementations welcome

## Real-World Example

```csharp
// Define your model
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Get a full REST API
[Route("api/[controller]")]
public class TodosController : EntityController<Todo> { }

// Use it naturally
var todo = await new Todo { Title = "Learn Sora" }.Save();
var todos = await Todo.Where(t => !t.IsDone);
```

That's it. You now have:
- `GET /api/todos` — List all todos
- `POST /api/todos` — Create new todo  
- `GET /api/todos/{id}` — Get specific todo
- `PUT /api/todos/{id}` — Update todo
- `DELETE /api/todos/{id}` — Delete todo
- Automatic health checks at `/health`
- OpenAPI documentation at `/swagger`

## Getting Started

1. **Quick Start** — [3-minute tutorial](docs/api/quickstart/) from zero to working API
2. **Documentation** — [Complete guides](docs/api/) for all features
3. **Examples** — Real applications in the `samples/` directory

## Built For

- **Rapid prototyping** — Get ideas into code fast
- **Microservices** — Lightweight, focused services  
- **CRUD APIs** — Perfect for data-driven applications
- **Enterprise applications** — Scales to complex patterns (CQRS, Event Sourcing)

## Community & Support

- **GitHub Issues** — Bug reports and feature requests
- **Discussions** — Questions and community help
- **Contributing** — See our [guidelines](docs/08-engineering-guardrails.md)

Built with ❤️ for .NET developers who want to focus on solving problems, not fighting frameworks.

---

**License:** Apache 2.0 | **Requirements:** .NET 9 SDK
