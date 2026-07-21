# Sylin.Koan.Storage

Entity-first object-storage runtime for Koan. It compiles logical profiles and referenced providers once, applies
active segmentation at the service chokepoint, and exposes one readable object lifecycle across local, remote, and
replicated storage.

## Install

Install a functional provider; it brings this runtime transitively:

```powershell
dotnet add package Sylin.Koan.Storage.Connector.Local
```

`AddKoan()` discovers both packages. Configure one profile and the provider's physical settings; there is no
Storage-specific registration call.

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

## Smallest meaningful use

```csharp
using Koan.Storage;
using Koan.Storage.Model;

[StorageBinding("main")]
public sealed class Document : StorageEntity<Document> { }

var document = await Document.CreateTextFile("terms.txt", "Current terms");
var text = await document.ReadAllText();
```

Add another bound Entity type when storage tiering is business intent:

```csharp
await document.CopyTo<ArchiveDocument>();
await document.MoveTo<ArchiveDocument>();
```

Use `IStorageService` directly for multi-model infrastructure workflows, explicit streams, ranges, listing, or
presigning.

## Guarantees and boundaries

- Exact provider pins win. Without a pin, Storage elects by declared Local/Remote placement, `[ProviderPriority]`,
  then stable identity. Provider names are never parsed for topology.
- A sole profile is the implicit default. With several profiles, configure `DefaultProfile` or select a profile in
  the binding/operation.
- `Mode: Local` and `Mode: Remote` require that placement. `Mode: Replicated` requires both and fails instead of
  weakening the guarantee. With no mode, one available placement is used; Local + Remote compose replication.
- Profiles, provider capabilities, elections, and replication composition compile once per host and feed startup
  facts. Runtime operations perform direct route lookup.
- Active segmentation is applied at `IStorageService`, including type-erased calls. Logical Entity keys do not leak
  their physical segmentation prefix.
- Same-provider copy is used only when declared; cross-provider transfer streams through the process. Source deletion
  happens only after the target write succeeds.
- Whole-object helpers buffer by definition. Provider-specific docs state whether returned streams are truly remote
  streams or materialized buffers.
- Entity metadata and bytes are separate resources; Storage does not claim one distributed transaction across them.

See [TECHNICAL.md](./TECHNICAL.md) for plan compilation, identity, capabilities, and resource semantics.
