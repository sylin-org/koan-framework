# Koan.Media.Web - Technical Reference

Generic media bytes controller for ASP.NET Core applications.

## Contract

- Inputs: HTTP HEAD/GET requests to a route mapped by your derived controller, with optional Range, If-None-Match, If-Modified-Since headers.
- Outputs: 200/206/304/416 responses with correct headers; stream body for 200/206.
- Entities: Any `StorageEntity<TEntity>`-derived model (e.g., `ProfileMedia`).
- Success criteria: Accurate content-type, robust range slicing, conditional caching, and opt-in Cache-Control.

## Features

- HEAD and GET with consistent metadata (ETag, Last-Modified when available).
- Byte ranges: standard and suffix; emits `Accept-Ranges` and `Content-Range` on 206/416.
- Conditional requests: `If-None-Match` (ETag) and `If-Modified-Since` (time).
- Content type resolution from stat or file extension.
- Cache-Control via `MediaContentOptions`.

## Options (MediaContentOptions)

- `EnableCacheControl` (bool, default true)
- `Public` (bool, default true)
- `MaxAge` (TimeSpan, default 5 minutes)

Example registration:

```csharp
services.Configure<MediaContentOptions>(o =>
{
    o.EnableCacheControl = true;
    o.Public = true;
    o.MaxAge = TimeSpan.FromMinutes(60);
});
```

## Headers

- Requests: `Range`, `If-None-Match`, `If-Modified-Since`.
- Responses: `ETag`, `Last-Modified`, `Accept-Ranges`, `Content-Range`, optional `Cache-Control`.
- Centralized constants: `HttpHeaderNames` in this project.

## Edge cases

- Invalid/unsatisfiable range → 416 with `Content-Range: bytes */<length>`.
- Suffix ranges: `bytes=-N` handled properly.
- Time precision: `Last-Modified` normalized to seconds for comparability.
- Provider-dependent ETag: Local provider emits a lightweight ETag derived from last-write ticks and length.

## Usage

- Derive a controller: `public sealed class MediaController : MediaContentController<ProfileMedia> { }`
- Keep I/O in models using `StorageEntity` statics (e.g., `OpenRead`, `Head`).
- Route using ASP.NET Core attribute routing on the derived controller.

## Extensibility

- Override content-type resolution if needed.
- Replace or extend caching policy via DI options.
- Swap storage providers; ETag/metadata flow through `Head`.

## Security

- Authorize your derived controller as required (attributes/filters).
- Validate access to keys/paths in your domain layer before streaming.

## Operations

- CDN: Safe to cache with `ETag`/`Last-Modified` and explicit `Cache-Control`.
- Range-friendly for resumable downloads.

## References

- Docs: `docs/reference/media.md`
- Decisions: `docs/decisions/WEB-0035-entitycontroller-transformers.md`, `docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`, `docs/decisions/ARCH-0042-per-project-companion-docs.md`
- Engineering guardrails: `docs/engineering/index.md`
