# SnapVault × tenancy — a worked proposal (the delight case study)

- Date: 2026-06-22
- Purpose: study `samples/S6.SnapVault` and propose how to incorporate **multi-tenancy + quotas + entitlements** (ARCH-0099 §1–§8, ARCH-0098) so the result is *delightful*. Doubles as the worked validation of the tenancy design against a real, non-trivial app — and surfaces the concrete framework gaps it exposes.

## What SnapVault is (the proposal it already makes)

A **self-hosted professional photo-management system** ("rivals Google Photos") — AI vision analysis (Ollama via ZenGarden), semantic search (Weaviate vectors, a 0.0–1.0 exact↔semantic slider), 3-tier storage (hot-cdn / warm / cold), a modern dark gallery. Intended users (from `UX-DESIGN-SPECIFICATION.md`): **professional event photographers** (2,000–5,000 photos/event), **studio managers** (5+ photographers, centralized storage, client delivery), **fine-art photographers**. Deployed as Docker Compose on a LAN — **single-tenant by design, no auth, no user/owner concept today** (`Program.cs` wires no `Koan.Web.Auth`; CORS is allow-all; no entity carries a `UserId`).

It is *almost perfectly* shaped to show off tenancy + tiers: its domain is **photo-count × resolution × storage × seats**, which is the entitlement example verbatim.

## Domain model (today) and how it maps to tenancy

| Entity | Base | Role | Tenancy disposition |
|---|---|---|---|
| `PhotoAsset` | `MediaEntity<T>` `[StorageBinding cold]` | full-res original + nested `AiAnalysis` + `[Embedding]` | **tenant-scoped** (auto) |
| `PhotoGallery` / `PhotoThumbnail` / `*MasonryThumbnail` / `*RetinaThumbnail` | `MediaEntity<T>` warm/hot-cdn | derivatives | **tenant-scoped** (auto) |
| `Event` | `Entity<T>` | shoot container; **already tracks `PhotoCount` + Hot/Warm/Cold `StorageBytes` + `CurrentTier`** | **tenant-scoped** (auto) — and *already a meter* |
| `Collection` | `Entity<T>` | user grouping (`PhotoIds[]`); has a `// FUTURE: add UserId` comment | **tenant-scoped** (auto) |
| `ProcessingJob`, `PhotoSetSession` | `Entity<T>` | job progress / browse context | **tenant-scoped** (auto) |
| `AnalysisStyle` | `Entity<T>` | AI templates; `IsSystemStyle` flag; system-seeded + user-creatable | **split**: system styles = solution-level; user styles = tenant-scoped (a tier entitlement) |

## The headline delight: you add *nothing* to the entities

SnapVault's own code says the multi-user path is *"add a `UserId` property to every entity and filter every query"* (`Collection.cs`, `AnalysisStyle.cs` FUTURE comments). **Our design makes that obsolete — and deleting those comments is the demo.** Reference `Koan.Tenancy` → tenancy activates (dev-open / prod-closed). Every `Entity<T>` is isolated by the **invisible `__koan_tenant` discriminator stamped below the adapter** (§12 write-stamp + read-filter) — there is **no `UserId` to add, no query to remember to filter, no `WHERE` to forget.** Studio A's wedding photos *cannot* appear in Studio B's gallery, and `AssertNoTenantLeak` regenerates the proof every build. This is the unanimous-flagship delight, realized: *"the cross-tenant leak the photographer literally cannot write."*

Day-one developer experience: `dotnet run` → the boot report prints `posture=Open`, you are already **Owner of a studio "Acme"** (smart-named from your git email), zero login. You upload a photo and it just works — tenancy disappeared into a package reference.

## Control plane (§2/§6): SnapVault becomes multi-studio

A **tenant = a photography studio** (or a solo photographer). The first photographer to sign up becomes **Owner** of their studio (one-shot, §2); they **invite** their team (the "studio manager managing 5+ photographers" persona) with roles on the membership (`koan:owner` / `photographer` / `viewer`). The **self-service portal** (`Koan.Tenancy.Web`, §6) is where a studio admin manages members + invites, sees usage/quota, and picks their plan — dev-open, prod Owner-gated. The "manage 5+ photographers, centralized storage" use case the UX spec already names becomes a first-class, Koan-provided surface.

## Quotas & entitlements (§8): SnapVault's tiers

SnapVault's domain is the tier example. Concrete plan ladder (using the canonical numbers):

| Entitlement | kind | Hobbyist (free) | Studio (tier-1) | Pro (tier-3) |
|---|---|---|---|---|
| `photos.max` | numeric-limit | **500** | **2000** | **10000** |
| `photo.maxResolution` | enum-level | **1280×1024** | **2k** | **4k / original** |
| `storage.maxBytes` | numeric-limit (metered) | 5 GB | 50 GB | 500 GB |
| `members.max` (seats) | numeric-limit (distinct-user) | 1 | 5 | 25 |
| `analysisStyles.custom.max` | numeric-limit | 0 (system only) | 5 | unlimited |
| `aiAnalyses.perMonth` | metered / rate | 100 | 2,000 | 20,000 |
| `semanticSearch` | boolean-feature | off | on | on + similarity |
| `placement` | enum (→ §3 rung) | shared-row | shared-row | **dedicated DB** |
| `defaultAlbumName` | config-value (template) | fixed | `TenantMayChange` | custom template |

- **Resolve AND enforce at the chokepoint (the moat).** The upload already funnels through `PhotoProcessingService.ProcessUpload` → `PhotoAsset.Save()`. The quota becomes a *declared* entitlement enforced at the entity-write chokepoint (the GitLab-`Limitable` shape, §8): **the framework rejects the 501st upload itself** — not 200 lines of metering glue the SnapVault dev maintains — with a message that names *which layer set the ceiling* + utilization% + the upgrade path. Stripe/Clerk could tell SnapVault the plan; only Koan stops the 501st upload at the same gate that already blocks cross-tenant access.
- **Metering is already half-built.** `Event` already carries `PhotoCount` + `Hot/Warm/Cold StorageBytes`. The §8 `Used/Allowed/Grace/Rollover` ledger surfaces the honest **"480/500 used · Studio gives 2000"** the end-user persona loved — and SnapVault already has the data; it just isn't a governed, enforced ledger yet.
- **Resolution cap = the media pipeline reads the entitlement.** `photo.maxResolution` (enum) feeds the existing `Koan.Media` transform: the upload auto-downscales to the tier cap (the studio on 2k gets 2k derivatives) — a tier that *gates the capability shape*, not just a count (the Snowflake/Databricks lesson).
- **The downgrade contract — freeze, never delete (the delight-vs-rage fork, and it matters most for a photo vault).** A Pro studio with 10,000 photos drops to Hobbyist (500). Per §8 + sweep-#3: **defer to the billing-cycle boundary, then freeze *new* uploads — the 10,000 existing photos stay browsable, exportable, and deletable, and are never auto-deleted.** A photographer's life's work is never destroyed by a billing event (Dropbox-Basic's delete-to-enforce is the anti-pattern; the over-limit state is explicit + queryable, with read + reduce-usage always open). Resolution downgrades are forward-only: existing 4k photos are grandfathered, new uploads cap lower.

## Per-tenant config (§7): the settings

- **"Default Album Name"** (the example) → a §7 governed config-value: a **closed-grammar template** (`{eventName}`, `{date:yyyy-MM-dd}`, `{seq}` — *data, not executable logic*, per sweep-#3's SSTI warning). Solution declares the default (`"Untitled Event"`), `TenantMayChange`; the tier gates whether custom templates are allowed.
- `DefaultSearchAlpha`, `AutoAnalyzeOnUpload`, `PreferredAnalysisStyle` → tenant config (`TenantMayChange`).
- `MaxPhotosPerCollection` (today a global appsetting `2048`) → a tier entitlement (`TenantMayChangeWithin([0, tier-max])` — the envelope).
- **Branding** → a config-value category (studio logo on the gallery, `studio.example.com` custom domain via §7b **DNS-TXT-verified domain capture**, which also doubles as the tenant resolver).
- The **RSoP explainer** surfaced in the portal UI: *"semantic search is off because your Hobbyist plan locks it — Studio enables it"* — the end-user-facing trust feature (a sleeper hit from the harvest).

## Classification (ARCH-0098): the photo-vault privacy angle

A wedding photographer holds **sensitive personal data of guests**: EXIF **GPS coordinates** (where people were), face data, client contact info, names in AI tags. Declare `[Pii]` on those fields → **encrypt-at-rest, per-tenant keys**, zero crypto code in the domain model. *"Delete my photos"* (a GDPR request from a wedding client) → **crypto-shred + a signed erasure certificate** — the compliance flagship, exactly the proof a studio handling EU clients needs. Note the **classification × AI tension** (memory): `[Pii]` GPS/face data is *excluded from the embedding* (you can't semantic-search encrypted PII); the non-PII AI description tags still embed — so search keeps working while privacy holds.

## The honest gaps SnapVault exposes (built vs needs-building)

SnapVault is a great proof *because* it stress-tests the design and reveals concrete gaps:

- **BUILT today, works on SnapVault now:** the isolation gate + write-stamp/read-filter (Event/Collection/PhotoAsset auto-isolated on Mongo — Mongo isolation is done), encrypt-at-rest for `[Pii]`.
- **Vector (Weaviate) tenant-isolation** — semantic search hits the vector store, which must carry the discriminator + filter by tenant, or one studio's search returns another's photos. (Framework: the embedding/vector leak guard — vector adapters must announce row-scoped isolation or fail closed. A real §-gap to close before SnapVault's search is safe.)
- **Storage (blob) per-tenant prefixing** — the `StorageBinding` key must incorporate the tenant (`{tenantId}/photos/{id}`) or blobs collide/leak across studios. (Framework: storage isolation, the §3 storage-scoping seam.)
- **The async-hop hole — SnapVault is the textbook case.** It uses an **in-memory queue + a custom `PhotoProcessingWorker` HostedService** (not `Koan.Jobs`). The AI vision analysis of a `[Pii]`-bearing photo runs in that background worker **outside the request chokepoint** — so the ambient tenant doesn't ride it today. This is *exactly* the durable-carrier gap the delight harvest flagged as the **existential threat to the flagship**: a quota or isolation check bypassed by doing the work in a background job makes "structurally unwriteable leak" *false*. SnapVault should migrate the worker to `Koan.Jobs` (durable, tenant-carrying, at-least-once) — and it makes the §7d durable-carrier requirement concrete and testable.
- **§8 itself** (typed entitlements, the resolution operators, the metering ledger, the downgrade-freeze, the portal) — **designed, not built.** SnapVault is the app to build it *against*.

## The pitch in one line

> SnapVault goes from a single-tenant self-hosted sample to **"SnapVault Cloud"** — a multi-studio SaaS with isolation you can't breach, tiers the framework *enforces* (not just resolves), photos that are never deleted by a downgrade, and GDPR-provable erasure — by **referencing two modules and declaring a tier table**, deleting the `// FUTURE: add UserId` comments along the way.

This *is* the north-star demo the delight harvest named — and SnapVault is the app to build it on.
