---
type: ARCHITECTURE
domain: framework
title: "Koan framework architecture principles"
audience: [architects, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: source-first
validation:
  date_last_tested: 2026-07-17
  status: reviewed
  scope: current product constitution, Entity semantics, semantic composition kernel, and public architecture
---

# Koan framework architecture principles

The [product constitution](product-constitution.md) defines Koan's durable product rules. The
[Entity Semantics Contract](entity-semantics-contract.md) governs how capabilities extend its
first-class application language. These principles explain the current architecture behind them.

## Business intent is the public API

Koan aims for a one-to-one mapping between an application decision and readable code:

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

The framework earns its existence by removing non-business ceremony without hiding decisions that
change guarantees. An extra public concept must express real domain intent, a guarantee, or a
deliberate override.

## Entity is the center of gravity

`Entity<T>` owns common persistence and genuinely Entity-centered semantics. The same model can gain
cache, lifecycle, jobs, embeddings, authorization, events, transport, HTTP, and MCP behavior from the
modules the application references.

```csharp
var todo = await new Todo { Title = "Ship" }.Save();
var same = await Todo.Get(todo.Id);
var open = await Todo.Query(item => !item.Done);
await todo.Remove();
```

Module facets extend Entity vocabulary only where the meaning remains honest. Multi-aggregate or
external-system workflows stay business-named workflows; they are not forced into Entity extensions.

## References declare availability; pillars resolve behavior

Referencing a functional package contributes one generated semantic module. `AddKoan()` compiles the
application constitution once, retains one instance per module, and freezes one composition. A
reference makes capability available; the owning pillar decides whether and how it participates.

A pillar may ship a minimal, very-low-priority local provider. Adding an eligible provider reference
can supersede that default without changing application terminals. Explicit configuration wins or
rejects with a correction—it never silently accepts a weaker guarantee.

Contracts consumed by another module live in isolated Core, Abstractions, or Contracts assemblies
without a `KoanModule`. Functional assemblies implement those contracts and own activation. Ordinary
.NET project/package identity is also module identity; no parallel Koan ID is required.

## One semantic composition kernel, domain-owned meaning

Core owns the generic laws for contribution ordering, provider election, immutable plan compilation,
segmentation, and explanation. Each pillar specializes those laws with domain meaning. Adapters remain
thin: they describe capabilities, contribute mechanics, and react to the elected plan.

This gives complexity one owner:

- Core owns generic composition law.
- A pillar owns semantic policy and its runtime chokepoint.
- An adapter owns backend mechanics and truthful capability declarations.
- The application owns business intent.

Cross-cutting capabilities contribute at the concern they affect. Tenancy, for example, contributes
segmentation to data, cache, storage, communication, and durable context only when the Tenancy module
is active. A contract reference alone stays inert because it contains no functional module.

## Compile structure once; keep runtime paths thin

Discovery, contribution ordering, provider eligibility, and plan construction are composition work.
They run once per host shape. Hot operations consume immutable plans and bind only operation-specific
values; they do not rescan assemblies, rediscover contributors, or renegotiate providers.

Memoization belongs at the owner of an expensive value. Process-static convenience must never erase
host ownership or leak one test/application composition into another.

## Semantic honesty is non-negotiable

One grammar does not imply identical backends. Providers declare what they can guarantee. A caller may
branch on optional capabilities; required intent fails loudly when no eligible implementation exists.

Events mean something happened to an Entity. Transport means distribute an isolated copy of current
Entity state. Persistence knows neither. Tenancy segmentation is supplied by the active Tenancy
capability, not embedded as tenant-specific branches inside data, cache, or communication cores.

Local-first defaults provide the complete semantic ring without external infrastructure. Networked,
durable, or weaker-liveness adapters extend mechanics only within their advertised guarantees.

## The application explains itself

Startup reporting, `/health/live`, `/health/ready`, `/.well-known/Koan/facts`, `koan://facts`, and the
composition lockfile project the same resolved decisions. They are not competing authorities.
Failures include a stable reason and a useful correction whenever the framework can know one.

Agents receive the same semantic economy as people: small APIs, current names, bounded tool exposure,
runtime self-description, and explicit denied/unsupported behavior. Documentation and examples must
prefer code a model can map directly back to business intent.

## Standard .NET is the substrate

Koan creates a new concept only when the BCL, hosting, DI, options, logging, health, MSBuild, NuGet,
or ordinary assembly metadata cannot express the required semantic guarantee. Custom identity,
activation metadata, wrappers, and registries must not restate standard .NET facts.

A functional assembly author normally writes only a domain-named `KoanModule` and overrides the verbs
the module genuinely needs:

```csharp
public sealed class BillingModule : KoanModule
{
    public override void Register(IServiceCollection services)
        => services.AddSingleton<InvoicePolicy>();
}
```

## One current path

Public documentation teaches one canonical expression per intent. Compatibility mechanisms may exist
inside the framework when justified, but removed APIs, migration plans, and historical alternatives do
not remain in the current curriculum. ADRs are retained as dated decisions; current behavior is
reported by source, focused executable evidence, and the
[generated product surface](../reference/product-surface.md).

## Review questions

Before adding a part, ask:

1. What business sentence becomes easier to express?
2. Which layer owns the decision: application, pillar, adapter, or Core law?
3. Can standard .NET already carry the identity or lifecycle?
4. Can the structure compile once instead of rediscovering at runtime?
5. How does startup, facts, health, and an agent explain the same decision?
6. What unsupported guarantee fails loudly?
7. Does deleting another moving part make this owner clearer?
