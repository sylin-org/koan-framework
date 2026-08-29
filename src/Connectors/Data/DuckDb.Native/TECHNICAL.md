# Sylin.Koan.Data.Connector.DuckDb.Native — technical contract

Owner of the `Sylin.Koan.Data.Connector.DuckDb.Native` package. Source lives at `src/Connectors/Data/DuckDb.Native/Koan.Data.Connector.DuckDb.Native.csproj`; the assembly carries exactly one `KoanModule` and activates through `AddKoan()` with no manual registration.

## Responsibilities

Native DuckDB engine (per-RID libduckdb) for Sylin.Koan.Data.Connector.DuckDb. Reference alongside the connector; the connector alone carries only the managed ADO.NET bindings.

## Failure boundary

Unsupported requests reject before provider work with a named capability and a correction. Facts, health, and lock evidence project the composed decision; no second activation owner exists for this assembly.
