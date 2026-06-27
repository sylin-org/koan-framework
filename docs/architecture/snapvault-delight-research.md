# SnapVault — Delight Research & Direction

- Status: **Accepted direction (2026-06-27)**
- Purpose: the user-pain / delight evidence base for the SnapVault greenfield build, mapped to Koan's structural moats, plus the decided product direction. Companion to [snapvault-product-spec.md](./snapvault-product-spec.md) (§9 records the build-shape consequences) and [snapvault-ui-api-contract.md](./snapvault-ui-api-contract.md).
- Source: web-research fan-out (4 facets — studio/client-gallery DAM, self-hosted, AI search + auto-culling, multi-tenant/privacy — each evidence-backed, 2023–2026 sources) + synthesis against Koan moats. The "delight == moat" gate (lead with the named differentiator competitors structurally can't copy) is canon for this product arc.

> **Why this exists:** before building features, we checked what the adjacent market (Pixieset / Pic-Time / ShootProof / PhotoShelter / SmugMug / CloudSpot; immich / PhotoPrism / Ente; Google/Apple Photos; Aftershoot / Narrative / Imagen) actually pains over and delights in, and asked which of those Koan can own *cheaply* because of its substrate. The answer reframed SnapVault from "a photo CRUD sample" into **a studio↔client lifecycle** that dogfoods the just-shipped Koan.Identity + tenancy stack.

---

## 1. Top cross-cutting pains (deduped across the 4 facets)

| # | Pain | Severity | Where it surfaced |
|---|------|----------|-------------------|
| **CP1** | **Vendor holds your library hostage** — no clean export; payment-/archive-tied data loss; "empty folder" horror stories. Lock-in + data destruction. | CRITICAL | all 4 facets |
| **CP2** | **"Privacy" is a guessable password, not real isolation** — boudoir-breach class; coarse sharing (only-owner links, no download gating, no per-client separation). | CRITICAL | studio-dam, selfhosted, multitenant |
| **CP3** | **Search silently fails / is confidently wrong** — ~40% untagged; CLIP returns "cats" for "dog"; can't read text-in-image; degrades on scroll; no honest empty state. | HIGH | studio-dam, selfhosted, ai-cull |
| **CP4** | **AI is a black box you re-review anyway** — no per-pick reason; tags too coarse ("contains a car", not *which*); invisible/uneditable. | HIGH | ai-cull, studio-dam, selfhosted |
| **CP5** | **GDPR/consent erasure is unverifiable** — can't prove backups/embeddings/derivatives are gone; consent lives in spreadsheets divorced from photos; no "find every photo of this person". | HIGH | multitenant, rising in studio-dam |
| **CP6** | **Pricing punishes the agency shape** — per-photo / per-seat / per-storage scales on the wrong unit; caps compound; free tiers are trials. | HIGH | studio-dam, multitenant |
| **CP7** | **Performance collapses on the largest libraries** — exactly where it matters (the biggest event gallery): endless "pending", timeouts, cache misses. | MED-HIGH | studio-dam, selfhosted |
| **CP8** | **Correction is destructive / not one-photo-scoped** — face mistag merges identities; can't untag. Manual cleanup is the universal tax. | MED | selfhosted, ai-cull |
| **CP9** | **AI vision silently upgrades photos to regulated biometric data** (CCPA/BIPA) without surfacing the liability. | MED-HIGH (rising) | multitenant |
| **CP10** | **White-label / client handoff unsolved** — "powered by" leaks; no portable ownership transfer at project end. | MED | multitenant |

---

## 2. Recommendations (ranked by rare × high-impact)

Verdict legend: **ADD-NOW** (cheap, high-fit, on the current build path) · **ADD-LATER** (high value, more work) · **NON-GOAL** (off-moat / too big for a sample).

| # | Change | Kills | Delight | Koan-fit | Verdict |
|---|--------|-------|---------|----------|---------|
| 1 | **Invite-gated shareable sets (client galleries)** — studio creates a set, invites a client/guest; access is scoped to that set only. *(User-mandated keystone, 2026-06-27.)* | CP2, CP10 | The core studio→client flow; portable identity; no SSO tax. | **Koan.Identity invite-binds-to-identity + SEC-0004 grant (gate·constrain·project). Near-free** — rides shipped seams. | **ADD-NOW** (steps 2 & 5) |
| 2 | **Verifiable client-erasure certificate** — "Delete this client, prove it": purges originals + derivatives + thumbnails + **vector embeddings + AI facts** + the guest's grant, emits a signed crypto-erasure certificate. | CP1, CP5, CP9 | "It's *provably* gone" — a closing argument no photo product has. | **Koan.Identity crypto-erasure cert + atomic deprovisioning. Near-free.** Highest moat. | **ADD-NOW** (step 2) |
| 3 | **Per-client access isolation, fail-closed** — a guest sees only their set; cross-set/cross-client reads return null by construction (NOT a second tenant axis — a capability grant; see spec §9). | CP2 | "Structurally impossible for one client's photos to surface in another's." | **SEC-0004 read-path predicate + per-studio tenant isolation already shipped. Near-free.** | **ADD-NOW** (step 2/5) |
| 4 | **Honest hybrid search: vector + lexical + OCR with a relevance floor** — keep the alpha slider; add OCR'd text + the facts map as a lexical lane; show an honest "no strong matches" state below the floor. | CP3 | Search that never silently lies; finds text-in-image. | **Built-in vector + AI facts + media recipes; OCR rides the [MediaAnalysis]→[Embedding] bridge. Near-free.** | **ADD-NOW** (step 6) |
| 5 | **Reroll-with-locks as the explainable-AI surface** — plain-English reason + sub-scores per photo; lock/veto judgments; never auto-discard, always reversible. | CP4, CP8 | "Trust or override in two clicks" — the category's #1 emerging delight. | **AI facts + lock-and-reroll already shipped; extends the surface. Near-free.** | **ADD-NOW** (step 5) |
| 6 | **No per-photo/per-MAU tax + one-click portable export** (originals + facts/tags/collections map). | CP1, CP6 | "You can always leave with everything." Portability flips lock-in fear. | Durable tenant-carried jobs; app-owned identity; no pricing tax. Near-free (export exists; promote it). | **ADD-NOW** (positioning, step 7) |
| 7 | **Consent + usage-rights + expiry as a first-class fact; "find every photo of this person" as a query.** | CP5, CP9 | One view of who consented; release bound to the image; erase-by-person on the same engine. | Entity<T> + facts map + search. Near-free, but post-core. | **ADD-LATER** |
| 8 | **Granular delivery-link controls** — expiry, password, view-only / disable-download, watermark (a media recipe), access journal. | CP2 | Secure-by-default is the path of least resistance. | Web + storage + media recipes. Moderate. | **ADD-LATER** |
| 9 | **Per-studio taste profile** — learns from favorites/ratings/kept-vs-rejected AI picks; never cross-contaminated. | CP4 | The "specialist employee" loyalty driver. | Isolation makes private-per-studio natural; ML loop is expensive. | **ADD-LATER** |
| 10 | **Burst/near-duplicate grouping** with best-of pre-pick + easy swap. | CP4 | "Review one, not ten." | Vector similarity available; grouping UX is real work. | **ADD-LATER** |
| 11 | **Idempotent processing** — never reprocess on re-ingest; virtualized grid stays fast at thousands. | CP7 | Holds the "wow" at the largest gallery where rivals time out. | Durable jobs + virtualized grid already exist; idempotency keys are moderate. | **ADD-LATER** (step 4) |
| 12 | Heavy **face/people recognition** (Google/Apple-parity clustering). | CP8 | Self-serve "all photos of me." | Off-moat, ML-heavy, **pulls SnapVault into biometric-data regulation (CP9)**. | **NON-GOAL** (sample) |
| 13 | **Print store / e-commerce / contracts / invoicing / Tap-to-Pay.** | CP6 | Passive revenue. | Commerce plumbing, not Koan substrate; far too large. | **NON-GOAL** (sample) |
| 14 | **Full infra white-label** (custom domains + SPF/DKIM/DMARC email). | CP10 | Agency resale premium. | Ops/deliverability, not a substrate win. | **NON-GOAL** (note as future) |

---

## 3. The flagship arc (lead with this) — the studio↔client lifecycle

The keystone that turns four moats into one coherent, demoable story **no competitor can tell** — and it is exactly the SEC-0007 P5 identity dogfood:

> **Invite** a client to their event set → they accept into a **durable, portable identity** (invite-binds-to-identity, no email-merge takeover) → they get **fail-closed scoped access to only that set** (a proofing gallery) → at engagement end, the studio **atomically deprovisions + hands them a signed erasure certificate**.

Five visible differentiators, in priority order:

1. **Invite-gated proofing galleries** — the studio shares a set; the invited client signs in to a *durable person* and sees only their set, can **favorite/rate/select picks (+ comment)**, and the studio sees the selections. Portable identity, no SSO tax.
2. **Verifiable client-data erasure certificate** — a downloadable signed certificate listing exactly what was purged (originals N, derivatives, thumbnails, M embeddings, facts-map keys) + timestamp + hash. *No photo product on the market does verifiable deletion.*
3. **Per-client isolation as structural impossibility** — fail-closed by construction (the framework's `AssertNoLeak` posture), not a password field. The boudoir breach is the cautionary tale this directly cures.
4. **Honest hybrid search (vector + lexical + OCR) with a relevance floor** — reads text-in-image; shows an honest empty state instead of confidently-wrong tail results.
5. **Reroll-with-locks as explainable AI** — reason + sub-scores; lock what you trust, reroll the rest; nothing auto-discarded.

---

## 4. What NOT to add (scope discipline)

- **Face/people recognition with identity clustering** — off-moat, ML-maintenance heavy, and structurally pulls SnapVault into biometric-data regulation. Scope AI as facts/tags/captions and be honest about it.
- **Print store / e-commerce / lab fulfillment / abandoned-cart**, **contracts / invoicing / booking / payments**, **full infra white-label (custom domains, email deliverability)**, **inbound consumer migration (Google Takeout / camera-roll sync)** — all off-moat or wrong-audience for a B2B studio sample.
- A standalone **per-studio ML taste-training pipeline** as a headline — the *isolation* that makes it safe is the moat, but the ML loop is expensive; keep it ADD-LATER, not a flagship.

---

## 5. Decided direction (user, 2026-06-27)

- **Sequencing:** *Build the studio↔client lifecycle as the spine now* — fold invite-gated sets + the erasure certificate into the greenfield build (step 2 tenancy/identity on-ramp + step 5 domain/guest endpoints). This is a deliberate **expansion** of the harvest spec (net-new guest endpoints + a new guest UI; the SPA-rewrite-out-of-scope clause is lifted for the guest surface).
- **Guest v1 scope:** *Proofing gallery* — the invited guest can favorite/rate/select picks (+ optional comments); the studio sees the client's selections. (The real wedding/client workflow, a step beyond minimal view-only.)
- **Architecture note (advisor correction to the raw synthesis):** "per-client isolation" is a **capability grant**, not a second tenant axis. Studio = tenant (shipped isolation); guest = identity + a grant to a `Collection`/`Event` (SEC-0004 gate·constrain·project — the grant *is* the read-path filter, fail-closed). No new framework axis. See spec §9.

Build consequences are recorded in [snapvault-product-spec.md](./snapvault-product-spec.md) §9.
