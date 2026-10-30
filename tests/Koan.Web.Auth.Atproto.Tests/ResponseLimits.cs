using System.Net;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Microsoft.AspNetCore.Authentication;
using Xunit;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>
/// A response sent for a caller streams, read no further than the limit the caller sets, or whole for a caller that
/// streams; the protocol documents the connector reads itself keep their bound.
/// </summary>
public sealed class ResponseLimits
{
    private const int FiveMegabytes = 5 * 1024 * 1024;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_response_over_four_megabytes_is_read_through_send_within_the_callers_limit()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var did = world.Dids[0];
        var node = world.Node();
        await world.SignIn(node, did);
        world.Pds.BlobBytes = FiveMegabytes;

        using (var declared = await node.Sessions.Send(did, "app.fixture.blob", HttpMethod.Get, 8 * 1024 * 1024, ct: Ct))
            Assert.Equal(FiveMegabytes, (await declared.Content.ReadAsByteArrayAsync(Ct)).Length);
        using (var streamed = await node.Sessions.Send(did, "app.fixture.stream", HttpMethod.Get, FiveMegabytes, ct: Ct))
            Assert.Equal(FiveMegabytes, await Length(streamed));
        using (var unbounded = await node.Sessions.Send(did, "app.fixture.stream", HttpMethod.Get, maximumResponseBytes: null, ct: Ct))
            Assert.Equal(FiveMegabytes, await Length(unbounded));
    }

    [Fact]
    public async Task A_response_longer_than_the_callers_limit_is_refused_whether_it_declares_its_length_or_not()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var did = world.Dids[0];
        var node = world.Node();
        await world.SignIn(node, did);
        world.Pds.BlobBytes = FiveMegabytes;

        var declared = await Assert.ThrowsAsync<HttpRequestException>(() =>
            node.Sessions.Send(did, "app.fixture.blob", HttpMethod.Get, 1024 * 1024, ct: Ct));
        Assert.Contains("declares 5242880 bytes", declared.Message);

        using var streamed = await node.Sessions.Send(did, "app.fixture.stream", HttpMethod.Get, 1024 * 1024, ct: Ct);
        var read = await Assert.ThrowsAsync<IOException>(() => Length(streamed));
        Assert.Contains("1048576 bytes the caller reads", read.Message);
    }

    [Fact]
    public async Task Caller_owned_requests_take_the_callers_limit_and_discovery_keeps_its_bound()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var node = world.Node(options => options.MaximumProtocolResponseBytes = 64 * 1024);
        world.Pds.BlobBytes = FiveMegabytes;

        using (var request = new HttpRequestMessage(HttpMethod.Get, world.Pds.Origin + "/fixture/stream"))
        using (var response = await node.Network.Send(request, FiveMegabytes, Ct))
            Assert.Equal(FiveMegabytes, await Length(response));
        using (var request = new HttpRequestMessage(HttpMethod.Get, world.Pds.Origin + "/fixture/stream"))
        using (var response = await node.Network.Send(request, FiveMegabytes - 1, Ct))
            await Assert.ThrowsAsync<IOException>(() => Length(response));

        world.Pds.MetadataPadding = 128 * 1024;
        var start = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            node.Sessions.StartServer(world.Pds.Origin, new AuthenticationProperties(), "browser", Ct));
        Assert.Contains(Messages(start), message => message.Contains("exceeded the configured byte bound"));
    }

    [Theory]
    [InlineData("/fixture/stream", null, 1000L)]
    [InlineData("/fixture/stream", 0L, -1L)]
    [InlineData("/fixture/stream", 1L, -1L)]
    [InlineData("/fixture/stream", 999L, -1L)]
    [InlineData("/fixture/stream", 1000L, 1000L)]
    [InlineData("/fixture/stream", long.MaxValue, 1000L)]
    [InlineData("/fixture/blob", null, 1000L)]
    [InlineData("/fixture/blob", 0L, -1L)]
    [InlineData("/fixture/blob", 1L, -1L)]
    [InlineData("/fixture/blob", 999L, -1L)]
    [InlineData("/fixture/blob", 1000L, 1000L)]
    [InlineData("/fixture/blob", long.MaxValue, 1000L)]
    public async Task Every_limit_from_zero_to_the_largest_reads_the_whole_body_or_refuses_it(string path, long? limit, long expected)
    {
        await using var world = await SessionWorld.Start(accounts: 0);
        var node = world.Node();
        world.Pds.BlobBytes = 1000;

        long read;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, world.Pds.Origin + path);
            using var response = await node.Network.Send(request, limit, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            read = await Length(response);
        }
        catch (Exception error) when (error is HttpRequestException or IOException) { read = -1; }

        Assert.Equal(expected, read);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(long.MaxValue)]
    public async Task An_empty_body_is_read_whole_at_any_limit(long limit)
    {
        await using var world = await SessionWorld.Start(accounts: 0);
        var node = world.Node();
        world.Pds.BlobBytes = 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, world.Pds.Origin + "/fixture/stream");
        using var response = await node.Network.Send(request, limit, Ct);

        Assert.Equal(0, await Length(response));
    }

    private static async Task<long> Length(HttpResponseMessage response)
    {
        await using var body = await response.Content.ReadAsStreamAsync(Ct);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await body.ReadAsync(buffer, Ct)) > 0) total += read;
        return total;
    }

    private static IEnumerable<string> Messages(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException) yield return current.Message;
    }
}
