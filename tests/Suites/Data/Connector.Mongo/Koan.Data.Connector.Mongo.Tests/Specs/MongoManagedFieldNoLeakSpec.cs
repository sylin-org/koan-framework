using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Mongo.Tests.Specs;

/// <summary>
/// MongoDB derivation of the managed-field isolation oracle (<see cref="ManagedFieldNoLeak"/>, DATA-0105 §3b /
/// ARCH-0079) — the tenancy substrate proven on the real Mongo adapter (the first bare-store / non-Newtonsoft
/// realization): the invisible discriminator is injected as a BSON element, filtered via the shared
/// <c>FieldPathResolver</c> (no Mongo-specific read branch), and the conflict-aware <c>ReplaceOne</c> rejects a
/// cross-scope id-keyed overwrite (the filtered upsert hits an E11000 duplicate _id). Isolation · IDOR · takeover
/// rejected · scoped RemoveAll.
/// </summary>
public sealed class MongoManagedFieldNoLeakSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact(DisplayName = "Mongo: the managed-field discriminator isolates reads/writes/deletes (no leak)")]
    public async Task Managed_field_isolation_holds()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await ManagedFieldNoLeak.AssertNoLeakAsync();
    }
}
