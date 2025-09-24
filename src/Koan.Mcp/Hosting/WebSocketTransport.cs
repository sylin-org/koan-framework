using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Mcp.Hosting;

/// <summary>
/// Placeholder for the WebSocket transport planned for a future MCP phase.
/// </summary>
internal sealed class WebSocketTransport : BackgroundService
{
    private readonly ILogger<WebSocketTransport> _logger;

    public WebSocketTransport(ILogger<WebSocketTransport> logger)
    {
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WebSocket MCP transport placeholder; implementation deferred to a future phase.");
        return Task.CompletedTask;
    }
}
