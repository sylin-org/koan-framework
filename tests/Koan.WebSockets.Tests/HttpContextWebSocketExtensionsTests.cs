using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.WebSockets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Xunit;

namespace Koan.WebSockets.Tests;

public class HttpContextWebSocketExtensionsTests
{
    [Fact]
    public async Task AcceptWebSocketStreamAsync_UsesOptionsAndLeavesSocketOpen()
    {
        var socket = new TestWebSocket();
        var feature = new TestWebSocketFeature { IsWebSocketRequest = true, WebSocketToReturn = socket };
        var context = CreateHttpContext(feature);
        var options = new WebSocketStreamOptions
        {
            SubProtocol = "proto.v1",
            MessageType = WebSocketMessageType.Text,
            LeaveOpen = true
        };

        await using (var stream = await context.AcceptWebSocketStream(options))
        {
            stream.Should().NotBeNull();
            feature.Accepted.Should().BeTrue();
            feature.LastAcceptContext!.SubProtocol.Should().Be("proto.v1");

            var payload = new byte[] { 1, 2, 3 };
            await stream.WriteAsync(payload, 0, payload.Length, CancellationToken.None);
        }

        socket.LastSendMessageType.Should().Be(WebSocketMessageType.Text);
        socket.DisposeInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task AcceptWebSocketStreamAsync_WhenNotWebSocketRequest_Throws()
    {
        var feature = new TestWebSocketFeature { IsWebSocketRequest = false };
        var context = CreateHttpContext(feature);

        await FluentActions
            .Invoking(() => context.AcceptWebSocketStream())
            .Should()
            .ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AcceptWebSocketStreamAsync_WhenLeaveOpenFalse_DisposesSocket()
    {
        var socket = new TestWebSocket();
        var feature = new TestWebSocketFeature { IsWebSocketRequest = true, WebSocketToReturn = socket };
        var context = CreateHttpContext(feature);

        await using (var stream = await context.AcceptWebSocketStream())
        {
            var payload = new byte[] { 42 };
            await stream.WriteAsync(payload, 0, payload.Length, CancellationToken.None);
        }

        socket.DisposeInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task AcceptWebSocketStreamAsync_WhenCancelled_Throws()
    {
        var feature = new TestWebSocketFeature { IsWebSocketRequest = true };
        var context = CreateHttpContext(feature);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await FluentActions
            .Invoking(() => context.AcceptWebSocketStream(cancellationToken: cts.Token))
            .Should()
            .ThrowAsync<OperationCanceledException>();

        feature.Accepted.Should().BeFalse();
    }

    private static HttpContext CreateHttpContext(IHttpWebSocketFeature feature)
    {
        var features = new FeatureCollection();
        features.Set(feature);
        return new DefaultHttpContext(features);
    }

    private sealed class TestWebSocketFeature : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest { get; set; }

        public bool Accepted { get; private set; }

        public WebSocketAcceptContext? LastAcceptContext { get; private set; }

        public TestWebSocket? WebSocketToReturn { get; set; }

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context)
        {
            Accepted = true;
            LastAcceptContext = context;
            var socket = WebSocketToReturn ?? new TestWebSocket();
            return Task.FromResult<WebSocket>(socket);
        }

        public Task<WebSocket> AcceptAsync(string subProtocol) => AcceptAsync(new WebSocketAcceptContext { SubProtocol = subProtocol });

        public void Abort() { }
    }
}
