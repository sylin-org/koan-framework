using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Model;
using Koan.Testing.Containers;
using Xunit;

namespace Koan.Data.Axes.Integration.Tests;

/// <summary>
/// ARCH-0101 §10 (Phase F) — the generalized <see cref="DataAxis.AssertNoLeak{TEntity,TKey}"/> isolation proof, through
/// a real <c>AddKoan()</c> boot (ARCH-0079), for a GENERIC (non-tenant) equality axis — the <see cref="RegionAxis"/>.
/// It rides the matrix legs the axis EQUIPS (read · get-by-id IDOR · cross-scope write-takeover · scoped DeleteMany /
/// DeleteAll / RemoveAll; the async-hop + cache legs self-skip — the region axis registers no carrier and the entity is
/// not <c>[Cacheable]</c>) — proving conformity-by-design: any value-isolation axis is proven by the identical call
/// (the tenant suite additionally drives the carrier + cache legs). And it actually DETECTS a leak: a no-axis entity
/// under a no-op "scope" throws on the read check, so the assertion is not vacuous.
/// </summary>
public sealed class AssertNoLeakSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Regional]
    public sealed class RegionDoc : Entity<RegionDoc> { public string Title { get; set; } = ""; }

    // No axis at all — used to prove the harness catches a non-isolating "axis".
    public sealed class UnscopedDoc : Entity<UnscopedDoc> { public string Title { get; set; } = ""; }

    [Fact(DisplayName = "AssertNoLeak proves a generic (region) equality axis isolates — read · IDOR · scoped delete")]
    public async Task AssertNoLeak_proves_the_region_axis()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        await DataAxis.AssertNoLeak<RegionDoc, string>(RegionAmbient.Use, "r1", "r2");
    }

    [Fact(DisplayName = "AssertNoLeak DETECTS a leak: a no-axis entity under a no-op scope throws on the read check")]
    public async Task AssertNoLeak_detects_a_leak()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        // No isolation axis + a no-op "scope" ⇒ context A sees context B's rows ⇒ the read check must throw (the proof
        // is not vacuous: a non-isolating axis fails it).
        var act = async () => await DataAxis.AssertNoLeak<UnscopedDoc, string>(_ => Noop.Instance, "x", "y");

        (await act.Should().ThrowAsync<DataAxisLeakDetectedException>()).Which.Check.Should().Be("read");
    }

    private sealed class Noop : IDisposable
    {
        public static readonly Noop Instance = new();
        public void Dispose() { }
    }
}
