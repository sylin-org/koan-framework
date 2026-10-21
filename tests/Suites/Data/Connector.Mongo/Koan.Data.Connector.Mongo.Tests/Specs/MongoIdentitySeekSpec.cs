using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Mongo.Tests.Specs;

/// <summary>
/// Mongo derivation of the identity-seek oracle (<see cref="IdentitySeekConvergence"/>; NK-179, NK-205, NK-207,
/// NK-306). Container-backed: skips without a reachable backing store. A stream in identity order continues after the last identity it read, so it follows the
/// store's own order for every supported key type and neither skips nor repeats a row while another writer changes
/// the source.
/// </summary>
public sealed class MongoIdentitySeekSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact(DisplayName = "Mongo: string, Guid, int and long keys stream in the store's identity order")]
    public async Task Keyed_streams_follow_the_store_order()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await IdentitySeekConvergence.AssertKeyedStreamsFollowStoreOrderAsync();
    }

    [Fact(DisplayName = "Mongo: deletes during a stream skip no row")]
    public async Task Deletes_during_a_stream_skip_nothing()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await IdentitySeekConvergence.AssertDeletesDuringReadSkipNothingAsync();
    }

    [Fact(DisplayName = "Mongo: inserts during a stream repeat no row")]
    public async Task Inserts_during_a_stream_repeat_nothing()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await IdentitySeekConvergence.AssertInsertsDuringReadRepeatNothingAsync();
    }

    [Fact(DisplayName = "Mongo: removing each streamed row visits every row")]
    public async Task Stream_and_remove_visits_every_row()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await IdentitySeekConvergence.AssertStreamAndRemoveVisitsEveryRowAsync();
    }
}
