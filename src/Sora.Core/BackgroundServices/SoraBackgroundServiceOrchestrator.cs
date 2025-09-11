using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Core.Observability.Health;
using Sora.Core.Logging;
using System.Reflection;

namespace Sora.Core.BackgroundServices;

/// <summary>
/// Configuration options for background services
/// </summary>
public class SoraBackgroundServiceOptions
{
    public const string SectionName = "Sora:BackgroundServices";

    public bool Enabled { get; set; } = true;
    public int StartupTimeoutSeconds { get; set; } = 120;
    public bool FailFastOnStartupFailure { get; set; } = true;
    public Dictionary<string, ServiceConfiguration> Services { get; set; } = new();
}

/// <summary>
/// Per-service configuration
/// </summary>
public class ServiceConfiguration
{
    public bool Enabled { get; set; } = true;
    public int? IntervalSeconds { get; set; }
    public int? StartupOrder { get; set; }
    public int? Priority { get; set; }
    public Dictionary<string, object> Settings { get; set; } = new();
}

/// <summary>
/// Orchestrates all Sora background services
/// </summary>
public class SoraBackgroundServiceOrchestrator : BackgroundService, IHealthContributor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SoraBackgroundServiceOrchestrator> _logger;
    private readonly SoraBackgroundServiceOptions _options;
    private readonly List<ServiceExecutionContext> _runningServices = new();

    public string Name => "sora-background-services";
    public bool IsCritical => true;

    public SoraBackgroundServiceOrchestrator(
        IServiceProvider serviceProvider,
        ILogger<SoraBackgroundServiceOrchestrator> logger,
        IOptionsMonitor<SoraBackgroundServiceOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.CurrentValue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogSoraServices("disabled via configuration");
            return;
        }

        _logger.LogSoraServices("starting...");

        try
        {
            // Initialize service locator
            ServiceLocator.SetProvider(_serviceProvider);

            // Discover all background services
            var backgroundServices = _serviceProvider.GetServices<ISoraBackgroundService>().ToList();
            _logger.LogSoraServices($"discovered {backgroundServices.Count} background services");

            if (!backgroundServices.Any())
            {
                _logger.LogSoraServices("no background services found");
                await Task.Delay(Timeout.Infinite, stoppingToken);
                return;
            }

            var startedServices = new List<string>();

            // Start startup services first (in order)
            var startupServices = backgroundServices.OfType<ISoraStartupService>().ToList();
            if (startupServices.Any())
            {
                startedServices.AddRange(await StartStartupServices(startupServices, stoppingToken));
            }

            // Start regular background services
            var regularServices = backgroundServices.Where(s => s is not ISoraStartupService).ToList();
            if (regularServices.Any())
            {
                startedServices.AddRange(await StartBackgroundServices(regularServices, stoppingToken));
            }

            if (startedServices.Any())
                _logger.LogSoraServices($"started: {string.Join(", ", startedServices)}");
            else
                _logger.LogSoraServices("none started");

            // Wait for cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background service orchestrator failed");
            throw;
        }
    }

    private async Task<List<string>> StartStartupServices(IEnumerable<ISoraStartupService> startupServices, CancellationToken cancellationToken)
    {
        var orderedServices = startupServices.OrderBy(s => s.StartupOrder).ToList();
        var startedServices = new List<string>();

        foreach (var service in orderedServices)
        {
            if (!ShouldStartService(service))
                continue;

            try
            {
                _logger.LogTrace("Starting startup service: {ServiceName} (Order: {Order})",
                    service.Name, service.StartupOrder);

                await service.IsReadyAsync(cancellationToken);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(_options.StartupTimeoutSeconds));

                var task = service.ExecuteAsync(cts.Token);

                _runningServices.Add(new ServiceExecutionContext
                {
                    Service = service,
                    Task = task,
                    CancellationTokenSource = cts,
                    StartedAt = DateTimeOffset.UtcNow
                });

                // For startup services, wait for completion or timeout
                try
                {
                    await task;
                    _logger.LogTrace("Startup service completed: {ServiceName}", service.Name);
                    startedServices.Add(service.Name);
                }
                catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError("Startup service {ServiceName} timed out after {Timeout} seconds",
                        service.Name, _options.StartupTimeoutSeconds);

                    if (_options.FailFastOnStartupFailure)
                        throw new TimeoutException($"Startup service {service.Name} timed out");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Startup service failed: {ServiceName}", service.Name);

                var attr = service.GetType().GetCustomAttribute<SoraStartupServiceAttribute>();
                if (_options.FailFastOnStartupFailure && attr?.ContinueOnFailure != true)
                {
                    throw;
                }
            }
        }
        return startedServices;
    }

    private async Task<List<string>> StartBackgroundServices(IEnumerable<ISoraBackgroundService> services, CancellationToken cancellationToken)
    {
        var startedServices = new List<string>();
        foreach (var service in services)
        {
            if (!ShouldStartService(service))
                continue;

            try
            {
                _logger.LogTrace("Starting background service: {ServiceName}", service.Name);

                await service.IsReadyAsync(cancellationToken);

                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var task = Task.Run(() => service.ExecuteAsync(cts.Token), cts.Token);

                _runningServices.Add(new ServiceExecutionContext
                {
                    Service = service,
                    Task = task,
                    CancellationTokenSource = cts,
                    StartedAt = DateTimeOffset.UtcNow
                });

                _logger.LogTrace("Background service started: {ServiceName}", service.Name);
                startedServices.Add(service.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start background service: {ServiceName}", service.Name);
                // Continue with other services
            }
        }
        return startedServices;
    }

    private bool ShouldStartService(ISoraBackgroundService service)
    {
        var serviceName = service.Name;

        // Check global configuration
        if (_options.Services.TryGetValue(serviceName, out var config) && !config.Enabled)
        {
            _logger.LogInformation("Service {ServiceName} disabled via configuration", serviceName);
            return false;
        }

        // Check attribute configuration
        var attr = service.GetType().GetCustomAttribute<SoraBackgroundServiceAttribute>();
        if (attr != null)
        {
            if (!attr.Enabled)
            {
                _logger.LogInformation("Service {ServiceName} disabled via attribute", serviceName);
                return false;
            }

            // Check environment-specific settings
            var envName = SoraEnv.EnvironmentName;
            var shouldRun = envName switch
            {
                "Development" => attr.RunInDevelopment,
                "Production" => attr.RunInProduction,
                "Testing" => attr.RunInTesting,
                _ => true
            };

            if (!shouldRun)
            {
                _logger.LogInformation("Service {ServiceName} disabled for environment {Environment}",
                    serviceName, envName);
                return false;
            }
        }

        return true;
    }

    public Task<HealthReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var failedServices = _runningServices
            .Where(ctx => ctx.Task.IsCompleted && ctx.Task.IsFaulted)
            .ToList();

        if (failedServices.Any())
        {
            var failedNames = string.Join(", ", failedServices.Select(s => s.Service.Name));
            return Task.FromResult(HealthReport.Unhealthy($"Failed services: {failedNames}"));
        }

        var runningCount = _runningServices.Count(ctx => !ctx.Task.IsCompleted);
        var totalCount = _runningServices.Count;

        return Task.FromResult(HealthReport.Healthy(
            $"Background services: {runningCount}/{totalCount} running"));
    }

    public override void Dispose()
    {
        foreach (var context in _runningServices)
        {
            try
            {
                context.CancellationTokenSource?.Cancel();
                context.CancellationTokenSource?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing service context for {ServiceName}", context.Service.Name);
            }
        }

        base.Dispose();
    }

    private record ServiceExecutionContext
    {
        public ISoraBackgroundService Service { get; init; } = null!;
        public Task Task { get; init; } = null!;
        public CancellationTokenSource CancellationTokenSource { get; init; } = null!;
        public DateTimeOffset StartedAt { get; init; }
    }
}