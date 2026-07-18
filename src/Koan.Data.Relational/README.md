# Sylin.Koan.Data.Relational

Shared relational execution for Koan Data providers: schema governance, SQL translation, comparable scalar encoding,
and AOT-clean ADO helpers.

## Application use

Applications normally reference a concrete connector, not this package directly:

```powershell
dotnet add package Sylin.Koan.Data.Connector.Sqlite
```

Provider authors who need the functional relational owner can reference it explicitly:

```powershell
dotnet add package Sylin.Koan.Data.Relational
```

The application remains Entity-first:

```csharp
builder.Services.AddKoan();

public sealed class Todo : Entity<Todo>;
```

The selected connector supplies its endpoint, schema, and provider-specific SQL. Koan applies the connector's
route-local DDL and matching policy automatically on first meaningful use.

## What this package owns

- the single functional registration for `IRelationalSchemaOrchestrator`;
- provider-neutral schema validation and add-only creation mechanics;
- restricted LINQ-to-SQL translation shared by relational connectors;
- comparable scalar JSON encoding and AOT-clean ADO command helpers.

It does not elect a Data provider, open a connection, activate PostgreSQL/SQL Server/SQLite/CockroachDB, or expose an
application-facing relational registration call. Cross-module contracts live in
`Sylin.Koan.Data.Relational.Abstractions`; PostgreSQL-wire repository mechanics live in the module-free
`Sylin.Koan.Data.Relational.Npgsql` package.

## Guarantees and limits

- Schema policy and resolved storage identity are supplied per selected provider/source route; connectors do not share
  a mutable global schema decision.
- Schema creation is additive. This package is not a destructive migration engine.
- Unsupported query expressions reject rather than silently scanning an unbounded source.
- A concrete connector and reachable database are required for persistence; this package alone provides no backend.

See [TECHNICAL.md](TECHNICAL.md) for provider-author contracts and supported translation boundaries.
