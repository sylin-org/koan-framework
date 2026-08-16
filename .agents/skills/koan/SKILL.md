---
name: koan
description: Build, extend, repair, and prove greenfield or current Koan applications. Use when turning a business outcome into an Entity-centered app; choosing or composing data providers, Web, identity, tenancy, classification, Jobs, Communication, cache, storage, media, AI, vectors, MCP, Canon, testing, or operational capabilities; replacing a provider; or fixing behavior or composition. For read-only explanation use koan-explain. For framework migration or removed Koan APIs use koan-upgrade.
---

# Koan

Turn the requested outcome into the smallest coherent Koan application change, then prove it. Act as one front door: choose the pieces internally and describe the stack in business language.

## The Koan grammar

Teach and preserve Koan's semantic shape:

1. **Reference = intent.** A referenced capability becomes available to composition; application code does not reproduce its registration.
2. **Compose once.** Call `AddKoan()` once. Referenced modules, providers, facts, and health join one application.
3. **The Entity is the vocabulary.** Model business state with `Entity<T>` and express common work directly: `Save`, `Get`, `Query`, `Page`, `AllStream`, `Remove`, relationships, and lifecycle policy.
4. **Global extensions keep fluent operations small.** Instance and collection extensions make useful behavior read like the business action.
5. **Context is deliberate and scoped.** Use `EntityContext.Adapter(...)`, `Source(...)`, or `Partition(...)` for an exceptional provider, route, or partition; use `Tenant.Use(...)` for tenant scope.
6. **Projection reuses the model.** `EntityController<T>` exposes governed Entity behavior over HTTP; OpenAPI, SSE, MCP, and other projections remain additive pieces.
7. **Composition explains itself.** Startup reporting, facts, health, and lock evidence show what was available, what was selected, and why a dependency failed.

Keep the ordinary path expressive:

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

var todo = await new Todo { Title = "Ship one useful thing" }.Save(ct);
var open = await Todo.Query(x => !x.Done, ct);
await new[] { new Todo { Title = "Compose" }, new Todo { Title = "Prove" } }.Save(ct);
```

One Entity can acquire semantic indexing, an agent surface, and HTTP projection as additive capabilities:

```csharp
[Embedding(Template = "{Title}. {Summary}")]
[McpEntity(Name = "knowledge", Description = "Curated knowledge")]
public sealed class KnowledgeItem : Entity<KnowledgeItem>
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
}

[Route("api/knowledge")]
public sealed class KnowledgeController : EntityController<KnowledgeItem> { }

var answer = await Client.Chat("Summarize today's knowledge.", ct);
var meaning = await Client.Embed("provider-neutral composition", ct);
```

Context switches are disposable and nestable, so exceptional routing stays visible and restores automatically:

```csharp
using (Tenant.Use("acme"))
using (EntityContext.Source("Archive"))
using (EntityContext.Partition("north"))
{
    var page = await Todo.FirstPage(25, ct);
}
```

Use `EntityContext.Adapter(...)` for a deliberate adapter override. Nest only scopes whose combined business meaning is verified.

Use lower-level data or provider APIs only when the requested behavior cannot be expressed honestly through the Entity surface.

Work that must survive a restart is an Entity that owns its own execution:

```csharp
public sealed class Review : Entity<Review>, IKoanJob<Review>
{
    public static Task Execute(Review review, JobContext context, CancellationToken ct) => ...;
}
```

## The real shape

Every Koan package identifier begins `Sylin.Koan.` while namespaces stay `Koan.*`. Identifiers are exact and are not derivable from a product name — copy them from [capabilities.md](references/capabilities.md); never construct them.

A new application starts from the template rather than a reconstructed host:

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o TodoApi
```

That produces the whole host — `net10.0`, `Sylin.Koan.App`, and one composition point:

```csharp
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

Adding a capability is a reference plus, when the provider needs intent, configuration under `Koan:`:

```powershell
dotnet add package Sylin.Koan.Data.Connector.Mongo
```

```json
{ "Koan": { "Data": { "Sources": { "Default": {
  "Adapter": "mongo", "ConnectionString": "mongodb://localhost:27017", "Database": "todos" } } } } }
```

No adapter registration, repository, mapping ceremony, or endpoint wiring accompanies it.

A running application answers for itself:

| Ask | Address |
|---|---|
| Is the process alive? | `/health/live` |
| Are required dependencies ready? | `/health/ready` |
| What composed, and which provider won? | `/.well-known/Koan/facts` · `koan://facts` |
| What is agent-visible? | `koan://entities` · `koan://self` |
| What did references compose, and has it drifted? | `koan.lock.json` |

## Know the Lego shelf

Keep the full application story visible while adding only the pieces required now:

- **Data:** JSON, InMemory, SQLite, MongoDB, PostgreSQL, SQL Server, Redis, Couchbase, and CockroachDB stores behind the same Entity vocabulary; recoverable deletion and verified default-store cutover as additive pieces.
- **Web:** `EntityController<T>`, conventional HTTP behavior, OpenAPI, SSE, link-preview cards, and focused projections.
- **Trust and isolation:** authentication, authorization, tenancy, and field classification/protection.
- **Work and integration:** Jobs for durable or scheduled work; Communication for occurrences, snapshots, and transports.
- **State and content:** cache, Entity-owned storage, and media recipes/derivatives.
- **Intelligence:** AI operations, inspectable prompts, explicit provider routing, embeddings, and vector search across local, dedicated, or search-engine providers.
- **Agent surfaces:** MCP tools, resources, transports, and application self-description governed by the same rules as HTTP.
- **Trusted records:** Canon for reconciling imperfect arrivals into explainable Entities.
- **Proof and operations:** local test infrastructure, facts, health, diagnostics, telemetry, and topology evidence.

Load [capabilities.md](references/capabilities.md) whenever choosing a piece; it carries the exact package identifier and recipe link for each one, and flags the one piece that is shelved. Load [stacks.md](references/stacks.md) when the outcome crosses pieces or the developer asks what to combine.

Open the linked recipe before writing code against a piece this application has not used yet — it owns the install command, configuration keys, working code, and provider limits that this skill deliberately does not duplicate. When a link cannot be retrieved, say so and proceed only on what the skill states.

Load only the focused aid the work needs: [build.md](references/build.md) for a first slice, [extend.md](references/extend.md) for a capability boundary, [fix.md](references/fix.md) for composition diagnosis, [test.md](references/test.md) for a proof matrix, [ship.md](references/ship.md) for runtime readiness, or [research.md](references/research.md) for a changing external seam. Never ask the developer to choose a route.

## Work from current reality

Read repository instructions and the closest existing pattern before editing. Inspect references, target frameworks, Entities, `AddKoan()`, configuration names, provider routes, tests, and available runtime facts, health, and lock evidence. This skill's own `scripts/inspect-koan.ps1` gives a compact read-only snapshot — invoke it by its path inside this skill directory, with `-Path <project-root> -Format Json`.

Once a package is referenced, its own README sits beside the restored package and matches the version actually in use; prefer it over any other copy when the exact version matters.

Verify a candidate capability against current evidence:

1. the application's effective composition and public behavior;
2. current Koan capability docs and public types;
3. focused source, tests, or samples that exercise the requested operation.

Do not infer behavior or provider parity from a reference, name, or sample alone. If the evidence is absent or contradictory, state the gap and offer the nearest truthful Koan or ordinary .NET seam. Consult current primary sources when an external provider, protocol, security rule, or platform can change.

Use `koan-explain` for explanation without mutation. Use `koan-upgrade` for framework migration or removed Koan APIs. Keep application-level provider replacement and data cutover here.

## Preview the stack

Before adding or replacing a capability, show:

```text
Smallest useful stack
  Required now: <business pieces and why>
  Easy later:   <one or two genuine extension points>
  Preserved:    <routes, data, security, and topology>
```

Recommend one composition. Mention an alternative only when it changes a consequential guarantee. Ask at most one question, and only when repository evidence cannot settle data ownership, security, a public contract, or topology.

## Make the smallest coherent change

- Reuse existing constants, types, and the closest current pattern.
- Add only the references that own required behavior; keep `AddKoan()` singular.
- Keep business rules on the owning Entity or application operation.
- Keep provider selection, connection intent, access rules, retry bounds, prompts, and recipes at explicit policy boundaries.
- Preserve routes, payloads, database names, persisted data, identity, tenant boundaries, secrets, and topology unless the request explicitly changes them.
- Do not scatter provider checks through business code or add fallback that makes a required capability appear healthy.

Never silently migrate or delete data, weaken a security boundary, or change a public contract. Get explicit authorization before irreversible work or a consequential contract change. Adding a provider never authorizes moving existing data.

## Prove the story

Use the narrowest credible evidence for all three:

- **Behavior:** the requested public journey works.
- **Composition:** the intended capability and provider actually participate, visible through facts, health, lock evidence, or a provider-specific assertion.
- **Correction:** a missing dependency, invalid configuration, unsupported operation, or denied action fails at the owning boundary with a useful next move.

Compilation supports the proof but does not replace it. Add negative identity and tenant paths for governed surfaces; idempotency, retry, bounds, and cancellation for repeatable work; and provider selection whenever the provider is part of the claim. Never weaken policy or substitute a hidden provider to make a proof pass.

Lead the handoff with what the application can do now. Name the semantic stack, changed files and public behavior, proof run, any unproved boundary, and at most one genuinely useful next piece.
