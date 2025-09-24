using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Mcp.Hosting;

/// <summary>
/// Placeholder for the HTTP + SSE transport planned for a future MCP phase.
/// </summary>
internal sealed class HttpSseTransport : BackgroundService
{
    private readonly ILogger<HttpSseTransport> _logger;

    public HttpSseTransport(ILogger<HttpSseTransport> logger)
    {
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HTTP + SSE MCP transport placeholder; implementation deferred to a future phase.");
        return Task.CompletedTask;
    }
}
