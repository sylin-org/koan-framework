# Koan.Data.Relational

Adapter-agnostic relational schema + LINQ translator used by providers like `Koan.Data.Connector.Sqlite` and `Koan.Data.Connector.SqlServer`.

- Contracts: `IRelationalDialect`, `IRelationalSchemaModel`, `IRelationalSchemaSynchronizer`
- LINQ translator hooks: `ILinqSqlDialect`, `LinqWhereTranslator<TEntity>`, `RelationalCommandCache`

## Capabilities
- Build table/index models from entity annotations
- Add-only schema synchronization (create table/index)
- Minimal LINQ-to-SQL pushdown for simple predicates and projections

## LINQ (minimal translator)

An intentionally small LINQ-to-SQL helper lives in `Linq/`:

- `ILinqSqlDialect`: tiny hooks the translator needs (identifier quoting, LIKE escaping, parameter naming).
- `LinqWhereTranslator<TEntity>`: translates a restricted subset of predicate expressions to a WHERE clause and parameters.
- `RelationalCommandCache`: caches select lists per (entity, dialect) to avoid repeated string building.

Providers can implement `ILinqSqlDialect` (in addition to schema `IRelationalDialect`) to enable pushdown. Unsupported expressions should throw NotSupportedException; callers should fallback to in-memory filtering.
- Builder: `RelationalModelBuilder.FromEntity(typeof(TEntity))` builds a table model from annotations
- Synchronizer: `EnsureCreated(dialect, model, connection)` emits CREATE TABLE + INDEX statements (add-only)

Notes:
- Provider-specific SQL grammar belongs in the provider (e.g., `SqliteDialect` in `Koan.Data.Connector.Sqlite`).
- Complex CLR types map to JSON-encoded TEXT columns; simple types map to native types.

## References
- Data access reference: `~/reference/data-access.md`
- Decision DATA-0061: `~/decisions/DATA-0061-data-access-pagination-and-streaming.md`

