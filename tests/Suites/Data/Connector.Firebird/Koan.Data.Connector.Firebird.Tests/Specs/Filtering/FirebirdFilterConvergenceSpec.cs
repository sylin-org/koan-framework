using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Firebird.Tests.Specs.Filtering;

/// <summary>
/// Firebird derivation of the shared filter-convergence oracle (ARCH-0079): every case in the shared
/// corpus — collection operators included — must return the id-set the in-memory oracle computes.
///
/// <para>The pushdown half of the oracle is split to match this store's declared limits. Firebird ships
/// no JSON functions, so the adapter declares no collection operators pushable and the framework's floor
/// carries those cases; <see cref="FilterConvergence.AssertPushesDownAsync"/> would demand the store
/// lower them, so it is deliberately not hosted here. What IS proven instead:</para>
/// <list type="bullet">
/// <item>scalar filters are answered by the store — no fallback fact appears for them;</item>
/// <item>collection filters converge correctly AND record the fallback fact, so the store's limitation
/// is visible in runtime facts, never silent.</item>
/// </list>
/// </summary>
public sealed class FirebirdFilterConvergenceSpec(FirebirdFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<FirebirdFixture>(fixture, output)
{
    [Fact(DisplayName = "Firebird: every filter converges with the in-memory oracle")]
    public async Task Adapter_converges_with_oracle_across_the_corpus()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await FilterConvergence.AssertConvergesAsync();
    }

    [Fact(DisplayName = "Firebird: scalar filters are answered by the store, not memory")]
    public async Task Scalar_filters_never_fall_back()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await FilterConvergence.AssertConvergesAsync();

        await PushdownGuard.NothingFallsBack(host.Services, "the scalar filter corpus", async () =>
        {
            _ = await Data<ConvergenceWidget, string>.Query("{ \"Name\": \"Bravo\" }");
            _ = await Data<ConvergenceWidget, string>.Query("{ \"Level\": { \"$gt\": 15 } }");
            _ = await Data<ConvergenceWidget, string>.Query("{ \"Score\": { \"$nin\": [100, 300] } }");
            _ = await Data<ConvergenceWidget, string>.Query("{ \"Tier\": \"Pro\", \"Level\": { \"$lte\": 10 } }");
            _ = await Data<ConvergenceWidget, string>.Query("{ \"Name\": \"Al*\" }");
        });
    }

    [Fact(DisplayName = "Firebird: collection filters ride the declared residual and say so in facts")]
    public async Task Collection_residual_is_recorded_not_silent()
    {
        RequireBackingStore();
        // A fresh host: convergence across the whole corpus (this case included) is proven by its own
        // spec, and that run records the same fact this spec exists to observe.
        await using var host = await BootAsync();

        // The declaration is the contract: collection operators are not announced, so answering them
        // from the floor MUST be visible as a fallback fact — silence here would mean the store claimed
        // work it did not do.
        var before = PushdownGuard.Fallbacks(host.Services);
        _ = await Data<ConvergenceWidget, string>.Query("{ \"Tags\": { \"$in\": [\"ffxiv\"] } }");
        var after = PushdownGuard.Fallbacks(host.Services);

        after.Where(fact => !before.Contains(fact)).Should().NotBeEmpty(
            "a collection filter on this store must be recorded as a runtime fallback, per its declared limits");
    }
}
