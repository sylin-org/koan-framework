# Koan.Media.Abstractions

The contracts behind Koan's Entity-backed media language, immutable recipes, and lazy media pipeline.
Most applications reference `Sylin.Koan.Media.Web` or `Sylin.Koan.Media.Core` and receive this package
transitively.

## Application shape

```csharp
using Koan.Media.Abstractions.Model;
using Koan.Media.Abstractions.Recipes;

public sealed class Photo : MediaEntity<Photo> { }

public static class PhotoRecipes
{
    [MediaRecipe("card", Description = "320px JPEG card")]
    public static MediaRecipe Card() => MediaRecipe.New()
        .Resize(width: 320)
        .EncodeAs("jpeg", Quality.Web)
        .Build();
}
```

`Photo.Upload(...)` stores a caller-named object. `Photo.Store(...)` uses a SHA-256 storage key and
deduplicates identical bytes. `MediaRecipe` describes ordered transformations; it does not perform work
until a pipeline or HTTP request materializes it.

## Principal contracts

- `MediaEntity<TEntity>` — Data Entity plus Storage-backed bytes and lineage fields.
- `MediaRecipe`, `MediaRecipeBuilder`, `[MediaRecipe]` — immutable, fingerprinted transform policy.
- `IMediaPipeline` and `MediaOutput` — lazy processing plus bounded terminal metadata.
- `IMediaRecipeRegistry` — the application recipe catalog consumed by Core and Web.

## Current limits

- `MediaEntity.Store(Stream, ...)` buffers the complete stream to compute its content hash.
- Recipe declarations do not imply upload-time prewarming or background work.
- Storage placement, access, tenancy, and HTTP serving belong to their owning Koan modules.

See the [Media reference](../../docs/reference/media/index.md) and
[technical companion](TECHNICAL.md).
