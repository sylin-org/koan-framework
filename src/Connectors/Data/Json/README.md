# Sylin.Koan.Data.Connector.Json

JSON file-backed data provider for Koan: simple repository for demos, tests, and local development.

- Target framework: net10.0
- License: Apache-2.0

## Capabilities

- Local file storage with simple filtering and paging semantics
- Automatic creation of the configured data directory on first use
- Great for seed data and smoke tests

## Install

```powershell
dotnet add package Sylin.Koan.Data.Connector.Json
```

## Usage

- Use `FirstPage/Page` for UI-like reads; avoid All on large files.
- Prefer first-class statics on your models where available.

## Readiness

Referencing the connector makes JSON available; it does not make JSON an application dependency.
The health contributor becomes critical only when JSON is default-elected, explicitly configured for
a source, or observed in entity use. An inactive connector reports `Unknown` and does not touch disk.

For active sources, readiness creates the directory using the same contract as the repository and
verifies that it can create and remove a probe file. A failure reports the selected connector as
unhealthy instead of silently falling back.

## References

- Data patterns: `~/guides/data/all-query-streaming-and-pager.md`

