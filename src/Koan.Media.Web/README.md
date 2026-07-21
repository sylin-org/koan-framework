# Sylin.Koan.Media.Web

Serve an application's `MediaEntity<T>` originals and named recipes through controller-owned HTTP routes. The
reference supplies discovery, conditional/cache headers, bounded request parsing, format negotiation, and optional
persisted derivatives.

```bash
dotnet add package Sylin.Koan.Media.Web
```

## Smallest meaningful result

```csharp
using Koan.Core;
using Koan.Media;
using Koan.Media.Abstractions.Recipes;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan().AsWebApi();

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

One concrete `MediaEntity<T>` is selected automatically. The application still needs Data and Storage providers for
its `Photo` Entity; no registration helper or application rendering controller is required.

When the application has several media Entity types, select the one that owns the bare route deliberately:

```csharp
builder.Services.AddMediaSource<Photo>();
builder.Services.AddKoan().AsWebApi();
```

A custom `IMediaSource` is the equivalent override for a non-Entity source. Zero or several candidates without an
override reject host startup with that correction.

## Routes

- `GET /media/{id}` — original bytes;
- `GET /media/{id}/{recipe}` — named recipe or supported format shortcut;
- `GET /media/{id}/{recipe}?w=...&q=...` — allowlisted overrides;
- `GET /media/recipes` — materialized catalog and shortcuts; and
- `GET /media/recipes/{name}?as=appsettings` — canonical recipe configuration.

`MediaEntitySource<TEntity>` resolves the source through the Entity data path before consulting a derivative,
so active tenancy and access axes gate both cold and warm requests.

## Current limits

- one bare `/media/{id}` route has one `IMediaSource`; applications with multiple media Entity types must choose one
  source or own a discriminating custom source;
- the default source buffers a completed render before persisting its derivative;
- derivative writes are best-effort and source deletion does not automatically reclaim derivative storage;
- there is no upload-time prewarm, signed route, or content-addressed route shape; and
- routes are currently fixed under `/media`.

See the [Media reference](../../docs/reference/media/index.md) and
[technical companion](TECHNICAL.md).
