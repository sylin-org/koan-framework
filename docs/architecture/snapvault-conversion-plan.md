# SnapVault conversion — the break-and-rebuild dogfood plan

- Date: 2026-06-22
- Vehicle: `samples/S6.SnapVault` (study + target proposal: [snapvault-tenancy-proposal.md](./snapvault-tenancy-proposal.md)).
- Canon: ARCH-0099 §1–§8, ARCH-0098 (classification). Research: [tenancy-prior-art-findings.md](./tenancy-prior-art-findings.md), [tenancy-entitlements-findings.md](./tenancy-entitlements-findings.md), [tenancy-delight-synthesis.md](./tenancy-delight-synthesis.md).

## Decisions

1. **SnapVault is the dogfood vehicle** for the tenancy / quota / entitlement / classification features. This is ARCH-0079 canon (integration tests as canon; dogfood-driven collapses) applied at the *sample* level — a real, non-trivial app (photos, AI vision, vector search, 3-tier storage, async processing) is the honest proving ground that unit fakes structurally cannot be. SnapVault's domain (**photos × resolution × storage × seats**) *is* the entitlement example, and it exposes three real framework gaps (vector isolation, storage prefixing, the async-hop carrier) — the proof's value is exposing them.
2. **Break-and-rebuild, not a parallel fork.** Per the standing rule (prefer one core + thin shim over a second parallel impl), SnapVault is converted *in place* — single-tenant assumptions are *replaced* by the framework primitives, not duplicated behind a flag. The biggest break-and-rebuild is the **in-memory queue + custom `PhotoProcessingWorker` → `Koan.Jobs`** (durable, tenant-carrying), which closes the async-hop hole the delight harvest named as the existential threat to the isolation flagship.
3. **Framework-first, dogfood-second, per feature.** Each phase: build/close the framework capability (with its own ARCH-0079 spec + mutation + green-ratchet), *then* exercise it in SnapVault, *then* lock the SnapVault behavior with a sample-level integration spec. The SnapVault spec is the acceptance test for the framework feature's delight.
4. **Honesty rule (from the delight harvest's master delight-killer):** never let a SnapVault demo claim a proof artifact (the erasure certificate, "enforce", a quota count) that the framework does not yet exhaustively back. The demo's proven spine ships proven; the rest ships as a dated, visibly-roadmap surface. Carry the no-boot-lies discipline into the sample.

## What gets broken and rebuilt

| Torn out (single-tenant assumption) | Rebuilt on (framework primitive) |
|---|---|
| `// FUTURE: add UserId` on `Collection`/`AnalysisStyle` (the manual-isolation plan) | the invisible `__koan_tenant` discriminator — **add nothing to the entities** (§1b/§12) |
| In-memory `IPhotoProcessingQueue` + `PhotoProcessingWorker` HostedService | `Koan.Jobs` (`IKoanJob`) — durable, **tenant-carried + rehydrated on execute** (§7d durable-carrier) |
| Global `./storage/{profile}/…` keys | per-tenant storage prefix `{tenantId}/…` (the §3 storage-scoping seam) |
| Global Weaviate vector index (unscoped search) | tenant-discriminated vector reads (the vector leak guard — adapter announces `RowScoped` or fails closed) |
| Global `MaxPhotosPerCollection` appsetting; ad-hoc `SnapVault:*` config | §7 governed config + §8 tier entitlements (declared, resolved, enforced) |
| No auth / CORS allow-all / no owner | §1 posture (dev-open/prod-closed) + §2 first-owner (the first photographer) + `Koan.Web.Auth` in prod |
| `SETTINGS-PAGE-DESIGN` (un-built bespoke settings UI) | the §6 `Koan.Tenancy.Web` portal (members/invites/usage/plan) + §7 RSoP-driven settings |

## Phased plan

**Phase 0 — framework prerequisites (close SnapVault's exposed gaps).** These are framework work, not SnapVault work; SnapVault is the acceptance test.
- 0.1 **Portal shell** `Koan.Tenancy.Web` (step 2c — already planned). Posture-governed; diagnostics endpoint; lazy dev `EnsureDevAsync`.
- 0.2 **Durable-carrier** — the ambient tenant rides `Koan.Jobs` work-items + rehydrates at execute (and messaging/outbox); fail-closed-restored. *The existential prerequisite.*
- 0.3 **Vector tenant-isolation** — the vector adapter (Weaviate) carries + filters the discriminator, or a tenant-scoped vector op fails closed (the embedding leak guard).
- 0.4 **Storage blob per-tenant prefix** — `StorageBinding` key incorporates the tenant.

**Phase 1 — SnapVault becomes multi-tenant (the isolation flagship).**
- Reference `Koan.Tenancy`; delete the `// FUTURE: add UserId` comments. Entities auto-isolate (no `UserId`).
- Migrate the worker to `Koan.Jobs` (the break-and-rebuild) so AI analysis carries the tenant.
- Sample spec: an `AssertNoTenantLeak`-shaped test over `PhotoAsset`/`Event`/`Collection` (incl. the **async path** — a job processes the right tenant) and the **vector search** (Studio A's search never returns Studio B's photos). *This spec is the flagship made real on a real app.*

**Phase 2 — control plane + portal (§2/§6).**
- First photographer = Owner; studio = tenant; members/invites/roles.
- The `Koan.Tenancy.Web` portal manages the studio team + shows usage. Dev-open / prod Owner-gated.

**Phase 3 — per-tenant config (§7).**
- "Default Album Name" → a §7 governed config-value with a **closed-grammar template** (`{eventName}`/`{date:…}`/`{seq}`).
- `DefaultSearchAlpha`, `AutoAnalyzeOnUpload`, `PreferredAnalysisStyle` → tenant config; `MaxPhotosPerCollection` → tier entitlement (envelope-bounded). The RSoP explainer surfaced in the portal UI ("semantic search is locked by your Hobbyist plan").

**Phase 4 — entitlements & tiers (§8) — the moat.**
- Declare the SnapVault tier table (Hobbyist 500/1280 · Studio 2000/2k · Pro 10000/4k · seats · semantic-search · placement). Typed entitlements.
- Enforce at the upload chokepoint (reject the 501st); metering reuses `Event.PhotoCount`/`StorageBytes`; honest "480/500"; the resolution cap feeds the `Koan.Media` transform.
- The **over-limit freeze** (downgrade contract): defer to boundary, freeze new uploads, never delete the existing photos. Observe-mode for tightening a tier.

**Phase 5 — classification (ARCH-0098).**
- `[Pii]` on EXIF-GPS / face data / client contact → encrypt-at-rest, per-tenant keys; excluded from the embedding (classification × AI); the erasure certificate for "delete my photos."

**Phase 6 — placement (§3).**
- Pro tier → dedicated DB/container via the P6 broker (grant-AND-reserve, not just a number). Relocate is a P8 saga.

## Acceptance = the north-star demo, phase by phase

The delight harvest's north-star demo *is* the SnapVault acceptance suite: (A) clone-run → Owner of "Acme", upload [P1]; (B) second studio → IDOR/takeover rejected + `AssertNoTenantLeak` green, incl. async + vector [P1]; (C) `[Pii]` → ciphertext at rest [P5]; (D) 501st upload rejected by the framework, naming the layer [P4]; (E) delete → crypto-shred + signed certificate [P5/§10]; (F) redeploy to prod with the dev key → refuses to boot [P1, built]. Spine **A·B·C·F is provable today**; D·E ship as the framework features land — and each demo step is a sample integration spec, not a slide.

## Sequencing note

Phase 0 (framework prerequisites) and §8 itself land **after** the posture seam (built) and the §7 config plane / RSoP explainer (the ADR's own rule: don't grow past 2 layers without the explainer). The conversion is therefore *interleaved* with the remaining framework build — SnapVault is the pull that orders it.

## Readiness status (2026-06-26)

A dependency re-evaluation against the shipped codebase. **SnapVault itself is still unconverted** (single-tenant: the
`// FUTURE: add UserId` comment, the in-memory `IPhotoProcessingQueue`/`PhotoProcessingWorker`, global `[StorageBinding]`
keys, unscoped `Vector<PhotoAsset>.Search()`; no `Koan.Tenancy`/`Koan.Jobs` reference).

| Dependency | Gates | Status |
|---|---|---|
| **0.2** durable-carrier (ARCH-0100) | Phase 1 | ✅ **shipped** — `AmbientCarrierRegistry` + `JobRecord.AmbientCarrier`; `DurableCarrierSpec` |
| **0.3** vector tenant-isolation | Phase 1 | ✅ **shipped** — `ScopedVectorRepository` + Weaviate/Qdrant AODB conformance (ARCH-0102/0103) |
| **0.4** storage per-tenant prefix | Phase 1 | ✅ **shipped** — `ScopedStorageService`; `StorageTenantIsolationSpec` |
| **0.1** portal `Koan.Tenancy.Web` | **Phase 2** | ⚠️ **missing** — control-plane *data* layer (`Tenant`/`Membership`/`Invite`) built; the web shell is not (ledger 2c) |
| §7 governed config + RSoP explainer | Phase 3 | ❌ missing |
| §8 entitlements / tiers / quota | Phase 4 | ❌ missing |
| classification (ARCH-0098) | Phase 5 | ◐ partial — `Koan.Classification` cipher/key-provider/`[Classified]`/field-transform shipped; per-tenant durable keys + erasure certificate pending |
| §3 / P6 placement broker | Phase 6 | ❌ missing |

**Verdict: Phase 1 (the isolation flagship) is UNBLOCKED** — its three prerequisites (0.2/0.3/0.4) are shipped and
proven; the portal (0.1) gates Phase 2, not Phase 1. **Phase 1 is the entry point** and dogfoods exactly the vector
isolation + durable-carrier work that just landed. Phases 2–6 pull their respective framework features (interleaved).
