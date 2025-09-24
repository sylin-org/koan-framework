using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Observability.Health;
using Koan.Mcp.Execution;
using Koan.Mcp.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Mcp.Hosting;

public sealed class StdioTransport : BackgroundService
{
    private readonly IMcpTransportDispatcher _dispatcher;
    private readonly McpEntityRegistry _registry;
    private readonly EndpointToolExecutor _executor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<StdioTransport> _logger;
    private readonly IOptionsMonitor<McpServerOptions> _optionsMonitor;
    private readonly IHealthAggregator? _healthAggregator;

    private readonly string _healthComponent = "mcp-stdio";
    private DateTimeOffset _startedAtUtc;
    private DateTimeOffset _lastHeartbeatUtc;
    private int _entityCount;
    private int _toolCount;

    public StdioTransport(
        IMcpTransportDispatcher dispatcher,
        McpEntityRegistry registry,
        EndpointToolExecutor executor,
        ILoggerFactory loggerFactory,
        ILogger<StdioTransport> logger,
        IOptionsMonitor<McpServerOptions> optionsMonitor,
        IHealthAggregator? healthAggregator = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _healthAggregator = healthAggregator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var options = _optionsMonitor.CurrentValue;
        var transportLogger = _loggerFactory.CreateLogger(options.Transport.LoggerCategory ?? "Koan.Transport.Mcp");

        var registrations = _registry.RegistrationsForStdio();
        var globallyEnabled = options.EnableStdioTransport;

        if (!globallyEnabled && registrations.Count == 0)
        {
            _logger.LogInformation("MCP STDIO transport disabled via configuration.");
            transportLogger.LogInformation("STDIO transport disabled by configuration.");
            ResetMetrics();
            PublishTransportHealth(HealthStatus.Healthy, "STDIO transport disabled (configuration).", options, 0, 0);
            return;
        }

        if (registrations.Count == 0)
        {
            _logger.LogInformation("No MCP entities opted into STDIO transport. Transport will remain idle.");
            transportLogger.LogInformation("STDIO transport idle: no registered entities.");
            ResetMetrics();
            PublishTransportHealth(HealthStatus.Healthy, "STDIO transport idle (no registered entities).", options, 0, 0);
            return;
        }

        if (!globallyEnabled)
        {
            _logger.LogInformation("STDIO transport globally disabled but {EntityCount} entity/entities opted in; starting limited session.", registrations.Count);
            transportLogger.LogInformation("STDIO transport limited mode with {EntityCount} entities.", registrations.Count);
        }

        Console.SetOut(Console.Error);

        var handlerLogger = _loggerFactory.CreateLogger<McpRpcHandler>();
        var handler = new McpRpcHandler(_registry, _executor, handlerLogger);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _entityCount = registrations.Count;
        _toolCount = registrations.Sum(r => r.Tools.Count);
        _startedAtUtc = DateTimeOffset.UtcNow;
        _lastHeartbeatUtc = _startedAtUtc;

        var heartbeatTask = RunHeartbeatAsync(options.Transport.HeartbeatInterval, transportLogger, linkedCts.Token, options);

        _logger.LogInformation("Starting MCP STDIO transport with {ToolCount} tools across {EntityCount} entities.", _toolCount, _entityCount);
        transportLogger.LogInformation("STDIO transport online with {ToolCount} tools.", _toolCount);
        PublishTransportHealth(HealthStatus.Healthy, "STDIO transport online.", options, _entityCount, _toolCount);

        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();

        try
        {
            await _dispatcher.RunAsync(handler, input, output, linkedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            await linkedCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected during shutdown
            }

            _logger.LogInformation("MCP STDIO transport stopped.");
            transportLogger.LogInformation("STDIO transport offline.");
            PublishTransportHealth(HealthStatus.Degraded, "STDIO transport offline.", options, _entityCount, _toolCount);
        }
    }

    private async Task RunHeartbeatAsync(TimeSpan interval, ILogger transportLogger, CancellationToken cancellationToken, McpServerOptions options)
    {
        if (interval <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                _lastHeartbeatUtc = DateTimeOffset.UtcNow;
                transportLogger.LogDebug("STDIO heartbeat {Timestamp:O}.", _lastHeartbeatUtc);
                PublishTransportHealth(HealthStatus.Healthy, "STDIO heartbeat.", options, _entityCount, _toolCount);
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
    }

    private void PublishTransportHealth(HealthStatus status, string message, McpServerOptions options, int entityCount, int toolCount)
    {
        if (_healthAggregator is null)
        {
            return;
        }

        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["entities"] = entityCount.ToString(CultureInfo.InvariantCulture),
            ["tools"] = toolCount.ToString(CultureInfo.InvariantCulture),
            ["heartbeatIntervalSeconds"] = Math.Max(0, options.Transport.HeartbeatInterval.TotalSeconds).ToString(CultureInfo.InvariantCulture)
        };

        if (_startedAtUtc != default)
        {
            facts["startedAtUtc"] = _startedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        }

        if (_lastHeartbeatUtc != default)
        {
            facts["lastHeartbeatUtc"] = _lastHeartbeatUtc.ToString("O", CultureInfo.InvariantCulture);
        }

        _healthAggregator.Push(_healthComponent, status, message, facts: facts);
    }

    private void ResetMetrics()
    {
        _startedAtUtc = default;
        _lastHeartbeatUtc = default;
        _entityCount = 0;
        _toolCount = 0;
    }
}


