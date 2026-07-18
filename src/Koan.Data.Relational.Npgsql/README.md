# Sylin.Koan.Data.Relational.Npgsql

Shared Npgsql repository mechanics for Koan's PostgreSQL-wire Data providers.

## Install

Application developers should install a concrete provider instead:

```powershell
dotnet add package Sylin.Koan.Data.Connector.Postgres
```

Provider authors implementing another compatible PostgreSQL-wire connector can reference the mechanism directly:

```powershell
dotnet add package Sylin.Koan.Data.Relational.Npgsql
```

## Meaningful result

A concrete provider maps its resolved endpoint, identity, schema policy, naming convention, and stable-order clause to
`NpgsqlRepositoryOptions`, then returns `NpgsqlRepository<TEntity,TKey>` from its normal `IDataAdapterFactory`. The
repository supplies the shared Entity CRUD, query, paging, batch, isolation, and schema behavior without borrowing or
activating another provider connector.

## Guarantees and limits

- This package contains no `KoanModule`, discovery adapter, provider election, health contributor, or startup report.
- Referencing it alone activates no Data provider and opens no connection.
- Concrete providers retain ownership of configuration, source routing, identity, discovery, and operations reporting.
- The mechanism assumes compatible Npgsql/PostgreSQL-wire SQL behavior. Provider-specific differences must be explicit
  options or remain in the concrete connector; compatibility is not inferred from protocol alone.

See [TECHNICAL.md](TECHNICAL.md) for the ownership boundary.
