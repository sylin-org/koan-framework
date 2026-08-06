---
name: koan-storage
description: Entity-owned bytes through Koan Storage profiles, the supported Local provider, streaming, range reads, transfer, and explicit single-node limits
pillar: storage
card: docs/reference/storage/index.md
status: current
last_validated: 2026-07-22
---

# Koan Storage

## Trigger this skill when you see

- `StorageEntity<T>`, `[StorageBinding]`, `IStorageService`, `StorageObject`, or a named storage profile
- file upload/download, object storage, range reads, metadata, listing, or cross-profile transfer
- application code carrying filesystem paths or provider clients through business workflows

## Core principle

Name a logical profile, not a vendor. An Entity can own byte metadata while the bytes stream through
the selected Storage provider. The current supported 0.20 path is the Local connector; it is
single-node filesystem storage and does not imply shared storage, presigned URLs, replication,
encryption, backup, or remote durability.

## Smallest supported shape

```powershell
dotnet add package Sylin.Koan.Storage.Connector.Local
```

```json
{
  "Koan": {
    "Storage": {
      "Profiles": {
        "main": { "Container": "files" }
      },
      "Providers": {
        "Local": { "BasePath": ".koan/storage" }
      }
    }
  }
}
```

<!-- validate -->
```csharp
using Koan.Storage;
using Koan.Storage.Model;

[StorageBinding("main")]
public sealed class Document : StorageEntity<Document> { }

public static class StorageExample
{
    public static async Task ReadTerms()
    {
        var document = await Document.CreateTextFile("terms.txt", "Current terms");
        var text = await document.ReadAllText();
        await using var stream = await document.OpenRead();
        var stat = await document.Head();
    }
}
```

The connector's `KoanModule` joins the application's ordinary `AddKoan()` composition. Do not call a
provider registrar or build a second provider list.

## Use the right operation

| Need | Use |
|---|---|
| Stream an upload | `Document.Onboard(name, stream, contentType)` |
| Read the full object | `document.OpenRead()` |
| Read a bounded byte range | `document.OpenReadRange(from, to)` |
| Inspect without reading the body | `document.Head()` |
| Copy or move between bound models | `document.CopyTo<TTarget>()` / `MoveTo<TTarget>()` |
| Run an infrastructure workflow without an Entity | inject `IStorageService` |

Persist the returned Entity with `.Save()` when the application needs a durable metadata row. Object
bytes and Entity metadata are separate resources; Koan does not claim one transaction across both.

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| Filesystem paths threaded through business code | Bind a `StorageEntity<T>` to a logical profile. |
| `File.OpenRead` or provider-client construction in controllers | Use Entity storage verbs or inject `IStorageService` at the workflow boundary. |
| Buffering an upload before saving | Pass its stream to `Onboard`. |
| Reading a full object for its size | Use `Head`. |
| Assuming Local works across replicas | Keep the application single-node or supply application-owned shared storage after its provider is admitted. |
| Assuming a public URL exists | Local has no presign capability; expose bytes through an application-authorized HTTP boundary when needed. |

## Escape hatches and limits

- `IStorageService` addresses raw operations by profile, container, and key when no Entity owns the
  workflow.
- Optional operations are capability-gated. Unsupported intent fails with a corrective error.
- Whole-object text/byte helpers buffer; Local range reads buffer only the requested range.
- Local keys are normalized below the configured base path and container; traversal and rooted
  escapes are rejected.
- The S3 provider and general remote/replicated Storage path are shelved. Do not recommend them for a
  greenfield 0.20 application.

## See also

- [Storage capability](../../../docs/reference/storage/index.md)
- [Local provider](../../../src/Connectors/Storage/Local/README.md)
- [State and content](../../../docs/reference/state-content/index.md)
- [Product surface](../../../docs/reference/product-surface.md)
