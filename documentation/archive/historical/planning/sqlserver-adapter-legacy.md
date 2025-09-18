# SQL Server Adapter

This guide covers how to use Koan's SQL Server data adapter. It mirrors the relational semantics used by the SQLite adapter, with JSON-based storage, projection pushdown, and governance controls.

## Capabilities

- Query: LINQ and string-based queries
- Projection by default: computed columns added for projected properties
- Pushdowns: WHERE, COUNT, and server-side paging (OFFSET/FETCH)
- Writes: bulk upsert, bulk delete, and atomic batch
- Governance: DDL policy (NoDdl | Validate | AutoCreate), matching mode (Relaxed | Strict)
- Observability: adapter-specific ActivitySource for tracing

## Package & registration

The adapter lives in `src/Koan.Data.SqlServer`. To enable it in DI:

```csharp
services.AddKoanCore();
services.AddKoanDataCore();
services.AddSqlServerAdapter();
```

## Configuration

Use any of the keys below (first-win) to configure the connection and paging:

- Koan:Data:SqlServer:ConnectionString
- Koan:Data:Sources:Default:sqlserver:ConnectionString
- ConnectionStrings:SqlServer
- ConnectionStrings:Default

Optional settings:

- Koan:Data:SqlServer:DefaultPageSize (default 50)
- Koan:Data:SqlServer:MaxPageSize (default 200)
- Koan:Data:SqlServer:DdlPolicy (NoDdl | Validate | AutoCreate; default AutoCreate)
- Koan:Data:SqlServer:SchemaMatchingMode (Relaxed | Strict; default Relaxed)
- Koan:AllowMagicInProduction (bool; allows DDL when production)

Example (appsettings.json):

```json
{
  "Koan": {
    "Data": {
      "SqlServer": {
        "ConnectionString": "Server=localhost,1433;User Id=sa;Password=yourStrong(!)Password;TrustServerCertificate=True;Encrypt=False",
        "DefaultPageSize": 50,
        "MaxPageSize": 200,
        "DdlPolicy": "AutoCreate",
        "SchemaMatchingMode": "Relaxed"
      }
    }
  }
}
```

## Naming

- Default naming style is taken from SqlServerOptions (FullNamespace by default)
- You can override naming globally with `INamingDefaultsProvider` or per-type via attributes

## Usage

Resolve a repository and perform CRUD queries:

```csharp
var repo = provider.GetRequiredService<IDataService>()
                   .GetRepository<Person, string>("sqlserver");

await repo.UpsertAsync(new Person { Id = "1", Name = "Ada", Age = 37 });
var adults = await ((ILinqQueryRepository<Person, string>)repo)
    .QueryAsync(p => p.Age >= 18);
var total = await ((ILinqQueryRepository<Person, string>)repo)
    .CountAsync(p => p.Age >= 18);
```

String queries support either a full SELECT (with entity token replacement) or a WHERE suffix:

```csharp
var q1 = await ((IStringQueryRepository<Person, string>)repo)
    .QueryAsync("SELECT [Id],[Json] FROM Person WHERE [Age] >= @min ORDER BY [Id]", new { min = 21 });

var q2 = await ((IStringQueryRepository<Person, string>)repo)
    .QueryAsync("[Age] >= @min", new { min = 21 });
```

## Instructions

Use the instruction executor for schema and raw SQL helpers:

- relational.schema.validate => Dictionary report
- relational.schema.ensureCreated => ensure table/columns exist
- data.clear => delete all rows in the table
- relational.sql.scalar => execute scalar SQL (full SELECT supported)
- relational.sql.nonquery => execute non-query SQL
- relational.sql.query => execute a query and return neutral rows (`IReadOnlyList<Dictionary<string, object?>>`)

```csharp
var exec = (IInstructionExecutor<Person>)repo;
var report = await exec.ExecuteAsync<Dictionary<string, object?>>(Instruction.Create("relational.schema.validate"));
var count = await exec.ExecuteAsync<int>(Instruction.Create("relational.sql.scalar", new { Sql = "SELECT COUNT(1) FROM Person" }));
```

See also:

- ADR-0050 (instruction name constants) — `decisions/DATA-0050-instruction-name-constants-and-scoping.md`
- ADR-0051 (Direct routing via instruction executors) — `decisions/DATA-0051-direct-routing-via-instruction-executors.md`
- ADR-0052 (Dapper boundary; Direct uses ADO.NET) — `decisions/DATA-0052-relational-dapper-boundary-and-direct-ado.md`

## Testing

The test project `tests/Koan.Data.SqlServer.Tests` uses Testcontainers to start SQL Server automatically if a connection string is not provided by environment variables.

- Env keys checked: `Koan_SQLSERVER__CONNECTION_STRING` or `ConnectionStrings__SqlServer`
- Fallback container image: `mcr.microsoft.com/mssql/server:2022-latest` with password `yourStrong(!)Password` and host port 14333

Run tests:

```pwsh
# Option A: Let tests start a container
dotnet test tests/Koan.Data.SqlServer.Tests/Koan.Data.SqlServer.Tests.csproj -c Debug

# Option B: Use an existing SQL Server
$env:Koan_SQLSERVER__CONNECTION_STRING = "Server=localhost,1433;User Id=sa;Password=yourStrong(!)Password;TrustServerCertificate=True;Encrypt=False"
dotnet test tests/Koan.Data.SqlServer.Tests/Koan.Data.SqlServer.Tests.csproj -c Debug
```

## Notes

- Projection columns are added as computed columns via JSON_VALUE and indexed when `[Index]` markers are present.
- DDL is disabled when `[ReadOnly]` is applied on the entity type or when policy forbids it; in production, DDL requires the magic flag.
