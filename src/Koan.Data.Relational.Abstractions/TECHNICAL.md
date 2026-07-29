# Sylin.Koan.Data.Relational.Abstractions technical notes

This assembly is a module-free boundary shared by the functional relational owner and physical providers. It contains
only SQL-dialect hooks, schema-executor/store-feature contracts, immutable per-route schema policy, and column/index
descriptions. It references Data abstractions for Entity constraints, physical paths, mapping receipts, and query
intent.

The lowering boundary is exact:

- the Framework compiles logical mapping and codec authority;
- the Relational Family emits `RelationalCommandPlan`, column definitions, and index definitions;
- an adapter implements `IRelationalMappingDialect`/`IRelationalDdlExecutor`, maps abstract scalar/structured shape to
  native types and SQL, owns resources/dispatch, and classifies exact native failures.

Feature flags for definition validation, mapped indexes, rewrite-free expressions, and native TTL are executable
claims. Default interface values are false so an older provider cannot gain a stronger profile by package upgrade.

It owns no configuration, DI registration, provider election, connection, schema state, or runtime cache. Those
decisions remain with the functional relational owner and the selected provider/source route.
