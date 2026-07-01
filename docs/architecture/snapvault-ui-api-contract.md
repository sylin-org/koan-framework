# SnapVault — UI API Contract (functional acceptance gate)

- Status: **Draft for review** (2026-06-26)
- Purpose: the exact HTTP + realtime surface the SnapVault SPA (`samples/S6.SnapVault/wwwroot`) actually calls. The greenfield backend **must honor every row below**, except the rows explicitly marked **CHANGE** (the deliberate migrations the UI gets matching affordances for). Companion to [snapvault-product-spec.md](./snapvault-product-spec.md).
- Source: understand-pass workflow `wf_2024d47f-292` (UI agent grepped `wwwroot/js/**`), corrected by hand where the UI map and backend map disagreed (notes below). All call sites are `file:line` in `wwwroot/`.

## Client conventions

- Central client `js/api.js` exposes 5 verbs: `get(url, params, {includeHeaders})`, `post(url, data)`, `put(url, data)`, `delete(url)`, `upload(url, formData, onProgress)` (XHR). `get()` returns parsed JSON, or `{data, headers}` when `includeHeaders:true`. Base URL = `window.location.origin`.
- **Two paths bypass `api.js`:** `POST /api/photos/upload` (multipart, `upload.js:340`) and **all of `settings.js`** (raw `fetch`, `apiBase='/api'`). The backend must serve these identically.
- **Browser-driven (not `fetch`):** `GET …/download` and the settings export/backup blobs use `window.open()`/anchor-click — **any tenant scoping must be in the URL itself** for these.
- **Error contract:** `api.js` throws `Error("HTTP <status>: <statusText>")` on non-2xx; bulk-delete reads `errors[]`; collection-add greps `error.message` for the word `limit`. Keep non-2xx semantics + an `errors[]` array on bulk-delete partial failure.
- **Photo object fields the UI reads (union):** `id, originalFileName, isFavorite, rating, detailedDescription, capturedAt, cameraModel, eventId, masonryThumbnailMediaId, retinaThumbnailMediaId`, and nested `aiAnalysis { facts (key→value map), lockedFactKeys[], summary, summaryLocked, analysisStyle }`. *(`masonryThumbnailMediaId`/`retinaThumbnailMediaId` disappear with the media-recipe change — see CHANGE rows.)*

## Endpoints (HTTP)

| # | Method | URL the UI calls | Purpose | Request | Response (fields UI consumes) | Called from |
|---|---|---|---|---|---|---|
| 1 | GET | `/api/photos/stats` | sidebar badges | — | `{ totalPhotos, favorites }` | `app.js:89` |
| 2 | GET | `/api/photos` | filters-panel list + count preview | query `page,pageSize,sort` (e.g. `-CapturedAt`), `filter` (JSON, Mongo-style `$and`) | **array body** + header `X-Total-Count` (only header read) | `filters.js:579,615` |
| 3 | GET | `/api/photos/{id}` | lightbox detail; poll after reroll | — | full photo + `aiAnalysis{facts,lockedFactKeys,summary,summaryLocked,analysisStyle}` | `lightbox.js:331`, `lightboxActions.js:223` |
| 4 | GET | `/api/photos/{id}/index` | lightbox jump-to-index | query `context,collectionId?,searchQuery?,searchAlpha?,sortBy,sortOrder,filters?` | `{ index }` | `PhotoSetManager.js:543` |
| 5 | **POST** | **`/api/photosets/query`** | **core windowed grid + lightbox nav (search/all/favorites/collection all route here)** | `{ startIndex, count, sessionId?, definition?:{context,searchQuery,searchAlpha,collectionId,sortBy,sortOrder} }` | `{ sessionId, startIndex, totalCount, photos[] }` | `PhotoSetManager.js:519,581` |
| 6 | GET | `/api/photos/by-event/{eventId}` | event click in sidebar | — | envelope; UI reads `.photos[]` ⚠️ | `app.js:546` |
| 7 | GET | `/api/photos/filter-metadata` | filters facets | — | `{ cameraModels[], years[], tags[] }` | `filters.js:41` |
| 8 | POST | `/api/photos/upload` | batch upload (≤10 files/chunk) | multipart: `files[]`, `eventId` (omitted for auto-organize) | `{ jobId, totalQueued }` | `upload.js:340` |
| 9 | POST | `/api/photos/{id}/favorite` | toggle favorite | — | `{ isFavorite }` | `app.js:579`, `lightboxActions.js:23`, `ActionRegistry.js:25` |
| 10 | POST | `/api/photos/{id}/rate` | set rating 0–5 | `{ rating }` | `{ rating }` | `app.js:616`, `lightboxActions.js:75` |
| 11 | POST | `/api/photos/bulk/favorite` | bulk favorite | `{ photoIds[], isFavorite }` | (not field-inspected) | `ActionRegistry.js:38,83`, `bulkActions.js:114` |
| 12 | POST | `/api/photos/bulk/delete` | bulk delete | `{ photoIds[] }` | `{ deleted, errors? }` | `ActionRegistry.js:159`, `bulkActions.js:153`, `lightboxActions.js:151` |
| 13 | GET | `/api/photos/{id}/download` | download original (`window.open`) | — | binary, download disposition | `ActionRegistry.js:112`, `lightboxActions.js:125`, `bulkActions.js:141` |
| 14 | POST | `/api/photos/{id}/regenerate-ai` | fire-and-forget reanalyze, then poll #3 | — | (not consumed) | `lightboxActions.js:205` |
| 15 | POST | `/api/photos/{id}/regenerate-ai-analysis` | reroll preserving locks, optional style | `null` or `{ analysisStyleId }` | updated photo (`.aiAnalysis.analysisStyle`) | `lightboxPanel.js:869` |
| 16 | POST | `/api/photos/{id}/facts/{factKey}/toggle-lock` | lock one fact (`factKey` = `encodeURIComponent(lowercased)`) | — | `{ isLocked, lockedFactKeys[] }` | `lightboxPanel.js:660` |
| 17 | POST | `/api/photos/{id}/facts/lock-all` | lock all facts | — | (client sets locally) | `lightboxKeyboard.js:193` |
| 18 | POST | `/api/photos/{id}/facts/unlock-all` | unlock all facts | — | (client sets locally) | `lightboxKeyboard.js:217` |
| 19 | POST | `/api/photos/{id}/summary/toggle-lock` | lock the summary | — | `{ summaryLocked }` | `lightboxPanel.js:706` |
| 20 | GET | `/api/analysis-styles/active` | regen split-button styles | — | `[{ id, name|label, icon, description?, priority? }]` | `lightboxPanel.js:734` |
| 21 | GET | `/api/events` | sidebar events + upload dropdown | — | `[{ id, name, photoCount?, type (enum; 6=DailyAuto hidden) }]` | `app.js:427`, `upload.js:171` |
| 22 | POST | `/api/events` | create event from upload modal | `{ name, type (0=General), eventDate (ISO) }` | created event (`.id`) | `upload.js:388` |
| 23 | GET | `/api/collections` | sidebar collections | — | `[{ id (GUIDv7), name, photoCount }]` | `collectionsSidebar.js:25` |
| 24 | GET | `/api/collections/{id}` | open collection view | — | `{ id, name, photoCount, createdAt }` | `collectionView.js:51` |
| 25 | POST | `/api/collections` | create collection | `{ name }` | created (`.id`) | `collectionsSidebar.js:140`, `dragDropManager.js:125` |
| 26 | PUT | `/api/collections/{id}` | rename | `{ name }` | (not field-inspected) | `collectionsSidebar.js:253`, `collectionView.js:205` |
| 27 | DELETE | `/api/collections/{id}` | delete collection (photos retained) | — | `ok` only | `collectionsSidebar.js:174` |
| 28 | POST | `/api/collections/{id}/photos` | add photos (drag/create) | `{ photoIds[] }` | `{ added }`; over-cap error message contains `limit` | `collectionsSidebar.js:146`, `dragDropManager.js:130,189` |
| 29 | POST | `/api/collections/{id}/photos/remove` | remove photos (not delete) | `{ photoIds[] }` | (not field-inspected) | `ActionRegistry.js:225` |
| 30 | GET | `/api/media/photos/{id}/gallery` 🔧 | lightbox progressive / grid fallback | — (img src) | image bytes | `grid.js:205`, `lightbox.js:390`, `ImagePreloader.js:193` |
| 31 | GET | `/api/media/photos/{id}/original` 🔧 | lightbox full-res / preload | — (img src) | image bytes | `lightbox.js:391,631`, `ImagePreloader.js:192` |
| 32 | GET | `/api/media/masonry-thumbnails/{masonryId\|photoId}` 🔧 | grid thumbnail | — (img src) | image bytes | `grid.js:185,208,215,220` |
| 33 | GET | `/api/media/retina-thumbnails/{retinaId}` 🔧 | retina grid thumbnail | — (img src) | image bytes | `grid.js:213` |
| 34 | GET | `/api/maintenance/stats` | settings storage stats | — | `{ hotTierGB, warmTierGB, coldTierGB, totalGB, photoCount, cacheEntries, cacheSizeMB }` | `settings.js:66` |
| 35 | GET | `/api/maintenance/index-status` | settings last-indexed | — | `{ lastIndexed }` | `settings.js:146` |
| 36 | POST | `/api/maintenance/{action}` | `rebuild-index` \| `clear-cache` \| `optimize-db` | — | `ok` | `settings.js:166` |
| 37 | GET | `/api/maintenance/export-metadata` | download JSON (Blob) | — | blob | `settings.js:205` |
| 38 | GET | `/api/maintenance/backup-config` | download JSON (Blob) | — | blob | `settings.js:234` |
| 39 | POST | `/api/maintenance/wipe-repository` | destructive wipe, streamed progress | — | **NDJSON** lines `{ percentage, message }` (response-body stream, not SSE) | `settings.js:337` |

## Realtime

| Kind | URL | Events / framing | Called from | Disposition |
|---|---|---|---|---|
| **SSE** (was SignalR) | `GET /api/photos/progress/{batchId}` | Server-Sent Events: `PhotoProgress{jobId,photoId,fileName,status,stage,error?}` per photo state-change, then a terminal `JobCompleted{jobId,status,totalPhotos,successCount,failureCount,errors}`; the stream then closes. Browser-native `EventSource` (no library). | `processMonitor.js` | **DELIVERED (D4, read side):** a read-projection of the durable jobs ledger — no hub/groups/subscribe. `PhotoQueued` folded into `PhotoProgress` (`status:"queued"`); the preserved UI fields (`photoId`/`fileName`/`status`/`stage`/`error`/`successCount`/`failureCount`/`jobId`) are unchanged, the rest additive. Upload POST (row 8) that mints `batchId` + submits the jobs is step 5. |
| Polling | `GET /api/photos/{id}` | 1 s up to 60 s after reroll, compares `aiAnalysis`/`detailedDescription` | `lightboxActions.js:217-255` | Keep (or replace with the SSE stream). |
| Response-stream | `POST /api/maintenance/wipe-repository` | reads `response.body.getReader()`, parses NDJSON `{percentage,message}` | `settings.js:346-364` | Keep as NDJSON, or fold into the SSE pattern. |

## Corrections & gotchas (verified by hand — these override the raw maps)

- ⚠️ **#6 `by-event` shape:** backend returns the full envelope `PaginatedResponse{Photos,Page,PageSize,TotalCount,TotalPages}` (`PhotosController.cs:420-440`), **not** a bare `{photos}`. The UI reads `response.photos` (a subset), so it works — but the contract is the envelope. Keep `photos` camelCase.
- ⚠️ **#4 `index` default sort mismatch:** the UI assumes `sortBy` default `capturedAt`; the backend `GetPhotoIndex` defaults `id`. If the UI omits `sortBy`, server sorts by `id` → navigation desync risk. **The new backend should default index/range sort to match what the UI actually browses by** (decide `capturedAt`), and accept the `filters` param the UI sends on #4 (legacy silently dropped it).
- **#5 is the load-bearing list endpoint.** It is **session-stateful**: first call sends `definition` and gets a `sessionId`; later calls reuse `sessionId`. Support both variants; return a stable `sessionId` + `totalCount`. Search, favorites, collection, and all-photos all flow through here via `definition.context` — there is **no separate `/search` call from the UI**.
- **Pagination shapes to honor:** (a) bare array + `X-Total-Count` (#2); (b) the session envelope (#5); plus the by-event envelope (#6) and collection-photos envelope (#24 family). Standardize backing on `EntityController` query + the session endpoint, but the consumed shapes above are the contract.
- **🔧 Media URLs (#30–33) — CHANGE (D3):** these are the only routes slated to change. The framework recipe controller serves `/media/{id}/{recipe}`; update the 4 UI call sites (`grid.js`, `lightbox.js`, `ImagePreloader.js`) and drop the `masonryThumbnailMediaId`/`retinaThumbnailMediaId` fields from the photo object. Variants map: `gallery`→`gallery`, `original`→`/media/{id}` (no recipe), masonry→`masonry`, retina→`retina`, plus the hot `thumbnail` recipe (`Eager=true`).

## Tenant carrier (the affordance to add — D1)

The SPA conveys **no tenant** today. To make it multi-tenant, inject the selected studio at one point in `api.js` (a header, e.g. `X-Studio`/`__koan_tenant`) and, because they bypass `api.js`, **embed it in the URLs** for: `GET …/download` (#13), the settings export/backup/wipe (#37–39), and the SSE stream URL (#D4). Persist the selection in `localStorage`; render a picker in the header. The backend turns the carrier into the ambient tenant via the sample-side resolver+middleware (no framework resolver ships — see product spec §5).

## Client-side stubs (NOT yet part of the contract)

`photo.addToCollection` (`ActionRegistry.js:187`), bulk `photo.analyzeAI` (`:252`), `collection.duplicate` (`:309`), `collection.export` (`:339`) are TODO stubs with no backend call — candidate future endpoints, not current contract.
