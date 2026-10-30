using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Mongo.Tests.Specs;

/// <summary>
/// DATA-0162 on the real MongoDB adapter: a Get and a Remove over more ids than MongoDB binds as parameters in one
/// statement work with and without a read scope (<see cref="LargeIdentitySets"/>).
/// </summary>
public sealed class MongoLargeIdentitySetSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact(DisplayName = "Mongo: 70,000 ids are read and removed without a read scope")]
    public async Task Unscoped()
    {
        RequireBackingStore();
        await using var host = await BootAsync(LargeIdentitySets.Declare);
        await LargeIdentitySets.AssertUnscopedAsync();
    }

    [Fact(DisplayName = "Mongo: 70,000 ids are read and removed under a read scope")]
    public async Task Scoped()
    {
        RequireBackingStore();
        await using var host = await BootAsync(LargeIdentitySets.Declare);
        await LargeIdentitySets.AssertScopedAsync();
    }
}
