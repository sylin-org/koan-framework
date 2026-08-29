---
type: GUIDE
domain: framework
title: "Koan agent skills"
audience: [developers, ai-agents]
status: current
last_updated: 2026-08-16
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-16
  status: verified
  scope: three-skill chooser, trust boundaries, and skill verification
---

# Choose one Koan skill

## Install

**A new application already has them.** Scaffolding writes a project `.claude/settings.json` that
registers Koan's marketplace and enables the plugin, so the skills arrive when you trust the folder —
no commands:

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o TodoApi
```

**An existing application adds them once:**

```
/plugin marketplace add sylin-org/koan-framework
/plugin install koan@koan
```

Either way the skills are referenced, not copied, so they follow the framework rather than freezing
into the project. Update with `/plugin marketplace update koan`; remove the two keys from
`.claude/settings.json` to opt a scaffolded project out.

The same files are readable directly under `.agents/skills/` for a harness that loads a portable
skill directory.

**Every other harness gets the bootstrap.** Scaffolding also writes an `AGENTS.md`, and every
graduated sample points at the one in this repository. It is deliberately small: the two rules that
shorten most work, the evidence an application produces about itself, and one pinned link to the
agent retrieval map. It names no capability, so shipping a package never edits it, and it defers to
the three skills wherever a harness supports them. See
[DX-0050](../decisions/DX-0050-portable-agent-bootstrap.md).

## Choose by outcome

Koan has three coding-agent entry points. Choose by the outcome, not by a package or subsystem.

| Skill | Say this when you want to… | Promise |
|---|---|---|
| `$koan` | “Build this.” “Add Mongo.” “Enable authentication.” “Use AI.” “Fix it.” “Get it ready to ship.” | Compose the smallest useful Koan stack, change only what the outcome earns, and prove the result. |
| `$koan-explain` | “What does this app do?” “Why did this provider win?” “Why is readiness red?” | Explain observed behavior and honest unknowns without changing anything. |
| `$koan-upgrade` | “Bring this older Koan app forward.” | Preserve application contracts while replacing obsolete Koan expressions with current ones. |
| `$koan-create-adapter` | “Koan doesn't reach our database/vector store/model server — build the connector.” | Research, author, place, and prove a conformant adapter for a provider the matrix does not cover, shipping it not assessed. |

An explicit invocation wins. Otherwise, choose `$koan-explain` for read-only work,
`$koan-upgrade` for a framework migration, and `$koan` for implementation.

A database or vector-provider switch is application evolution, so it stays with `$koan`. It becomes
an upgrade only when the Koan framework contract itself changes.

## What makes `$koan` delightful

Start with the developer's sentence. Show the proposed stack as three short parts:

- **Now** — the few pieces required for this outcome;
- **Later** — capabilities that remain easy to snap on;
- **Preserved** — routes, data, security, and topology that will not change.

Then build one working vertical slice. Prefer Koan's recognizable pieces: one `AddKoan()` composition
point, an `Entity<T>` domain model, expressive Entity operations, and `EntityController<T>` when an
HTTP surface is wanted. References declare intent; Koan composes the pieces.

The same approach scales from a tiny local application to MongoDB or PostgreSQL, authentication and
tenancy, jobs and communication, storage and media, AI and vector search, Canon, or MCP. The skill
loads only the relevant recipe and still considers the whole application so a new piece does not
create a second architecture.

Its capability shelf names the exact package identifier for each piece and links the recipe that owns
that piece's install command, configuration keys, and provider limits — so exact details stay one
click away without crowding the conversation. Package identifiers are not derivable from product
names, so the shelf is where they are read rather than constructed.

The shelf reports product truth; it does not set it. A package carrying a product claim appears as an
ordinary choice. A package carrying none is marked *not assessed*, so an agent can still reach for it
when the outcome needs it while saying plainly what is and is not promised.

## How the skills stay true

`scripts/skills-verify.ps1` checks four things, each answering a failure that would otherwise pass
unnoticed:

| Check | Answers |
|---|---|
| Routing | Does each skill still declare the name and description that select it? |
| Links | Does every reference and pinned recipe still resolve? |
| Shelf | Does the capability shelf match the claim ledger and package inventory? |
| Bootstrap | Does every scaffold and sample still carry a portable `AGENTS.md` that names no capability? |
| Truth | Does every package identifier restore, and does every construct the skill teaches compile? |

The first four run in the pull-request gate. The fifth needs a real restore from nuget.org, so it
runs on a schedule — against the **published** packages a developer installs, never a repository
project reference. A journey that compiled against repository sources would pass while the guidance
it verifies was unusable.

Verification proves the guidance is true. Whether it is *good* is judged by running real prompts
against [the evaluation rubric](../../evals/koan/rubric.md).

## Your first application

Scaffolding gives you a running, persisted API before any agent gets involved:

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o TodoApi
cd TodoApi
dotnet run
```

Six files, three of which are your application:

```
TodoApi/
  Program.cs             compose referenced Koan capabilities
  Todo.cs                the state the application owns
  TodosController.cs     expose Todos over HTTP
  TodoApi.csproj         Sylin.Koan.App + Sylin.Koan.Data.Connector.Sqlite
  .claude/settings.json  make Koan's skills available to an agent working here
  README.md
```

`Program.cs` is the whole host:

```csharp
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

It works immediately:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/todos `
  -ContentType application/json -Body '{"title":"buy milk"}'
# {"title":"buy milk","done":false,"id":"01a01087-c881-7059-aca7-d6d528b77d65"}

Invoke-RestMethod http://localhost:5000/api/todos
# [{"title":"buy milk","done":false,"id":"01a01087-c881-7059-aca7-d6d528b77d65"}]
```

No adapter registration, repository, mapping, or endpoint wiring was written. SQLite was elected from
the package reference and the database landed at `.koan/data/Koan.sqlite`. `/health/live` and
`/health/ready` answer `200`, and `/.well-known/Koan/facts` reports what composed.

`.claude/settings.json` registers Koan's marketplace and enables the plugin, so an agent opening this
folder already has `$koan`, `$koan-explain`, and `$koan-upgrade`. Delete the file to opt out.

## What to say next

Describe the outcome in business terms. The skill picks the packages, configuration, and proof — you
do not name a subsystem or choose a route.

| You want | Say something like |
|---|---|
| A new slice | “Build a reading-list app with durable local data, an HTTP surface, and a health check.” |
| A different store | “Use Mongo for new Order records. Keep the existing HTTP contract.” |
| Sign-in | “Enable sign-in and protect writes to the inventory; reads stay public.” |
| AI and search | “Add a concise product-summary operation and semantic search over the catalog.” |
| A provider move | “Switch the Order default route from MongoDB to PostgreSQL. Preserve existing data.” |
| Something broken | “Startup says Redis is unhealthy but facts show an in-memory cache. Fix it.” |
| Deployment readiness | “Prepare this service for its container deployment. Preserve routes and PostgreSQL.” |
| An explanation only | “Why did Mongo win for Order when both Mongo and PostgreSQL are referenced?” |
| An older app | “Bring this older Koan application to current Koan. Preserve its public contract.” |

Before changing anything, `$koan` shows the stack it intends — what is required now, what stays easy
later, and what it will preserve — then builds one working vertical slice and proves it.

These are the prompts the skills are evaluated against; the full corpus lives in
[evals/koan/cases.jsonl](../../evals/koan/cases.jsonl), including cases where the skills should
*not* engage.

## Trust boundaries

Skill selection never grants extra permission. Destructive data changes, credentials, publication,
external messages, and production operations retain their normal authorization requirements.

`$koan-explain` is strictly read-only. If a request begins with explanation and later asks for a fix,
the agent finishes the explanation, announces the transition to `$koan`, and only then makes changes.

`$koan-upgrade` may inspect old code to learn what the application must preserve. Old instructions do
not define the replacement; current Koan evidence does. An unknown replacement is reported rather
than guessed.

The canonical sources live under `.agents/skills/`. Compatibility and retirement records stay
outside the loaded skill experience.

For the durable design decision, see
[DX-0049](../decisions/DX-0049-three-skill-public-agent-surface.md).
