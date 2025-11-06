using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Koan.Mcp.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace S12.MedTrials.Infrastructure.Mcp;

internal sealed class McpCapabilityProbe : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<McpBridgeOptions> _options;
    private readonly ILogger<McpCapabilityProbe> _logger;
    private int _lastToolCount = -1;
    private int _lastTransportCount = -1;

    public McpCapabilityProbe(IHttpClientFactory httpClientFactory, IOptionsMonitor<McpBridgeOptions> options, ILogger<McpCapabilityProbe> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            if (!options.Enabled)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            await ProbeAsync(options, stoppingToken);

            var delay = options.GetProbeInterval();
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProbeAsync(McpBridgeOptions options, CancellationToken ct)
    {
        var baseUri = options.TryGetBaseUri();
        if (baseUri is null)
        {
            _logger.LogWarning("Skipping MCP capability probe because BaseUrl is not configured or invalid.");
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(McpHttpClientNames.McpBridge);
            var requestUri = new Uri(baseUri, "capabilities");
            using var response = await client.GetAsync(requestUri, ct);
            response.EnsureSuccessStatusCode();

            if (!options.LogCapabilities)
            {
                _logger.LogInformation("MCP capability probe succeeded (status {StatusCode}).", (int)response.StatusCode);
                return;
            }

            var document = await response.Content.ReadFromJsonAsync<McpCapabilityDocument>(cancellationToken: ct);
            if (document is null)
            {
                _logger.LogWarning("MCP capability probe returned no document from {Endpoint}.", requestUri);
                return;
            }

            var toolCount = document.Tools.Count;
            var transportCount = document.Transports.Count;

            if (_lastToolCount != toolCount || _lastTransportCount != transportCount)
            {
                _logger.LogInformation(
                    "MCP capability probe discovered {ToolCount} tools across {TransportCount} transports.",
                    toolCount,
                    transportCount);

                _lastToolCount = toolCount;
                _lastTransportCount = transportCount;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Ignore cancellation.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to probe MCP capabilities at {BaseUrl}.", options.BaseUrl);
        }
    }
}
