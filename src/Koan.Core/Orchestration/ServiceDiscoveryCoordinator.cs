using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Koan.Core.Logging;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Core.Orchestration;

/// <summary>
/// Pure delegation coordinator - routes to adapters, aggregates results.
/// Zero provider-specific knowledge.
/// </summary>
internal sealed class ServiceDiscoveryCoordinator : IServiceDiscoveryCoordinator
{
    private readonly ConcurrentDictionary<string, IServiceDiscoveryAdapter> _adapters = new();
    private readonly ILogger<ServiceDiscoveryCoordinator> _logger;

    public ServiceDiscoveryCoordinator(
        IEnumerable<IServiceDiscoveryAdapter> adapters,
        ILogger<ServiceDiscoveryCoordinator> logger)
    {
        _logger = logger;
        RegisterAdapters(adapters);
    }

    public async Task<AdapterDiscoveryResult> DiscoverServiceAsync(
        string serviceName,
        DiscoveryContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (!_adapters.TryGetValue(serviceName.ToLowerInvariant(), out var adapter))
        {
            KoanLog.ConfigWarning(_logger, LogActions.Lookup, "no-adapter", ("service", serviceName));
            return AdapterDiscoveryResult.NoAdapter(serviceName);
        }

        context ??= new DiscoveryContext();

        KoanLog.ConfigDebug(_logger, LogActions.Delegate, null,
            ("service", serviceName),
            ("adapter", adapter.GetType().Name));

        try
        {
            // Pure delegation - "Adapter, discover yourself"
            var result = await adapter.DiscoverAsync(context, cancellationToken);

            var outcome = result.IsSuccessful ? LogOutcomes.Success : LogOutcomes.Failure;
            KoanLog.ConfigInfo(_logger, LogActions.Result, outcome,
                ("service", serviceName),
                ("url", result.ServiceUrl));

            return result;
        }
        catch (Exception ex)
        {
            KoanLog.ConfigError(_logger, LogActions.Result, "exception",
                ("service", serviceName),
                ("adapter", adapter.GetType().Name),
                ("error", ex.Message));
            return AdapterDiscoveryResult.Failed(serviceName, $"Adapter exception: {ex.Message}");
        }
    }

    public IServiceDiscoveryAdapter[] GetRegisteredAdapters() =>
        _adapters.Values.ToArray();

    private void RegisterAdapters(IEnumerable<IServiceDiscoveryAdapter> adapters)
    {
        foreach (var adapter in adapters.OrderByDescending(a => a.Priority))
        {
            RegisterAdapter(adapter);
        }
    }

    private void RegisterAdapter(IServiceDiscoveryAdapter adapter)
    {
        var serviceNames = new[] { adapter.ServiceName }.Concat(adapter.Aliases);

        foreach (var serviceName in serviceNames)
        {
            var key = serviceName.ToLowerInvariant();
            _adapters.AddOrUpdate(key, adapter, (_, existing) =>
                adapter.Priority > existing.Priority ? adapter : existing);

            KoanLog.BootInfo(_logger, LogActions.Register, "adapter",
                ("service", serviceName),
                ("adapter", adapter.GetType().Name),
                ("priority", adapter.Priority));
        }
    }

    private static class LogActions
    {
        public const string Lookup = "discovery.lookup";
        public const string Delegate = "discovery.delegate";
        public const string Result = "discovery.result";
        public const string Register = "discovery.register";
    }

    private static class LogOutcomes
    {
        public const string Success = "success";
        public const string Failure = "failure";
    }
}