# Sylin.Koan.Cache.Abstractions

Contracts for Koan Cache policies, entry builders, values, stores, and capability negotiation. Reference this
package when implementing a Cache provider or a module that describes cache intent without activating the Cache
runtime.

## Install

```powershell
dotnet add package Sylin.Koan.Cache.Abstractions
```

Applications normally install `Sylin.Koan.Cache` or a functional adapter instead.

## Meaningful result

```csharp
public sealed class AcmeCacheStore : ICacheStore
{
    public string Name => "acme";
    public CacheStorePlacement Placement => CacheStorePlacement.Remote;

    public void Describe(ICapabilities caps)
        => caps.Add(CacheCaps.Tags)
            .Add(CacheCaps.BinaryPayload);

    // Implement Fetch, Set, Remove, Touch, Exists, and EnumerateByTag.
}
```

Register the provider through standard .NET DI as `IEnumerable<ICacheStore>`:

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Singleton<ICacheStore, AcmeCacheStore>());
```

Use `[ProviderPriority]` only when automatic election should prefer this store over another candidate in the same
placement. Cache compiles election once; registration order never decides.

## Guarantees and boundaries

- This package contains no `KoanModule`; referencing it alone performs no registration or I/O.
- `CacheStorePlacement` describes topology. `CacheCaps` describes provider guarantees. A provider name implies
  neither.
- Declare only capabilities the implementation actually honors. Runtime operations fail loudly when a selected
  provider cannot satisfy the requested semantic.
- `AllowStaleFor` means bounded stale serving; it does not imply background revalidation.
- Policy tier is application intent. Host-wide provider election belongs to `Sylin.Koan.Cache`.

See [TECHNICAL.md](TECHNICAL.md) for provider-author rules.
