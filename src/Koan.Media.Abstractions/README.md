# Sylin.Koan.Media.Abstractions

Inert contracts for Koan media recipes and pipelines: immutable transformation policy, media metadata, registry and
pipeline interfaces, step vocabulary, output shapes, and the storage-backed `IMediaObject` contract.

## Install

```powershell
dotnet add package Sylin.Koan.Media.Abstractions
```

Applications normally reference `Sylin.Koan.Media.Core` or `Sylin.Koan.Media.Web` and receive this package
transitively. Reference it directly for a recipe library, source/projection contract, or alternate pipeline engine
that must not activate Koan's media runtime.

## Smallest meaningful use

```csharp
using Koan.Media.Abstractions.Recipes;

var card = MediaRecipe.New()
    .AutoOrient()
    .Resize(width: 320)
    .EncodeAs("jpeg", Quality.Web)
    .Build();
```

The result is immutable transformation intent. A functional pipeline such as `Sylin.Koan.Media.Core` performs the
decode and encode. `[MediaRecipe]` marks static recipe factories for a runtime registry to discover.

## Guarantees and boundaries

- Referencing this package registers no module, recipe registry, image engine, storage service, controller, or route.
- Recipe construction and fingerprinting are deterministic; execution and encoder availability belong to a runtime.
- `IMediaPipeline` expresses lazy processing and streaming terminals but does not promise bounded decoded pixel state.
- `IMediaObject` describes media lineage over Storage contracts; Entity upload/dedup/read behavior belongs to
  `Sylin.Koan.Media.Core` as `Koan.Media.MediaEntity<TEntity>`.
- Storage placement, tenancy/access gates, derivative persistence, request limits, and HTTP negotiation belong to
  their owning functional packages.

See [TECHNICAL.md](./TECHNICAL.md) for recipe and pipeline invariants.
