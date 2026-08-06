---
type: ARCHITECTURE
domain: framework
title: "Small code. Serious architecture."
audience: [architects, developers, technical-leads, ai-agents]
status: current
last_updated: 2026-07-23
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-23
  status: reviewed
  scope: architect fit, brownfield adoption, responsibility boundary, and production ownership
---

# Small code. Serious architecture.

The two declarations are not demo sleight of hand. They are the architecture:

```csharp
public sealed class Todo : Entity<Todo>;
[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

`Todo` says what the application knows. `TodosController` says how the outside world reaches it. With
the web application bundle and SQLite connector, that is a persisted, queryable HTTP API.

The same Entity stays at the center when the application gains authorization, background work,
events, caching, semantic search, media, or agent access. The business idea remains visible while
Koan brings the machinery together around it.

**The magic is not that Koan hides everything. It is that Koan asks you to say only what matters—and
can explain the rest.**

## Why it stays small

- **Intent stays in charge.** Application code talks about `Todo`, not database sessions, transport
  envelopes, or tool schemas.
- **References bring capabilities.** Add a supported package and Koan discovers what it contributes.
  `AddKoan()` brings those contributions together once as the application starts.
- **Conventions have exits.** Use a normal ASP.NET Core controller, service, provider SDK, or
  deliberate override wherever the default is not the right shape.
- **The application can explain itself.** Startup reports, health, runtime facts, and the composition
  lockfile show what became active and why.

## Where Koan shines

Koan is a strong fit when:

- business state maps naturally to Entities;
- conventions are more valuable than repeating data, API, job, and integration plumbing;
- the application should start locally and move to external providers without changing its domain
  vocabulary;
- people and agents should work through the same governed application model; and
- architects need framework choices to remain visible at runtime.

Take a different path, or keep Koan at a smaller boundary, when the design depends on:

- a stable 1.0 compatibility contract—Koan 0.20 is still a preview;
- general transactions across different providers;
- transparent failover between providers with different guarantees;
- framework-owned infrastructure provisioning, backup, or disaster recovery; or
- NativeAOT.

Koan will not quietly pretend those guarantees exist.

## It can join an application already in motion

Koan does not need to own the whole system. Add `AddKoan()` to an existing ASP.NET Core host,
introduce one Entity at a useful boundary, and leave the rest alone.

Existing middleware, controllers, EF Core models, repositories, SDK clients, authentication, and
deployment topology can remain exactly where they are. Adopt another Koan capability only after the
first one earns its place.

[See incremental adoption in an existing ASP.NET Core application.](../getting-started/adopt-existing-app.md)

## Who owns what

Koan is an application framework, not a deployment platform.

| Koan makes easier | Your application and platform still own |
|---|---|
| Discovering referenced capabilities and bringing them together | Choosing which capabilities belong in the application |
| Selecting an eligible provider and explaining the choice | Provider configuration, deliberate overrides, and acceptable guarantees |
| Entity persistence, API, access, work, and agent conventions | Domain rules, workflows, and integration contracts |
| Health, runtime facts, and actionable startup failures | SLOs, scaling, credentials, networks, and incident response |
| Supported authentication, access, and tenant boundaries | Trust design, entitlements, exposed operations, and compliance |
| Provider behavior that Koan explicitly documents | Backups, restore testing, retention, RPO/RTO, and disaster recovery |

Docker, Aspire, Kubernetes, databases, brokers, model runtimes, and identity providers keep doing
their jobs. Koan connects application intent to them; it does not become them.

When several instances run, local SQLite and local files remain local to each instance. External
providers may give instances shared reach, but those services still own consistency, availability,
backup, and failover. Background work remains at-least-once, so handlers must be idempotent.

## Look behind the magic

The small code does not require blind trust. A running Koan application can tell you which modules and
providers became active, why they were selected, whether their dependencies are ready, and whether
the referenced composition has drifted.

That visibility is there for development, tests, architecture review, and operations—not as homework
before someone writes their first Entity.

## Keep exploring

- [See what Koan can do today](../reference/what-works.md)
- [Bring Koan into an existing application](../getting-started/adopt-existing-app.md)
- [Read the design principles](principles.md)
- [Review how capabilities extend Entities](entity-semantics-contract.md)
- [Test and inspect an application](../reference/operations/index.md)
- [Plan external infrastructure](../reference/operations/external-topology.md)
- [Check exact package and support status](../reference/product-surface.md)
