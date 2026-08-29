using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.CouchDb.Tests.Specs.Filtering;

/// <summary>
/// CouchDB derivation of the shared filter oracle (ARCH-0079). Mango lowers the whole scalar set and
/// the collection set (has/any/all/none/size, with bare element equality parser-lowered to has), so
/// unlike the JSON-less relational siblings this store hosts the full pushdown guard.
/// </summary>
public sealed class CouchDbFilterConvergenceSpec(CouchDbFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<CouchDbFixture>(fixture, output)
{
    [Fact(DisplayName = "CouchDb: every filter converges with the in-memory oracle")]
    public async Task Adapter_converges_with_oracle_across_the_corpus()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await FilterConvergence.AssertConvergesAsync();
    }

    [Fact(DisplayName = "CouchDb: declared scalar and collection filters are answered by the store")]
    public async Task Declared_filters_never_fall_back()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await FilterConvergence.AssertConvergesAsync();

        await PushdownGuard.NothingFallsBack(host.Services, "the declared filter corpus", async () =>
        {
            _ = await Data<ConvergenceWidget, string>.Query("{ \"Name\": \"Bravo\" }");
            _ = await Data<ConvergenceWidget, string>.Query("{ \"Level\": { \"$gt\": 15 } }");
            _ = await Data<ConvergenceWidget, string>.Query("{ \"Tags\": { \"$in\": [\"ffxiv\"] } }");
            _ = await Data<ConvergenceWidget, string>.Query("{ \"Tags\": { \"$all\": [\"ffxiv\", \"wow\"] } }");
            _ = await Data<ConvergenceWidget, string>.Query("{ \"Tags\": { \"$size\": 1 } }");
            _ = await Data<ConvergenceWidget, string>.Query("{ \"Tier\": \"Pro\", \"Tags\": { \"$nin\": [\"gw2\"] } }");
        });
    }

    [Fact(DisplayName = "CouchDb: the whole shared corpus is answered by the store, not memory")]
    public async Task The_whole_corpus_pushes_down()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        // Every corpus operator is declared on this adapter — including bare equality on a collection,
        // which the parser lowers to element-match and Mango answers with $all. The one declared limit
        // is element-LIKE ($like inside an array): Mango's $regex does not cross array elements, so the
        // posture is pinned residual-and-recorded and the guard holds the adapter to exactly that.
        await FilterConvergence.AssertPushesDownAsync(host.Services, expectsHasContainsPushdown: false);
    }
}
