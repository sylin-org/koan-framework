# Sylin.Koan.Storage.Abstractions

Provider-neutral contracts for Koan object storage. This package defines storage services, providers, object metadata,
optional backend capabilities, and model binding without registering the Storage runtime or selecting a backend.

## Install

```powershell
dotnet add package Sylin.Koan.Storage.Abstractions
```

Applications normally reference `Sylin.Koan.Storage` and a provider package. Reference Abstractions directly when
authoring a storage provider or a module that must describe storage behavior without activating it.

## Smallest meaningful use

A provider implements the required byte lifecycle and advertises only guarantees it actually supplies:

```csharp
using Koan.Storage.Abstractions;

public sealed class ArchiveProvider : IStorageProvider
{
    public string Name => "archive";
    public StorageProviderCapabilities Capabilities => new(
        SupportsSequentialRead: true,
        SupportsSeek: false,
        SupportsPresignedRead: false,
        SupportsServerSideCopy: false);

    // Implement Write, OpenRead, OpenReadRange, Delete, and Exists.
}
```

Implement `IStatOperations`, `IListOperations`, `IServerSideCopy`, or `IPresignOperations` only when the backend can
honor that optional operation. `StorageBindingAttribute` carries an Entity model's logical profile/container intent.

## Guarantees and boundaries

- Referencing this package alone performs no discovery, dependency injection, routing, IO, or provider election.
- `IStorageProvider` is the backend SPI; `IStorageService` is the runtime orchestration boundary consumed by modules.
- `StorageObject` and `ObjectStat` report observed metadata; they do not imply durability, atomicity, consistency,
  checksum verification, public access, or presign support.
- Range semantics, listing consistency, server-side copy, and presigned URLs remain explicit provider capabilities.
- Routing, fallback policy, segmentation, replication, validation, and Entity operations belong to
  `Sylin.Koan.Storage`.

See [TECHNICAL.md](./TECHNICAL.md) for the complete contract ownership map.
