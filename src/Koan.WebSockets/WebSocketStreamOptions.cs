using System;
using System.Net.WebSockets;

namespace Koan.WebSockets;

/// <summary>
/// Options controlling how Koan exposes a <see cref="WebSocket"/> as a <see cref="WebSocketStream"/>.
/// </summary>
public sealed class WebSocketStreamOptions
{
    /// <summary>
    /// Gets the singleton default instance.
    /// </summary>
    public static WebSocketStreamOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets the negotiated WebSocket sub-protocol. Applies when accepting server-side connections.
    /// </summary>
    public string? SubProtocol { get; init; }

    /// <summary>
    /// Gets or sets the message type used when framing outgoing payloads. Defaults to <see cref="WebSocketMessageType.Binary"/>.
    /// </summary>
    public WebSocketMessageType MessageType { get; init; } = WebSocketMessageType.Binary;

    /// <summary>
    /// Gets or sets a value indicating whether the underlying <see cref="WebSocket"/> should remain open when the stream is disposed.
    /// </summary>
    public bool LeaveOpen { get; init; }
}
