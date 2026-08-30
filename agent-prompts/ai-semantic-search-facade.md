# Handoff prompt — `Todo.Ai.SemanticSearch(...)` type-level AI facade

Copy everything below the line into a fresh agent session.

---

You are working in the Koan framework repository at `F:\Replica\NAS\Files\repo\github\sylin-org\koan-framework` (branch `dev`). Koan is an opinionated .NET 10 meta-framework: **a package reference is the intent**, and entity types carry a static, fluent surface (`Todo.All()`, `Todo.Query(...)`, `Todo.Jobs.Schedule(...)`, `todo.Job.Submit()`).

## Read first (non-negotiable, in order)

1. `AGENTS.md` (routes you), then `CLAUDE.md` (contributor law — read it fully before changing production code).
2. `docs/MEMORY.md` (working conventions and hard-won lessons).
3. The precedent: `src/Koan.Jobs/JobAccessor.cs` lines 124–155, decision reference JOBS-0005 §12.14.

## The feature

Today, semantic search on an entity is callable but not entity-idiomatic. `Koan.Data.AI` ships `EntityEmbeddingExtensions.SemanticSearch<TEntity>(string query, int limit = 10, double threshold = 0.0, string? partition = null, CancellationToken ct)` and `SemanticSearchScored<TEntity>(...)` (`src/Koan.Data.AI/EntityEmbeddingExtensions.cs`) — plain static generics the caller must invoke as `EntityEmbeddingExtensions.SemanticSearch<Todo>(...)`, plus the lower-level shape `Client.Embed(q)` + `Vector<Todo>.Search(qv, s => s.Top(k))` taught in `docs/recipes/search-by-meaning.md`.

The maintainer wants the Jobs grammar extended to AI:

```csharp
var homelyTasks = Todo.Ai.SemanticSearch("errands around the house", s => s.Top(10), ct);
var scored      = Todo.Ai.SemanticSearchScored("quick wins", s => s.Top(10).Threshold(0.7), ct);
```

`Ai` is a **type-level static facade**, sibling to `Jobs`, so application code keeps saying `Todo`.

## How to build it (the verified mechanism)

`Todo.Jobs` is a **C# 14 static extension property** — no source generator, no `partial` requirement. Precedent at `src/Koan.Jobs/JobAccessor.cs:136-140`:

```csharp
extension<T>(T) where T : Entity<T>, IKoanJob<T>
{
    public static JobStatics<T> Jobs => default;
}
```

Mirror that in **`Koan.Data.AI`** (the package that already owns `EntityEmbeddingExtensions` and `EntityAi`):

- `extension<TEntity>(TEntity) where TEntity : Entity<TEntity> { public static EntityAiStatics<TEntity> Ai => default; }` — note: **no marker interface**. Unlike Jobs, AI needs no `IKoanJob<T>`-style opt-in; on-demand search works by convention, and `[Embedding]` gates only the save-time lifecycle (that split is documented on `EntityAi` and `EntityEmbeddingExtensions` — preserve it).
- `EntityAiStatics<TEntity>` (readonly struct, `=> default` pattern like `JobStatics<T>`) exposing:
  - `SemanticSearch(string query, Action<SemanticSearchQuery>? configure = null, CancellationToken ct = default)` → `Task<List<TEntity>>`
  - `SemanticSearchScored(string query, Action<SemanticSearchQuery>? configure = null, CancellationToken ct = default)` → `Task<List<(TEntity Entity, double Similarity)>>`
  - `SemanticSearchQuery` is a small fluent options struct mirroring the existing `Vector<T>.Search(query, s => s.Top(k))` shape: `Top(int)`, `Threshold(double)`, `Partition(string)`. Defaults must equal today's (`Top`=10, `Threshold`=0.0, `Partition`=null).
- The facades delegate to the existing `EntityEmbeddingExtensions` methods (refactor those to accept the options object if cleaner, but keep their existing signatures compiling — **no breaking change** to current callers; SnapVault uses them).
- Namespace: the extension members live in `Koan.Data.AI`, so `using Koan.Data.AI;` brings `Todo.Ai` into scope. Watch for collision confusion with the `Koan.AI` namespace (`Client`) and the existing `EntityAi` static class — delegate to them, don't duplicate or shadow.

## Constraints (framework law, not suggestions)

- Follow the explore skill (`.codex/skills/explore/SKILL.md`) before writing production code.
- AOT: everything must publish under NativeAOT (`docs/guides/nativeaot-howto.md`). No `dynamic`, no IL emit, no System.Text.Json reflection serialization — Koan's canonical serializer is Newtonsoft.
- **Standing rule from the maintainer's bench: any `MakeGenericType` introduced on a changed contract gets swept repo-wide.** The generic extension here is exactly the kind of surface that needs that sweep.
- Read `docs/guides/ai-vector-howto.md` and `docs/recipes/search-by-meaning.md` so the facade's semantics (threshold = minimum `Similarity`, top-k, partition passthrough, tenancy note in the recipe's "Interacts with" section) stay consistent with the taught behavior.

## Prove it

- Unit/integration tests beside the existing AI suites (see `tests/Koan.AI.Integration.Tests`, `tests/Koan.AI.EndToEnd.Tests` for harness patterns; `src/Koan.Testing` for fixtures). Cover: happy path over the InMemory/SqliteVec vector providers, `Top` clamping, `Threshold` filtering, `Partition` passthrough, and that the facade resolves for a plain `Entity<T>` with **no** `[Embedding]` attribute (convention path).
- Build the solution: 0 errors. Run the suites you touched plus anything they gate.
- Update `docs/recipes/search-by-meaning.md` to teach `Todo.Ai.SemanticSearch(...)` as the first shape (keep the explicit `Client.Embed` + `Vector<T>.Search` shape below it as the long form), and check `docs/reference/capability-map.md` and `docs/reference/product-surface.md` rows that describe semantic search for staleness.
- Report: what you changed, suite results (numbers), and any deviation from this brief with the reason. Update `docs/MEMORY.md` pointers if you created durable new docs, per its conventions.
