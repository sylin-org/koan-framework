# Sylin.Koan.Data.Connector.Cockroach

Supported CockroachDB provider for Koan Entity persistence over the PostgreSQL wire protocol.

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
- CRUD, native filters, explicit pages, provider-bounded Entity streams, and all three declared
  isolation modes use the supported relational/Npgsql foundation.
- Cockroach uses primary-key ordering where PostgreSQL would use `ctid`; streams are offset-based,
  not snapshot-based, resumable, or mutation-safe.
- Schema changes are additive; Koan is not a destructive migration engine.
- Merely referencing the connector does not make an unused CockroachDB endpoint a readiness dependency.
  Default election or runtime source use does.
- A reachable selected CockroachDB service is required. Unsupported SQL/filter semantics reject rather
  than silently scanning or substituting PostgreSQL.

See [TECHNICAL.md](TECHNICAL.md) for configuration and provider boundaries.
