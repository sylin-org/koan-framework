# Koan.Media.Abstractions

## Contract
- **Purpose**: Define media contracts, transforms, and task models shared across Koan media pipelines.
- **Primary inputs**: `MediaAsset`, `MediaVariant`, and pipeline task definitions consumed by storage and web modules.
- **Outputs**: Serializable DTOs for persistence, events emitted through messaging adapters, and helper extensions for variant resolution.
- **Failure modes**: Unregistered media tasks, missing variant identifiers, or incompatible serialization when persisting custom metadata.
- **Success criteria**: Media modules share a consistent shape, variant naming stays predictable, and downstream services can compose media tasks without duplication.

## Quick start
```csharp
using Koan.Media.Abstractions;

public static class MediaVariants
{
    public static MediaVariant HdMp4(string assetId) => new()
    {
        VariantId = "video:hd",
        SourceAssetId = assetId,
        ContentType = "video/mp4",
        Transforms = { MediaTransform.For("encode", new { profile = "h264_hd" }) }
    };
}

public async Task<MediaAsset> CreateMediaAsync(string title)
{
    var asset = new MediaAsset
    {
        Title = title,
        Variants = { MediaVariants.HdMp4(Guid.NewGuid().ToString()) }
    };
    return await MediaAssetExtensions.ValidateAsync(asset);
}
```
- Use the core models to describe variants and transforms; downstream modules (storage, web) will respect the shared schema.
- Leverage helper extensions for validation and canonical naming.

## Configuration
- Pair with `Koan.Storage` providers to persist media assets.
- Register default variants in your module’s auto-registrar to keep variant IDs consistent across environments.
- Use `MediaTask` descriptors to configure pipeline workers (encoding, thumbnailing, etc.).

## Edge cases
- Unsupported content types: provide fallback transforms or mark variants as optional to avoid failing the entire asset.
- Large metadata payloads: ensure custom metadata remains serializable through Koan storage adapters.
- Concurrent updates: apply optimistic concurrency using the asset’s version fields.
- Cross-service access: share variant IDs as constants to avoid drift between producers and consumers.

## Related packages
- `Koan.Media.Core` – runtime orchestration consuming these abstractions.
- `Koan.Media.Web` – HTTP APIs that rely on the contracts defined here.
- `Koan.Storage` – storage routing used for persisting assets.

## Reference
- `MediaAsset` – root entity describing media items.
- `MediaVariant` – variant metadata structure.
- `MediaTask` – pipeline work item descriptor.
