---
type: RECIPE
recipe: poc-an-idea
title: "Turn an idea into a running application"
domain: core
status: current
last_updated: 2026-08-22
audience: [developers, ai-agents]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/poc-an-idea.md
gets_you: "A running, seeded application built from an idea described in plain language - entities named as nouns, one flow chosen to feel great, the first slice alive before the coffee cools."
works_if: "You can say who uses it and what it should do. Code can come later; the conversation comes first."
costs: "Nothing beyond the local floor: SQLite, embedded vectors, in-process ONNX. No accounts, no services, everything stays on your machine."
ingredients:
  - "one | the application bundle and a local database | Sylin.Koan.App, Sylin.Koan.Data.Connector.Sqlite"
  - "optional | meaning-based search from day one | Sylin.Koan.Data.AI, Sylin.Koan.Data.Vector.Connector.SqliteVec, Sylin.Koan.AI.Connector.Onnx"
---

# Turn an idea into a running application

This is the first destination: an idea described in sentences becomes an application you can
click. It is also a conversation, not a form. Expect a few turns about nouns and flows before
anything compiles - that is the work.

## Start in business language

Name the actors and objects as they would be said out loud, not as tables:

> "a tool to help our kitchen team track prep work" becomes PrepTask, Recipe, Station, and a
> prep schedule.

If the idea is fuzzy, two questions usually sharpen it: who uses this, and which single flow
must feel great when they do? Everything else can wait.

## Stand up the smallest living slice

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o KitchenPrep
```

Three files, two references. Entities become classes, flows become controller methods or agent
sentences, and storage starts as local SQLite. If the idea leans on meaning rather than keywords,
add the search trio now - one attribute makes saves index themselves:

```diff
+ [Embedding(Template = "{Title}. {Notes}")]
  public sealed class PrepTask : Entity<PrepTask>;
```

## Let the data shape argue back

When entities start looking tabular - rows, joins, totals - relational storage (SQLite here,
Postgres later) fits naturally. When something is schema-flexible JSON inside a row, keep it a
document column in that same relational shell instead of reaching for a second engine. The
capability map carries this guidance as a table; cite it, choose once, move on.

Seed a handful of realistic records through ordinary saves so the first look shows a lived-in
application rather than an empty grid, then read `/well-known` facts together: which store was
elected, which embedder joined, what the composition locked in.

## Copy, do not invent

Agents lose hours rebuilding solved structure. These shipped files are the canonical shape -
open them before writing anything:

| Pattern | Where it already works |
|---|---|
| An entity that indexes meaning (`[Embedding]`) | [GardenCoop ch. 2 - `Models/Produce.cs`](../../../samples/journeys/GardenCoop/02-LocalDiscovery/Models/Produce.cs) |
| Seeding through ordinary saves (reset-by-rerun) | [LocalChecklist - `Program.cs`](../../../samples/fundamentals/LocalChecklist/Program.cs) |
| The local search UI over the same origin | [GardenCoop ch. 2 - `wwwroot/index.html`](../../../samples/journeys/GardenCoop/02-LocalDiscovery/wwwroot/index.html) |

## When the idea survives contact

Someone besides you wants to click it. That is the next destination - see
[share-a-prototype](share-a-prototype.md) for sign-in, access rules, and an exposure path.

## Boundaries

- The local floor has no authentication ceremony yet; adding it early is prototype work.
- Seeded records are for eyes, not for keeps - graduation to a real database starts empty by design.
- Retrieval quality on your own vocabulary is unmeasured until you measure it.
