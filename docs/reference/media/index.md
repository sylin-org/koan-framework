---
type: REFERENCE
domain: media
title: "Media Pillar Reference"
audience: [developers, architects, support-engineers, ai-agents]
last_updated: 2026-07-16
framework_version: v0.18.0
status: current
validation:
  date_last_tested: 2026-07-16
  status: verified
  scope: recipe discovery, configuration, startup facts, pipeline, Entity source, and HTTP rendering
---

# Media pillar reference

Koan Media turns a stored Entity original plus a named recipe into inspectable, on-demand HTTP rendering.
The application declares media meaning; Koan owns recipe discovery, validation, execution, negotiation, and
the controller.

## Shortest path

For a Koan web application that already has Data and Storage providers:

```bash
dotnet add package Sylin.Koan.Media.Web
```

```csharp
using Koan.Core;
using Koan.Media;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Web.Routing;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan().AsWebApi();
builder.Services.AddMediaSource<Photo>();

var app = builder.Build();
await app.RunAsync();

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

The resulting useful surface is `GET /media/{photoId}/card`. No application rendering controller or recipe
registration loop is required.

## Package responsibilities

| Package | Owns |
|---|---|
| `Sylin.Koan.Media.Abstractions` | inert recipes, steps, pipeline/media contracts, output values |
| `Sylin.Koan.Media.Core` | `MediaEntity<TEntity>`, recipe discovery/configuration, startup validation/facts, image engine |
| `Sylin.Koan.Media.Web` | Entity source, recipe controller, request bounds, format negotiation, HTTP diagnostics |

`Sylin.Koan.Media.Web` brings the other two packages transitively. Applications still reference concrete Data
and Storage providers appropriate to their environment.

## Entity-backed originals

`MediaEntity<TEntity>` composes Koan Data and Storage:

```csharp
var uploaded = await Photo.Upload(stream, "original.jpg", "image/jpeg", ct: ct);
var deduplicated = await Photo.Store(bytes, "original.jpg", "image/jpeg", ct: ct);
await using var original = await Photo.OpenRead(uploaded.Key, ct);
```

- `Upload` preserves the caller's storage name.
- `Store` uses a SHA-256 key and returns an existing Entity for identical content.
- the stream overload of `Store` currently buffers the complete source to hash it.
- tenancy, access, and storage placement come from the active Data/Storage axes, not from Media-specific
  controller code.

## Recipes

A recipe method must be static, parameterless, and return `MediaRecipe` or `MediaRecipeBuilder`.

```csharp
[MediaRecipe(
    "poster",
    Description = "800px WebP poster",
    Version = 2,
    Mutators = MutatorKind.Dimensions | MutatorKind.Quality)]
public static MediaRecipe Poster() => MediaRecipe.New()
    .AutoOrient()
    .Resize(width: 800).Name("size").Primary()
    .EncodeAs("webp", Quality.Web)
    .Build();
```

At host startup, Koan rejects duplicate names, reserved shortcut collisions, invalid step grammar, and output
formats without an encoder. Configuration replaces code when both declare the same recipe name.

### Configuration recipes

```json
{
  "Koan": {
    "Media": {
      "Recipes": {
        "card": {
          "description": "Configuration-owned 320px JPEG card",
          "version": 1,
          "steps": [
            { "op": "resize", "width": 320 },
            { "op": "encodeAs", "format": "jpeg", "quality": 80 }
          ],
          "mutators": ["dimensions", "quality"]
        }
      }
    }
  }
}
```

`GET /media/recipes/{name}?as=appsettings` emits this paste-ready shape. Configuration reload is monitored;
an invalid reload throws from the change callback rather than partially replacing the catalog.

## Pipeline

```csharp
await using var destination = File.Create("card.jpg");
var output = await source.AsMedia()
    .Apply(PhotoRecipes.Card())
    .WriteToAsync(destination, ct);
```

The pipeline is lazy until a terminal:

| Terminal | Meaning |
|---|---|
| `ProbeAsync` | inspect format, dimensions, frames, alpha, and metadata posture |
| `WriteToAsync` | write one output to a destination stream; preferred terminal |
| `ToBytesAsync` | buffered compatibility terminal |
| `MaterializeAsync` | decode once and produce a configured bundle |

Canonical stages are orient, frame/sample, rotate, shape, size, overlay, metadata, and encode. Stage order is
stable regardless of declaration order. Direct Core callers own source bounds; Web applies the limits below
before full decode where possible.

## HTTP routes

| Route | Result |
|---|---|
| `GET /media/{id}` | original bytes |
| `GET /media/{id}/{seed}` | named recipe or producible format shortcut |
| `GET /media/{id}/{seed}?w=...&q=...` | recipe plus allowlisted request overrides |
| `GET /media/recipes` | recipes, shortcuts, aliases, and ad-hoc grammar |
| `GET /media/recipes/{name}` | one canonical recipe |
| `GET /media/recipes/{name}?as=appsettings` | paste-ready configuration |

Recipe responses carry ETag/cache headers plus `X-Koan-Media-Recipe`, recipe fingerprint, source/output format,
frame count, ignored-parameter, and media-kind diagnostics where applicable. Negotiated responses emit
`Vary: Accept`; an explicit format shortcut pins the output.

## Web options

Under `Koan:Media:Web`:

| Option | Default | Meaning |
|---|---:|---|
| `MaxOutputEdge` | `4096` | largest requested output edge |
| `MaxSourceMegapixels` | `100` | source pixel limit; `0` disables |
| `MaxFrameCount` | `600` | source frame limit; `0` disables |
| `StrictUnknownParams` | `false` | reject rather than report unknown query parameters |
| `AllowAdHoc` | `true` | allow requests without a named recipe |
| `DefaultCacheControl` | `public, max-age=3600, stale-while-revalidate=86400` | response cache policy |

Routes are currently fixed under `/media`.

## Source and derivative behavior

`AddMediaSource<Photo>()` selects one Entity type for the bare route. `MediaEntitySource<TEntity>` resolves the
source through `Data<TEntity,string>.Get` before derivative lookup; warm results cannot bypass active tenant or
access filters.

The default source persists completed derivatives as separate framework-owned `MediaDerivation` records keyed
by source id and recipe fingerprint. Persistence is best-effort. A source implementation may decline derivative
storage by relying on the default `IMediaSource` methods; requests then render every time.

## Startup and inspection

`AddKoan()` materializes recipes before host startup completes. The shared fact envelope reports:

- recipe and producible-shortcut counts;
- each recipe's code/config source, version, fingerprint, and step count; and
- accepted mutators and output-format posture.

The same facts are available to startup reporting, well-known/operator projections, and agent-facing inspection.
The HTTP recipe endpoints read the same registry.

## Unsupported today

- upload-time or scheduled prewarming;
- automatic orphan-derivative reclamation;
- automatic routing across several media Entity types;
- signed or content-addressed Media routes;
- configurable route prefixes; and
- a generic scalar/set/stream Entity Media facet.

Source deletion makes an orphan derivative unreachable because the source gate runs first, but does not reclaim
its storage. Applications that own deletion currently perform targeted derivative cleanup. Koan will not ship a
context-free sweep: it cannot safely infer source absence across every tenant/access axis.

## Evidence

- Media Core suite: recipe grammar, pipeline, formats, negotiation, limits, derivation behavior, and errors.
- Media Web hosted suite: Entity access gating, persisted derivative round-trip, code/config recipe startup facts,
  and invalid-configuration boot failure.
- Maintained photo sample: one original Entity, named on-demand recipes, HTTP serving, direct in-process rendering,
  and explicit targeted deletion cleanup.

See [R07-17](../../initiatives/koan-v1/work-items/r07/R07-17-media-recipe-truthfulness.md) for the current semantic
election and lifecycle boundary.
