using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.WebSockets.Tests;

internal sealed class TestWebSocket : WebSocket
{
    public bool DisposeInvoked { get; private set; }

    public WebSocketMessageType ReceiveMessageType { get; set; } = WebSocketMessageType.Binary;

    public WebSocketMessageType? LastSendMessageType { get; private set; }

    public override WebSocketCloseStatus? CloseStatus => null;

    public override string? CloseStatusDescription => null;

    public override string? SubProtocol => null;

    public override WebSocketState State => WebSocketState.Open;

    public override void Abort()
    {
    }

    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public override void Dispose()
    {
        DisposeInvoked = true;
    }

    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        => Task.FromResult(new WebSocketReceiveResult(0, ReceiveMessageType, true));

    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        LastSendMessageType = messageType;
        return Task.CompletedTask;
    }
}
