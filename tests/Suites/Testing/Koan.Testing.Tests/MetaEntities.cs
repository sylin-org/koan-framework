using Koan.Cache.Abstractions.Policies;
using Koan.Data.Core.Model;

namespace Koan.Testing.Tests;

/// <summary>A plain entity (no traits) for the positive end-to-end proof of the kit's batteries.</summary>
public sealed class FakeWidget : Entity<FakeWidget>
{
    public string Name { get; set; } = "widget";
    public int Level { get; set; }
}

/// <summary>A [Cacheable] entity so the cache battery RUNS (not skipped) and is exercised against L1.</summary>
[Cacheable(60)]
public sealed class CachedWidget : Entity<CachedWidget>
{
    public string Name { get; set; } = "cached";
}

/// <summary>Deliberately broken: every instance shares one id, so UpsertMany collapses to a single row —
/// the Paging battery MUST catch the row-count violation (proves the battery has teeth).</summary>
public sealed class BrokenWidget : Entity<BrokenWidget>
{
    public string Name { get; set; } = "broken";
}
