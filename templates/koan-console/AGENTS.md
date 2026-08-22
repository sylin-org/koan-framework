# Working on this Koan application

This is a Koan application. Koan is a .NET meta-framework in which **a package reference is the
intent**: referencing a capability makes it available, and starting Koan composes everything
referenced, once. Application code states business meaning; the framework owns composition, provider
election, lifecycle, and explanation.

```csharp
using var app = new ServiceCollection().StartKoan();

var todo = await new Todo { Title = "buy milk" }.Save();
var loaded = await Todo.Get(todo.Id);
var open = await Todo.Query(t => !t.Done);
```

```csharp
public sealed class Todo : Entity<Todo>;
```

## Two rules that shorten most tasks

1. **To add a capability, add its package reference.** Do not write provider registration, a
   repository around ordinary Entity operations, or a service locator. A capability that needs
   configuration documents its own keys and rejects unsupported intent with a corrective explanation.
2. **Never construct an identifier or an API from a product name.** Package identifiers are exact and
   are not derivable. Copy them from the retrieval map below; do not guess them.

## What this application actually composes

Read the evidence before inferring composition from source. In rough order of cost:

- `koan.lock.json` — the referenced-module composition, written at build time and refreshed on every
  build. Readable without running the application.
- **Startup output** — what was elected, and why a dependency failed.

An application that adds a web host also serves `/.well-known/Koan/facts`, `/health/live`, and
`/health/ready`: the running application's redacted account of its own runtime decisions and
readiness.

## To add something this application does not have yet

**If the request is an outcome** — "add AI", "make it multi-tenant" — start at the recipe index:

<https://github.com/sylin-org/koan-framework/blob/main/docs/recipes/index.md>

Every entry is something a person actually asks for, and says what it gets you, what must already be
true, and what it costs to operate. Read it against *this* application and compose the answer: a
request like "add AI" covers several recipes with different runtimes and different operating costs, so
offer the ones this application is close to instead of naming a package. Open a single recipe only
once the conversation narrows to one.

**If the request already names a piece** — "add Mongo", "use SqliteVec" — the capability map is the
direct lookup:

<https://github.com/sylin-org/koan-framework/blob/main/docs/reference/capability-map.md>

Read both at their current revision rather than a pinned one; a frozen copy hides everything shipped
since. For architecture, operations, and migration, the agent retrieval map indexes the whole
documentation set:

<https://github.com/sylin-org/koan-framework/blob/main/llms.txt>

Paths inside it are repository-relative; resolve them against that same pinned base.

Prefer a capability the framework owns over hand-rolling equivalent behavior.

## If your harness supports skills

Koan publishes three coding skills — `koan` to build, extend, repair, and prove; `koan-explain` for
read-only explanation; `koan-upgrade` for framework migration. Where they are available, prefer them:
they carry more than this file does. `.claude/settings.json` enables them for Claude Code once you
trust the folder. This file is the portable fallback for every other harness.
