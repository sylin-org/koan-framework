using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Koan.Core.Diagnostics;
using Koan.Core.Logging;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Orchestration.Composition;
using Koan.Core.Semantics;
using CoreConstants = Koan.Core.Infrastructure.Constants;

namespace Koan.Core.Orchestration;

/// <summary>
/// Routes discovery through the selected adapter and the host's immutable optional-source plan.
/// </summary>
internal sealed class ServiceDiscoveryCoordinator : IServiceDiscoveryCoordinator
{
    private readonly ConcurrentDictionary<string, IServiceDiscoveryAdapter> _adapters = new();
    private readonly ServiceDiscoveryRuntime _runtime;
    private readonly ILogger<ServiceDiscoveryCoordinator> _logger;
    private readonly IKoanRuntimeFactRecorder? _facts;

    public ServiceDiscoveryCoordinator(
        IEnumerable<IServiceDiscoveryAdapter> adapters,
        ServiceDiscoveryRuntime runtime,
        ILogger<ServiceDiscoveryCoordinator> logger,
        IKoanRuntimeFactRecorder? facts = null)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _facts = facts;
        RegisterAdapters(adapters);
    }

    public async Task<AdapterDiscoveryResult> DiscoverService(
        string serviceName,
        DiscoveryContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAdapter(serviceName, out var adapter, out var failure)) return failure;

        context ??= new DiscoveryContext();
        var request = CreateRequest(adapter, context, intent: null);
        var query = await _runtime.QueryAutomatic(request, cancellationToken).ConfigureAwait(false);
        LogSourceFailures(adapter.ServiceName, query.Failures);

        var plannedContext = context with
        {
            PlannedCandidates = query.Candidates,
            PlannedCandidateMode = DiscoveryCandidateMode.Automatic
        };
        return await DiscoverThroughAdapter(adapter, plannedContext, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdapterDiscoveryResult> ResolveServiceIntent(
        string serviceName,
        string intent,
        DiscoveryContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAdapter(serviceName, out var adapter, out var failure)) return failure;

        if (!TryReadIntentScheme(intent, out var scheme))
        {
            return RejectRequiredIntent(
                adapter.ServiceName,
                "The explicit discovery intent is not a valid absolute URI.",
                "Use a registered source URI such as 'provider://offering'.",
                "invalid-intent");
        }

        context ??= new DiscoveryContext();
        var request = CreateRequest(adapter, context, intent);
        var query = await _runtime.QueryRequired(scheme, request, cancellationToken).ConfigureAwait(false);

        if (!query.IsMatched)
        {
            return RejectRequiredIntent(
                adapter.ServiceName,
                $"No active discovery source handles the '{scheme}' intent scheme.",
                "Reference and enable the requested source, or choose automatic discovery.",
                scheme);
        }

        if (query.Failure is not null)
        {
            LogSourceFailure(adapter.ServiceName, query.Failure);
            return RejectRequiredIntent(
                adapter.ServiceName,
                $"The active '{scheme}' discovery source could not resolve the explicit intent.",
                "Make the requested source available, or choose automatic discovery.",
                query.Failure.Id);
        }

        if (query.Candidates.Length == 0)
        {
            return RejectRequiredIntent(
                adapter.ServiceName,
                $"The active '{scheme}' discovery source returned no candidates for the explicit intent.",
                "Correct the requested offering, or choose automatic discovery.",
                scheme);
        }

        var requiredContext = context with
        {
            PlannedCandidates = query.Candidates,
            PlannedCandidateMode = DiscoveryCandidateMode.Required
        };
        return await DiscoverThroughAdapter(adapter, requiredContext, cancellationToken).ConfigureAwait(false);
    }

    public IServiceDiscoveryAdapter[] GetRegisteredAdapters() => _adapters.Values.ToArray();

    private async Task<AdapterDiscoveryResult> DiscoverThroughAdapter(
        IServiceDiscoveryAdapter adapter,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        KoanLog.ConfigDebug(_logger, LogActions.Delegate, null,
            ("service", adapter.ServiceName),
            ("adapter", adapter.GetType().Name));

        try
        {
            var result = await adapter.Discover(context, cancellationToken).ConfigureAwait(false);
            var method = SafeIdentity(result.DiscoveryMethod, "unknown");
            KoanLog.ConfigInfo(_logger, LogActions.Result,
                result.IsSuccessful ? LogOutcomes.Success : LogOutcomes.Failure,
                ("service", adapter.ServiceName),
                ("method", method));
            RecordDiscovery(
                adapter.ServiceName,
                result.IsSuccessful ? KoanFactState.Selected : KoanFactState.Rejected,
                result.IsSuccessful
                    ? $"Selected service discovery through '{method}'."
                    : "Service discovery exhausted its eligible candidates.",
                result.IsSuccessful
                    ? CoreConstants.Diagnostics.Reasons.DiscoverySelected
                    : CoreConstants.Diagnostics.Reasons.DiscoveryFailed,
                result.IsSuccessful ? null : "Correct the configured endpoint or activate a reachable provider.",
                result.IsSuccessful ? method : adapter.GetType().Name);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            KoanLog.ConfigError(_logger, LogActions.Result, "exception",
                ("service", adapter.ServiceName),
                ("adapter", adapter.GetType().Name),
                ("error", exception.GetType().Name));
            RecordDiscovery(
                adapter.ServiceName,
                KoanFactState.Rejected,
                "The selected discovery adapter failed before it could elect an endpoint.",
                CoreConstants.Diagnostics.Reasons.DiscoveryFailed,
                "Inspect the adapter health and configuration facts.",
                adapter.GetType().Name);
            return AdapterDiscoveryResult.Failed(
                adapter.ServiceName,
                "The discovery adapter failed. Inspect adapter health and configuration facts.");
        }
    }

    private AdapterDiscoveryResult RejectRequiredIntent(
        string serviceName,
        string summary,
        string correction,
        string correlation)
    {
        KoanLog.ConfigWarning(_logger, LogActions.Result, "required-intent-rejected",
            ("service", serviceName),
            ("reason", SafeIdentity(correlation, "unavailable")));
        RecordDiscovery(
            serviceName,
            KoanFactState.Rejected,
            summary,
            CoreConstants.Diagnostics.Reasons.DiscoveryFailed,
            correction,
            SafeIdentity(correlation, "unavailable"));
        return AdapterDiscoveryResult.Failed(serviceName, $"{summary} {correction}");
    }

    private bool TryGetAdapter(
        string serviceName,
        out IServiceDiscoveryAdapter adapter,
        out AdapterDiscoveryResult failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        if (_adapters.TryGetValue(serviceName.Trim().ToLowerInvariant(), out adapter!))
        {
            failure = null!;
            return true;
        }

        KoanLog.ConfigWarning(_logger, LogActions.Lookup, "no-adapter", ("service", serviceName));
        RecordDiscovery(
            serviceName,
            KoanFactState.Rejected,
            "No discovery adapter is active for the requested service.",
            CoreConstants.Diagnostics.Reasons.DiscoveryAdapterMissing,
            "Reference and enable an adapter that handles this service.",
            "none");
        failure = AdapterDiscoveryResult.NoAdapter(serviceName);
        return false;
    }

    private static DiscoveryCandidateRequest CreateRequest(
        IServiceDiscoveryAdapter adapter,
        DiscoveryContext context,
        string? intent) =>
        new(adapter.ServiceName, adapter.Aliases ?? [], context, intent);

    private void LogSourceFailures(
        string serviceName,
        IEnumerable<DiscoverySourceFailure> failures)
    {
        foreach (var failure in failures) LogSourceFailure(serviceName, failure);
    }

    private void LogSourceFailure(string serviceName, DiscoverySourceFailure failure)
    {
        KoanLog.ConfigDebug(_logger, LogActions.Delegate, "source-failed",
            ("service", serviceName),
            ("owner", failure.Owner),
            ("source", failure.Id),
            ("error", failure.ErrorType));
    }

    private void RegisterAdapters(IEnumerable<IServiceDiscoveryAdapter> adapters)
    {
        foreach (var adapter in adapters.OrderByDescending(static adapter => adapter.Priority))
        {
            foreach (var serviceName in new[] { adapter.ServiceName }.Concat(adapter.Aliases ?? []))
            {
                if (string.IsNullOrWhiteSpace(serviceName)) continue;
                var key = serviceName.Trim().ToLowerInvariant();
                _adapters.AddOrUpdate(key, adapter, (_, existing) =>
                    adapter.Priority > existing.Priority ? adapter : existing);

                KoanLog.BootInfo(_logger, LogActions.Register, "adapter",
                    ("service", serviceName),
                    ("adapter", adapter.GetType().Name),
                    ("priority", adapter.Priority));
            }
        }
    }

    private void RecordDiscovery(
        string serviceName,
        KoanFactState state,
        string summary,
        string reason,
        string? correction,
        string correlation)
    {
        _facts?.Record(new KoanFactDescriptor(
            CoreConstants.Diagnostics.Codes.ServiceDiscovery,
            KoanFactKind.Discovery,
            state,
            $"service:{serviceName.ToLowerInvariant()}",
            summary,
            reason,
            correction,
            "Koan.Core.Orchestration",
            correlation));
    }

    private static bool TryReadIntentScheme(string? intent, out string scheme)
    {
        scheme = string.Empty;
        if (string.IsNullOrWhiteSpace(intent)
            || !Uri.TryCreate(intent.Trim(), UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.Scheme))
        {
            return false;
        }

        scheme = uri.Scheme.ToLowerInvariant();
        return true;
    }

    private static string SafeIdentity(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try
        {
            return new SemanticId(value).Value;
        }
        catch (ArgumentException)
        {
            return fallback;
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
