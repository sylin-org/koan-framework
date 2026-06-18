# H10 · DX/AX Skill-Set Overhaul — card-anchored skills + validation gate

> **Source**: discovered 2026-06-18 from the full skills quality/feasibility audit (13-agent source-accuracy pass; see PROGRESS Divergence log) · **Tier**: T3 (architecture) → T2/T3 per sub-unit · **Depends on**: H2 (validate-gate), H4 (the 8 pillar cards) · **Spawns**: per-skill + per-card sub-sessions
> This is a **program**, not a single paste-and-go session. Phase 0 is foundational and unblocks the rest. Each skill/card below is its own session under the per-unit acceptance contract (§Acceptance).
> Update [PROGRESS.md](../PROGRESS.md): track the umbrella row `H10` plus a sub-row per unit as they land.

---

## Why

The audit graded **all 13 existing skills `refresh`** (none keep/cut/merge). They are on the right topics with decent structure, but every one re-states its pillar's API inline and then **rots** — the first copy-paste example in most skills no longer compiles against current source. Worst offenders: mcp-integration (22), vector-migration (38), relationships (42), ai-integration (46). The drift is one event, not 13 bugs — the same ghosts recur (`QueryCaps`/`QueryCapabilities.HasFlag`→`CapabilitySet.Has(DataCaps.Query.Linq)`; `DataQueryOptions`→`QueryDefinition`; `[VectorField]`→`[Embedding]`; manual FK nav→`[Parent]`/`Relatives()`; `.SaveAsync()`→`Save()`; `List<T>`→`IReadOnlyList<T>`). Crucially, **the rot also lives in the feeding guides** (building-apis, data-modeling, ai-vector-howto) — the skills faithfully copied ghosts *from* the docs. Nothing compiles skill examples, so it all drifted invisibly.

The fix is structural, not a one-time scrub. Three moves:

1. **Anchor.** The H4 pillar cards (`docs/reference/cards/*.md`) are now the source-verified, one-screen **API truth** per pillar. Skills become the **activation layer** that *leans on* the card — carrying only the one canonical pattern (compile-gated), triggers, anti-patterns, escape hatches, and see-also links. One fact, one home → most drift surface disappears.
2. **Realign.** Fix the misalignment the matrix exposed: 5 data skills → 1 card; auth card → 0 skills; vector-migration is a sliver of the vector card; pillars with neither card nor skill (storage, messaging, media, orchestration, observability).
3. **Gate.** Extend H2's opt-in `<!-- validate -->` compile-lint to `.claude/skills/**` so a drifted skill **breaks the build**. This is the permanent honesty mechanism — without it we are back here in three months.

Goal: **top-tier DX and AX (agentic experience).** Agents copy the first example and activate on `description` — so the canonical pattern is first + compile-gated, and the `description` is trigger-rich and distinctive.

---

## The layered information architecture (canon)

Each pillar fact has exactly one home; the others link to it.

| Layer | Location | Owns | Drift control |
|---|---|---|---|
| **Card** | `docs/reference/cards/<pillar>.md` | The one-screen **API truth**: what-it-does · the one canonical pattern · ≤5 attributes · escape hatch · sample | validation block (date_last_tested) |
| **Skill** | `.claude/skills/koan-<x>/SKILL.md` | **Activation**: trigger · core principle · the canonical pattern (compile-gated) · anti-patterns · escape hatches · see-also | `<!-- validate -->` gate + skills-lint |
| **Guide** | `docs/guides/<x>-howto.md` | The deep **narrative how-to** | doc-code gate (marked blocks) |
| **Sample** | `samples/Sx.*` | Working **proof** | builds in CI |
| **ADR** | `docs/decisions/` | The **decision/rationale** | — |

The skill does **not** exhaustively re-document the API (that is the card). It teaches the *pattern* + the *anti-patterns* and routes to the card/guide/sample.

---

## Skill contract (the ergonomics fix)

Every skill conforms to this. Model exemplars already in-tree: **koan-caching** and **koan-jobs** (the only A-graders; dir==name; verified).

**Frontmatter**
```yaml
name: koan-<x>                       # MUST equal the directory name (kebab-case)
description: <trigger-rich, distinctive — this is the AX activation surface>
pillar: <x>                          # optional structured metadata
card: docs/reference/cards/<x>.md    # the anchor (omit for cross-cutting skills)
status: current
last_validated: YYYY-MM-DD
```

**Sections** (in order)
1. `## Trigger this skill when you see` — concrete signals (types/attributes/phrases) for deterministic AX activation. Replaces the weak "When This Skill Applies" applicability lists.
2. `## Core principle` — the Reference=Intent / pillar essence, immediately followed by **the one canonical pattern** as a single `<!-- validate -->`-marked, compile-clean block (mirrors the card).
3. `## <Reference=Intent table / capability ladder>` — the activation/effect table.
4. `## Anti-patterns to flag` — the red flags (AX guards: what NOT to generate). The owning skill absorbs the relevant rows from CLAUDE.md's "Critical Anti-Patterns to Detect."
5. `## Escape hatches` — the power-user / opt-out surface.
6. `## See also` — **card (anchor)** + guide + sample + ADR; every link must resolve.

**Rules**: dir == name; Newtonsoft canonical (no STJ); canonical verbs Save/Remove/Query; no version pins (NBGV floats); no removed/migrated surfaces presented as live.

---

## The anti-drift gate (permanent)

- **Compile gate**: each skill's canonical-pattern block is `<!-- validate -->`-marked; extend `scripts/validate-code-examples.ps1` (H2) to scan `.claude/skills/**`; wire into `green-ratchet.ps1` Leg C and the `pr-gate.yml` (B2). Drift → red build.
- **skills-lint** (`scripts/skills-lint.ps1`, new): asserts dir==name; frontmatter contract (name+description present, name==dir); the `card:` link present and the file exists; every see-also link resolves; no `0.6.x`/version pins. Add to green-ratchet + pr-gate.
- **Catalog parity**: the README catalog and CLAUDE.md pattern table must list exactly the on-disk skill set (a lint cross-check).

---

## The new roster (keep · overhaul · new · retire)

19 skills (from 14), 13 cards (8 existing + 5 new). `→ card` shows the anchor.

### Foundation (cross-cutting; no single card)
| Skill | Disposition | Fix |
|---|---|---|
| `koan-quickstart` | overhaul | `using Koan.Web`→`Koan.Web.Controllers`; point to `[Parent]` |
| `koan-entity-first` | overhaul → data.md | `.SaveAsync()`→`Save()`; `DataQueryOptions`→`QueryDefinition`; `List<T>`→`IReadOnlyList<T>`; Created/Updated require `[Timestamp]` |
| `koan-bootstrap` | overhaul | templates: `BootReport`→`ProvenanceModuleWriter`; `Configuration.Read(multi)`→`ReadFirst` |
| `koan-debugging` | overhaul | `QueryCaps`/`QueryCapabilities.HasFlag`→`CapabilitySet.Has(DataCaps.Query.Linq)`; `KoanEnv.CurrentEnvironment`→`EnvironmentName` |

### Pillar skills — existing cards
| Skill | Disposition | → card |
|---|---|---|
| `koan-web` | rename (`api-building`) + overhaul: `IPayloadTransformer`→`IEntityTransformer<T,TShape>`; `override Post`→`Upsert` | web.md |
| `koan-caching` | light refresh: ghost `Cache.Evict<T,K>`; stale `Koan.Data.Direct`; `[Cacheable(…, AllowStaleForSeconds: …)]` colon→`=` | cache.md |
| `koan-jobs` | ✅ **done** (`3317a31b`) | jobs.md |
| `koan-vector` | **new** — absorbs `vector-migration`: `ExportAllAsync`→`ExportAll`; reframe `EmbeddingCache`/`IEmbeddingCache` as **sample-only** (not framework); search+embed+migrate | vector.md |
| `koan-ai` | overhaul (`ai-integration`), **full surface**: `ChatAsync/StreamAsync`→`Chat/Stream`; `Converse().PromptAsync`→`Conversation().Send()/Ask`; `IAiPipeline.PromptAsync`→`Prompt`; `.Content/.Usage`→`.Text`; `Vector.SearchAsync(string)`→`Search(float[])`; `[VectorField]`→`[Embedding]`; delete fabricated `Koan:AI:Providers/Type:OpenAI` block (use `Koan:Ai`) | ai-data.md |
| `koan-mcp` | overhaul (`mcp-integration`) — **DEFERRED** (concurrent Mcp boundary): `IMcpTool/McpToolSchema/McpToolResult`→`[McpEntity]`/`[McpTool]`/`MapKoanMcpEndpoints`; flat `Koan:Mcp` keys | mcp.md |
| `koan-auth` | **NEW** | auth.md |

### Data facets (lean; anchored to data.md)
| Skill | Disposition | Fix |
|---|---|---|
| `koan-data-modeling` | overhaul **+ absorb `relationships`** | lifecycle: ghost `EntityLifecycleBuilder`/`(ctx,next)`/`ctx.Entity`→`Entity<T>.Events.BeforeUpsert(ctx=>ctx.Proceed())`, `ctx.Current`; **add `[Parent]`/`Relatives()`/`RelationshipGraph` (DATA-0072)** folded from relationships |
| `koan-multi-provider` | overhaul | `QueryCaps`→`CapabilitySet`; `[VectorField]`→`[Embedding]`; drop pgvector; `CONTAINS(...)` string→JSON filter DSL |
| `koan-performance` | overhaul | `DataQueryOptions{OrderBy,Descending}`→`QueryDefinition.WithSort(...)` |

### Pillar skills — new cards (Phase 2 authors the card first, then the skill)
| Skill (+ new card) | Anchor scope |
|---|---|
| `koan-storage` + `storage.md` | `Koan.Storage` — profiles, providers, streaming, fallback |
| `koan-messaging` + `messaging.md` | `Koan.Messaging` — bus, handlers, transport |
| `koan-media` + `media.md` | `Koan.Media` — pipeline, `[MediaAnalysis]` (bridges AI) |
| `koan-orchestration` + `orchestration.md` | `Koan.Orchestration` — CLI, Aspire, compose/devops (`start.bat`) |
| `koan-observability` + `observability.md` | `Koan.Observability` (ARCH-0088) — opt-in OTel leaf |

### Retire (fold, don't delete the value)
| Skill | Folds into |
|---|---|
| `relationships` (grade 42; contradicts DATA-0072) | `koan-data-modeling` (its `[Parent]`/`Relatives()` rewrite) |
| `vector-migration` (grade 38; thin) | `koan-vector` (one corrected migration section) |

> **Candidate further cards (flagged, not in scope yet — card-first discipline):** canon (mid-rebuild, defer), backup (niche → a `koan-data` section), security/trust (emerging, DEC-0053). Revisit once stable.

---

## Phases

**Phase 0 — Architecture & gate (foundational; do first).**
- ADR `DX-00xx` (next free): *card-anchored skill architecture + validation gate* — records the layered IA, the skill contract, and the gate.
- Implement the skill contract template; **rename all dirs → `name` (dir==name)** and verify the loader resolves them (resolves the 12-of-14 mismatch).
- `scripts/skills-lint.ps1` + extend `validate-code-examples.ps1` to `.claude/skills/**`; wire both into `green-ratchet.ps1` + `pr-gate.yml`.
- Rewrite `.claude/skills/README.md` (tiers, the card-anchor model, accurate resource inventory, NBGV version). Update CLAUDE.md's pattern-recognition table to the new roster with precise triggers.

**Phase 1 — Guide truth pass.** Bring the feeding guides to source-truth so skills can lean on them: `building-apis.md` (`IPayloadTransformer`→`IEntityTransformer`), `data-modeling.md` (`EntityLifecycleBuilder`→`Events`), `ai-vector-howto.md` (`EmbeddingCache` is sample-only; `ExportAllAsync`→`ExportAll`), `ai-integration.md`, the relationships material (→DATA-0072), auth guides as needed. Each guide's marked blocks compile.

**Phase 2 — New pillar cards** (H4-style, source-verified, validated): `storage`, `messaging`, `media`, `orchestration`, `observability`.

**Phase 3 — Overhaul existing skills** (card-anchored, koan-jobs standard), one session each: entity-first · bootstrap · debugging · quickstart · web · caching(light) · data-modeling(+relationships fold) · multi-provider · performance · vector(+migration fold) · ai(full).

**Phase 4 — New skills**: auth · storage · messaging · media · orchestration · observability.

**Phase 5 — mcp** (deferred until the concurrent Mcp boundary clears): overhaul against `[McpEntity]`/`[McpTool]`.

**Phase 6 — AX finish**: bidirectional skill↔card cross-links; align with `/llms.txt` (H7); retire `relationships` + `vector-migration`; full gate green; distribute CLAUDE.md anti-patterns into owning skills; final review.

---

## Acceptance (per skill/card unit — the koan-jobs standard)

1. **Every identifier grep-verified** at its `src` file:line (premises re-derived empirically — the audit proved cards/guides drift).
2. **The canonical pattern compiles** under the extended validate-gate (`<!-- validate -->`).
3. **Contract holds**: dir==name; frontmatter complete; every see-also link resolves; `card:` anchor present.
4. **Adversarially reviewed** — an independent verifier panel re-greps the identifiers; **the orchestrator is the final reviewer** (never the agents' self-verdicts; cf. H6 rate-limit lesson).
5. **Gates green**: docs-lint + skills-lint + doc-code, 0 errors.
6. Two commits per unit (impl, then PROGRESS row-flip + Divergence entry).

## Boundaries & risks

- **mcp** is fenced by the concurrent Mcp session — Phase 5 waits; do not edit `src/Koan.Mcp/**` (read-only sourcing only).
- **AI full-surface** is an accepted-churn decision: agentic/RAG/orchestration content will need re-pointing (or deletion) when the ARCH-0089 3-repo migration lands (`X-ai-dissolution-migration`). Mark those sections with an ADR pointer.
- **jobs** already meets the bar — leave it; it is the reference.
- **Newtonsoft canonical**, **persona separation**, **conventional commits** apply throughout.
