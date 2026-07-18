# Sylin.Koan.Data.Relational.Abstractions technical notes

This assembly is a module-free boundary shared by the functional relational owner and physical providers. It contains
only SQL-dialect hooks, schema-executor/store-feature contracts, immutable per-route schema policy, and column
descriptions. It references Data abstractions only for the `IEntity<TKey>` constraint.

It owns no configuration, DI registration, provider election, connection, schema state, or runtime cache. Those
decisions remain with the functional relational owner and the selected provider/source route.
