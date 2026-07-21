# Sylin.Koan.Data.Connector.Cockroach

CockroachDB provider for Koan Entity persistence over the PostgreSQL wire protocol.

## Install

```powershell
dotnet add package Sylin.Koan.Data.Connector.Cockroach
```

## Meaningful use

Reference the package, call `AddKoan()`, and use normal Entity verbs. With a reachable CockroachDB endpoint, Koan
discovers or reads the connection, selects the provider, creates allowed schema on first use, and persists entities.

```csharp
builder.Services.AddKoan();

public sealed class Order : Entity<Order>;

await new Order().Save();
```

Set `ConnectionStrings:Cockroach` when autonomous discovery is not appropriate. Provider-local `DdlPolicy`,
`SchemaMatching`, and `AllowProductionDdl` settings override their safe defaults only for CockroachDB routes.

## Guarantees and limits

- Referencing Cockroach activates CockroachDB, not the PostgreSQL connector.
- Shared Npgsql mechanics do not own discovery, configuration, election, or startup reporting.
- Cockroach uses primary-key ordering where PostgreSQL would use `ctid`.
- Schema changes are additive; Koan is not a destructive migration engine.
- A reachable CockroachDB service is required. Unsupported SQL/filter semantics reject rather than silently scanning.

See [TECHNICAL.md](TECHNICAL.md) for configuration and provider boundaries.
