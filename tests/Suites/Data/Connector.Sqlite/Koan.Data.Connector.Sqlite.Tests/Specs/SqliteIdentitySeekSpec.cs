using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// Sqlite derivation of the identity-seek oracle (<see cref="IdentitySeekConvergence"/>; NK-179, NK-205, NK-207,
/// NK-306). Dockerless. A stream in identity order continues after the last identity it read, so it follows the
/// store's own order for every supported key type and neither skips nor repeats a row while another writer changes
/// the source.
/// </summary>
public sealed class SqliteIdentitySeekSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Fact(DisplayName = "Sqlite: string, Guid, int and long keys stream in the store's identity order")]
    public async Task Keyed_streams_follow_the_store_order()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await IdentitySeekConvergence.AssertKeyedStreamsFollowStoreOrderAsync();
    }

    [Fact(DisplayName = "Sqlite: deletes during a stream skip no row")]
    public async Task Deletes_during_a_stream_skip_nothing()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await IdentitySeekConvergence.AssertDeletesDuringReadSkipNothingAsync();
    }

    [Fact(DisplayName = "Sqlite: inserts during a stream repeat no row")]
    public async Task Inserts_during_a_stream_repeat_nothing()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await IdentitySeekConvergence.AssertInsertsDuringReadRepeatNothingAsync();
    }

    [Fact(DisplayName = "Sqlite: removing each streamed row visits every row")]
    public async Task Stream_and_remove_visits_every_row()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await IdentitySeekConvergence.AssertStreamAndRemoveVisitsEveryRowAsync();
    }
}
