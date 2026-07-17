# Sylin.Koan.Data.Connector.Json

Zero-configuration JSON persistence for small Koan applications, tests, and local development.

- Target framework: net10.0
- License: Apache-2.0

## What it adds

- local file persistence with simple filtering and paging semantics
- automatic data-directory creation on first elected use
- an inspectable storage floor for the `Sylin.Koan` foundation bundle
- explicit rejection of provider-bounded streams it cannot truthfully execute

## Install

```powershell
dotnet add package Sylin.Koan.Data.Connector.Json
```

## Meaningful result

Reference the package and keep the normal `AddKoan()` bootstrap. When JSON is elected, the first Entity write creates
its data directory and file; application code remains provider-neutral:

```csharp
var todo = new Todo { Title = "Prove the first result" };
await todo.Save(ct);

var saved = await Todo.Get(todo.Id, ct);
```

Use `FirstPage`/`Page` for UI-like reads and reserve `All`/`Query` for deliberately small files.

## Streaming boundary

`AllStream` and `QueryStream` reject correctively with `QueryStreamRejectedException` before yielding because the
current JSON query path loads the file-backed source before slicing. Koan does not hide that work behind a
streaming-shaped API.

Numbered pages limit the result returned to application code; they do not make file loading provider-bounded.

## Readiness and inspection

Package presence makes JSON available; it does not make JSON an application dependency. The health contributor becomes
critical only when JSON is default-elected, explicitly configured for a source, or observed in Entity use. An inactive
connector reports `Unknown` and does not touch disk.

For active sources, readiness creates the directory through the same contract as the repository and verifies that it can
create and remove a probe file. Failure reports the selected connector as unhealthy instead of silently falling back.

## Boundaries and failures

- JSON is a local, single-process persistence floor, not a multi-process database. Concurrent writers in different
  processes are unsupported and can overwrite each other's snapshots.
- Each write persists the aggregate file. This favors an inspectable first result over high write throughput or large
  datasets; there are no transactions, indexes, server-side queries, or cross-process coordination.
- Writes replace the previous file only after a complete temporary snapshot is written. Invalid existing JSON fails
  with a corrective `InvalidDataException`; Koan never interprets corrupt storage as an empty database.
- Package presence makes the provider available. Election, explicit source selection, or actual Entity use activates
  readiness and storage access.

## References

- [Technical reference](https://github.com/sylin-org/Koan-framework/blob/main/src/Connectors/Data/Json/TECHNICAL.md)
- [DATA-0107 — provider-bounded Entity streams](https://github.com/sylin-org/Koan-framework/blob/main/docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](https://github.com/sylin-org/Koan-framework/blob/main/docs/guides/data/entity-access-and-streaming.md)
