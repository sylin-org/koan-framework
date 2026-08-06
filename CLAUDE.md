# Agent instructions for Koan Framework

Work as a direct, constructive collaborator. Challenge architecture when a simpler owner or stronger
semantic boundary exists. Once a decision is accepted, carry it consistently through code, tests,
samples, facts, and documentation.

## Product objective

Koan is the opinionated meta-framework for agentic, data-driven .NET applications. It should help
humans and coding agents take an application from V0 to V1 in meaningful small steps. Application
code reads as business; framework pillars own composition, backend negotiation, lifecycle, and
explanation.

The canonical application grammar is:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

```csharp
public sealed class Todo : Entity<Todo>;
public sealed class TodosController : EntityController<Todo>;
```

## Architectural laws

- `Entity<T>` is the first-class application language and IntelliSense discovery surface.
- References make capabilities available; `AddKoan()` compiles generated semantic modules once.
- A functional assembly normally owns one domain-named `KoanModule`. Identity derives from standard
  package/assembly identity; do not invent a second Koan ID.
- Contracts consumed across modules live in isolated Core, Abstractions, or Contracts assemblies
  without a functional module.
- Core owns generic composition law. Pillars own semantic policy and runtime chokepoints. Adapters own
  mechanics and capability declarations. Applications own business intent.
- Structural discovery, contribution ordering, and provider election compile once per host shape.
  Runtime operations consume immutable plans.
- Same syntax does not imply backend parity. Required guarantees negotiate or reject correctively.
- Cross-cutting capabilities contribute to each affected pillar only when the functional module is active.
- Prefer standard .NET hosting, DI, options, health, assembly, MSBuild, and NuGet concepts before
  creating a Koan-specific part.
- Complexity centralized at one owner is acceptable; the same complexity distributed across consumers is not.

## Application patterns

Prefer Entity statics and instance verbs:

```csharp
var todo = await new Todo { Title = "Ship it" }.Save();
var same = await Todo.Get(todo.Id);
var open = await Todo.Query(item => !item.Done);
await todo.Remove();
```

Jobs are Entities that own business work:

```csharp
public sealed class Review : Entity<Review>, IKoanJob<Review>
{
    public static Task Execute(Review review, JobContext context, CancellationToken ct) => ...;
}
```

Communication expresses intent:

```csharp
await order.Events.Raise<OrderApproved>(ct);
await order.Transport.Send(ct);
```

Events mean something happened to the Entity. Transport distributes an isolated copy of current
Entity state. Persistence knows neither. Both terminals are local-first and lift pointwise over
finite Entity collections and lazy streams.

## Preview boundary

Koan 0.20 is the current preview line. The generated
`docs/reference/product-surface.md` is the authority: only packages owned by supported-foundation or
supported-extension claims carry 0.20. Verified, demonstrated, experimental, specified, unassessed,
deprecated, and retired are separate dispositions defined in that document; dependency proximity
does not promote a package or turn evidence into a support promise.

## Module authoring

Create a `KoanModule` only for real registration, one-time startup, or reporting responsibility:

```csharp
public sealed class BillingModule : KoanModule
{
    public override void Register(IServiceCollection services)
        => services.AddSingleton<InvoicePolicy>();
}
```

Do not add manual framework registration to application `Program.cs`, parallel activation metadata,
process-global service providers, repository/service ceremony around ordinary Entity operations, or
provider-specific behavior without a capability boundary.

## Evidence and diagnostics

- Use focused integration tests through a real `AddKoan()` host for composition behavior.
- Read startup facts before guessing.
- Use `/health/live` and `/health/ready` for liveness/readiness.
- `/.well-known/Koan/facts` and `koan://facts` project the same runtime decisions.
- `koan.lock.json` records static composition drift.
- Every active sample must reach one meaningful result and prove the projections it advertises.
- Run focused owner/consumer checks during a slice. Reserve the full release ratchet for an explicit
  certification boundary.

## Documentation authority

Current guidance is:

- `README.md` and `llms.txt`;
- `docs/toc.yml` and its linked pages;
- `samples/README.md` and graduated samples;
- `docs/reference/product-surface.md`;
- source and focused tests.

Initiatives, assessments, plans, proposals, and archives are engineering evidence, not alternate
application patterns. ADRs are dated records and remain unchanged unless the task explicitly owns a
decision update.

Before changing production code, follow `.codex/skills/explore/SKILL.md`: map the business intent,
layers, existing contracts/options/constants, closest pattern, exact owner, failure boundary, and
focused proof before editing.
