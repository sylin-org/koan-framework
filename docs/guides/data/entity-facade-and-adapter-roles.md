# Entity-first facade and adapter roles — technical spec

Updated: 2025-08-21

This spec supports DATA-0058 and DATA-0059. See DATA-0060 for the vector module split.

## Goals

- Make multi-adapter routing explicit and predictable via attributes
- Provide an ergonomic entity-first facade that covers the 80% path
- Keep escape hatches (explicit repos, Direct commands) untouched

## Scope

- New attributes: `SourceAdapterAttribute`, `VectorAdapterAttribute`
- `DataService` resolution changes to honor role attributes
- `Entity<T>` facade (Doc/Vector) with Save/Get/Delete and batch helpers
- Result types for batch operations and error propagation

## Attribute design

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class SourceAdapterAttribute(string provider) : Attribute
{
    public string Provider { get; } = provider;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class VectorAdapterAttribute(string provider) : Attribute
{
    public string Provider { get; } = provider;
}
```

- No tunables in attributes; providers read typed options
- Continue using `[Storage(Name, Namespace?)]` for set/class names

## Resolution precedence

1) Per-call override (adapter alias or explicit repo)
2) Role attribute on entity (Source/Vector)
3) App/module default for that role
4) Fail fast with clear diagnostics when unresolved

## DataService changes (sketch)

```csharp
public partial class DataService : IDataService
{
    public IRepository<T, TId> GetRepository<T, TId>()
    {
        var provider = ResolveSourceProvider(typeof(T));
        return _docRepos.GetOrCreate(provider, () => CreateDocRepo<T, TId>(provider));
    }

    public IVectorRepository<T, TId> GetVectorRepository<T, TId>()
    {
        var provider = ResolveVectorProvider(typeof(T));
        return _vecRepos.GetOrCreate(provider, () => CreateVecRepo<T, TId>(provider));
    }
}
```

- `ResolveSourceProvider` and `ResolveVectorProvider` look up attributes, then defaults

## Entity facade (sketch)

```csharp
// Note: vector helpers moved to Sora.Data.Vector (see DATA-0060)
public static class Entity<T>
{
    public static class Doc
    {
        public static Task Save(T entity, CancellationToken ct = default);
        public static Task SaveMany(IEnumerable<T> entities, CancellationToken ct = default);
        public static Task<T?> Get(object id, CancellationToken ct = default);
        public static Task Delete(object id, CancellationToken ct = default);
    }

    public static class Vector
    {
        public static Task Save<TId>(VectorEntity<T> ve, CancellationToken ct = default);
        public static Task SaveMany<TId>(IEnumerable<VectorEntity<T>> items, CancellationToken ct = default);
    }

    public static Task SaveWithVector(
        T entity,
        Func<T, ValueTask<ReadOnlyMemory<float>>> embed,
        object? vectorizerOptions,
        CancellationToken ct = default);
}

public readonly record struct VectorEntity<T>(T Entity, ReadOnlyMemory<float> Vector, string? Anchor = null, IReadOnlyDictionary<string, object>? Metadata = null);
```

- Implementation thinly delegates to `IDataService`
- Overloads may accept an optional adapter alias to override per call

## Batch results

Introduce a non-throwing batch result for SaveMany, with aggregate error bag:

```csharp
public sealed class BatchResult
{
    public int Succeeded { get; init; }
    public int Failed { get; init; }
    public IReadOnlyList<Exception> Errors { get; init; } = Array.Empty<Exception>();
}
```

Default behavior: best-effort; return `BatchResult`. For fail-fast semantics, provide `SaveManyOrThrow` variants.

## Tests

- Attribute resolution precedence (role vs default vs override)
- Doc and Vector repos obtained correctly for `AnimeDoc` sample
- SaveWithVector happy path + embedding exception propagates
- Batch SaveMany result accounting

## Notes for samples

- Annotate domain models:

```csharp
[SourceAdapter("mongo")]
[VectorAdapter("weaviate")]
[Storage(Name = "Anime")]
public sealed class AnimeDoc : IEntity<string> { /* ... */ }
```

- Update seeding to prefer `Entity<T>` helpers where appropriate, while keeping explicit repository code in advanced sections

## Open items

- Decide on sync vs async batch vector upserts API shape — align with adapters
- Ensure schema/class Ensure policy is consistent across adapters (lazy idempotent)
