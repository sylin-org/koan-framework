# Sylin.Koan.Data.Relational.Abstractions

Inert relational schema, dialect, and DDL vocabulary for Koan provider authors.

## Install

Application developers reference a functional relational connector instead. Provider authors can reference the
contract without activating relational runtime behavior:

```powershell
dotnet add package Sylin.Koan.Data.Relational.Abstractions
```

## Meaningful result

Providers implement the narrow dialect/DDL feature contracts and pass one immutable `RelationalSchemaPolicy` for
each physical route. This keeps PostgreSQL, SQL Server, SQLite, and CockroachDB decisions isolated in one host.

`IRelationalMappingDialect` receives already-resolved `PhysicalPath` values. `RelationalCommandPlan` carries symbolic
reads, encoded values, identity/conditional predicates, query intent, and the originating mapping receipt.
`RelationalColumnDefinition` and `RelationalIndexDefinition` carry definition-level shape and encoding facts. None of
these contracts opens a connection or chooses a provider.

## Guarantees and limits

- This package contains no `KoanModule`, repository, translator, driver, or schema executor.
- Referencing it cannot activate Data or open/change a database.
- Application code does not need these contracts; normal persistence remains Entity-first.
- The functional `Sylin.Koan.Data.Relational` package owns schema execution and translation mechanics.

See [TECHNICAL.md](TECHNICAL.md) for the contract boundary.
