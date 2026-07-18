# Sylin.Koan.Web.Extensions

Add terse Entity REST exposure or explicit moderation, audit, and soft-delete HTTP capabilities without writing
framework hosting code.

## Install

```powershell
dotnet add package Sylin.Koan.Web.Extensions
```

The package composes through the application's existing `AddKoan()` call. Do not add an Extensions-specific service
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

Equivalent bases exist for audit and soft delete. The declaration is the opt-in; the package does not expose those
surfaces for every Entity merely because it is referenced.

## Guarantees and boundaries

- Generic controller declarations belong to one application host and cannot leak into another host in the process.
- One generic capability projection has one route. Conflicting registrations fail with a correction; use an explicit
  controller when multiple route projections are genuinely required.
- `[RestEntity]` declares exposure only. Entity `[Access(...)]`, `EntityAccess<T>`, authentication, and configured
  capability policies remain the authorization authority shared by REST and MCP.
- Audit, moderation, and soft delete are partition-backed Entity workflows. They do not promise cross-partition
  transactions, immutable compliance logging, legal retention, or a universal workflow engine.
- List operations are explicitly paged. This package does not turn an adapter's unsupported query or streaming
  capability into a supported one.

Use `AddKoanAuthorization(...)` only when the application needs named ASP.NET policies or capability-policy mappings.
The ordinary `[RestEntity]` path requires no additional registration.

See [TECHNICAL.md](TECHNICAL.md) for activation, routing, authorization, and persistence ownership.
