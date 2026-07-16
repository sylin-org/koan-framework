# Koan.Media.Web

Recipe-driven HTTP rendering for an application's `MediaEntity<TEntity>` originals. The package supplies the
controller, conditional/cache headers, bounded request parsing, format negotiation, and optional persisted
derivatives.

## Minimal application code

```csharp
using Koan.Core;
using Koan.Media.Abstractions.Model;
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

The application still needs selected Data and Storage providers for its `Photo` Entity. No application
rendering controller is required.

## Routes

- `GET /media/{id}` — original bytes;
- `GET /media/{id}/{recipe}` — named recipe or supported format shortcut;
- `GET /media/{id}/{recipe}?w=...&q=...` — allowlisted overrides;
- `GET /media/recipes` — materialized catalog and shortcuts; and
- `GET /media/recipes/{name}?as=appsettings` — canonical recipe configuration.

`MediaEntitySource<TEntity>` resolves the source through the Entity data path before consulting a derivative,
so active tenancy and access axes gate both cold and warm requests.

## Current limits

- one bare `/media/{id}` route has one `IMediaSource`; applications with multiple media Entity types must own
  a discriminating router;
- the default source buffers a completed render before persisting its derivative;
- derivative writes are best-effort and source deletion does not automatically reclaim derivative storage;
- there is no upload-time prewarm, signed route, or content-addressed route shape; and
- routes are currently fixed under `/media`.

See the [Media reference](../../docs/reference/media/index.md) and
[technical companion](TECHNICAL.md).
