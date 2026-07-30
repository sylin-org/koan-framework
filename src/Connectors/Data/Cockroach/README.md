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

The same connector can describe an external shape without introducing provider vocabulary:

```csharp
builder.Services.AddKoan(koan => koan.Data.Source("Legacy").Map<Customer>(map => map
    .Container("CUSTOMER")
    .Key(customer => customer.Id).Name("CUSTOMER_NO")
    .Property(customer => customer.Name).Name("DISPLAY_NM")
    .Property(customer => customer.Profile).Object("PROFILE_JSON")));
```

External sources can be read-only, inspected through Koan's container/record descriptors, and exposed through bounded
registered `Query` and `Scalar` operations. Opaque SQL executes only through a configured read lane, where CockroachDB
enforces a read-only transaction.

## Guarantees and limits

- Referencing Cockroach activates CockroachDB, not the PostgreSQL connector.
- Shared Npgsql mechanics do not own discovery, configuration, election, or startup reporting.
- CRUD, native filters, explicit pages, provider-bounded Entity streams, and all three declared
  isolation modes use the supported relational/Npgsql foundation.
- Cockroach uses primary-key ordering where PostgreSQL would use `ctid`; streams are offset-based,
  not snapshot-based, resumable, or mutation-safe.
- CockroachDB serialization failures remain native provider failures. Koan does not automatically replay application
  work whose safety and idempotence it cannot prove.
- Schema changes are additive; Koan is not a destructive migration engine.
- Merely referencing the connector does not make an unused CockroachDB endpoint a readiness dependency.
  Default election or runtime source use does.
- A reachable selected CockroachDB service is required. Unsupported SQL/filter semantics reject rather
  than silently scanning or substituting PostgreSQL.

See [TECHNICAL.md](TECHNICAL.md) for configuration and provider boundaries.
