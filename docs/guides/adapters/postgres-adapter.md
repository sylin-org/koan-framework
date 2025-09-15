# PostgreSQL Adapter

This guide covers Koan's PostgreSQL data adapter. It mirrors relational semantics used by the SQL Server and SQLite adapters with JSONB-based storage, projection pushdown, and governance controls.

## Capabilities

- Query: LINQ and string-based queries
- Pushdowns: WHERE, COUNT, server-side paging (LIMIT/OFFSET)
- Writes: upsert (ON CONFLICT), bulk delete, atomic batch
- Governance: DDL policy (NoDdl | Validate | AutoCreate), matching mode (Relaxed | Strict)
- Observability: ActivitySource for tracing (db.system=postgresql)

## Package & registration

Namespace: `Koan.Data.Postgres`

```csharp
services.AddKoanCore();
services.AddKoanDataCore();
services.AddPostgresAdapter();
```

## Configuration

Keys (first-win):

- Koan:Data:Postgres:ConnectionString
- Koan:Data:Sources:Default:postgres:ConnectionString
- ConnectionStrings:Postgres
- ConnectionStrings:Default

Optional:

- Koan:Data:Postgres:DefaultPageSize (default 50)
- Koan:Data:Postgres:MaxPageSize (default 200)
- Koan:Data:Postgres:DdlPolicy (NoDdl | Validate | AutoCreate; default AutoCreate)
- Koan:Data:Postgres:SchemaMatchingMode (Relaxed | Strict; default Relaxed)
- Koan:Data:Postgres:SearchPath (default public)
- Koan:AllowMagicInProduction (bool)

## Usage

```csharp
var repo = provider.GetRequiredService<IDataService>()
                   .GetRepository<Person, string>("postgres");

await repo.UpsertAsync(new Person("1") { Name = "Ada", Age = 37 });
var lrepo = (ILinqQueryRepository<Person, string>)repo;
var adults = await lrepo.QueryAsync(p => p.Age >= 18);
var total = await lrepo.CountAsync(p => p.Age >= 18);
```

String query WHERE suffix or full SELECT supported. Property tokens map to projected columns or JSONB extraction.

## Instructions

- relational.schema.validate => diagnostics
- relational.schema.ensureCreated | data.ensureCreated => idempotent ensure
- relational.schema.clear | data.clear => delete all rows
- relational.sql.scalar/nonquery/query => parameterized SQL; `query` returns neutral rows (`IReadOnlyList<Dictionary<string, object?>>`)

See also:

- ADR-0050 (instruction name constants) — `decisions/DATA-0050-instruction-name-constants-and-scoping.md`
- ADR-0051 (Direct routing via instruction executors) — `decisions/DATA-0051-direct-routing-via-instruction-executors.md`
- ADR-0052 (Dapper boundary; Direct uses ADO.NET) — `decisions/DATA-0052-relational-dapper-boundary-and-direct-ado.md`

## Testing

The test project uses Testcontainers to start PostgreSQL if env vars are unset.
Env keys: `Koan_POSTGRES__CONNECTION_STRING` or `ConnectionStrings__Postgres`
Default image: `postgres:16-alpine`, port 54329.

Docker detection and endpoints:

- The tests now probe the Docker daemon directly and pass a stable endpoint to Testcontainers.
- Honors `DOCKER_HOST` when set; otherwise falls back to platform defaults:
    - Windows: `npipe://./pipe/docker_engine` (falls back to `http://localhost:2375` if needed)
    - Linux/macOS: `unix:///var/run/docker.sock` (falls back to `http://localhost:2375`)
- Sets `TESTCONTAINERS_RYUK_DISABLED=true` to avoid occasional stream hijack issues on Windows.

If Docker is not reachable and no connection string is provided, tests no-op to keep CI green. Provide a Postgres connection string to force execution without Docker:

- `Koan_POSTGRES__CONNECTION_STRING` or `ConnectionStrings__Postgres`

Example:

```
# Windows PowerShell
$env:Koan_POSTGRES__CONNECTION_STRING = "Host=localhost;Port=5432;Database=Koan;Username=postgres;Password=postgres"

# Optional: force a Docker endpoint if needed
$env:DOCKER_HOST = "npipe://./pipe/docker_engine"
```

## Notes

- Complex properties are stored in `Json` JSONB column; projections use expression indexes on extraction `(Json #>> '{Prop}')`.
- Paging guardrails enforced with DefaultPageSize/MaxPageSize; fallbacks remain bounded.
