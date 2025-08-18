# Executing Provider Instructions (escape hatch)

Sora offers a small, optional Instruction API so you can access provider-specific features (like raw SQL) safely when you need to.

- Instruction name: namespaced strings, e.g. `relational.sql.nonquery`, `relational.sql.scalar`, `relational.schema.ensureCreated`.
- Parameterization: pass an anonymous object; adapters bind parameters (e.g., Dapper) to avoid injection.
- Optional capability: providers opt-in; unsupported instructions throw NotSupportedException.

## Quick examples

- With an IDataService (recommended in tests and libraries):
  - NonQuery (affected rows):
    - `var rows = await data.Execute<Todo,int>(InstructionSql.NonQuery("INSERT INTO Todo(Id,Title) VALUES(@id,@t)", new { id = "1", t = "x" }));`
  - Scalar:
    - `var count = await data.Execute<Todo,long>(InstructionSql.Scalar("SELECT COUNT(*) FROM Todo"));`

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

- SQLite (Sora.Data.Sqlite):
  - Supported: `relational.schema.ensureCreated`, `relational.sql.nonquery`, `relational.sql.scalar`.
  - Optional: `IStringQueryRepository` for raw SELECT queries or WHERE clause suffixes; parameters are safely bound via Dapper.
  - Future: `relational.sql.query` returning rows.

If an instruction isn’t supported by the repository/provider, a NotSupportedException is thrown.
