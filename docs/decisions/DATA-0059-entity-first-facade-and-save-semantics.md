# DATA-0059 - Entity-first facade and Save semantics (docs and vectors)

Status: Proposed

Date: 2025-08-21

## Context

Developers interact with entities most of the time, not repositories. Today, saving a document and saving its vector often require two different calls and sometimes different repository instances. This increases cognitive load, especially in samples and small services.

We want an entity-first facade that:

- Centralizes common actions (Save, Delete, Get) with predictable defaults
- Keeps vector operations explicit but co-located
- Supports batch operations ergonomically
- Preserves advanced escape hatches (direct commands, explicit repos)

## Decision

Introduce a static facade `Entity<T>` exposing role-focused helpers for common operations:

- `Entity<T>.Doc.Save(entity, ct)` - resolves Source adapter via attributes/defaults and saves the document
- `VectorData<T>.SaveWithVector(entity, embedding, metadata, ct)` - resolves the configured vector adapter and saves embeddings alongside the entity
- `Entity<T>.Doc.Get(id, ct)`
- `Entity<T>.Doc.Delete(id, ct)`
- Batch helpers:
  - `Entity<T>.Doc.SaveMany(IEnumerable<T> items, ct)`
  - `VectorData<T>.SaveManyWithVector(IEnumerable<VectorData<T>.VectorEntity> items, ct)`

Additionally, expose policy helpers for common orchestration:

- `VectorData<T>.SaveWithVector(entity, embedder(entity), metadata, ct)`
- A `VectorData<T>.VectorEntity` DTO: `{ T Entity; ReadOnlyMemory<float> Vector; string? Anchor; IDictionary<string, object>? Metadata; }`

These are thin wrappers over `IDataService` and repositories; they do not bypass configuration or options. Overloads may accept an optional adapter alias to override routing per call.

## Consequences

- Simplifies 80% of CRUD + vector workflows while keeping advanced scenarios intact
- Improves sample clarity and reduces boilerplate
- Encourages consistent naming (Save/SaveMany/Delete) instead of Upsert in public surface

## Alternatives considered

- Keep only repository-first API - flexible but verbose for common cases
- Extension methods on `IDataService` instead of a facade - viable, but `Entity<T>` groups the contract discovery better and reads naturally

## Migration notes

- Existing repository APIs remain. The facade is additive and can be adopted incrementally.
- Samples can migrate to the facade to demonstrate best practices.

## Example sketch

```csharp
// Save document
await Entity<AnimeDoc>.Doc.Save(doc, ct);

// Save vector after embedding
var ve = new VectorData<AnimeDoc>.VectorEntity(doc, vector);
await VectorData<AnimeDoc>.SaveManyWithVector(new[] { ve }, ct);

// Or orchestrated
await VectorData<AnimeDoc>.SaveWithVector(doc, ai.Embed(doc), new() { Model = "mxbai-embed-large" }, ct);
```

## Testability

- Unit tests for facade routing (attribute, override, default)
- Batch operations: happy path + partial failure handling
- Ensure exceptions flow through and are not swallowed

## Open questions

- Should `Entity<T>.Vector.Save` auto-create the vector schema/class if missing, or require Ensure/Init to be called explicitly? Default proposal: ensure lazily with an idempotent check, surfaced via adapters.
- Partial failures in `SaveMany`: default to fail-fast with aggregate exception vs. best-effort? Proposal: best-effort with a result object `{ Succeeded, Failed, Errors }`.
