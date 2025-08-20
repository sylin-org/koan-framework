# SQLite adapter logging and schema governance

This page documents developer-friendly logging and governance behavior in the SQLite data adapter.

## Structured logging (dev-friendly)

The adapter emits concise, structured logs using precompiled LoggerMessage delegates. Enable your preferred logger (Console, Serilog, etc.) and set level to Debug for rich breadcrumbs.

Event IDs and messages:
- 1000 sqlite.ensure_table.start — Ensure table start: table={Table} policy={Policy} ddlAllowed={DdlAllowed} readOnly={ReadOnly}
- 1001 sqlite.ensure_table.created — Table created (or exists): table={Table}
- 1002 sqlite.ensure_table.skip — Ensure table skipped: table={Table} reason={Reason}
- 1003 sqlite.ensure_table.add_column — Added generated column: table={Table} column={Column} path={JsonPath}
- 1004 sqlite.ensure_table.add_index — Ensured index: table={Table} column={Column}
- 1005 sqlite.ensure_table.end — Ensure table end: table={Table}
- 1100 sqlite.validate_schema.start — Validate schema start: table={Table}
- 1101 sqlite.validate_schema.result — Validate schema result: table={Table} state={State}
- 1102 sqlite.validate_schema.result_unhealthy — Validate schema result: table={Table} state={State}
- 1200 sqlite.retry_missing_table — Retry after ensuring missing table: table={Table} code={Code}

Notes:
- Healthy results log at Information; non-Healthy logs at Warning. A second Debug line includes detailed fields for tests/diagnostics.
- No SQL or PII is logged.

## Tracing (OpenTelemetry)

The adapter also adds Activity events under ActivitySource "Sora.Data.Sqlite" at similar points, so you can capture them via OpenTelemetry without enabling logs.

## DDL governance (policy + environment)

DDL is allowed only when all are true:
- Policy = AutoCreate, and
- Entity is not [ReadOnly], and
- Not running in Production, unless a magic flag is enabled (Sora:AllowMagicInProduction=true) or adapter option AllowProductionDdl=true.

The adapter reads the magic flag from IConfiguration and SoraEnv to support tests and dev servers reliably.

## Quick enable for dev server

1) Add Console logging at Debug in your host:

```csharp
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Debug);
```

2) To allow DDL while running with ASPNETCORE_ENVIRONMENT=Production in a dev shell, set:

```powershell
$env:Sora__AllowMagicInProduction = "true"
```

Or set in appsettings:

```json
{
  "Sora": { "AllowMagicInProduction": true }
}
```

3) (Optional) Force Strict schema matching via config for stronger drift signals:

```json
{
  "Sora": {
    "Data": {
      "Sqlite": { "SchemaMatchingMode": "Strict" }
    }
  }
}
```

## Observability tips

- To troubleshoot table creation: filter by EventId >= 1000 and Source "Sora.Data.Sqlite".
- To assert on drift in tests, use the instruction `relational.schema.validate` which returns a dictionary with Provider, Table, TableExists, MissingColumns, Policy, DdlAllowed, MatchingMode, and State.

