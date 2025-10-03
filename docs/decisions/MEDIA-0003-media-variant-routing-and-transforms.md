---
id: MEDIA-0003
slug: media-variant-routing-and-transforms
domain: Media
status: Accepted
date: 2025-08-24
title: Media variant routing, automatic transforms, and canonical signature
---

## Context

Koan’s media pipeline streams original content with strong HTTP semantics (ETag/Last-Modified, Range, HEAD). We now need collision-free addressing, ergonomic transforms via query parameters (e.g., resize), deterministic variant keys to prevent reprocessing/storage bloat, and clear policies around unknown params and operator overlap.

## Decision

- Addressing (ID-first with filename flair)
  - GET /api/media/{mediaId:guid}/{\*filename} → filename is flair-only, ignored for lookup.
  - GET /api/media/{mediaId:guid}.{ext} → extension is flair-only.
- Automatic operator resolution (deterministic)
  - Providers register: id (e.g., resize@1), supported content-types, canonical param names, and aliases (e.g., w:[w,width], h:[h,height], format, quality, fit).
  - Resolution walks the query param pool once; when a key matches an operator’s alias set, that operator is selected and its owned params are claimed/removed; loop until no matches.
  - Startup overlap detection builds a reverse index alias→operators. Policy:
    - Strict: throw at startup on overlaps.
    - Relaxed: log a warning and require a precedence list for disambiguation.
  - Placement constraints: each provider declares placement = Terminal | Pre | Free. typeConverter@1 is Terminal (must run last).
  - Fixed v1 precedence (after resolution): rotate → resize → typeConverter. Query param order does not influence execution order.
- Canonicalization and signature (cache key)
  - Build a canonical map: alias→canonical name, lowercase keys/enums, trim, clamp ranges, round floats, drop defaults/empty, sort keys (invariant culture), media-type gate.
  - Duplicate params: first occurrence wins; later duplicates are ignored (Relaxed) or 400 (Strict if conflicting). Only the first value contributes to the signature.
  - Signature JSON includes: src mediaId, src ETag (or Last-Modified fallback), selected operator id(s) with version(s), and canonical params.
  - Hash: SHA-256 of the canonical JSON; encode base32/base64url. This is the variant key/id.
- Short-circuit and creation
  - Before work: HEAD variants/{srcId}/{hash}/… (or id lookup) → if exists, 301/302 redirect to canonical variant URL (/api/media/{variantId}.{ext?}).
  - On miss: run pipeline, persist variant with conditional create (CreateNew or If-None-Match: \*). If another writer won, discard and redirect.
  - Redirect code: 301 when the signature embeds source ETag and operator version (immutable); 302 otherwise.
  - Primitive-only rule: only original sources (no parentId) are eligible for transforms to prevent transform-of-transform explosions.
- Unknown params (Relaxed vs Strict)
  - Relaxed (default): ignore and drop unknown/unregistered params; do not include them in the signature; expose X-Media-Ignored-Params header; optionally log at Information.
  - Strict: reject with 400 and actionable messages (e.g., suggest canonical names/known operators).
- Policies as MediaEntity decorators
  - ResolutionPolicy: Strict | Relaxed; optional precedence list per entity/type.
  - LimitsPolicy: maxWidth/Height, allowUpscale, allowedFormats, maxQuality, maxTransformsPerRequest.
  - ParamsPolicy: unknownParamBehavior, alias overrides.
  - Effective policy resolution: instance → type → global options.
- HTTP semantics and headers
  - Preserve existing ETag/Last-Modified/Range/HEAD behavior for both originals and variants.
  - Content-Type from stored metadata, not flair.
  - Add Content-Disposition: inline; filename="<flair-name-or-derived>" on final bytes responses.
- Storage layout (no DB required)
  - Default path: variants/{srcId}/{hash}/{originalNameOrExt}. The hash serves as the stable variant id.
  - Optional mapping store (srcId, hash) → variantId if opaque IDs are preferred later.
- Growth controls
  - Record last-access (async) on variant reads.
  - Background GC: TTL for inactive variants and per-source caps.

### Providers (v1)

- resize@1

  - Params (canonical → aliases):
    - w:[w,width], h:[h,height]
    - fit:[fit,mode] ∈ {cover, contain, fill, inside, outside} (default: contain)
    - q:[q,quality] ∈ [1,100] (lossy only; default: 82)
    - bg:[bg,background] (hex/named color for letterbox/fill)
    - up:[up,upscale] ∈ {true,false} (default: false)
  - Rules: if only one of w/h provided, preserve aspect ratio; clamp to policy max; reject non-image.

- rotate@1

  - Params: angle:[angle,a] ∈ {0,90,180,270}; exif:[autoOrient,orient] ∈ {true,false} (default: true)
  - Rules: apply auto-orient first (if exif=true), then angle; reject non-image.

- typeConverter@1 (Terminal)
  - Params: format:[format,f] ∈ {jpg,png,webp,avif}; q:[q,quality] (lossy only); bg for jpeg from transparent sources; cs:[colorspace] ∈ {srgb,displayp3} (optional)
  - Rules: set Content-Type from format; ignore q for lossless; if converting transparent→jpeg and bg missing, use policy default.

## Scope

- Applies to Koan.Media.Web and sample apps (e.g., S6). ID routes become the canonical public API for media bytes.
- Keeps the existing byte-streaming controller semantics and options (cache-control) intact.

## Consequences

Positive

- Deterministic, collision-free IDs; no reprocessing for identical requests.
- Ergonomic URLs with optional filename/extension flair; stable redirects to canonical variant ids.
- Clear operator/version evolution via signature; cache safety with 301 when immutable.

Negative

- Startup registry discipline required (overlap detection/precedence) or the app fails in Strict mode.
- Storage growth persists without GC policies.
- Slight overhead for canonicalization/signature/HEAD check on first-hit.

## Implementation notes

- Controllers
  - Add id-based GET routes to sample controllers: /api/media/{id:guid}/{\*filename?} and /api/media/{id:guid}.{ext}.
  - On recognized transforms: normalize → signature → short-circuit/transform → conditional create → redirect.
- Registry and normalization
  - Centralize operator and param/alias constants (no magic strings).
  - Build a param→operator index for O(1) resolution; HashSet alias sets.
  - Enforce placement constraints and v1 precedence (rotate→resize→typeConverter). If constraints cannot be satisfied, 400 (Strict) or reorder per precedence (Relaxed).
  - Read query in arrival order; on duplicates for the same canonical key, keep the first value and drop the rest. In Strict mode, 400 when conflicting duplicates are detected.
  - Provide normalize/validate helpers per operator; reject invalid combos (400).
- Concurrency and storage
  - Use provider-level conditional create (CreateNew or If-None-Match: \*). On conflict, redirect.
  - Store variant metadata: parentId, signature payload, content-type, createdAt, lastAccessed.
  - Enforce primitive-only eligibility before any transform work; return 409/400 when parentId is present.
- Observability
  - Log signature and hash at Debug; include an X-Media-Variant header on redirects.
  - Metrics: misses/hits, transform duration, ignored params (unknown/duplicates), strict rejections, placement reorders.
- Docs and DX
  - Document operator params (canonical names and aliases) and policies in reference docs.

## Follow-ups

1. Implement overlap detector and precedence policy in the operator registry (+ unit tests).
2. Add per-entity decorators and global options for policies; wire strict/relaxed modes.
3. Implement resize@1 provider (w,h,fit,q,format) with safety limits and tests.
4. Add GC policy job (TTL/caps) and last-access tracking (lightweight).
5. Extend samples and docs with id-based routes and canonical redirect examples.

## References

- MEDIA-0001 - Media pillar baseline and storage integration
- MEDIA-0002 - S6 Social Creator sample and htmx UI
- WEB-0035 - EntityController transformers
- DATA-0061 - Data access pagination and streaming
- docs/reference/media.md (caching headers, controller semantics)
