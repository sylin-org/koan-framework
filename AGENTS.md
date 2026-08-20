# Koan — agent bootstrap

This is the Koan framework repository. Koan is an opinionated .NET meta-framework for agentic,
data-driven applications, in which **a package reference is the intent**: referencing a capability
makes it available, and `AddKoan()` composes everything referenced, once. Application code states
business meaning; the framework owns composition, provider election, lifecycle, and explanation.

This file is harness-neutral and deliberately short. It routes; it does not restate.

## Which work are you doing?

**Changing the framework.** [CLAUDE.md](CLAUDE.md) owns the contributor law — product objective,
architectural laws, module authoring, evidence and diagnostics, and documentation authority. Read it
regardless of which agent you are; nothing in it is vendor-specific. Before changing production code,
follow [.codex/skills/explore/SKILL.md](.codex/skills/explore/SKILL.md).

**Working on an application in [samples/](samples/README.md), or on your own Koan application.**
Continue below.

**Picking work up where a previous session left it.** [docs/MEMORY.md](docs/MEMORY.md) indexes where
current state lives and carries the working conventions and hard-won lessons that are written nowhere
else. It is model-agnostic and lives in the repository on purpose: an assistant's private memory is a
cache, not the source.

## The application grammar

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

```csharp
public sealed class Todo : Entity<Todo>;

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

```csharp
var todo = await new Todo { Title = "Ship it" }.Save();
var open = await Todo.Query(item => !item.Done);
```

## Two rules that shorten most tasks

1. **To add a capability, add its package reference.** Do not write provider registration, a
   repository around ordinary Entity operations, a service locator, or manual endpoint mapping. A
   capability that needs configuration documents its own keys and rejects unsupported intent with a
   corrective explanation.
2. **Never construct an identifier or an API from a product name.** Package identifiers are exact and
   are not derivable. Copy them from the retrieval map; do not guess them.

## What an application actually composes

Read the evidence before inferring composition from source:

- `koan.lock.json` — the referenced-module composition, written at build time, refreshed on every
  build, readable without running the application.
- **Startup output** — what was elected, and why a dependency failed.
- `/.well-known/Koan/facts` and `koan://facts` — the same redacted runtime decisions.
- `/health/live` and `/health/ready` — process liveness and dependency readiness.

## To find a capability you have not used yet

**If the request is an outcome** — "add AI", "make it multi-tenant" — start at the
[recipe index](docs/recipes/index.md). Every entry is something a person actually asks for, and says
what it gets you, what must already be true, and what it costs to operate. Read it against this
application and compose the answer rather than naming a package.

**If the request already names a piece** — "add Mongo" — the
[capability map](docs/reference/capability-map.md) is the direct lookup: outcome, exact package, and
the recipe that carries install, configuration, working code, and provider limits.

Read both at their current revision — a frozen copy hides everything shipped since.

For anything the map does not cover, [llms.txt](llms.txt) indexes the whole documentation set, and
[product-surface.md](docs/reference/product-surface.md) is the evaluated inventory.

Prefer a capability the framework owns over hand-rolling equivalent behavior.

## If your harness supports skills

Koan publishes three coding skills — `koan` to build, extend, repair, and prove; `koan-explain` for
read-only explanation; `koan-upgrade` for framework migration. Where they are available, prefer them:
they carry more than this file does. See [docs/guides/agent-skills.md](docs/guides/agent-skills.md);
the portable sources are under [.agents/skills/](.agents/skills). This file is the fallback for every
other harness.
