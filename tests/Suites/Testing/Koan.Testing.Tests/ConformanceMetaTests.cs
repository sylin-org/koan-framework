using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace Koan.Testing.Tests;

/// <summary>
/// POSITIVE proof: a plain entity passes every applicable battery and skips the trait-gated ones
/// (Cacheable / Embedding) — xUnit discovers this subclass and runs the inherited batteries directly.
/// </summary>
public sealed class FakeWidgetConformance : Koan.Testing.EntityConformanceSpecs<FakeWidget>
{
    protected override FakeWidget NewValid() => new() { Name = "alpha", Level = 1 };
}

/// <summary>
/// TRAIT-GATING proof: a [Cacheable] entity makes the cache battery RUN (invalidate-on-delete) against
/// the default L1 memory cache, while the other batteries still pass.
/// </summary>
public sealed class CachedWidgetConformance : Koan.Testing.EntityConformanceSpecs<CachedWidget>
{
    protected override CachedWidget NewValid() => new() { Name = "beta" };
}

/// <summary>
/// TEETH proof: drive a deliberately-broken entity through the Paging battery directly (the class is
/// not public, so xUnit never auto-runs it) and assert the battery FAILS — the kit catches violations,
/// it doesn't pass vacuously.
/// </summary>
public class ConformanceBatteriesHaveTeeth
{
    private sealed class BrokenWidgetConformance : Koan.Testing.EntityConformanceSpecs<BrokenWidget>
    {
        // Every instance shares one id → UpsertMany collapses 23 rows into 1.
        protected override BrokenWidget NewValid() => new() { Id = "duplicate-id", Name = "x" };
    }

    [Fact]
    public async Task Paging_battery_fails_when_rows_are_lost()
    {
        var spec = new BrokenWidgetConformance();
        await ((IAsyncLifetime)spec).InitializeAsync();
        try
        {
            // The Paging battery asserts "every row exactly once"; with a shared id only one row exists,
            // so the xUnit assertion throws. A vacuous battery would not.
            Func<Task> battery = spec.Paging_returns_every_row_exactly_once;
            await battery.Should().ThrowAsync<Exception>(
                "the Paging battery must catch a row-count violation, proving it has teeth");
        }
        finally
        {
            await ((IAsyncLifetime)spec).DisposeAsync();
        }
    }
}
