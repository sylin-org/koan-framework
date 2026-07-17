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
                return new HealthReport(
                    Name,
                    HealthState.Unhealthy,
                    $"Data source '{source}' is unavailable",
                    null,
                    new Dictionary<string, object?>
                    {
                        ["active"] = true,
                        ["provider"] = Provider,
                        ["sources"] = string.Join(",", sources),
                        ["failedSource"] = source,
                        ["error"] = Redaction.DeIdentify(ex.Message)
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
            ["sources"] = string.Join(",", sources)
        };

    private IReadOnlyCollection<string> GetActiveSources()
    {
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Matches(defaultProvider.ProviderId))
        {
            sources.Add(DefaultSource);
        }

        foreach (var participation in _runtimeDiagnostics.GetAdapterParticipationsSnapshot()
                     .Where(participation => Matches(participation.Provider)))
        {
            sources.Add(participation.Source);
        }

        return sources.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private bool Matches(string? candidate) =>
        string.Equals(candidate, Provider, StringComparison.OrdinalIgnoreCase);
}
