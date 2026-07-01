# SnapVault — Product Spec & Greenfield Harvest Map

- Status: **Draft for review** (2026-06-26)
- Scope: `samples/S6.SnapVault`. This is the **spec-first** artifact for the greenfield backend rebuild: it says *what SnapVault is meant to offer*, realigns the surface around "fewer, more meaningful parts," and is the **harvest map** for porting the genuine domain out of the legacy backend.
- Companions: [snapvault-ui-api-contract.md](./snapvault-ui-api-contract.md) (the functional acceptance gate — what the SPA calls) · [snapvault-koan-modernization.md](./snapvault-koan-modernization.md) (the original in-place ADR, **superseded** by this greenfield approach; its strip/build/keep table is folded in below).
- Evidence: every claim here is cited to `file:line` in the current tree, gathered by a 6-agent understand-pass (workflow `wf_2024d47f-292`) and re-verified by hand on the load-bearing points (the verify-empirically discipline caught the critic itself being wrong about `Count.Fast`).
- Applies: `koan-design-principles` ("fewer but more meaningful parts"), `break-and-rebuild-preferred` (one clean core, delete the legacy at swap — *not* a second permanent impl), `koan-ergonomics-first`, `no-stopgaps`, `contributor-pipelines-never-bespoke`.

---

## 0. How to use this document

Two acceptance gates bound the rebuild. Nothing else is canon:

1. **The flagship isolation spec** — `tests/S6.SnapVault.Tests/SnapVaultTenancyFlagshipSpec.cs` (real `AddKoan()`, no Docker). It ports to the new entities verbatim and must stay green.
2. **The UI API contract** — [snapvault-ui-api-contract.md](./snapvault-ui-api-contract.md). The new backend must honor every endpoint/shape the SPA actually calls, *minus* the deliberate changes called out there (progress transport SignalR→SSE; optionally the media URLs).

The greenfield discipline: **port the domain verbatim, build the structure from scratch.** "From scratch" applies to the file/service/controller shape — never to re-deriving the hard-won domain algorithms in §3, which copy across.

---

## 1. What SnapVault is (product intent)

SnapVault is a **multi-tenant photo library for photography studios**. A studio uploads photos; SnapVault auto-organizes them into events (albums), runs AI vision analysis to caption + tag + extract structured facts per photo, makes the whole library searchable by natural language, and lets the studio curate ordered collections. Each studio's library is invisible to every other studio.

The core domain objects and the flows over them:

| Object | What it is |
|---|---|
| **Event** | An album (wedding, conference, or an auto-created per-day bucket). Groups photos. |
| **PhotoAsset** | The aggregate root — one stored original photo + EXIF + AI analysis + embedding + processing state. |
| **AnalysisStyle** | A photography-style profile (Portrait, Food, Gaming…) that retunes the AI vision prompt. Platform-shared reference data. |
| **AiAnalysis** | The structured result of analyzing a photo: summary, tags, a key→value *facts* map, and per-item lock state. Embedded on the photo. |
| **Collection** | A user-curated, ordered, many-to-many grouping of photos (drag-to-reorder). |

The user-facing flows:

1. **Upload & ingest** — drop a batch of files (or "auto-organize"); each photo is staged, EXIF-extracted, filed into an event (a per-day album if none chosen), AI-analyzed under a chosen or auto-detected style, embedded for search, and its progress streamed live.
2. **Browse** — a virtualized, sorted/filtered grid (all photos / favorites / an event / a collection), with a lightbox that shows the full image + the AI facts panel and supports keyboard navigation consistent with the current ordered set.
3. **Analyze & refine** — pick an analysis style; **reroll** the AI analysis while **locking** the summary or individual facts you like; lock-all / unlock-all.
4. **Search** — natural-language search with an alpha slider (lexical ↔ semantic), degrading to keyword search if the vector store is unavailable.
5. **Curate** — favorite / rate (0–5); add/remove photos to ordered collections; bulk favorite/delete; discovery groupings ("smart collections").
6. **Operate** — storage/tier stats, reindex, cache clear, metadata export, destructive wipe.

---

## 2. The realignment — "fewer, more meaningful parts"

The single biggest finding of the understand-pass is **directional**: the legacy backend's surface is far larger than what the product needs, because it grew endpoints the UI never calls and hand-rolled plumbing the framework now owns.

**Thesis: the new backend exposes only what the UI contract calls (plus the deliberate additions), built on Koan primitives. Everything else is dropped.**

Two reductions deliver almost all of "fewer, more meaningful parts":

### 2a. Drop the orphan surface
Backend routes with **zero UI callers** (critic re-verified by grepping all of `wwwroot/`):
- The **entire `AdminController`** (`/api/admin/embedding/*`, 6 routes) — backed by the to-be-dropped `EmbeddingMonitoringService`.
- `POST /api/Photos/search`, `GET /api/Photos/favorites`, `GET /api/Photos/range`, `GET /api/Photos/{id}/adjacent`, `PUT /api/Photos/{id}/favorite` — the UI reaches search/navigation through the **session window** (`POST /api/photosets/query`), not these.
- `GET /api/Events/timeline`, `/by-tier/{tier}`, `POST /api/Events/{id}/archive` — the Timeline UI renders from already-loaded `/api/events` data and makes no call (verified `timeline.js`).
- `GET /api/analysis-styles/system`, `/user`.
- The whole **`EntityController<T>` inherited surface** (`POST query`, `GET new`, `bulk` upsert/delete, `DELETE all`/`by-query`, `PATCH`) on Photos/Events/AnalysisStyles — present by inheritance, unused by the SPA.

> **DECIDED (2026-06-26):** drop **all** orphans **and** smart collections. The new backend exposes only the currently-UI-called endpoints — no Discovery panel, no orphan routes. Smallest meaningful surface.

### 2b. Replace the bespoke plumbing with Koan (the strip/build crosswalk)
Every replacement below was **re-verified present in `src/` today** unless noted.

| Bespoke (drop) | Koan replacement (verified in `src/`) |
|---|---|
| 4 derivative entity types (`PhotoGallery/Thumbnail/Masonry/Retina`) + 3 `*MediaId` FK fields + `MaterializeAsync` pre-gen + the `BaseType` reflection hack + bespoke `MediaController` | `[MediaRecipe("…")]` named transforms served on-demand by the framework `Koan.Media.Web` controller at `GET /media/{id}/{recipe}` (`src/Koan.Media.Abstractions/Recipes/MediaRecipeAttribute.cs:25`; `src/Koan.Media.Web/Controllers/MediaController.cs:98,103`). `Eager=true` on the hot grid recipe. Derivations are cached + lineage-stamped by the framework (MEDIA-0007). |
| 3-strategy `JObject` AI parser (`ParseAiResponse`/`TryParseJson`/`ExtractJsonByBalancedBraces`, ~155 lines) | Typed structured output. **Nuance:** `Client.Chat<T>` exists (`src/Koan.AI/Client.cs:126,147`) but takes `Prompt`/`variables` — there is **no typed + image overload**; the vision call stays untyped `Chat(prompt, ChatOptions{Image})` (`PhotoProcessingService.cs:644`) and the result is `Deserialize<AiAnalysis>`'d. The parser dies; the **normalization** (§3) must be re-kept by hand. |
| `ProcessingJob` batch-tracker + `UpdateJobProgress` hand-sync | The Koan.Jobs **ledger** as source of truth + `ctx.Progress(fraction,msg)` (`src/Koan.Jobs/JobContext.cs:62`, `JobRecord.cs:97-98`) behind a thin read-only `BatchStatus` facade. **Nuance:** `JobQuery` keys on `WorkType/WorkId/Status` — filtering by the domain `BatchJobId` is the facade's job over returned records. |
| `PhotoProcessingHub` (SignalR) + scattered `EmitProgress` | `Koan.Web.Sse` — an SSE endpoint streaming progress **from the ledger** via `SseResults.StreamJson<T>` (`src/Koan.Web.Sse/Results/SseResults.cs:25`). Progress reported once, durably, via `ctx.Progress`. The `PhotoProgressEvent`/`JobCompletionEvent` payload shapes are kept as SSE payloads. |
| `PhotoSetService` page-math (double-pagination, in-memory `Skip/Take`) + `BuildSortExpression` + `GetPhotoRange`/`GetStats` + `BulkDelete`/`BulkFavorite` N+1 loops | `EntityController<T>` `POST /query`, `QueryDefinition.WithSort/WithPagination` + `QueryWithCount`/`Page` (`src/Koan.Web/Endpoints/EntityEndpointService.cs:1011`), `Count.Fast`/`Count.Exact` (`src/Koan.Data.Core/Model/Entity.cs:154` — **present**, the critic was wrong), batch `Get(ids)` + `list.Save()` + `RemoveAll(RemoveStrategy.Fast)` (`src/Koan.Data.Abstractions/RemoveStrategy.cs`). |
| In-memory `eventId` post-filter + per-match reload loop in `SemanticSearch` | `Filter.Eq("eventId", …)` **push-down** to `Vector<PhotoAsset>.Search(filter:)` + `eventId`/tags as metadata on `SaveWithVector` (`src/Koan.Data.Vector/Vector.cs:67,216`). **Nuance:** `ScopedVectorRepository` fail-closes with `VectorFilterUnsupportedException` if the elected adapter lacks `VectorCaps.Filters`; the kept keyword fallback covers no-vector, not capable-but-no-filter. |
| `EmbeddingMonitoringService` (~240 lines) | `EmbeddingTelemetry` (Meter API, `src/Koan.Data.AI/Telemetry/EmbeddingTelemetry.cs:23`) + a thin `IHealthContributor`. **Nuance:** the framework ships `EmbeddingHealthCheck : IHealthCheck`, not an embedding-flavored `IHealthContributor` — the sample authors a small one. |
| `EntityLifecycleConfiguration` static cascade + raw-string FK nav + the `Program.cs` seeding/config/registration ceremony | `[Parent(typeof(Event))]` / `[Parent(typeof(AnalysisStyle))]` + `GetRelatives`; cascade via `BeforeRemove` **lifecycle events** (already the mechanism in use); a `SnapVaultModule : KoanModule` owning seeding/config/registration → `Program.cs ≈ AddKoan().AsWebApi()`. |

**Net shape:** ~9 entity types → ~6 (drop the 4 derivatives + legacy `ProcessingJob`); ~6 plumbing DTOs gone; the reflection hack / JObject parser / SignalR hub / page-math / monitor service all gone. The hand-written code that remains is the §3 domain core — all of it genuinely SnapVault's product.

---

## 3. The kept domain core (port **verbatim** — the crown jewels)

These are the "meaningful parts." Each ports as-is; the **subtleties** are hard-won correctness a naive rewrite silently loses. Two cross-cutting invariants bind several of them:

> **INV-1 (lowercase facts everywhere):** every AI fact key is stored lowercased (parse-time `PhotoProcessingService.cs:495`, toggle-time `PhotosController.cs:563`). Reroll-with-holds, lock-all/unlock-all, and the facts UI all assume direct lowercase dictionary access.
>
> **INV-2 (UTC discipline):** EXIF capture date is force-stamped `DateTimeKind.Utc`; the daily-event lookup uses a **half-open UTC day range** (`>= dayStart && < dayStart.AddDays(1)`), *never* `.Date ==` (which makes the Mongo translator emit `$dateTrunc`, unsupported on older servers). Both are portability fixes, not incidental.

| Capability | Source | Port-verbatim subtlety |
|---|---|---|
| **EXIF extraction** (camera/lens/exposure, capture-date, GPS DMS→decimal) | `PhotoProcessingService.cs:343-416` | ISO arrives as `ushort[]` (read `[0]` after length guard); GPS lat/lon are `Rational[3]` folded `deg + min/60 + sec/3600` (length==3 guard else 0); `DateTimeOriginal` is unspecified-kind → force `SpecifyKind(Utc)` (INV-2); EXIF failure is swallowed (a no-EXIF photo still ingests); the staging stream must be buffered seekable (re-read from position 0 for dimensions, EXIF, full-res upload). |
| **The 15 analysis styles** (style library + prompt parameters) | `Initialization/AnalysisStyleSeeder.cs:57-618` | The `FocusInstructions` prose + the exact field-key triples (Mandatory/Emphasis/Deemphasized) **are** the tuned product and encode the cross-style disambiguation rules. `EmphasisFields` strings like `"composition details"` are matched **verbatim** by `GetEnhancedExamples` (`AnalysisPromptFactory.cs:212-235`). Restore idempotency via `TemplateVersion`-aware upsert (the existence guard is currently commented out), not blind skip. |
| **Smart two-stage classification** (classify→analyze, cached) | `PhotoProcessingService.cs:718-782`, branch `:627-633` | Candidate set filtered to `!IsSmartStyle && IsActive && IsSystemStyle` (smart never classifies to itself; user styles excluded); fuzzy ordered match (`Name.Contains` OR `Id ==`, fallback lowest-Priority); result cached on `InferredStyleId`, honored only if the cached style is still active and not smart. Dropping the cache doubles vision calls. |
| **Reroll-with-holds** (lock summary/facts, regenerate the rest) | `PhotoProcessingService.cs:897-1000`; locks on `AiAnalysis.cs:28-29,43-44` | Ordering is load-bearing: **buffer** locked summary+facts *before* `GenerateDetailedDescription` (it wholesale-replaces `AiAnalysis`); after regen, restore Summary+SummaryLocked, write each buffered fact back into the **new** Facts dict, rebuild `LockedFactKeys`. Depends entirely on INV-1. |
| **Hybrid/alpha semantic search + keyword fallback** | `PhotoProcessingService.cs:275-341`; embedding text `AiAnalysis.cs:69-92` | `alpha` is the semantic↔lexical dial (do not hardcode); two independent fallbacks (a `Vector.IsAvailable` pre-check + a catch-all) → `FallbackKeywordSearch` over `OriginalFileName`+`AutoTags`+`MoodDescription`; `ToEmbeddingText` format (tags, summary, fact values joined `, `) determines what search matches. **Adapt:** push the `eventId` filter down (§2b) instead of post-filtering after topK. |
| **Daily-auto-event from EXIF** | `PhotoProcessingService.cs:788-828`, invoked `:102-109` | Get-or-create by **half-open UTC day range** (INV-2, the `$dateTrunc` trap); album name `"MMMM d, yyyy"`; `CapturedAt ?? UploadedAt` fallback files EXIF-less photos sensibly. |
| ~~**Smart collections**~~ **(DROPPED — D2)** | `PhotosController.cs:800-945` | **Not ported.** Per D2 the Discovery panel is dropped (UI never called it). Bucket predicates preserved here only as historical reference if it's ever revived. |
| **Index navigation** (position-in-context for the lightbox) | `PhotosController.cs:57-180` + shared `BuildPhotoQuery :186-259` | The UI calls only `GET /api/photos/{id}/index` (contract #4) + the session window (#5) — the standalone `/adjacent` and `/range` endpoints are **dropped** (orphans, D2). INV-3 still holds: the index lookup and the session window **must derive from the same ordered list** or navigation desyncs. Search context = relevance order un-resorted; collection context = curated `PhotoIds` order, sort params ignored. **Adapt** into a service over `QueryWithCount`/`Page`; the `BuildPhotoQuery` context dispatch survives. |
| **Collection ordered membership** (curated many-to-many) | `Collection.cs:13-85`; `CollectionsController.cs:261-374` | `PhotoIds` index == display position (reorder = RemoveAt+Insert). Order-preserving load: iterate `PhotoIds`, pull from a dict, **drop nulls** (deleted-but-referenced). `AddPhotos` verifies existence, dedups, enforces `MaxPhotosPerCollection` counting only genuinely-new ids. Remove/Delete never delete photos. Dead-id cleanup on photo-deletion moves to a `BeforeRemove` lifecycle event. |
| **AI vision orchestration** (`GenerateDetailedDescription` + the prompt factory) | `PhotoProcessingService.cs:582-679`; `AnalysisPromptFactory.cs:48-235` + `FactFieldDefinition.cs` | The prompt-factory engine ports verbatim: `FullPromptOverride` escape hatch; base-mandatory + style-promoted fields with `GetEnhancedExamples` swaps and trailing-comma trim; commented optional example lines are deliberate model hints; `SubstituteVariables` (`{{width}}/{{height}}/{{aspectRatio:F2}}/{{camera}}/{{orientation}}` via aspect thresholds). Style resolution priority: explicit→last-used→base, each must be active. Uses the **gallery** derivative for the vision call. Whole method non-fatal (a failed analysis must not fail ingest). `[MediaAnalysis]` is a **non-goal** — the factory is too rich for the attribute. |
| **JSON-response normalization** (the part that survives the parser's deletion) | `PhotoProcessingService.cs:421-576` | Even though `Client.Chat<T>` obviates the *parsing*, keep: tags trimmed/empty-dropped/deduped case-insensitively; **every fact key lowercased** (INV-1); fact arrays trimmed/deduped/joined `, ` into one string; total-failure yields `AiAnalysis.CreateError(...)` (tags=`["error"]`), never null/throw. |

**Already-modern, keep as-is (don't re-port from scratch):** `PhotoProcessingJob` (tenant-carrying `[JobAction]` Ingest/Reanalyze, ARCH-0100 carrier), `UploadStaging` (`StorageEntity<T>` staged blob), the `[Embedding(Policy=AllStrings, Async=true, Exclude=[EventId,InferredStyleId])]` + `float[]` vector wiring on `PhotoAsset`, and the Phase-0 tenancy model.

---

## 4. The entity model (target)

**Keep (domain core, 6 stored + 2 embedded):** `PhotoAsset` (`MediaEntity<T>`, the single stored original), `Event`, `Collection`, `AnalysisStyle` (`[HostScoped]`), `PhotoSetSession` (volatile browsing cursor); `PhotoProcessingJob` (`IKoanJob<T>`), `UploadStaging` (`StorageEntity<T>`); embedded value objects `AiAnalysis`, `GpsCoordinates`.

**Add relationship metadata:** `[Parent(typeof(Event))]` on `PhotoAsset.EventId`, `[Parent(typeof(AnalysisStyle))]` on `PhotoAsset.InferredStyleId` (additive → `GetRelatives`; no save-time FK enforcement, verified).

**Drop (plumbing):** `PhotoGallery`, `PhotoThumbnail`, `PhotoMasonryThumbnail`, `PhotoRetinaThumbnail` (→ recipes); the 3 `*MediaId` FK fields on `PhotoAsset`; `ProcessingJob` (legacy batch tracker — **naming trap:** distinct from `PhotoProcessingJob`); `DetailedDescription` (legacy free-text, superseded by `AiAnalysis`); and the DTO cluster `PhotoIndexResponse`/`PhotoRangeResponse`/`PhotoMetadata`/`PhotoStats`/`PhotoSetQueryRequest`/`PhotoSetQueryResponse`.

**Merge:** `PhotoSetDefinition` (wire DTO) is a byte-twin of `PhotoSetSession`'s query fields — collapse to one shape.

**Tenancy posture:** exactly one `[HostScoped]` entity (`AnalysisStyle`, platform-shared). Everything else carries no tenant field and is auto-isolated by the invisible `__koan_tenant` discriminator. (Known Phase-1 limitation, documented on `AnalysisStyle.cs:12-21`: because the *whole* entity is host-scoped, user-created custom styles are also platform-visible — a later phase splits the system seed from per-tenant styles.)

---

## 5. Tenancy — the on-ramp (the key finding for "add tenant affordances")

The understand-pass confirmed, by hand: **Koan ships the isolation *mechanism* but not the request→tenant *binding*.**
- `ITenantResolver` is a **seam only** — its own doc says *"Concrete resolvers land in a later slice"* (`src/Koan.Tenancy/ITenantResolver.cs:6`). **No concrete resolver and no per-request middleware ship** (the only reference is the boot pre-flight `services.GetServices<ITenantResolver>().Any()`).
- **Dev (Open posture):** a dev tenant auto-seeds; an unset ambient scope falls back to it — "no day-one 403." So the app today runs single-tenant-by-default.
- **Production (Closed):** tenancy active with no resolver **refuses to boot** (fail-fast).
- The flagship spec drives tenants **programmatically** (`Tenant.Use(StudioA)`); the SPA sends **no tenant carrier** (`api.js` adds no header/cookie/param; no studio-picker UI).

**Implication:** a UI studio-picker needs a backend that turns a request into an ambient tenant. See Decision **D1**.

> **UPDATE (2026-06-27): the request→tenant binding now SHIPS in the framework — this section's "no concrete resolver / no middleware" finding is superseded.** SEC-0007 P4 added **`Koan.Identity.Tenancy`**: the four tenant-resolution carriers (claim / header `X-Koan-Tenant` / subdomain `{code}.host` / path `/t/{code}`, the latter two resolving a `TenantRecord.Code`) + an `AfterAuthentication` `IKoanWebPipelineContributor` middleware that **membership-authorizes** the candidate against `Membership` and wraps the request in `Tenant.Use(...)` (and projects `Membership.Roles`). So the rebuild does **not** build a sample-side resolver — it **references `Koan.Identity` + `Koan.Identity.Tenancy`**, and the studio-picker just sets a carrier (a header, or a `/t/{code}` path). Building it this way also makes the rebuild the **SEC-0007 P5 identity dogfood**.

---

## 6. Cross-cutting surfaces (uncovered by the maps; needed for the rebuild)

- **Data provider:** Mongo (`appsettings.json` → `Koan:Data:Mongo:Database = SnapVault`). The flagship test uses `inmemory`. INV-2's `$dateTrunc` avoidance matters because prod is Mongo.
- **Storage profiles:** `cold`=photos, `warm`=gallery, `hot-cdn`=thumbnails — these map 1:1 to the derivative entities being dropped. When `[MediaRecipe]` replaces them, the profile config collapses to the original (`cold`) + the recipe cache; **revisit on the media phase.**
- **Dead config:** `Koan:Ai:AnalysisStyles` (7 styles, `promptTemplate`/`systemContext` shape) in `appsettings.json` is **read by no code** — the 15 styles are hard-coded in `AnalysisStyleSeeder.cs` with a different shape (`FocusInstructions` + field triples). **Delete it** (the seeder is the source of truth).
- **Auth:** none. No `UseAuthentication`/`UseAuthorization`, no `[Authorize]`. Fully anonymous → ambient tenant resolves to the dev default. Ties into D1.
- **CORS:** `AllowAnyOrigin + AnyMethod + AnyHeader` with a "credentials for SignalR" comment that is actually incompatible with `AllowAnyOrigin`. Revisit for the SSE migration.
- **Static hosting:** `UseStaticFiles` + `MapFallbackToFile("index.html")` serves the SPA; `window.open()` downloads/exports depend on these resolving server-side; `DownloadPhoto` 302s to `/storage/{Key}`. Preserve.
- **ZenGarden:** Reference = Intent (auto-registrar calls `AddKoanZenGarden()` binding config from DI). The explicit `AddKoanZenGarden(builder.Configuration)` in `Program.cs` is **redundant** and goes. The model-advisor diagnostic logging moves into `SnapVaultModule.Start`.

---

## 7. Open decisions (the forks — recommendations inline)

- **D1 — Tenant on-ramp. DELIVERED in the framework (2026-06-27, SEC-0007 P4 — `Koan.Identity.Tenancy`).** The first-class tenant-resolution module envisioned here was built as part of SEC-0007: composable carriers (claim / header / subdomain `{code}.host` / path `/t/{code}`) implementing the ARCH-0099 `ITenantResolver` seam + an `AfterAuthentication` middleware that wraps each request in `Tenant.Use(...)`, **membership-authorizes** the candidate against `Membership`, projects `Membership.Roles`, and honors the dev-open/prod-closed posture. **The rebuild consumes it** (reference `Koan.Identity` + `Koan.Identity.Tenancy`) — no sample-side resolver. The UI studio-picker sets a carrier (recommend the `X-Koan-Tenant` header or a `/t/{code}` path). This makes SnapVault the SEC-0007 P5 dogfood of the durable person + membership model. *(Deferred framework piece, not blocking: DNS-verified custom-domain routing.)*
- **D2 — Surface policy. DECIDED (2026-06-26): drop ALL orphans AND smart collections.** The new backend exposes only the currently-UI-called endpoints (the contract doc). Gone: the whole `AdminController`; `/Photos/search`·`/favorites`·`/range`·`/adjacent`·`PUT favorite`; Events `timeline`·`by-tier`·`archive`; styles `system`·`user`; the smart-collections Discovery endpoint; and the unused inherited `EntityController<T>` verbs. Smallest meaningful surface.
- **D3 — Media URLs.** The UI calls `/api/media/photos/{id}/{gallery|original}` + `/api/media/{masonry|retina}-thumbnails/{id}`; the framework recipe controller serves `/media/{id}/{recipe}`. **Recommended:** update the 4 UI call sites (`grid.js`, `lightbox.js`, `ImagePreloader.js`) to the recipe URLs — a small, contained UI edit bundled with the tenant-affordance work — rather than maintaining a compatibility alias.
- **D4 — Progress transport.** SignalR → SSE (already confirmed). The UI's `processMonitor.js` moves from a SignalR hub to an `EventSource` on a per-batch stream endpoint; payload shapes (`PhotoProgressEvent`/`JobCompletionEvent`) are preserved.
- **D5 — Error/pagination contract.** Standardize on one error envelope (the legacy uses `{Error}`/`{Message}`/bare inconsistently) and back the UI's two list shapes (bare array + `X-Total-Count`; the session envelope) with `EntityController` query + the session endpoint. Honor the consumed shapes (see the contract doc) exactly.

---

## 8. Build shape (greenfield, always-green)

The legacy backend stays runnable as the reference until the new surface is coherent, then is deleted in one swap. Suggested sequence (each step compiles green; the flagship spec is the gate; TDD where it pays):

1. **Skeleton** — `SnapVaultModule : KoanModule`, the kept entities (with `[Parent]`), `Program.cs ≈ AddKoan().AsWebApi()`, config trimmed (delete dead styles config), the flagship spec pointed at the new entities.
2. **Tenancy on-ramp** (D1) — reference `Koan.Identity` + `Koan.Identity.Tenancy` (the framework's carriers + the `AfterAuthentication` membership-authorizing middleware ship it, SEC-0007 P4); add the UI studio-picker that sets a carrier (the `X-Koan-Tenant` header or a `/t/{code}` path). No sample-side resolver. This is also the SEC-0007 P5 identity dogfood.
3. **Media recipes** (D3) — `[MediaRecipe]` set + `IMediaSource`; UI media URLs repointed; derivative types deleted.
4. **Jobs/progress** (D4) — ledger + `ctx.Progress`; SSE stream; SignalR + hub deleted.
5. **Domain services** — port §3 verbatim into thin services (ingest pipeline, search, navigation, collections); the UI-faced endpoints only. (Smart-collections dropped per D2.)
6. **AI & vector cleanup** — typed-ish parse + normalization; vector `eventId` push-down; `EmbeddingTelemetry` + a thin health contributor; monitor service deleted. **Media coupling (from the step-3 media rebuild):** the legacy vision call read the *gallery derivative entity* (`PhotoGallery.Get(GalleryMediaId).OpenRead()`), which is now gone — re-source the vision bytes by rendering the `gallery` recipe in-process (`original.OpenRead().AsMedia().Apply(PhotoRecipes.Gallery()).WriteToAsync(buf)`; the pipeline API is public, already used by the legacy code) rather than feeding the full-res original (which defeats the 1200px downscale the gallery recipe exists to provide).
7. **Swap & measure** — delete the legacy backend; flagship green (+ re-home the parked blob leg); record LOC / entity-count / removed-package reduction.

---

## Acceptance criteria

1. Backend **builds green**; in `Koan.sln`; CI guards it.
2. **Flagship tenancy spec green** (record + job + vector + blob + `[HostScoped]`), real `AddKoan()`, no Docker.
3. **UI works against the new backend** — every endpoint/realtime in the contract doc honored (minus the D3/D4 deliberate changes, for which the UI gets matching affordances).
4. The hand-written code that remains is exactly the §3 domain core + the §5 tenant on-ramp.
5. `Program.cs ≈ AddKoan().AsWebApi()` + `SnapVaultModule`; no derivative entity types, reflection hack, bespoke queue/hub/JSON-parser/page-math/monitor service.
6. A measured before/after recorded as a follow-up.

---

## 9. The studio↔client lifecycle (scope expansion — 2026-06-27)

After the skeleton (step 1) landed green, a delight pass ([snapvault-delight-research.md](./snapvault-delight-research.md)) + a user directive reframed SnapVault from a studio-only photo CRUD into a **studio↔client lifecycle**. This section records the consequences for the build shape. It is an *expansion* of §2–§8, not a replacement — the harvest plan still holds; this adds the client/guest surface on top.

**User decisions (2026-06-27):** (a) build the lifecycle **as the spine now** (fold into steps 2 & 5, not a later follow-on); (b) the invited-guest v1 scope is a **proofing gallery** (favorite/rate/select picks + optional comments; the studio sees the selections); (c) persist the delight research (done — the companion doc).

### 9.1 The flagship arc (lead with this)
> **Invite** a client to their event set → they accept into a **durable, portable identity** (invite-binds-to-identity, no email-merge takeover) → **fail-closed scoped access to only that set** (the proofing gallery) → at engagement end, **atomic deprovision + a signed erasure certificate**.

This is the SEC-0007 P5 identity dogfood made visible. Five differentiators, priority order: ① invite-gated proofing galleries ② verifiable client-erasure certificate ③ per-client isolation as structural impossibility ④ honest hybrid search (vector+lexical+OCR+floor) ⑤ reroll-with-locks as explainable AI.

### 9.2 Personas (new third persona)
- **Studio operator / staff** — the existing tenant member; uploads, organizes, full library.
- **Invited client/guest** — a durable *person* granted scoped read+proof access to **one set**, never the studio's library. NOT a tenant member.

### 9.3 Architecture: guest access = a capability grant, NOT a second tenant axis
The market-research synthesis suggested "per-client sub-isolation" as a nested tenant axis. **Rejected** — a client doesn't *own* a library; the studio does and grants a view. Correct, cheaper, on-moat shape:
- **Studio = tenant** — the shipped fail-closed isolation (the invisible `__koan_tenant` discriminator). Unchanged.
- **Guest = identity + a grant to a set** — SEC-0004 **gate·constrain·project**: the grant *is* the read-path filter, fail-closed; a guest's queries are constrained to their granted set, cross-set/cross-client get-by-id returns null by construction. No new framework axis; rides shipped seams (`Koan.Identity` + `Koan.Identity.Tenancy` + the SEC-0004 floor).

### 9.4 Entity additions (sketch — detailed shape is the step-2/5 seam-map's job)
- **Shareable set** — reuse `Collection` (curated ordered) and/or `Event` as the unit a studio shares; add a lightweight "shared" marker + the grant binding rather than a new container type.
- **Gallery invite** — email → set → guest role; **binds to the canonical person on accept** (reuse the `Koan.Identity` invite-binds-to-identity primitive; do NOT hand-roll). Pending-until-accepted.
- **Guest access grant** — identity → set → permissions `{view, download?, select}`. Realized via the SEC-0004 grant model (AgentGrant-style, resource = the set), enforced on the read path.
- **Proof selection** — `(guestIdentityId, setId, photoId)` with pick/rating + optional comment; the studio reads the aggregated client selections. Guest favorites/picks are attributed to the **guest**, distinct from the studio's own `IsFavorite`/`Rating` on `PhotoAsset`.

> Port-verbatim invariants INV-1/INV-2 are unaffected. New entities are GUIDv7 `Entity<T>`; the grant + selection writes are tenant-carried (the studio tenant) and guest-attributed.

### 9.5 Build-step consequences
- **Step 2 (identity/tenancy on-ramp) — expanded.** In addition to the studio carrier (`X-Koan-Tenant` / `/t/{code}`): reference `Koan.Identity` + `Koan.Identity.Tenancy`; wire **gallery invite → accept → identity-bound guest grant**; wire **atomic deprovision + erasure certificate** ("delete client & prove it"). The flagship spec gains a leg: a guest sees only their set; a cross-set read fails closed; deprovision purges set photos + derivatives + embeddings + facts + the grant and emits the certificate.
- **Step 5 (domain services) — expanded.** Add the **guest-scoped proofing endpoints** (list my set, view photo, favorite/rate/select, comment) constrained by the grant, and the **studio-side "client selections" view**. Reroll-with-locks gains the explainable reason + sub-scores surface here.
- **Step 6 (AI/vector) — unchanged scope**, plus the OCR lexical lane + relevance-floor honest empty state for hybrid search.
- **NEW: guest UI.** The existing SPA has no guest/share surface; a minimal guest gallery view (+ a studio share/invite affordance + a selections view) is net-new frontend. The §6 "SPA rewrite out of scope" clause is **lifted for the guest surface only** (the studio SPA is still harvested, not rewritten).

### 9.6 Acceptance additions
7. **Lifecycle flagship green** — invite→accept→scoped guest access; cross-set read fails closed; deprovision purges + certifies (real `AddKoan()`, no Docker).
8. **Proofing works end-to-end** — an invited guest selects picks; the studio sees them; the guest never sees another set.
9. **No bespoke axis logic** — guest isolation rides the SEC-0004 grant + read-path predicate + the shipped tenant isolation; the invite rides `Koan.Identity` invite-binds-to-identity. (Contributor-pipelines-never-bespoke.)

### 9.7 Step-5 enforcement tripwires (from the step-2 adversarial review)
The step-2 review confirmed step 2 **sound** (one fix-now hole — erasure now revokes pending invites — closed + proven by `SnapVaultGuestLifecycleSpec.Erasure_revokes_pending_invites`; no fail-open exists; multi-studio-guest is correct-by-construction as the tenant + access axes AND-fold). These obligations MUST land **with** the step-5 guest HTTP surface (they are unreachable until a guest caller exists — the flagship enters the guest `Subject` server-side the same way the real middleware will):
- **Guest proofing WRITE floor** — when the guest proofing endpoint lands, `ProofingService.SetSelectionAsync` gains real authz: check `GalleryGrant.Allows("view"/"select"/"comment")`; derive `eventId`/`studioTenantId` FROM the grant/photo (never trust the caller); load the photo under the guest's own `Subject` so a cross-set `photoId` returns null → reject; honor `GalleryInvite.GuestRole` as the grant's permission source (today the grant hardcodes the widest set — inert while unenforced); clamp `Rating` 0–5. Add negative spec legs (each must fail pre-fix). This is the recurring "granted-but-not-enforced" defect, safe only because the WRITE path has no guest caller yet.
- **Subject discipline** — the `AfterAuthentication` guest-scope middleware must DENY (not fall through) on a scope-resolution throw; derive the request tenant server-side from the grant's `StudioTenantId` (never a client hint); a boot/test guard must catch any `PhotoAsset` path reaching the store subject-less (fail-closed-but-silent = an operator/job sees nothing); verify the ARCH-0100 carrier flows the guest `Subject` across the job hop (a guest-triggered job stays event-scoped).
- **The READ floor is already enforced** (SEC-0008 `[AccessScoped]` fires on a raw `Entity.Query`, incl. the vector-search chokepoint via `ReadScopeFold` — so a guest's semantic search is event-scoped for free); only the WRITE path + the middleware wiring are step-5.
