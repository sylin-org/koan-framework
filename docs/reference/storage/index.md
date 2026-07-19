---
type: REFERENCE
domain: storage
title: "Storage reference"
audience: [developers, architects, ai-agents]
last_updated: 2026-07-17
framework_version: v0.20.0
status: current
validation:
  date_last_tested: 2026-07-17
  status: verified
  scope: Storage runtime, contracts, Local/S3 providers, routing tests, and tenant-isolation proof
---

# Storage reference

Use Storage when an Entity owns or describes bytes that must live behind a logical profile rather than a vendor API.
The common path is model-first; `IStorageService` remains the deliberate infrastructure escape hatch.

## Start with local storage

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

```csharp
using Koan.Storage;
using Koan.Storage.Model;

[StorageBinding("main")]
public sealed class Document : StorageEntity<Document> { }

var document = await Document.CreateTextFile("readme.txt", "Hello");
var text = await document.ReadAllText();
```

The application still boots through its existing `builder.Services.AddKoan()` call. The connector reference supplies
both the provider and Storage runtime; do not add a second registration path.

## Entity operations

`StorageEntity<TEntity>` supplies the common lifecycle:

```csharp
var binary = await Document.Create("report.pdf", bytes, "application/pdf");
var streamed = await Document.Onboard("video.mp4", upload, "video/mp4");

await using var full = await streamed.OpenRead();
var (range, length) = await streamed.OpenReadRange(0, 1023);
var stat = await streamed.Head();
var exists = await Document.Head("video.mp4") is not null;
await streamed.Delete();
```

Persist the returned Entity with `.Save()` when the application needs a durable metadata row. Blob bytes and Entity
metadata are separate resources; Koan does not claim one transaction across both.

Tiering remains business-readable when the target is another bound model:

```csharp
[StorageBinding("archive", "documents")]
public sealed class ArchivedDocument : StorageEntity<ArchivedDocument> { }

var copy = await document.CopyTo<ArchivedDocument>();
var moved = await document.MoveTo<ArchivedDocument>();
```

## Configure profiles

Each profile accepts:

| Setting | Meaning |
|---|---|
| `Container` | Required logical backend container/bucket. |
| `Provider` | Optional exact provider identity. Exact means required. |
| `Mode` | Optional `Local`, `Remote`, or `Replicated` topology requirement. |
| `LocalCache` | Cache quota/watermark settings for a replicated route. |

At the Storage root, `DefaultProfile` selects the implicit route when several profiles exist. A sole profile becomes
the default automatically. With several profiles and no default, explicit bindings and service calls work; an
unqualified operation fails with the available correction.

Provider settings live under `Koan:Storage:Providers:<Provider>` rather than inside every profile. One provider can
serve several logical profiles/containers.

## Understand provider election

Storage compiles one route per profile during host composition:

1. An exact `Provider` pin is selected or rejected.
2. Otherwise `Mode` filters candidates by their declared `StorageProviderPlacement`.
3. Higher `[ProviderPriority]` wins within a placement; stable identity breaks ties.
4. `Mode: Replicated` requires both Local and Remote.
5. With no mode, one placement is used directly; Local + Remote composes the replicated provider.

This decision does not run again for every blob operation. Startup facts and the lockfile project the resulting
election and unified `StorageCaps` tokens.

## Use S3

```powershell
dotnet add package Sylin.Koan.Storage.Connector.S3
```

```json
{
  "Koan": {
    "Storage": {
      "Profiles": {
        "archive": { "Provider": "s3", "Container": "documents" }
      },
      "Providers": {
        "S3": {
          "Endpoint": "http://localhost:9000",
          "Region": "us-east-1"
        }
      }
    }
  }
}
```

If the functional Zen Garden module is active and bound, S3 can discover a storage replica when `Endpoint` is absent.
The contracts dependency alone is inert. Presigning specifically requires a Moss endpoint; it is not generic
client-side S3 signing. Supply credentials with `Koan__Storage__Providers__S3__AccessKey` and
`Koan__Storage__Providers__S3__SecretKey` or another .NET configuration provider.

## Use the service boundary

Inject `IStorageService` for multi-model workflows:

```csharp
public sealed class ExportWriter(IStorageService storage)
{
    public Task<StorageObject> Write(string key, Stream content, CancellationToken ct)
        => storage.Put("exports", "exports", key, content, "application/zip", ct);
}
```

The service exposes put/read/range/delete/exists/stat, cross-profile transfer, presign, and listing. Optional operations
require the provider's compiled capability; unsupported intent throws the standard capability correction.

## Operational boundaries

- Segmentation is applied at `IStorageService`, covering Entity, Media, raw service, listing, presign, and transfer
  paths. Tenant values never appear in general composition facts.
- Cross-provider copy streams through the process. Same-provider copy uses the backend only when declared.
- Whole-object text/byte helpers buffer. Local range reads buffer the requested range; current S3 full/range reads
  materialize their response in memory.
- Local is single-node filesystem storage. S3 inherits service-side durability, consistency, encryption, lifecycle,
  and availability behavior.
- Replication is asynchronous local-cache/durable-remote behavior, not a distributed transaction. Required replication
  refuses to start without both placements.
- Configuration is a host-composition input; live profile/provider reload is not a supported implicit contract.

For provider-author contracts, reference `Sylin.Koan.Storage.Abstractions` and implement `IStorageProvider`, placement,
`Describe(ICapabilities)`, and only the optional operation interfaces the provider can honor.
