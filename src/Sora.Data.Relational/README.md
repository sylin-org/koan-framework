# Sora.Data.Relational

Adapter-agnostic relational schema toolkit used by providers like `Sora.Data.Sqlite`.

- Contracts: `IRelationalDialect`, `IRelationalSchemaModel`, `IRelationalSchemaSynchronizer`

## LINQ (minimal translator)

An intentionally small LINQ-to-SQL helper lives in `Linq/`:

- `ILinqSqlDialect`: tiny hooks the translator needs (identifier quoting, LIKE escaping, parameter naming).
- `LinqWhereTranslator<TEntity>`: translates a restricted subset of predicate expressions to a WHERE clause and parameters.
- `RelationalCommandCache`: caches select lists per (entity, dialect) to avoid repeated string building.

Providers can implement `ILinqSqlDialect` (in addition to schema `IRelationalDialect`) to enable pushdown. Unsupported expressions should throw NotSupportedException; callers should fallback to in-memory filtering.
- Builder: `RelationalModelBuilder.FromEntity(typeof(TEntity))` builds a table model from annotations
- Synchronizer: `EnsureCreated(dialect, model, connection)` emits CREATE TABLE + INDEX statements (add-only)

Notes:
- Provider-specific SQL grammar belongs in the provider (e.g., `SqliteDialect` in `Sora.Data.Sqlite`).
- Complex CLR types map to JSON-encoded TEXT columns; simple types map to native types.
