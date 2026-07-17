# SnapVault → Koan modernization — the break-and-rebuild ADR

> **⚠️ Approach superseded (2026-06-26):** the *in-place 6-phase* rebuild below is replaced by a **greenfield harvest** — build a clean Koan-native backend, port the genuine domain verbatim, keep the SPA (add tenant affordances), and delete the legacy backend in one swap once green. The reason, grounded in the understand-pass (workflow `wf_2024d47f-292`): the strip-list dwarfs the keep-list, so in-place mutation pays to carefully un-wire code that's being deleted anyway, and a clean build lets "fewer, more meaningful parts" be a *design* decision rather than a subtraction (and stays green throughout — no red window). **The STRIP/BUILD/KEEP table and the kept-domain list below remain accurate and are the harvest map.** The authoritative plan is now [snapvault-product-spec.md](./snapvault-product-spec.md) + [snapvault-ui-api-contract.md](./snapvault-ui-api-contract.md).

- Status: **Superseded in approach** (Proposed 2026-06-26; greenfield pivot 2026-06-26)
- Scope: `samples/applications/SnapVault` (the tenancy/AI/vector/media dogfood). Framework-side: **no changes** — this is a sample adopting capabilities the framework already ships.
- Companion to: [snapvault-conversion-plan.md](./snapvault-conversion-plan.md) (the *tenancy* roadmap). This ADR is the *Koan-adoption* axis; the two share the Phase-1 tenancy foundation.
- Applies: `break-and-rebuild-preferred`, `koan-ergonomics-first`, `contributor-pipelines-never-bespoke`, `no-stopgaps`. Design lens: `koan-design-principles` — "fewer but more meaningful parts".

## Context

SnapVault was authored on a much earlier framework version. A 7-area inventory (workflow `wf_694385e3-6ac`, each finding's Koan capability **verified present in the current tree**) found that the sample hand-rolls a large amount of code that Koan now provides declaratively: four pre-generated media-derivative entity types, a reflection hack, a multi-strategy JSON parser, a bespoke SignalR hub, a parallel job-batch tracker that duplicates the Koan.Jobs ledger, manual pagination math, raw-string foreign keys with hand-written navigation, ad-hoc boot seeding, and a hand-rolled embedding monitor.

This rot was also masking real defects: the sample **did not even compile** on `dev` (an ImageSharp restore pin failed before the compiler ran), an un-awaited `PhotoAsset.Get(id)` (the `StorageEntity` sync-proxy shadow) broke `DownloadPhoto`, and the sample was **not in `Koan.sln`** so CI never built it.

The user's directive: a **full break-and-rebuild** that strips the bespoke code in favor of Koan capabilities, optimizing for **simplicity, maximal reliance on Koan, and "less but more meaningful parts."** The sample may go **red** during the rebuild; it is reassembled to green at the end. No effort is spent preserving code that the rebuild strips (e.g. the maintenance-era `MaterializeAsync` derivative pipeline is superseded by recipes).

## Principles (how every decision below was made)

1. **Declare, don't orchestrate.** Prefer an attribute/recipe the framework executes over hand-written orchestration.
2. **Delete, don't wrap.** If Koan owns the capability, remove the bespoke version rather than adapt it.
3. **Keep only true domain logic.** The "meaningful parts" are SnapVault's product: photography styles, reroll-with-holds, smart collections, EXIF, the search UX. Everything else is framework plumbing and goes.
4. **Reference = Intent.** Capabilities arrive by referencing the package; no manual ceremony in `Program.cs`.
5. **The flagship stays the proof.** `SnapVaultTenancyFlagshipSpec` (real `AddKoan()`, no Docker) is green at the end; it is the acceptance gate for the rebuild.

## Decision — the target architecture

### Foundation (Phase-0, already in flight on this branch)
Multi-tenancy (`Koan.Tenancy`, `[HostScoped] AnalysisStyle`, invisible `__koan_tenant`), the in-memory worker → `Koan.Jobs` (`PhotoProcessingJob`, tenant-carried, ARCH-0100), and the flagship spec. **Kept.** The bit-rot maintenance got the sample compiling again; its media-pipeline rewrite is intentionally superseded below.

### Area targets — strip / build / keep

| Area | STRIP (bespoke) | BUILD (Koan) | KEEP (domain) |
|---|---|---|---|
| **Media derivatives & serving** | `PhotoGallery`/`PhotoThumbnail`/`PhotoMasonryThumbnail`/`PhotoRetinaThumbnail` entity types · the 4 `*MediaId` FK fields on `PhotoAsset` · the `MaterializeAsync` pre-gen block · the `BaseType` reflection hack · the bespoke `MediaController` · the `DownloadPhoto` storage-redirect | `[MediaRecipe("gallery"/"masonry"/"retina")]` static methods + one `IMediaSource` over `PhotoAsset`'s stored original → served on demand by the framework controller at `/media/{id}/{recipe}` with lineage-stamped derivative persistence. | `PhotoAsset` as the **single** `MediaEntity` holding the original; EXIF metadata. |
| **AI vision & analysis** | the 3-strategy JObject parser (`ParseAiResponse`/`TryParseJson`/`ExtractJsonByBalancedBraces`, ~150 lines) | `Koan.AI.Client.Chat<AiAnalysis>(prompt, ChatOptions{Image})` typed structured output → `AiAnalysis` direct | `AnalysisPromptFactory` + the 15 styles + smart two-stage classification + **reroll-with-holds** — orchestrated in the job (the prompt factory is too rich for `[MediaAnalysis]`; that attribute stays a non-goal) |
| **Embedding & vector search** | the in-memory `eventId` post-filter + per-match reload-then-filter loop in `SemanticSearch` | `Filter.Eq("eventId", …)` **push-down** to `Vector<PhotoAsset>.Search(filter:)` + `eventId`/tags as **metadata on `SaveWithVector`** (the [Embedding] save passes a metadata dict) | `[Embedding(Async=true)]` (already Koan) · the alpha hybrid UX · the `IsAvailable` keyword fallback policy |
| **Background jobs & tracking** | the `ProcessingJob` batch-tracker entity + `UpdateJobProgress` hand-sync (duplicates the ledger) | the Koan.Jobs **ledger** as the source of truth: `PhotoProcessingJob.Jobs.WithStatus/Query` by `BatchJobId` behind a thin read-only `BatchStatus` facade · per-photo `ctx.Progress(fraction,msg)` (`JobRecord.ProgressFraction/Message`) · optional `JobMetric` throughput | `PhotoProcessingJob` (tenant-carrying, `[JobAction]` Ingest/Reanalyze) |
| **Real-time progress** | `PhotoProcessingHub` (SignalR) + hub group mgmt + the ~dozen scattered `EmitProgress`/`SendAsync` calls + the `Microsoft.AspNetCore.SignalR.Client` ref | `Koan.Web.Sse` — an SSE endpoint (`/api/photos/jobs/{batchId}/stream`) that streams progress **from the ledger** (`SseResults.StreamJson<T>`). Progress is reported once, durably, via `ctx.Progress` in the job; the stream surfaces it. | the `PhotoProgressEvent`/`JobCompletionEvent` payload *shapes* (now SSE payloads) |
| **Web surface & queries** | `BuildPhotoQuery` context-switch · `BuildSortExpression` · `GetPhotoRange`/`GetStats` · `PhotoSetService` manual page-math + in-memory `Skip/Take` + double-pagination · the `BulkDelete`/`BulkFavorite` N+1 loops | `EntityController<PhotoAsset>` `POST /query` (filter+sort+page) · `QueryDefinition.WithSort/WithPagination` + `QueryWithCount`/`Page` (single round-trip) · batch `Get(ids)` + `list.Save()` + `RemoveAll(RemoveStrategy.Fast)` | smart-collections · adjacent/index navigation · the search context · `Collection.PhotoIds` ordered membership — **thinned into services**, not deleted |
| **Lifecycle, relationships, seeding, observability** | `EntityLifecycleConfiguration` cascade plumbing · raw-string FK navigation · the `Program.cs` seeding/config/registration calls · `EmbeddingMonitoringService` (~200 lines) | `[Parent(typeof(Event))]`/`[Parent(typeof(AnalysisStyle))]` + scalar/set/stream `Relatives` nav · cascade via **lifecycle events** (`BeforeRemove`) · a **`SnapVaultModule : KoanModule`** (`Register`/`Start`) owning seeding+config/registration → `Program.cs` ≈ `AddKoan().AsWebApi()` · `EmbeddingTelemetry` (Meter API) + `IHealthContributor` | the `AnalysisStyleSeeder` factory (the 15 style definitions are domain); `AnalysisStyle` + `[HostScoped]` |

### The kept domain core — the "meaningful parts"
`PhotoAsset` (one media entity) · `Event` · `Collection` · `AnalysisStyle` · `AiAnalysis` · `PhotoSetSession` · EXIF extraction · `AnalysisPromptFactory` + 15 styles + smart classification + reroll-with-holds · the semantic-search UX (alpha) · smart-collections · adjacent/index navigation · the tenant-carrying `PhotoProcessingJob` · the Phase-1 multi-tenancy. **This is the only hand-rolled code that should remain, and all of it is genuinely SnapVault's product.**

## Migration phases (break-and-rebuild — red is allowed mid-flight)

Each phase: TDD where it pays, adversarial multi-lens review of the diff, and the **flagship tenancy spec stays the green gate at the end**.

- **Phase 0 — Foundation** *(in flight)*: tenancy + worker→Jobs + flagship spec. Compiling. *(The maintenance media rewrite is throwaway — Phase 2 replaces it.)*
- **Phase 1 — Bootstrap & relationships** *(low risk, sets the skeleton)*: `SnapVaultModule : KoanModule` (move seeding/config/registration off `Program.cs`); `[Parent]` on `EventId`/`InferredStyleId`; cascade deletes → lifecycle events; `Program.cs` → minimal.
- **Phase 2 — Media recipes** *(flagship; goes red)*: delete the 4 derivative types + the FK fields + the reflection hack + the pre-gen block + the bespoke `MediaController`; add `SnapVaultRecipes` + `IMediaSource`; rewire serving to `/media/{id}/{recipe}`.
- **Phase 3 — Jobs, progress & real-time**: delete `ProcessingJob` → ledger queries + `ctx.Progress`; SignalR hub → `Koan.Web.Sse` stream.
- **Phase 4 — Web/query consolidation**: `EntityController` `POST /query`; `QueryWithCount`/`Page`; batch ops; thin the domain endpoints into services; delete `GetPhotoRange`/`GetStats`/`BuildSortExpression`/`PhotoSetService` page-math.
- **Phase 5 — AI & vector cleanup**: `Client.Chat<AiAnalysis>` typed parse (delete the JObject parser); vector `eventId` push-down + metadata-on-save; `EmbeddingTelemetry` + `IHealthContributor` (delete `EmbeddingMonitoringService`).
- **Phase 6 — Reassemble to green**: build green; flagship spec green (re-home the parked blob leg too); add specs for the new flows where they carry their weight; measure the reduction.

## Consequences

- **Frontend impact (the SPA, out of scope here but must be tracked):** media URLs change (`/api/media/photos/{id}/thumbnail` → `/media/{id}/thumbnail`); the progress client moves from SignalR to SSE; query/pagination params shift to the `EntityController` shapes. A short URL-migration note ships with Phase 2/3.
- **Cold-render tradeoff:** the first request pays the encode. Koan does not schedule prewarming; an application may explicitly render in an upload/job workflow when the business latency budget justifies it.
- **Red window:** the sample is intentionally non-compiling between phases; CI guards the *end state* (the sample + test are now in `Koan.sln`).
- **Net shape:** four fewer entity types, the reflection hack / JObject parser / manual page-math / `ProcessingJob` / SignalR hub / `EmbeddingMonitoringService` all gone; a materially smaller, more declarative sample whose remaining hand-written code is all product logic.

## Acceptance criteria

1. Sample **builds green**; **`Koan.sln`** includes it; CI guards it.
2. **Flagship tenancy spec green** (record + job + vector + blob + `[HostScoped]`), real `AddKoan()`, no Docker.
3. `Program.cs` ≈ `AddKoan().AsWebApi()` + the `SnapVaultModule`; **no** derivative entity types, **no** reflection, **no** bespoke queue/hub/JSON-parser/page-math/`ProcessingJob`.
4. The hand-written code that remains is exactly the **kept domain core** list above.
5. A measured before/after (LOC, entity-type count, removed packages) recorded in this ADR's follow-up.

## Out of scope
The SnapVault SPA/frontend rewrite (URL + transport changes are *noted*, not done here); the tenancy roadmap Phases 2–6 (control plane, config, entitlements, classification, placement — tracked in the conversion plan); any framework-side change (none required).
