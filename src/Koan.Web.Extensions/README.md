# Sylin.Koan.Web.Extensions

Add terse Entity REST exposure or explicit moderation and audit HTTP capabilities without writing
framework hosting code.

## Install

```powershell
dotnet add package Sylin.Koan.Web.Extensions
```

The package composes through the application's existing `AddKoan()` call, which is the whole
registration.

## Smallest meaningful result

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

var app = builder.Build();
await app.RunAsync();

[RestEntity]
public sealed class Todo : Entity<Todo>;
```

`Todo` now has full Entity CRUD at `/api/todo`. Supply `[RestEntity("api/todos")]` when the route is a business
decision. A concrete `EntityController<Todo>` takes precedence automatically when the API needs custom behavior.

Use an explicit capability controller when the business surface needs more than CRUD:

```csharp
[Route("api/todos")]
public sealed class TodoModerationController : EntityModerationController<Todo>;
```

An equivalent base exists for audit. The declaration is the opt-in; the package does not expose those
surfaces for every Entity merely because it is referenced. The declaration opens no authority either: every action
answers 403, naming what to map, until a policy is mapped for it (see below).

## Guarantees and boundaries

- Generic controller declarations belong to one application host and cannot leak into another host in the process.
- One generic capability projection has one route. Conflicting registrations fail with a correction; use an explicit
  controller when multiple route projections are genuinely required.
- `[RestEntity]` declares exposure only. Entity `[Access(...)]`, `EntityAccess<T>`, authentication, and configured
  capability policies remain the authorization authority shared by REST and MCP.
- Audit and moderation are partition-backed Entity workflows. They do not promise cross-partition
  transactions, immutable compliance logging, legal retention, or a universal workflow engine.
- List operations are explicitly paged. This package does not turn an adapter's unsupported query or streaming
  capability into a supported one.
- Soft deletion is owned by `Sylin.Koan.Data.SoftDelete`. Referencing that package makes ordinary Entity controller
  deletion inherit its Data-axis semantics; recycle-bin HTTP workflows remain explicit application controllers.

Use `AddKoanAuthorization(...)` or `AddCapabilityAuthorization(...)` to map capability actions to named ASP.NET
policies; a moderation or audit controller needs them, because an action with no policy is denied:

```csharp
builder.Services.AddCapabilityAuthorization(caps =>
{
    caps.Defaults.Audit.List = "can-audit";
    caps.Entities["Todo"] = new CapabilityPolicy { Moderation = { Approve = "is-editor" } };
});
```

`caps.DefaultBehavior = CapabilityDefaultBehavior.Allow` opens every unmapped action to any signed-in caller, and is
the application's explicit choice. The ordinary `[RestEntity]` path requires no additional registration.

See [TECHNICAL.md](TECHNICAL.md) for activation, routing, authorization, and persistence ownership.
