# Sylin.Koan.Templates

Two `dotnet new` paths to a persisted Koan Entity application. The templates choose a proved package family; the
generated application contains business code and ordinary .NET structure, not framework scaffolding.

## Install

```powershell
dotnet new install Sylin.Koan.Templates
```

This is the canonical Koan 0.20 preview entry. Public-feed publication follows the final package-only proof; the
same command already runs against the exact locally compiled candidate used by release validation.

## First result

```powershell
dotnet new koan-web -o TodoApi
cd TodoApi
dotnet run
```

Use the URL ASP.NET Core prints, then create and read a Todo:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/todos -ContentType application/json -Body '{"title":"buy milk"}'
Invoke-RestMethod http://localhost:5000/api/todos
```

For a console application:

```powershell
dotnet new koan-console -o TodoConsole
cd TodoConsole
dotnet run
```

## What each template states

| Short name | Explicit application intent |
|---|---|
| `koan-web` | `AddKoan()` + `Entity<Todo>` + `EntityController<Todo>` over SQLite |
| `koan-console` | `StartKoan()` + `Entity<Todo>` save/get/query over SQLite |

Both generated projects reference the appropriate Koan entry bundle and the SQLite provider. SQLite is elected by
reference priority and uses `.koan/data/Koan.sqlite`; there is no generated `appsettings.json`, provider registration,
schema script, repository, or version prompt. Add configuration only when the application intends to override a
derived default.

Each template release carries compatibility ranges compiled from the exact independently versioned package family it
was proved against. The application does not align Koan package versions itself.

## Inspectability and next steps

Startup output reports composition, selected data provider, connection intent, readiness, and corrections. A web app
also exposes the redacted `/.well-known/Koan/facts` envelope. Add a property to `Todo` or a second Entity/controller
pair and run again; the business model and API evolve together.

To choose another backend, reference its connector and provide only the endpoint or credentials it cannot derive.
Domain and controller code remain unchanged. Provider documentation owns its migration, concurrency, durability, and
deployment guarantees.

## Requirements and boundaries

- The templates target .NET 10 and require a feed containing their compiled Koan dependency ranges.
- They are minimal learning and first-application shapes, not production security or deployment templates.
- Authentication, authorization, tenancy, validation, backup, public API design, and external infrastructure remain
  explicit application decisions.
- SQLite provides durable embedded storage, not multi-node availability or a remote database service.
- Direct `dotnet pack` of this template source is unsupported because it cannot prove dependency floors; the Koan
  release compiler prepares and verifies the content-only package.
