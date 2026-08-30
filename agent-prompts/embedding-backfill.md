# Handoff prompt — `[Embedding(AutoGenerateMissing = true)]`: lazy backfill of missing embeddings

Copy everything below the line into a fresh agent session.

---

You are working in the Koan framework repository at `F:\Replica\NAS\Files\repo\github\sylin-org\koan-framework` (branch `dev`). Koan is an opinionated .NET 10 meta-framework: **a package reference is the intent** — referencing a capability makes it available, and `AddKoan()` composes everything referenced, once.

## Read first (non-negotiable, in order)

1. `AGENTS.md`, then `CLAUDE.md` (contributor law — read fully before changing production code).
2. `docs/MEMORY.md` (working conventions).
3. `docs/recipes/search-by-meaning.md` — especially the **Boundaries** section, which documents the gap you are closing.

## The gap, in the maintainer's words

When a developer adds `[Embedding]` to an entity type that **already has saved records**, nothing happens to those records. Koan embeds only saves from that point on; the recipe states it as a boundary: *"`[Embedding]` governs saves from that point on. Rows already saved are not embedded — re-embedding existing data is separate work."* That contradicts the framework's own grammar: adding `[Embedding]` to an existing entity **is** the intent to have it embedded, and demanding a hand-written migration script is the kind of ceremony Koan exists to remove.

The maintainer's requested design: an **opt-in** attribute flag that fires a **lazy job** to generate the missing embeddings:

```csharp
[Embedding(AutoGenerateMissing = true, Template = "{Title}. {Body}")]
public sealed class Article : Entity<Article> { ... }
```

## What already exists (verified — build on it, don't rebuild it)

All under `src/Koan.Data.AI/`:

- `EmbeddingMetadata.cs` — attribute resolution (`Template` > `Properties` > `Policy`), and `ComputeSignature($"v{Version}:{text}")`. **The signature is the idempotency key**: an entity whose stored signature matches its current content needs no re-embed.
- `EmbeddingWriter.cs` — the single lifecycle-to-vector write boundary (`Describe`/`Write`), model/source routing, and `VectorModelGuard` consistency enforcement.
- `Workers/EmbeddingWorker.cs` — the background worker that `Async = true` already uses. **This is the delivery mechanism for the lazy job.**
- `Migration/EmbeddingMigrator.cs` — imperative re-embedding today (`ReEmbedAll`, `ReEmbed` finite set, `MigrateToVersion`, `CleanupOrphanedStates`), including the `VectorModelGuard.Reset` dance for whole-collection transitions (AI-0036). Study how it batches and reports.
- `Initialization/DataAiModule.cs` — module init and the save-time lifecycle hook (see ~line 405). **This is where the opt-in flag gets observed and the backfill work gets scheduled.**

What does **not** exist: a *missing-only* sweep (the migrator re-embeds everything, which is wrong for backfill — most rows may already be current after a partial failure or a `Version` bump that only touched some entities), and the boot/lazy trigger.

## The work

1. **Attribute surface**: add `bool AutoGenerateMissing { get; set; } = false;` to `EmbeddingAttribute` (`src/Koan.Data.AI/Attributes/EmbeddingAttribute.cs`), documented in its own doc comment (when to opt in, cost model — it schedules work proportional to row count).
2. **Missing-only sweep**: a `BackfillMissing<TEntity>(...)` (placement: beside the migrator, same project) that enumerates entities of the type, diffs against persisted embedding state (missing vector **or** stale signature), and routes only the delta through `EmbeddingWriter.Write`. Must batch (study `ReEmbedAll`'s `batchSize`), respect the `VectorModelGuard` rules (backfill of *missing* rows is not a model transition — do not reset the registry), and be safe to re-run (idempotency comes free via signature).
3. **The lazy trigger**: when `DataAiModule` resolves an entity with `AutoGenerateMissing = true`, schedule the sweep as deferred/background work — prefer the existing `EmbeddingWorker`/queue path, or a Koan.Jobs `@boot`-style registration if that fits the module's lifecycle better. It must be **lazy and non-blocking**: host startup must not wait on embedding a large table. Log what was scheduled; make re-registration on restart a no-op.
4. **Tenancy is a hard requirement**, not a detail: `docs/recipes/search-by-meaning.md` §Interacts-with warns that background embedding crossing an async boundary must carry the ambient tenant or it "reads nothing and silently indexes nothing." The sweep must handle ambient tenant propagation and state plainly (docs + behavior) what happens in multi-tenant applications. Get this wrong silently and the feature is a booby trap.
5. **Enumeration cost**: backfill on a large table must stream (`AllStream` / batched queries), never `All()` a collection into memory.

## Constraints (framework law, not suggestions)

- Follow `.codex/skills/explore/SKILL.md` before writing production code.
- NativeAOT-clean throughout (`docs/guides/nativeaot-howto.md`): no `dynamic`, no IL emit, Newtonsoft only.
- **Standing rule: any `MakeGenericType` introduced on a changed contract gets swept repo-wide.**
- No breaking changes to `EmbeddingAttribute`'s existing surface; the flag defaults false, so existing apps are untouched — say so in the docs.

## Prove it

- Tests beside the existing AI suites (`tests/Koan.AI.Integration.Tests`, `tests/Koan.AI.EndToEnd.Tests`; harness patterns in `src/Koan.Testing`). Minimum coverage: (a) flag off → today's behavior byte-for-byte; (b) flag on + pre-existing rows → rows become searchable; (c) re-run → zero redundant embeds (assert via a counting fake of the embed client); (d) `Version` bump → only stale rows re-embed; (e) tenancy: sweep under an ambient tenant indexes that tenant's rows.
- Solution build: 0 errors; run the suites you touched plus their gates.
- **Docs move with the code**: `docs/recipes/search-by-meaning.md` Boundaries line changes to describe the opt-in backfill; check `docs/reference/capability-map.md`, `docs/reference/product-surface.md`, and the `[Embedding]` rows in `llms.txt`-indexed docs for staleness.
- Report: what changed, suite results (numbers), open design questions you hit, and any deviation from this brief with the reason.
