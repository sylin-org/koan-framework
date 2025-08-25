# Media

What
- First-class media handling: upload, download, variants/derivatives, pipelines/tasks, ancestry.

Why
- Enterprise-grade media with low ceremony and consistent semantics; integrates with Sora.Storage for placement and URLs.

Key concepts
- MediaObject<T>: model with static methods — Upload, Get, Open, Url, Ensure, RunTask, Derivatives, Ancestors, Descendants.
- Derivatives: first-class media linked to source via SourceMediaId + RelationshipType; idempotent by DerivationKey.
- Named tasks: code@version; registered per model; args allowed; DescribeTask exposes schema.
- Task tracking: TaskId, statuses, step timeline; GetTask and SSE StreamTask.
- Storage integration: placement via Sora.Storage profiles; tags/metadata can influence routing; presigned URLs and CDN-friendly headers.

HTTP endpoints (controller-based)
## HTTP media bytes endpoint (reference)

Contract
- Inputs: key (string) — storage key identifying the media object.
- Outputs: bytes of the media content; content-type resolved from storage or file extension.
- Options: Cache-Control via MediaContentOptions (EnableCacheControl, Public, MaxAge).
- Error modes: 404 NotFound (missing), 416 Range Not Satisfiable (invalid range), 304 Not Modified (conditional GET).

Endpoints
- GET /api/media/{key}
	- Supports conditional GET: If-Modified-Since and, when available, If-None-Match (ETag).
	- Supports byte ranges: Range request header (e.g., bytes=0-99, bytes=100-, bytes=-50).
	- Response headers: Accept-Ranges: bytes; Content-Range (for 206/416); Last-Modified; ETag (if provider returns one); Cache-Control (when enabled by options).
- HEAD /api/media/{key}
	- Same metadata headers as GET without body; includes Content-Length when available.

Examples
- Full GET
	- Request: GET /api/media/2025/08/hi.txt
	- Response: 200 OK; Content-Type: text/plain; Accept-Ranges: bytes; Last-Modified: <rfc1123>.
- Conditional GET
	- Request: GET /api/media/2025/08/hi.txt with If-Modified-Since: <rfc1123>
	- Response: 304 Not Modified (when unchanged); headers include Last-Modified and optional ETag.
 - Conditional GET (ETag)
	- Request: GET /api/media/2025/08/hi.txt with If-None-Match: "<etag>"
	- Response: 304 Not Modified when ETag matches; otherwise 200/206 depending on Range.
- Range GET
	- Request: GET /api/media/2025/08/hi.txt with Range: bytes=0-0
	- Response: 206 Partial Content; Content-Range: bytes 0-0/<total>; Accept-Ranges: bytes.
- Invalid range
	- Request: GET /api/media/2025/08/hi.txt with Range: bytes=999-
	- Response: 416 Range Not Satisfiable; Content-Range: bytes */<total>.

Implementation notes
- Controller: MediaContentController<TEntity> (Sora.Media.Web) — attribute-routed, controller-based as per Sora guardrails.
- IO and storage semantics live in StorageEntity<TEntity> (Sora.Storage.Model); controllers defer to model statics like OpenRead/OpenReadRange/Head.
- Content type is resolved from storage stat (preferred) or file extension fallback.
- Last-Modified is normalized to seconds for If-Modified-Since comparisons.

Edge cases
- Unknown length providers: range requests return 416 if content length cannot be determined.
- Suffix ranges (bytes=-N) are supported when length is known; validated against content size.
- ETag emission depends on provider support; Local provider now emits a lightweight ETag based on LastWriteTimeUtc and Length (hex), which changes whenever the file changes.

Options
- MediaContentOptions (Sora.Media.Web):
	- EnableCacheControl: default true.
	- Public: default true.
	- MaxAge: default 1 hour.
	- When enabled, responses include Cache-Control: <public|private>, max-age=<seconds>.

See also
- decisions/MEDIA-0001-media-pillar-baseline-and-storage-integration.md
- reference/storage.md
 - src/Sora.Media.Web/README.md
 - src/Sora.Media.Web/TECHNICAL.md
