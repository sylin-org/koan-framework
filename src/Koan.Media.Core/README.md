# Koan.Media.Core

Recipe discovery, startup validation, and the image-processing engine for Koan Media.

## Direct processing

```csharp
await using var source = File.OpenRead("photo.jpg");
await using var destination = File.Create("card.jpg");

var output = await source.AsMedia()
    .Resize(width: 320)
    .EncodeAs("jpeg", Quality.Web)
    .WriteToAsync(destination, ct);
```

For reusable policy, declare a `[MediaRecipe]` method and apply the resulting recipe:

```csharp
var output = await source.AsMedia()
    .Apply(PhotoRecipes.Card())
    .WriteToAsync(destination, ct);
```

Referencing the package plus `AddKoan()` discovers code recipes and configuration overrides. The catalog is
materialized during host startup: invalid steps, duplicate names, reserved shortcut collisions, and unsupported
output formats stop the host before traffic. Valid decisions enter Koan's runtime facts automatically.

## Configuration

Recipes may be declared under `Koan:Media:Recipes`. Configuration replaces a code recipe with the same name.
Use `GET /media/recipes/{name}?as=appsettings` from `Koan.Media.Web` to obtain the canonical paste-ready shape.

## Current limits

- Core is an in-process image pipeline, not a durable media job system.
- It does not prewarm recipes or schedule background rendering.
- Encoder availability defines which output formats are accepted at startup.

See the [Media reference](../../docs/reference/media/index.md) and
[technical companion](TECHNICAL.md).
