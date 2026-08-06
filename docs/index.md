---
type: GUIDE
domain: framework
title: "Koan documentation"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-07-23
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-23
  status: reviewed
  scope: public documentation home and reader paths
---

# Bring an idea

Koan starts with the thing your application is about.

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

That is enough for a persisted, queryable API in the Koan web starter. From there, the same `Todo`
can gain durable work, events, identity, semantic search, media, or a governed agent surface without
turning into a different application at every layer.

## Start where you are

- **[Make your first Koan application](getting-started/quickstart.md)** — Go from a template to
  stored data and a working API.
- **[Bring Koan into an application you already have](getting-started/adopt-existing-app.md)** — Add
  one Entity boundary while your controllers, services, EF models, and deployment stay put.
- **[Run an application with a story](../samples/README.md)** — Tend a garden, organize a photo
  vault, reconcile customers, or follow the complete first journey.
- **[Let an agent meet your application](reference/agents/index.md)** — Expose the same model and
  access rules through MCP—without building a second agent API.

## Let the idea grow

- [Store and query it](reference/data/index.md), then [give it an HTTP surface](reference/web/index.md).
- [Know who is acting](reference/identity/index.md) and keep tenant boundaries intact.
- [Run durable work and communicate](reference/work/index.md) without inventing a parallel work model.
- [Add AI and semantic search](reference/ai/index.md), or [work with files and media](reference/state-content/index.md).
- [Turn imperfect arrivals into trusted records](reference/canon/index.md).

Add what the application asks for. The domain code should remain the easiest part to recognize.

## Look behind the magic

- [Decide whether Koan fits](architecture/index.md).
- [See what works today](reference/what-works.md).
- [Troubleshoot a running application](support/troubleshooting.md).
- [Orient a coding agent](../llms.txt).

> Koan 0.20 is a .NET 10 preview.
