---
type: GUIDE
domain: media
title: "Media Recipes How-To"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.18.0
validation:
  date_last_tested: 2026-07-16
  status: verified
  scope: direct pipeline, Entity source, named/config recipes, HTTP rendering, and inspection
related_guides:
  - building-apis.md
  - entity-capabilities-howto.md
related_adrs:
  - MEDIA-0004
  - MEDIA-0007
---

# Media recipes: from original to served result

This guide builds one useful path: store a media Entity, describe a business-named render, and let Koan serve it.
Stop after the direct pipeline if you do not need HTTP.

## 1. Install the useful layer

For a Koan web app with Data and Storage providers already selected:

```bash
dotnet add package Sylin.Koan.Media.Web
```

`Media.Web` brings `Media.Core` and `Media.Abstractions` transitively.

## 2. Try one direct transform

```csharp
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Pipeline;

await using var source = File.OpenRead("photo.jpg");
await using var destination = File.Create("card.jpg");

var result = await source.AsMedia()
    .AutoOrient()
    .Resize(width: 320)
    .EncodeAs("jpeg", Quality.Web)
    .WriteToAsync(destination, ct);

Console.WriteLine($"{result.Format} {result.Width}x{result.Height}");
```

The chain is lazy until the terminal. The pipeline owns the source after materialization. Prefer
`WriteToAsync`; `ToBytesAsync` is the buffered compatibility terminal.

Useful intent verbs include:

- `Resize`, `ResizeFit`, and `ResizeCover`;
- `Crop`, `Shape`, `Position`, and `Background`;
- `Rotate`, `FlipHorizontal`, and `FlipVertical`;
- `Sample`, `Trim`, and the deliberately destructive `Freeze`;
- media/text overlays;
- `Strip`, `PreserveFormat`, `EncodeAs`, and `FlattenTo`.

## 3. Make the original an Entity

```csharp
using Koan.Media;

public sealed class Photo : MediaEntity<Photo>
{
    public string AlbumId { get; set; } = "";
}
```

Store bytes through the model:

```csharp
var photo = await Photo.Upload(stream, "original.jpg", "image/jpeg", ct: ct);
photo.AlbumId = album.Id;
await photo.Save(ct);
```

Use `Photo.Store(bytes, ...)` when content-addressed SHA-256 deduplication is the desired write meaning.
The `Stream` overload of `Store` currently buffers the complete source to compute the hash.

## 4. Name business render policy

<!-- validate -->
```csharp
using Koan.Media.Abstractions.Recipes;

public static class PhotoRecipes
{
    [MediaRecipe(
        "card",
        Description = "Catalog card: 320px JPEG",
        Version = 1,
        Mutators = MutatorKind.Dimensions | MutatorKind.Quality)]
    public static MediaRecipe Card() => MediaRecipe.New()
        .AutoOrient()
        .Resize(width: 320).Name("size").Primary()
        .EncodeAs("jpeg", Quality.Web)
        .Build();
}
```

Koan discovers the method automatically. Duplicate names, reserved shortcut collisions, invalid steps, and
formats without encoders fail host startup.

The recipe fingerprint changes with version or canonical steps. Persisted derivatives therefore rotate without
manual invalidation when the recipe meaning changes.

## 5. Compose HTTP serving

```csharp
using Koan.Core;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan().AsWebApi();

var app = builder.Build();
await app.RunAsync();
```

Try:

```text
GET /media/{photoId}
GET /media/{photoId}/card
GET /media/{photoId}/card?w=480&q=85
GET /media/{photoId}/webp
```

Named recipes accept only their declared mutator classes. A format shortcut pins the output format; ordinary
named recipes may negotiate among their allowed/producible formats.

`MediaEntitySource<Photo>` resolves the source through the Entity data path before derivative lookup. Active
tenant and access restrictions therefore gate both a cold render and a stored warm result.

One concrete `MediaEntity<T>` is selected automatically. If the application defines several media Entity types,
select the owner of the bare route with `builder.Services.AddMediaSource<Photo>()`; a custom `IMediaSource` is the
equivalent non-Entity override.

## 6. Let operations override a recipe

```json
{
  "Koan": {
    "Media": {
      "Recipes": {
        "card": {
          "description": "Operations-owned card policy",
          "version": 2,
          "steps": [
            { "op": "resize", "width": 360, "name": "size", "primary": true },
            { "op": "encodeAs", "format": "webp", "quality": 78 }
          ],
          "mutators": ["dimensions", "quality"]
        }
      }
    }
  }
}
```

Configuration wins over code on a name collision. The registry monitors configuration reload. Generate the
canonical shape from a running app instead of hand-transcribing it:

```text
GET /media/recipes/card?as=appsettings
```

## 7. Inspect what actually won

```text
GET /media/recipes
GET /media/recipes/card
```

The same materialized catalog appears in Koan runtime facts with recipe source, version, fingerprint, steps,
mutators, and output posture. That gives developers, operators, reviewers, and coding agents one answer.

Rendered responses include ETag/cache semantics and `X-Koan-Media-*` diagnostics, including recipe identity,
fingerprint, formats, frame count, ignored parameters, and cache hit/miss posture where applicable.

## 8. Know the lifecycle boundary

The default Entity source persists completed derivatives on first use. It does not schedule prewarming or a
generic orphan sweep. When source deletion belongs to your application, perform targeted derivative cleanup in
the same source-lifecycle path. A leftover derivative is not served—the source gate runs first—but it occupies
storage until reclaimed.

If upload latency or a first-request budget justifies explicit generation, call the pipeline or
`MaterializeAsync` from your own durable job. That is application-owned work today, not an effect of the recipe
declaration.

Other current limits:

- one bare route selects one `IMediaSource`;
- routes are fixed under `/media`;
- signed/content-addressed route forms are not shipped; and
- there is no generic scalar/set/stream Entity Media facet.

For every option, route, and unsupported scenario, see the
[Media pillar reference](../reference/media/index.md).
