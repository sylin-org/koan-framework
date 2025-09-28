using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
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
            _logger.LogWarning("No discovery adapter registered for service: {ServiceName}", serviceName);
            return AdapterDiscoveryResult.NoAdapter(serviceName);
        }

        context ??= new DiscoveryContext();

        _logger.LogDebug("Delegating discovery of {ServiceName} to {AdapterType}",
            serviceName, adapter.GetType().Name);

        try
        {
            // Pure delegation - "Adapter, discover yourself"
            var result = await adapter.DiscoverAsync(context, cancellationToken);

            _logger.LogInformation("Service {ServiceName} discovery result: {IsSuccessful} -> {ServiceUrl}",
                serviceName, result.IsSuccessful, result.ServiceUrl);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discovery adapter {AdapterType} failed for service {ServiceName}",
                adapter.GetType().Name, serviceName);
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

            _logger.LogInformation("Registered discovery adapter: {ServiceName} -> {AdapterType} (priority: {Priority})",
                serviceName, adapter.GetType().Name, adapter.Priority);
        }
    }
}