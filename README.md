# Sora Framework

**Build services like you're talking to your code, not fighting it.**

Sora is a backend framework for .NET developers who value clarity, comfort, and the ability to grow. Whether you're spinning up a quick prototype or scaling into enterprise-grade patterns, Sora keeps the path clear. Start with a three-file API. Add messaging, vector search, or AI when you're ready. Nothing more, nothing less.

---

## 🧱 From First Line to First Endpoint

Let’s start simple:

```bash
dotnet add package Sora.Core

dotnet add package Sora.Web

dotnet add package Sora.Data.Sqlite
```

Then:

```csharp
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsDone { get; set; }
}

[Route("api/[controller]")]
public class TodosController : EntityController<Todo> { }
```

That’s a full REST API:

- `GET /api/todos`
- `POST /api/todos`
- `PUT /api/todos/{id}`
- Health checks at `/health`

It works. Right now. No ceremony.

---

## 🌱 A Framework That Grows With You

Sora isn’t trying to impress you with magic. It earns trust by staying out of your way—until you need more.

- Add AI? One line.
- Need vector search? Drop in a package.
- Ready for messaging? Plug it in.
- CQRS? Recipes exist.

You never pay for complexity you didn’t ask for.

```bash
dotnet add package Sora.Web.Swagger           # Interactive docs

dotnet add package Sora.AI                    # Local LLMs with Ollama

dotnet add package Sora.Data.Weaviate         # Semantic search

dotnet add package Sora.Messaging.RabbitMq    # Production messaging

dotnet add package Sora.Web.GraphQl           # REST + GraphQL side-by-side
```

Everything integrates naturally. No glue scripts. No boilerplate.

---

## 🧭 Philosophy: Start Simple, Grow Smart

Sora is designed by developers who’ve scaled codebases and lived to talk about it.

- **Minimal friction**: Build a real service in a single file.
- **Clear structure**: Follow .NET conventions, not opinions.
- **Honest complexity**: Add what you need, skip what you don’t.
- **Escape hatches everywhere**: Drop to raw SQL, write custom controllers, or override behavior freely.

---

## 🔧 The Pillars Behind the Curtain

Sora is modular by nature. Each of its components works independently—and shines together.

| Pillar        | Purpose                                                             |
| ------------- | ------------------------------------------------------------------- |
| **Core**      | Unified runtime, secure defaults, health checks, observability      |
| **Web**       | REST and GraphQL from your models, Swagger UI, clean routing        |
| **Data**      | Unified access to SQL, NoSQL, JSON, and vector DBs                  |
| **Storage**   | File/blob handling from local to cloud with profiles                |
| **Messaging** | Reliable queues via RabbitMQ, Redis, or in-memory                   |
| **AI**        | Embeddings, vector search, chat, and RAG via local or remote models |
| **Recipes**   | Best-practice bundles for reliability, telemetry, and scale         |

---

## 🧪 Real Use, Not Just Hello World

Sora is already being used to build:

- Microservices with event sourcing and inbox/outbox patterns
- Developer tools with built-in AI assistance
- Internal apps with rapid UI prototyping and Swagger docs

It’s ready for you too.

```csharp
var todo = await new Todo { Title = "Learn Sora" }.Save();
var todos = await Todo.Where(t => !t.IsDone);
```

---

## 🛠 Getting Started

1. Clone or create a new project with the Sora template
2. Explore the `samples/` directory
3. Browse documentation under `/docs/engineering`
4. Try something real—you’ll know in 10 minutes if it clicks

---

## ❤️ For developers who love clarity

Sora is open-source, MIT-licensed, and community-friendly. We welcome contributions, ideas, and questions. Built by folks who got tired of choosing between simplicity and power.

- GitHub Issues for bugs/requests
- GitHub Discussions for questions
- See [CONTRIBUTING.md](CONTRIBUTING.md) to jump in

---

**License:** Apache 2.0
**Requirements:** .NET 9 SDK
**Current version:** v0.2.18
