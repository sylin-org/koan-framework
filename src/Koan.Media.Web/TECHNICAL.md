# Sylin.Koan.Media.Web — technical contract

## Composition

Reference activates controller discovery and compiles one source choice. Exactly one concrete `MediaEntity<T>` is the
automatic default. An already registered `IMediaSource` dominates discovery; `AddMediaSource<T>()` replaces the
automatic choice regardless of module order. Zero or several candidates without an override fail host startup with a
corrective error. `MediaController` consumes the Core recipe registry, the selected source, Web options, and optional
overlay/font services.

## Request flow

1. resolve a named recipe or producible format shortcut;
2. parse and validate allowlisted query mutators;
3. reject output-edge violations;
4. resolve the source through `IMediaSource.OpenAsync`;
5. negotiate an allowed, producible response format;
6. attempt the source's optional derivative lookup;
7. execute the pipeline on a miss and attempt a best-effort derivative write; and
8. return an ASP.NET Core file result with ETag, Last-Modified, cache, range, length, negotiation, and
   `X-Koan-Media-*` diagnostic headers.

The default `MediaEntitySource<TEntity>` performs step 4 through `Entity<TEntity,string>.Get`, preserving active
Entity read axes. Its derivative records are framework-owned `MediaDerivation` entities stored separately from
the source Entity.

Discovery uses Koan's already compiled assembly closure once during composition. Request execution performs no type
scan or source election. Startup reporting states the candidate/default posture.

GET and HEAD share the same representation path for originals, persisted derivations, and freshly rendered recipes.
Seekable streams and buffered recipe results enable ASP.NET Core range processing, including conditional and
`If-Range` behavior. A custom `IMediaSource` may return a non-seekable stream for complete delivery; a GET carrying a
`Range` header then returns corrective `416` with `Accept-Ranges: none`. The controller transfers seekable response
stream ownership to the MVC result executor, which disposes it after delivery.

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
tenant/request-context-scoped source. Applications that own deletion currently perform targeted cleanup. A future
framework lifecycle service must centralize source identity and context before replacing that explicit path.

## Unsupported

No prewarm endpoint, scheduled render worker, automatic multi-source routing, signed route, content-addressed
route, configurable route prefix, or automatic orphan reclamation is claimed.
