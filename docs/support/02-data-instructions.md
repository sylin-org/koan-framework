# Data Instructions and SQL Sugar

The Instruction API gives a safe escape hatch for provider-specific features.

- Instruction envelope: `Instruction` with Name, Payload, Parameters, Options.
- Optional capability: repositories may implement `IInstructionExecutor<TEntity>`.
- DataService shim: `data.Execute<TEntity,TResult>(Instruction)`.

## SQL Sugar
- `Data<TEntity, TKey>.Execute<TResult>(string sql, ...)`
  - TResult = int → defaults to NonQuery.
  - Otherwise → Scalar.
- `Data<TEntity>.Execute(string sql, IDataService data, ...)` returns rows affected.

Examples:
- `await Data<Todo>.Execute<int>("INSERT INTO Todo(Id, Title) VALUES(@id,@t)", data, new { id = "1", t = "x" });`
- `var count = await Data<Todo>.Execute<long>("SELECT COUNT(*) FROM Todo", data);`

Tests can add a local shim on the entity:
- `public static Task Execute(string sql, IDataService data, object? parameters = null, CancellationToken ct = default) => Data<Todo>.Execute(sql, data, parameters, ct);`
