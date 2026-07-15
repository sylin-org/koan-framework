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
    private readonly IDiscoveryCandidateContributor[] _contributors;
    private readonly ILogger<ServiceDiscoveryCoordinator> _logger;

    public ServiceDiscoveryCoordinator(
        IEnumerable<IServiceDiscoveryAdapter> adapters,
        IEnumerable<IDiscoveryCandidateContributor> contributors,
        ILogger<ServiceDiscoveryCoordinator> logger)
    {
        _logger = logger;
        _contributors = contributors?.ToArray() ?? [];
        RegisterAdapters(adapters);
    }

    public async Task<AdapterDiscoveryResult> DiscoverService(
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

        // Gather candidates from external discovery contributors (Zen Garden / Koi, if present) and fold them
        // into the context so the adapter's health-checked probe treats them as candidates — not authoritative
        // short-circuits. A contributor failure must never break discovery, so each is guarded.
        context = await ApplyContributedCandidates(serviceName, context, cancellationToken);

        KoanLog.ConfigDebug(_logger, LogActions.Delegate, null,
            ("service", serviceName),
            ("adapter", adapter.GetType().Name));

        try
        {
            // Pure delegation - "Adapter, discover yourself"
            var result = await adapter.Discover(context, cancellationToken);

            var outcome = result.IsSuccessful ? LogOutcomes.Success : LogOutcomes.Failure;
            KoanLog.ConfigInfo(_logger, LogActions.Result, outcome,
                ("service", serviceName),
                ("url", Redaction.DeIdentify(result.ServiceUrl)));

            return result;
        }
        catch (Exception ex)
        {
            KoanLog.ConfigError(_logger, LogActions.Result, "exception",
                ("service", serviceName),
                ("adapter", adapter.GetType().Name),
                ("error", Redaction.DeIdentify(ex.Message)));
            return AdapterDiscoveryResult.Failed(serviceName, $"Adapter exception: {ex.Message}");
        }
    }

    public IServiceDiscoveryAdapter[] GetRegisteredAdapters() =>
        _adapters.Values.ToArray();

    private async Task<DiscoveryContext> ApplyContributedCandidates(
        string serviceName, DiscoveryContext context, CancellationToken cancellationToken)
    {
        if (_contributors.Length == 0) return context;

        var contributed = new List<DiscoveryCandidate>();
        foreach (var contributor in _contributors)
        {
            try
            {
                var items = await contributor.ContributeCandidates(serviceName, context, cancellationToken);
                if (items is { Count: > 0 })
                {
                    contributed.AddRange(items.Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Url)));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // A genuine caller cancellation must propagate — it is not a contributor failure to swallow.
                throw;
            }
            catch (Exception ex)
            {
                // Best-effort: a contributor is an aid to discovery, never a gate. Log and carry on.
                KoanLog.ConfigDebug(_logger, LogActions.Delegate, "contributor-failed",
                    ("service", serviceName),
                    ("contributor", contributor.GetType().Name),
                    ("error", Redaction.DeIdentify(ex.Message)));
            }
        }

        // Preserve any pre-existing contributed candidates the caller set explicitly.
        if (context.ContributedCandidates is { Count: > 0 })
            contributed.InsertRange(0, context.ContributedCandidates);

        return contributed.Count > 0 ? context with { ContributedCandidates = contributed } : context;
    }

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
