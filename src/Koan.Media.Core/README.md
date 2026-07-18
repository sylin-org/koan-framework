# Sylin.Koan.Media.Core

The Koan media runtime: Entity-backed originals, content-addressed deduplication, recipe discovery and validation,
lazy image pipelines, encoder selection, and inspectable startup facts.

## Install

```powershell
dotnet add package Sylin.Koan.Media.Core
```

An application also references Data and Storage providers appropriate to its environment. `AddKoan()` discovers
Media Core; no media-specific registration call is required for Entity or direct-pipeline use.

## Smallest meaningful use

```csharp
using Koan.Media;

public sealed class Photo : MediaEntity<Photo> { }

var photo = await Photo.Upload(source, "original.jpg", "image/jpeg", ct: ct);
await using var bytes = await Photo.OpenRead(photo.Key, ct);
```

`Photo.Store(bytes, ...)` uses a SHA-256 key and returns the existing Entity when identical content is already
present. `Photo.Upload(...)` preserves caller-owned naming.

Direct processing is lazy until a terminal:

```csharp
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Pipeline;

var output = await source.AsMedia()
    .Resize(width: 320)
    .EncodeAs("jpeg", Quality.Web)
    .WriteToAsync(destination, ct);
```

Static `[MediaRecipe]` factories and `Koan:Media:Recipes` configuration enter one catalog. Invalid steps, duplicate
names, reserved shortcut collisions, and unavailable output encoders stop host startup before traffic.

## Guarantees and boundaries

- Media Core is an in-process Entity/media runtime, not a durable rendering job system.
- `MediaEntity.Store(Stream, ...)` currently buffers the complete source to compute its hash.
- A successful `Upload` writes storage and returns hydrated Entity metadata; callers own any additional domain save.
- Pipeline output can stream, but decoders may retain complete pixel/frame state; direct callers own ingress limits.
- Recipes do not imply prewarming, background scheduling, orphan cleanup, HTTP routes, or access policy.
- `Sylin.Koan.Media.Web` owns bounded HTTP rendering, negotiation, diagnostics, and access-gated source resolution.

See [TECHNICAL.md](./TECHNICAL.md) and the [Media reference](../../docs/reference/media/index.md).
