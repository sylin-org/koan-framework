using Koan.Core;
using Koan.Core.Observability.Health;
using Koan.Data.Core.Routing;

namespace Koan.Data.Core.Diagnostics;

/// <summary>
/// Bases data-adapter health on runtime participation rather than package availability.
/// </summary>
/// <remarks>
/// Referencing a connector makes it available; it does not necessarily make that connector an
/// application dependency. A provider participates when it wins the default election or is selected
/// by a runtime repository or Direct operation. Merely configuring a named source describes an
/// available route; it does not make that optional route a readiness dependency.
/// </remarks>
public abstract class DataAdapterHealthContributorBase(
    string provider,
    IServiceProvider services,
    IDataDiagnostics diagnostics,
    DataDefaultProviderPlan defaultProvider) : IHealthContributor
{
    private const string ComponentPrefix = "data:";
    private const string DefaultSource = "Default";
    private readonly IDataDiagnostics _runtimeDiagnostics =
        services.GetService(typeof(DataDiagnostics)) as IDataDiagnostics ?? diagnostics;
    private readonly DefaultDataRouteAuthority? _routeAuthority =
        services.GetService(typeof(DefaultDataRouteAuthority)) as DefaultDataRouteAuthority;

    /// <summary>The adapter identifier used by routing and source configuration.</summary>
    protected string Provider { get; } = provider;

    /// <inheritdoc />
    public string Name => ComponentPrefix + Provider;

    /// <inheritdoc />
    public bool IsCritical => GetActiveSources().Count > 0;

    /// <inheritdoc />
    public async Task<HealthReport> Check(CancellationToken ct = default)
    {
        var sources = GetActiveSources();
        if (sources.Count == 0)
        {
            return new HealthReport(
                Name,
                HealthState.Unknown,
                "Adapter is available but not active",
                null,
                new Dictionary<string, object?>
                {
                    ["active"] = false,
                    ["provider"] = Provider
                });
        }

        foreach (var source in sources)
        {
            try
            {
                await ProbeSource(source, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _ = ex;
                var plan = FindPlan(source);
                return new HealthReport(
                    Name,
                    HealthState.Unhealthy,
                    "An active Data source is unavailable",
                    null,
                    new Dictionary<string, object?>
                    {
                        ["active"] = true,
                        ["provider"] = Provider,
                        ["sourceCount"] = sources.Count,
                        ["failedDecision"] = plan?.RouteIdentity,
                        ["failureCode"] = "koan.data.health.probe-failed",
                        ["claims"] = plan is null ? string.Empty : string.Join(",", plan.ClaimReferences)
                    });
            }
        }

        return new HealthReport(
            Name,
            HealthState.Healthy,
            null,
            null,
            HealthyData(sources));
    }

    /// <summary>Probes one logical source that makes this provider an application dependency.</summary>
    protected abstract Task ProbeSource(string source, CancellationToken ct);

    /// <summary>Adds adapter-specific details to a successful report.</summary>
    protected virtual IReadOnlyDictionary<string, object?> HealthyData(
        IReadOnlyCollection<string> sources) =>
        new Dictionary<string, object?>
        {
            ["active"] = true,
            ["provider"] = Provider,
            ["sourceCount"] = sources.Count,
            ["decisions"] = string.Join(",", sources.Select(FindPlan)
                .Where(static plan => plan is not null).Select(static plan => plan!.RouteIdentity)),
            ["claims"] = string.Join(",", sources.Select(FindPlan)
                .Where(static plan => plan is not null)
                .SelectMany(static plan => plan!.ClaimReferences).Distinct(StringComparer.Ordinal))
        };

    private DataSourcePlanInfo? FindPlan(string source) =>
        _runtimeDiagnostics.GetSourcePlansSnapshot().FirstOrDefault(plan =>
            string.Equals(plan.Source, source, StringComparison.OrdinalIgnoreCase) && Matches(plan.Adapter));

    private IReadOnlyCollection<string> GetActiveSources()
    {
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var active = _routeAuthority?.Current;
        if (active is not null && Matches(active.Plan.Adapter))
        {
            sources.Add(active.Plan.Source);
        }
        else if (active is null && Matches(defaultProvider.ProviderId))
        {
            sources.Add(DefaultSource);
        }

        foreach (var participation in _runtimeDiagnostics.GetAdapterParticipationsSnapshot()
                     .Where(participation =>
                         participation.Role == DataAdapterParticipationRole.Explicit &&
                         Matches(participation.Provider)))
        {
            sources.Add(participation.Source);
        }

        return sources.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private bool Matches(string? candidate) =>
        string.Equals(candidate, Provider, StringComparison.OrdinalIgnoreCase);
}
