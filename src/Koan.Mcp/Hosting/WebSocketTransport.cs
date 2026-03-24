using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Mcp.Hosting;

/// <summary>
/// Placeholder for the WebSocket transport planned for a future MCP phase.
/// </summary>
internal sealed class WebSocketTransport(ILogger<WebSocketTransport> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("WebSocket MCP transport placeholder; implementation deferred to a future phase.");
        return Task.CompletedTask;
    }
}
