# Koan.Media.Web

Generic media bytes controller for ASP.NET Core with correct HTTP semantics: HEAD/GET, Range (206/416), conditional requests (ETag/If-None-Match and If-Modified-Since), and configurable Cache-Control.

## Capabilities
- Attribute-routed controller base: MediaContentController<TEntity>
- Full and ranged reads over StorageEntity<TEntity>
- Caching headers: ETag, Last-Modified; Accept-Ranges and Content-Range
- Cache-Control via options (MediaContentOptions)

## Minimal setup
- Inherit for your entity:

```csharp
public sealed class MediaController : MediaContentController<ProfileMedia> { }
```

- Optional: configure Cache-Control

```csharp
services.Configure<MediaContentOptions>(o =>
{
    o.EnableCacheControl = true; // default true
    o.Public = true;             // default true
    o.MaxAge = TimeSpan.FromHours(1);
});
```

## Notes
- Content type is inferred from stat.ContentType or file extension.
- ETag emission depends on provider support (Local emits a lightweight ETag derived from last-write time and length).
- Prefer thin controllers; keep I/O in models via StorageEntity statics.
