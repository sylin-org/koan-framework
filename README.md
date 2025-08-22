# Sora Framework

**Building backend services should feel like playing with Legos, not assembling puzzles with missing pieces.**

Sora is designed to having a conversation with your code rather than wrestling with it. Start with a three-file API, add vector search when you need it, scale to enterprise patterns when you're ready.

## What makes Sora different?

- **Start simple, grow smart**  
   Your first API can be three files. When you need CQRS, messaging, AI, or vector search, they're there—but they don't get in your way until you're ready.

- **Familiar, but better**  
   Controllers work like you expect. Configuration follows .NET conventions. No magic, no surprises—just the good parts of what you already know, refined.

- **Batteries included, assembly optional**  
  Health checks, OpenAPI docs, flexible data access, message handling, and AI integration all work out of the box. Use what you need, ignore the rest.

```csharp
// This is a complete, working API with persistence
using Sora.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSora();

var app = builder.Build();
app.UseSora();
app.Run();
```

## Why choose Sora?

- **Zero to API in minutes**  
  Real CRUD endpoints with just `EntityController<T>`

- **Escape hatches everywhere**  
  Drop to raw SQL, custom controllers, or provider-specific features

- **Modular by design**  
  Add SQLite, MongoDB, Redis, RabbitMQ, AI providers, or vector search as you grow

- **AI-ready**  
  Built-in streaming chat, embeddings, vector search, and RAG patterns

- **Production ready**  
  Health checks, OpenAPI docs, observability, and message reliability built-in

- **Predictable**  
  Convention over configuration, but configuration always wins

## Core philosophy

- **Start simple, grow smart**  
  Begin with basics, add complexity only when needed

- **Familiarity first**  
  Uses patterns you already know (Controllers, DI, EF-style entities)

- **Developer experience**  
  Clear error messages, helpful defaults, minimal friction

- **Everything is optional**  
  Data providers, messaging, AI, vector search—add what you need, when you need it

## Real-World Example

First, install the essential packages:

```bash
dotnet add package Sora.Core
```

```bash
dotnet add package Sora.Web
```

```bash
dotnet add package Sora.Data.Sqlite  # or your preferred data adapter
```

Then write your code:

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

- `GET /api/todos` - List all todos
- `POST /api/todos` - Create new todo
- `GET /api/todos/{id}` - Get specific todo
- `PUT /api/todos/{id}` - Update todo
- `DELETE /api/todos/{id}` - Delete todo
- Automatic health checks at `/health`

**That's it.** Real data, clean routing, and production patterns—all working.

## And now, what if I want to see my API specs?

Add interactive API documentation with one package:

```bash
dotnet add package Sora.Web.Swagger
```

Now visit `/swagger` to explore and test your endpoints interactively. No additional configuration needed; Swagger auto-discovers your controllers and generates beautiful, interactive docs.

## Need more? Just add it

**Want AI chat and embeddings?**

```bash
dotnet add package Sora.AI
dotnet add package Sora.Ai.Provider.Ollama
```

Now you have `/ai/chat` with streaming and `/ai/embed` endpoints working with local models.

**Need vector search?**

```bash
dotnet add package Sora.Data.Weaviate
```

Your entities can now be embedded and searched semantically.

**Want reliable messaging?**

```bash
dotnet add package Sora.Messaging.RabbitMq
```

Send messages, handle failures, and process with inbox patterns.

**GraphQL from your REST models?**

```bash
dotnet add package Sora.Web.GraphQl
```

Your `EntityController<T>` now serves both REST and GraphQL automatically.

## Getting started

1. **Quick Start** - [3-minute tutorial](docs/api/quickstart/) from zero to working API
2. **Documentation** - [Complete guides](docs/api/) for all features
3. **Examples** - Real applications in the `samples/` directory

## Built for

- **Rapid prototyping** - Get ideas into code fast, add AI features with a single line
- **Microservices** - Lightweight, focused services with built-in messaging and observability
- **Modern APIs** - REST + GraphQL from the same models, with vector search when you need it
- **Enterprise applications** - Scales to complex patterns (CQRS, Event Sourcing, AI workflows)

## Community & support

- **GitHub Issues** - Bug reports and feature requests
- **Discussions** - Questions and community help
- **Contributing** - See our [guidelines](CONTRIBUTING.md)

Built with ❤️ for .NET developers who want to focus on solving problems, not fighting frameworks.

---

**License:** Apache 2.0 | **Requirements:** .NET 9 SDK | **Current:** v0.2.18

Quick links
- Engineering front door: `/docs/engineering/index.md`
- Architecture principles: `/docs/architecture/principles.md`
