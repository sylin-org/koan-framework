# Sylin.Koan.Storage.Abstractions

Inert provider, service, object, capability, and binding contracts for Koan Storage. Reference this package to build a
provider or a module that speaks Storage without activating routing, IO, or Entity behavior.

## Install

```powershell
dotnet add package Sylin.Koan.Storage.Abstractions
```

Applications normally install a functional provider package instead; it brings the Storage runtime transitively.

## Smallest meaningful use

```csharp
using Koan.Core.Capabilities;
using Koan.Storage.Abstractions;
using Koan.Storage.Abstractions.Capabilities;

public sealed class ArchiveProvider : IStorageProvider
{
    public string Name => "archive";
    public StorageProviderPlacement Placement => StorageProviderPlacement.Remote;

    public void Describe(ICapabilities caps)
        => caps.Add(StorageCaps.SequentialRead);

    // Implement Write, OpenRead, OpenReadRange, Delete, and Exists.
}
```

Add `IStatOperations`, `IListOperations`, `IServerSideCopy`, or `IPresignOperations` only when the provider can perform
that operation, and declare the matching `StorageCaps` token. Storage rejects inconsistent claims during composition.
Use `[ProviderPriority]` only when this provider should win automatic election over another provider in the same
placement.

## Guarantees and boundaries

- This package contains no `KoanModule`; referencing it alone registers and activates nothing.
- `StorageProviderPlacement` describes topology, while `StorageCaps` describes backend guarantees. Provider names do
  not imply either.
- `IStorageProvider` is the backend SPI. `IStorageService` is the functional runtime boundary consumed by modules.
- Result objects report observed metadata; they do not imply durability, consistency, atomicity, public access, or
  distributed transactions.
- Profile policy, provider election, replication, segmentation, Entity helpers, and startup reporting belong to
  `Sylin.Koan.Storage`.

See [TECHNICAL.md](./TECHNICAL.md) for contract ownership and provider-author rules.
