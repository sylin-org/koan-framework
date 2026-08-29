# Sylin.Koan.Data.Connector.DuckDb.Native

Native DuckDB engine (per-RID libduckdb) for Sylin.Koan.Data.Connector.DuckDb. Reference alongside the connector; the connector alone carries only the managed ADO.NET bindings.

## What it adds

Referencing the package makes this capability available through the ordinary `AddKoan()` composition; the sections above describe the surface it projects.

## Install

```powershell
dotnet add package Sylin.Koan.Data.Connector.DuckDb.Native
```

## Limits

Referencing the package alone does not elect it for every workload; composition and configuration decide participation, and unsupported paths reject with a corrective explanation. See the owning capability documentation for provider limits.
