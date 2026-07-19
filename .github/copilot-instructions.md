# Koan contribution instructions

Koan is an opinionated .NET 10 meta-framework for agentic, Entity-centered applications. Design from
the developer's business sentence inward: application code states intent while framework pillars own
composition, provider negotiation, lifecycle, correction, and explanation.

Koan 0.20 is the preview line. Only packages named by `supported-foundation` or
`supported-extension` claims carry that signal; repository presence is not support. Read
`docs/reference/product-surface.md` before strengthening a public promise.

## Canonical application grammar

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

Prefer Entity statics and instance verbs over application repositories:

```csharp
var todo = await new Todo { Title = "Ship it" }.Save();
var same = await Todo.Get(todo.Id);
var open = await Todo.Query(item => item.Title != "");
await todo.Remove();
```

## Architectural laws

- References make capabilities available; `AddKoan()` compiles generated semantic modules once.
- `Entity<T>` is the primary application and IntelliSense surface.
- A functional assembly normally owns one domain-named `KoanModule`. Its identity comes from normal
  package/assembly identity.
- Cross-module contracts live in genuinely inert Core, Abstractions, or Contracts assemblies.
- Core owns generic composition mechanics. Pillars own semantic policy and runtime chokepoints.
  Adapters own provider mechanics and capability declarations. Applications own business rules.
- Compile structural discovery, contributions, provider election, and context plans once per host;
  operations consume the immutable result.
- Same syntax does not imply provider parity. Required guarantees resolve or reject with a correction.
- Prefer standard .NET hosting, DI, options, health, logging, MSBuild, and NuGet concepts before adding
  Koan-specific ceremony.
- Fewer meaningful owners beat a concern spread across models, middleware, services, and adapters.

## Public capability language

- Data: `Entity<T>` with `Save`, `Get`, `Query`, paging, streams, and `Remove`.
- Web: `EntityController<T>` and contributor-owned request context/policy.
- Jobs: an Entity implements `IKoanJob<T>` and owns its business execution.
- Communication: `entity.Events.Raise<TEvent>()` says something happened;
  `entity.Transport.Send()` distributes the current Entity snapshot.
- MCP: referenced modules project governed Entities and custom verbs; application code does not map a
  second tool/router pipeline.
- Operations: startup, `/health/live`, `/health/ready`, `/.well-known/Koan/facts`, `koan://facts`, and
  `koan.lock.json` explain the same composition.

## Working rules

- Before production code, follow `.codex/skills/explore/SKILL.md` and record the architecture
  checkpoint required there.
- Reuse the closest existing owner, option, constant, contributor, and test pattern before creating a
  new abstraction.
- Keep public APIs small; use internal helpers for mechanics.
- Do not add compatibility shims, duplicate activation paths, process-global service providers,
  silent fallback from configured intent, or provider-specific policy in application code.
- Keep controllers rather than inline route maps for public HTTP application surfaces.
- Never log credentials, connection strings, bearer tokens, or raw exception payloads.
- Use focused owner and consumer tests during implementation. The full release ratchet runs only at an
  explicit certification boundary.
- Update code, focused tests, package presentation, samples, facts, and public docs together when the
  public promise changes.

## Documentation routing

- Current public curriculum: `README.md`, `docs/toc.yml`, and linked current pages.
- Package use and limits: each package's `README.md`; mechanics: its proportional `TECHNICAL.md`.
- Runnable learning: only samples listed by `samples/README.md`.
- Maturity/package authority: generated `docs/reference/product-surface.md` and
  `docs/reference/package-quality.md`.
- Durable architectural law: `docs/architecture/`; dated rationale: `docs/decisions/`.
- Engineering initiatives, assessments, migrations, proposals, archives, and shelved source are not
  ordinary application guidance.

Do not hand-edit generated product/package reports. Run the existing compiler. Run
`scripts/public-docs-lint.ps1`, focused docs/skills/example checks, and `git diff --check` after public
guidance changes.
