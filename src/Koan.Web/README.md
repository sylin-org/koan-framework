# Sylin.Koan.Web

Turn Koan entities into controller-first ASP.NET Core APIs, with shared policy, health, and runtime facts.

- Target framework: net10.0
- License: Apache-2.0

## What it adds

- `EntityController<TEntity, TKey>` and the string-key `EntityController<TEntity>` convenience surface
- attribute-routed ASP.NET Core MVC integration
- health and optional OpenAPI wiring
- static-file middleware only when the host supplies a real web-root provider
- redacted runtime facts at `GET /.well-known/Koan/facts`
- response transformers for deliberate representation shaping

## Install

```powershell
dotnet add package Sylin.Koan.Web
```

## Meaningful result

Define the business model and inherit one controller. `AddKoan()` discovers the capability; the base controller supplies
the CRUD projection through the shared Entity operation boundary:

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

The result is the `/api/todos` entity API. Lifecycle, authorization, relationship budgets, and other
Entity policies execute at the shared operation boundary rather than being copied into controller actions. Add custom
actions only for business operations that are not entity CRUD.

Use transformers when a representation must differ from the stored model; see WEB-0035 below.

## Inspection and configuration

Runtime facts use the same schema as startup, health, and `koan://facts`. Outside Development, enable
`Koan:Web:ExposeObservabilitySnapshot` deliberately and protect `GET /.well-known/Koan/facts` as an operational
surface. Static-file wiring stays dormant in API-only hosts that have no real web root.

## Boundaries and failures

- This package projects capabilities into ASP.NET Core; it is not a standalone server and does not choose a data
  provider. Use `Sylin.Koan.App` for the shortest web entry bundle or compose lower-level packages deliberately.
- `EntityController<T>` exposes direct Entity CRUD. It does not infer workflow endpoints, recursive graph traversal,
  authorization policy, or a public contract versioning strategy.
- Relationship expansion preserves per-type visibility and finite budgets. Unsupported implicit scans return a
  corrective response instead of silently loading a whole source.
- Runtime facts are redacted, not anonymous. Non-Development exposure remains an operator-owned security decision.
- Authentication and richer authorization projections live in optional Web/Auth packages; referencing Web alone does
  not make an API secure.

## Technical reference

- [Koan.Web technical reference](https://github.com/sylin-org/Koan-framework/blob/main/src/Koan.Web/TECHNICAL.md)
- [Web API conventions](https://github.com/sylin-org/Koan-framework/blob/main/docs/api/web-http-api.md)
- [WEB-0035 — EntityController transformers](https://github.com/sylin-org/Koan-framework/blob/main/docs/decisions/WEB-0035-entitycontroller-transformers.md)
- [Engineering guardrails](https://github.com/sylin-org/Koan-framework/blob/main/docs/engineering/index.md)
