# Sylin.Koan.Storage

Entity-first object storage for Koan. The runtime resolves logical profiles to referenced providers, applies active
segmentation to physical keys, validates routing, orchestrates transfers, and exposes one readable object lifecycle.

## Install

```powershell
dotnet add package Sylin.Koan.Storage
dotnet add package Sylin.Koan.Storage.Connector.Local
```

`AddKoan()` discovers both packages. Configure at least one profile and the selected provider; no Storage-specific
service-registration call is required.

```json
{
  "Koan": {
    "Storage": {
      "DefaultProfile": "main",
      "Profiles": {
        "main": { "Provider": "local", "Container": "files" }
      },
      "Providers": {
        "Local": { "BasePath": "./data/storage" }
      }
    }
  }
}
```

## Smallest meaningful use

```csharp
using Koan.Storage;
using Koan.Storage.Model;

[StorageBinding("main")]
public sealed class Document : StorageEntity<Document> { }

var document = await Document.CreateTextFile("terms.txt", "Current terms");
var text = await Document.Get(document.Key).ReadAllText();
```

Add another bound Entity type to make tiering read as business intent:

```csharp
await document.CopyTo<ArchiveDocument>();
await document.MoveTo<ArchiveDocument>();
```

The same primitives remain available through `IStorageService` for multi-model or infrastructure workflows.

## Guarantees and boundaries

- Profile resolution is exact when explicitly named. An unknown profile/provider or missing container fails with a
  corrective startup/operation error; Storage does not silently choose an unrelated backend.
- With one profile, `SingleProfileOnly` permits omission. Multiple profiles require `DefaultProfile` or explicit
  selection.
- Active segmentation is composed once by Storage and applied at the service chokepoint, including type-erased calls.
- Cross-provider copy streams through the process; same-provider server-side copy is used only when advertised.
- Presign, listing, stat quality, range behavior, consistency, atomicity, and durability are provider guarantees.
- Entity metadata and object bytes are separate resources; Storage does not claim a distributed transaction between
  them. Whole-object helpers buffer by definition—use stream/range operations for large payloads.

See [TECHNICAL.md](./TECHNICAL.md) for routing, identity, replication, and failure semantics.
