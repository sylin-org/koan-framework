using Koan.Data.AdapterSurface.TestKit;
using Koan.Data.Core;

namespace Koan.Data.Connector.Json.Tests.Specs;

/// <summary>
/// The JSON floor answers every query by holding the whole collection, and now says so (DATA-0119).
///
/// <para>It is not a defect — the floor has no query engine, and carrying it is what makes a bare reference
/// compose. The defect was silence. Because the floor applies every axis itself, no axis fell back, so the
/// coordinator's in-memory-fallback fact never fired for precisely the adapter where everything is in memory.
/// An application could not tell a bounded read from an unbounded one by asking.</para>
/// </summary>
public sealed class JsonUnboundedReadIsVisibleSpec(JsonFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<JsonFixture>(fixture, output)
{
    [Fact(DisplayName = "Json: an unbounded read is reported, not silent")]
    public async Task Unbounded_read_is_reported()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await SortPushdownConvergence.Corpus.ToList().Save();

        PushdownGuard.Fallbacks(host.Services).Should().BeEmpty("nothing has been read yet");

        _ = await Data<SortedWidget, string>.Page(1, 2, "Name");

        PushdownGuard.Fallbacks(host.Services).Should().ContainSingle()
            .Which.Should().Contain("was not bounded")
            .And.Contain("no query engine",
                "the floor materializes every candidate, and the fact must say that rather than name a layer");
    }
}
