using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Firebird.Tests.Specs;

/// <summary>
/// Firebird derivation of the sort oracle (ARCH-0079), split to match the store's declared limits:
/// <list type="bullet">
/// <item>every portable scalar orders on the store exactly as the framework's sorter would, both
/// directions, with the adapter's receipt claiming the key (scalar ordering);</item>
/// <item>a paged window is the real window of the whole ordering — on this store a collection key
/// rides the declared sort fallback, and the windows must still be exact;</item>
/// <item>scalar-order paged reads record no fallback fact.</item>
/// </list>
/// The collection-aggregate ordering (<c>Sightings.LastChangedAt</c>) and streams are deliberately not
/// hosted: Firebird has no JSON functions to compute the aggregate, and streaming is not announced —
/// the AODB suite proves the fail-closed rejection instead.
/// </summary>
public sealed class FirebirdSortPushdownSpec(FirebirdFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<FirebirdFixture>(fixture, output)
{
    [Fact(DisplayName = "Firebird: portable scalars order on the store in both directions")]
    public async Task Scalar_ordering_converges_and_is_claimed()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await SortPushdownConvergence.AssertScalarOrderingConvergesAsync(host.Services);
    }

    [Fact(DisplayName = "Firebird: a paged window is the real window of the whole ordering")]
    public async Task Paged_windows_stay_exact()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await SortPushdownConvergence.AssertPagesAsync();
    }

    [Fact(DisplayName = "Firebird: scalar-ordered pages record no fallback")]
    public async Task Scalar_pages_never_fall_back()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await SortPushdownConvergence.AssertScalarOrderingConvergesAsync(host.Services);
        await PushdownGuard.NothingFallsBack(host.Services, "scalar-ordered pages", async () =>
        {
            _ = await Data<SortedWidget, string>.Page(1, 2, "Sequence");
            _ = await Data<SortedWidget, string>.Page(1, 2, "-ObservedAt");
            _ = await Data<SortedWidget, string>.Page(1, 2, "Duration");
        });
    }
}
