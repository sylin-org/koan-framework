# Executing Provider Instructions (escape hatch)

Sora offers a small, optional Instruction API so you can access provider-specific features (like raw SQL) safely when you need to.

- Instruction names are stable, namespaced strings (also exposed as constants):
  - `relational.sql.nonquery`, `relational.sql.scalar`, `relational.sql.query`
  - `relational.schema.ensureCreated`, `relational.schema.validate`, `relational.schema.clear`
  - `data.ensureCreated`, `data.clear`
- Parameterization: pass an anonymous object; relational adapters bind via Dapper, while Direct uses ADO.NET. Both paths are parameterized to avoid injection.
- Optional capability: providers opt-in; unsupported instructions throw NotSupportedException.

## Quick examples

- With an IDataService (recommended in tests and libraries):
  - NonQuery (affected rows):
    - `var rows = await data.Execute<Todo,int>(InstructionSql.NonQuery("INSERT INTO Todo(Id,Title) VALUES(@id,@t)", new { id = "1", t = "x" }));`
  - Scalar:
    - `var count = await data.Execute<Todo,long>(InstructionSql.Scalar("SELECT COUNT(*) FROM Todo"));`
  - Query (rows):
    - `var rows = await data.Execute<Todo, System.Collections.Generic.IReadOnlyList<System.Collections.Generic.Dictionary<string, object?}}>(InstructionSql.Query("SELECT Id, Json FROM Todo"));`

- Static facade using SoraApp.Current (app context):
  - `await Data<Todo>.Execute<int>("INSERT INTO Todo(Id,Title) VALUES(@id,@t)");`
  - `var count = await Data<Todo>.Execute<long>("SELECT COUNT(*) FROM Todo");`

- Minimal test-local sugar (optional):
  - You can define on your test entity:
    - `public static Task Execute(string sql, IDataService data, object? parameters = null, CancellationToken ct = default) => Data<Todo>.Execute(sql, data, parameters, ct);`
  - Then call:
    - `await Todo.Execute("INSERT INTO Todo(Id,Title) VALUES(@id,@t)", data, new { id = "1", t = "x" });`

## Defaults

- `Data<TEntity, TKey>.Execute<TResult>(string sql, ...)`:
  - If `TResult` is `int`, it defaults to NonQuery.
  - Otherwise, it defaults to Scalar.
- `Data<TEntity>.Execute(string sql, IDataService data, ...)` returns `int` (rows affected).

## Provider support

- Relational adapters (SqlServer, Postgres, Sqlite):
  - Supported: `relational.schema.*`, `relational.sql.nonquery`, `relational.sql.scalar`, `relational.sql.query`.
  - Return for `relational.sql.query`: `IReadOnlyList<Dictionary<string, object?>>` (neutral rows).
  - WHERE suffixes still supported via string query repos for convenience.

## Direct API routing

The Direct API (`data.Direct(name)`) can route through adapter instruction executors when a target entity is specified:

- `data.Direct("entity:Namespace.TypeName").Query("SELECT ... FROM TypeName WHERE ...")`
- No explicit `WithConnectionString` override should be set (otherwise ADO.NET path is used).
- Results are materialized as neutral rows or mapped to T via Newtonsoft.Json.

If an instruction isn’t supported by the repository/provider, a NotSupportedException is thrown.

## References

- ADR-0049: Direct Commands API (async-only, mapping, connection resolution) — `decisions/0049-direct-commands-api.md`
- ADR-0050: Instruction name constants and scoping — `decisions/0050-instruction-name-constants-and-scoping.md`
- ADR-0051: Direct routing via instruction executors — `decisions/0051-direct-routing-via-instruction-executors.md`
- ADR-0052: Constrain Dapper to relational adapters; Direct uses ADO.NET — `decisions/0052-relational-dapper-boundary-and-direct-ado.md`
