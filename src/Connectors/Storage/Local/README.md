# Sylin.Koan.Storage.Connector.Local

Local-filesystem provider for Koan Storage. Referencing it adds one Local placement candidate; `AddKoan()` activates
the provider and the Storage runtime without a provider-specific registration call.

## Install

```powershell
dotnet add package Sylin.Koan.Storage.Connector.Local
```

Configure a profile and a writable base directory:

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

With one profile and one provider, the profile and Local provider are inferred. Add `Provider: "local"` when an exact
pin communicates useful intent.

## Smallest meaningful use

```csharp
using Koan.Storage;
using Koan.Storage.Model;

[StorageBinding("main")]
public sealed class Document : StorageEntity<Document> { }

var document = await Document.CreateTextFile("terms.txt", "Current terms");
var text = await document.ReadAllText();
```

## Guarantees and boundaries

- Keys are normalized below `BasePath/container`; traversal segments, rooted escapes, and invalid filename characters
  are rejected.
- Writes use a temporary file followed by same-filesystem replacement. Cross-platform crash durability and atomic
  replacement semantics are filesystem guarantees, not stronger Koan guarantees.
- Full reads return a seekable file stream. Range reads currently materialize the requested range in memory.
- Stat, listing, and same-provider copy are supported. Presigned URLs are not.
- Listing is a weakly consistent filesystem snapshot; concurrent removals are skipped. Content type is not persisted
  in a sidecar and may be absent from stat/list results.
- This provider is single-node storage. Shared folders, network filesystems, multi-writer coordination, encryption,
  backup, and replication durability require the corresponding infrastructure or another provider.

See [TECHNICAL.md](./TECHNICAL.md) for layout, capability, and failure details.
