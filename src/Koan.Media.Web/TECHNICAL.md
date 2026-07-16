# Koan.Media.Web — technical contract

## Composition

Reference activates controller discovery. `AddMediaSource<TEntity>()` selects the application Entity type for
the bare Media route. `MediaController` consumes the Core recipe registry, one `IMediaSource`, Web options, and
optional overlay/font services.

## Request flow

1. resolve a named recipe or producible format shortcut;
2. parse and validate allowlisted query mutators;
3. reject output-edge violations;
4. resolve the source through `IMediaSource.OpenAsync`;
5. negotiate an allowed, producible response format;
6. attempt the source's optional derivative lookup;
7. execute the pipeline on a miss and attempt a best-effort derivative write; and
8. emit ETag, cache, negotiation, and `X-Koan-Media-*` diagnostic headers.

The default `MediaEntitySource<TEntity>` performs step 4 through `Data<TEntity,string>.Get`, preserving active
Entity read axes. Its derivative records are framework-owned `MediaDerivation` entities stored separately from
the source Entity.

## Options

`Koan:Media:Web` currently consumes:

- `MaxOutputEdge` (4096);
- `MaxSourceMegapixels` (100);
- `MaxFrameCount` (600);
- `StrictUnknownParams` (`false`);
- `AllowAdHoc` (`true`); and
- `DefaultCacheControl` (`public, max-age=3600, stale-while-revalidate=86400`).

## Lifecycle boundary

Derivative identity is source id plus recipe fingerprint. Writes are idempotent and best-effort. No generic
orphan sweep is shipped: a context-free background probe cannot safely decide source existence for every
tenant/access-scoped source. Applications that own deletion currently perform targeted cleanup. A future
framework lifecycle service must centralize source identity and context before replacing that explicit path.

## Unsupported

No prewarm endpoint, scheduled render worker, automatic multi-source routing, signed route, content-addressed
route, configurable route prefix, or automatic orphan reclamation is claimed.
