# Koan Framework Agent Skills Catalog

Specialized, progressively-disclosed guidance for Koan Framework development — for both human
developers (DX) and AI agents (AX). Skills load on demand based on conversation context.

> **Architecture: card-anchored skills ([DX-0048](../../docs/decisions/DX-0048-card-anchored-skill-architecture.md)).**
> The pillar **cards** under [`docs/reference/cards/`](../../docs/reference/cards/) are the
> source-verified, one-screen **API truth**. A skill is the **activation layer** that *leans on*
> its card — it carries the trigger, the one (compile-gated) canonical pattern, the anti-patterns,
> and see-also links; it does **not** re-document the full API. One fact, one home.
>
> This set is mid-overhaul under the **H10 program**
> ([card](../../docs/assessment/prompts/06/H10-skills-overhaul.md)): realigning every skill to its
> card, fixing the API drift the 2026-06 audit found, and adding the missing pillars. Status flags
> below: ✅ current · ⟳ overhaul pending · ＋ planned.

## What are Agent Skills?

Filesystem-based capability packages that **load progressively** (only when relevant),
**stay focused** (one domain each), and **reference** the authoritative cards/guides/samples
rather than duplicating them.

## The skill contract (DX-0048)

Every skill conforms — exemplars: **koan-caching**, **koan-jobs**.

- **Frontmatter**: `name` (== directory name), `description` (trigger-rich — the AX activation
  surface), optional `pillar` / `card` / `status` / `last_validated`.
- **Sections**: `Trigger this skill when you see` → `Core principle` + **the one canonical pattern**
  (a `<!-- validate -->`-marked, self-contained, compile-clean block) → activation table →
  `Anti-patterns to flag` → `Escape hatches` → `See also` (card anchor + guide + sample + ADR).
- **Gate**: `scripts/skills-lint.ps1` (dir==name, frontmatter, no version pins, link/card resolution)
  + the canonical pattern compiles under `scripts/validate-code-examples.ps1` — both wired into
  `green-ratchet.ps1` (Leg D) and the PR gate, so drift breaks the build.

## Catalog

### Foundation (cross-cutting — no single card)

| Skill | Card | Status | When to use |
|-------|------|--------|-------------|
| **koan-quickstart** | — | ✅ | Zero to first Koan app; S0 + S1 patterns |
| **koan-entity-first** | data.md | ✅ | `Entity<T>`, GUID v7, static methods vs manual repositories |
| **koan-bootstrap** | — | ✅ | Auto-registration, `KoanAutoRegistrar`/`KoanModule`, minimal `Program.cs` |
| **koan-debugging** | — | ✅ | Boot-report analysis, capability/provider diagnostics, common errors |

### Pillar skills (1:1 with a card)

| Skill | Card | Status | When to use |
|-------|------|--------|-------------|
| **koan-caching** | cache.md | ✅ | `[Cacheable]`, L1/L2, coherence, `EntityContext.NoCache()` |
| **koan-jobs** | jobs.md | ✅ | `IKoanJob<T>`, `.Job`/`.Jobs`, scheduled/retried work, conveyors |
| **koan-web** | web.md | ✅ | `EntityController<T>`, custom routes, `IEntityTransformer`, auth policies |
| **koan-vector** | vector.md | ✅ | Vector search (`Vector<T>.Search`), `[Embedding]`, provider-migration export |
| **koan-ai** | ai-data.md | ✅ | `EntityAi.Embed/Chat/Ocr`, `[Embedding]`, `[MediaAnalysis]`, `Client` facade |
| **koan-mcp-integration** → koan-mcp | mcp.md | ⟳ (deferred) | `[McpEntity]`/`[McpTool]`, MCP server, Code Mode |
| **koan-auth** | auth.md | ✅ | OAuth2/OIDC connectors, `[Authorize]`/roles, `Can*` gates, Security.Trust bearer |

### Data facets (anchored to data.md)

| Skill | Status | When to use |
|-------|--------|-------------|
| **koan-data-modeling** | ✅ (absorbed relationships) | Aggregates, persistence `Lifecycle`, value objects, `[Parent]`/`Relatives()` |
| **koan-multi-provider** | ✅ | Provider transparency, capability detection (`CapabilitySet`/`DataCaps`), context routing |
| **koan-performance** | ✅ | Streaming, pagination (`QueryDefinition`), count strategies, bulk operations |
| **koan-relationships** | ⟳ (retiring → data-modeling; dir removed in Phase 6) | Entity navigation, batch loading |

### New pillars (card-anchored; added under H10)

| Skill | Card | Status | When to use |
|-------|------|--------|-------------|
| **koan-storage** | storage.md | ✅ | `StorageEntity<T>` + `[StorageBinding]`, profiles, streaming, `MoveTo`/`CopyTo` tiering |
| **koan-messaging** | messaging.md | ✅ | Entity `Events.Raise`/`Transport.Send`, typed handlers, local outcomes, legacy Messaging boundary |
| **koan-media** | media.md | ✅ | `MediaEntity<T>`, content-addressed `Store`, `[MediaRecipe]` transforms, `[MediaAnalysis]` |
| **koan-tenancy** | tenancy.md | ✅ | Automatic tenant isolation (data/blob/cache), `Tenant.Use`/`None`, `[HostScoped]`, dev-open/prod-closed posture, `AssertNoLeak` |
| **koan-orchestration** | orchestration.md | ✅ | `[KoanService]` descriptors, DevHost CLI, `OrchestrationMode` self-orchestration, Aspire |
| **koan-observability** | observability.md | ✅ | opt-in OTel leaf (ARCH-0088), traces/metrics, `IHealthContributor` self-reporting |

## Automatic activation

Skills load on conversation context via their `description`. Examples: entity work →
`koan-entity-first`; project setup → `koan-bootstrap`; API work → `koan-api-building`; caching →
`koan-caching`; background jobs → `koan-jobs`; errors → `koan-debugging`. Explicit invocation via
the Skill tool also works.

## Skill resources

Most skills are a single `SKILL.md` — the overhaul folds the canonical pattern (compile-gated)
and escape hatches inline, retiring the old uncompiled `.cs`/`.template` bundles that drifted.
Two skills keep a focused extra: **koan-bootstrap** (`templates/` — a minimal `Program.cs` +
`appsettings.json`) and **koan-entity-first** (`anti-patterns/manual-repositories.md`). New
bundled directories are added per-skill only when they earn their keep — not promised by default.

## Relationship to the rest of the docs

Skills are intelligent indexes, not replacements: **cards** own the API truth, **guides**
(`docs/guides/`) the deep how-to, **ADRs** (`docs/decisions/`) the rationale, **samples**
(`samples/`) the working proof. A skill routes to all four.

---

**Aligned with:** Koan Framework v0.17.x (NBGV-versioned — see `version.json`; do not pin a patch here)
**Last Updated:** 2026-06-18 · **Architecture:** [DX-0048](../../docs/decisions/DX-0048-card-anchored-skill-architecture.md)
