using System;
using System.Net.WebSockets;

namespace Koan.WebSockets;

/// <summary>
/// Provides helpers for bridging <see cref="WebSocket"/> instances with <see cref="Stream"/> pipelines.
/// </summary>
public static class WebSocketStreamAdapter
{
    /// <summary>
    /// Creates a <see cref="WebSocketStream"/> representing the bidirectional WebSocket message channel.
    /// </summary>
    /// <param name="webSocket">The connected WebSocket instance.</param>
    /// <param name="options">Optional adapter configuration.</param>
    /// <returns>A <see cref="Stream"/>-compatible wrapper over the WebSocket.</returns>
    public static WebSocketStream Create(WebSocket webSocket, WebSocketStreamOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(webSocket);

        var resolved = options ?? WebSocketStreamOptions.Default;
        return WebSocketStream.Create(webSocket, resolved.MessageType, ownsWebSocket: !resolved.LeaveOpen);
    }

    /// <summary>
    /// Creates a readable <see cref="WebSocketStream"/> for consuming complete WebSocket messages as a <see cref="Stream"/> sequence.
    /// </summary>
    public static WebSocketStream CreateReadable(WebSocket webSocket)
    {
        ArgumentNullException.ThrowIfNull(webSocket);
        return WebSocketStream.CreateReadableMessageStream(webSocket);
    }

    /// <summary>
    /// Creates a writable <see cref="WebSocketStream"/> for emitting complete WebSocket messages as a <see cref="Stream"/>.
    /// </summary>
    public static WebSocketStream CreateWritable(WebSocket webSocket, WebSocketMessageType messageType = WebSocketMessageType.Binary)
    {
        ArgumentNullException.ThrowIfNull(webSocket);
        return WebSocketStream.CreateWritableMessageStream(webSocket, messageType);
    }

    /// <summary>
    /// Creates a <see cref="WebSocketStream"/> for the provided <see cref="ClientWebSocket"/> instance.
    /// </summary>
    public static WebSocketStream Create(ClientWebSocket webSocket, WebSocketStreamOptions? options = null)
        => Create((WebSocket)webSocket, options);
}
