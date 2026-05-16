using System;
using System.Net.WebSockets;
using Microsoft.Extensions.Options;

namespace Koan.WebSockets;

public interface IWebSocketStreamFactory
{
    WebSocketStream Create(WebSocket webSocket, WebSocketStreamOptions? options = null);

    WebSocketStream Create(ClientWebSocket webSocket, WebSocketStreamOptions? options = null);

    WebSocketStream CreateReadable(WebSocket webSocket);

    WebSocketStream CreateWritable(WebSocket webSocket, WebSocketMessageType? messageType = null);
}

internal sealed class WebSocketStreamFactory : IWebSocketStreamFactory
{
    private readonly IOptions<WebSocketStreamOptions> _options;

    public WebSocketStreamFactory(IOptions<WebSocketStreamOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public WebSocketStream Create(WebSocket webSocket, WebSocketStreamOptions? options = null)
    {
        return WebSocketStreamAdapter.Create(webSocket, options ?? _options.Value);
    }

    public WebSocketStream Create(ClientWebSocket webSocket, WebSocketStreamOptions? options = null)
    {
        return WebSocketStreamAdapter.Create(webSocket, options ?? _options.Value);
    }

    public WebSocketStream CreateReadable(WebSocket webSocket)
    {
        return WebSocketStreamAdapter.CreateReadable(webSocket);
    }

    public WebSocketStream CreateWritable(WebSocket webSocket, WebSocketMessageType? messageType = null)
    {
        var resolvedType = messageType ?? _options.Value.MessageType;
        return WebSocketStreamAdapter.CreateWritable(webSocket, resolvedType);
    }
}
