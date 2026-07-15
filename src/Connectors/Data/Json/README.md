# Sylin.Koan.Data.Connector.Json

JSON file-backed data provider for Koan: simple repository for demos, tests, and local development.

- Target framework: net10.0
- License: Apache-2.0

## Capabilities

- Local file storage with simple filtering and paging semantics
- Automatic creation of the configured data directory on first use
- Great for seed data and smoke tests
- Explicitly does not advertise `DataCaps.Query.ProviderBoundedPaging`

## Install

```powershell
dotnet add package Sylin.Koan.Data.Connector.Json
```

## Usage

- Use `FirstPage/Page` for UI-like reads; avoid All on large files.
- Prefer first-class statics on your models where available.

## Streaming boundary

`AllStream` and `QueryStream` reject correctively with `QueryStreamRejectedException` before yielding
because the current JSON query path loads the file-backed source before slicing. Koan does not hide
that work behind a streaming-shaped API.

Use `All`/`Query` only for deliberately small files, or `FirstPage`/`Page` when a bounded result returned
to application code is sufficient. Numbered pages do not make file loading provider-bounded.

## Readiness

Referencing the connector makes JSON available; it does not make JSON an application dependency.
The health contributor becomes critical only when JSON is default-elected, explicitly configured for
a source, or observed in entity use. An inactive connector reports `Unknown` and does not touch disk.

For active sources, readiness creates the directory using the same contract as the repository and
verifies that it can create and remove a probe file. A failure reports the selected connector as
unhealthy instead of silently falling back.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

