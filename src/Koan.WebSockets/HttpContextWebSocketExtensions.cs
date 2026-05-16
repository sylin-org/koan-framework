using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Koan.WebSockets;

/// <summary>
/// Extension helpers for accepting WebSocket connections within ASP.NET Core hosts.
/// </summary>
public static class HttpContextWebSocketExtensions
{
    /// <summary>
    /// Accepts the current HTTP request as a WebSocket upgrade and returns a <see cref="WebSocketStream"/> wrapper.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="options">Optional adapter configuration.</param>
    /// <param name="cancellationToken">A cancellation token that aborts the handshake.</param>
    /// <returns>A bidirectional <see cref="WebSocketStream"/>.</returns>
    public static async Task<WebSocketStream> AcceptWebSocketStream(
        this HttpContext context,
        WebSocketStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.WebSockets.IsWebSocketRequest)
        {
            throw new InvalidOperationException("The current request is not a WebSocket upgrade.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var resolved = options ?? WebSocketStreamOptions.Default;
        var acceptContext = new WebSocketAcceptContext
        {
            SubProtocol = resolved.SubProtocol
        };

        var webSocket = await context.WebSockets.AcceptWebSocketAsync(acceptContext).ConfigureAwait(false);
        return WebSocketStreamAdapter.Create(webSocket, resolved);
    }
}
