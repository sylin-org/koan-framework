using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.Data.Core.Diagnostics;

/// <summary>
/// Bases data-adapter health on runtime participation rather than package availability.
/// </summary>
/// <remarks>
/// Referencing a connector makes it available; it does not necessarily make that connector an
/// application dependency. A provider participates when it wins the default election, owns an
/// explicitly configured source, or is selected by a runtime repository or Direct operation.
/// </remarks>
public abstract class DataAdapterHealthContributorBase(
    string provider,
    IServiceProvider services,
    DataSourceRegistry sourceRegistry,
    IDataDiagnostics diagnostics) : IHealthContributor
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

        try
        {
            return await CheckActive(sources, ct).ConfigureAwait(false);
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
                Redaction.DeIdentify(ex.Message),
                null,
                new Dictionary<string, object?>
                {
                    ["active"] = true,
                    ["provider"] = Provider,
                    ["sources"] = string.Join(",", sources)
                });
        }
    }

    /// <summary>Probes the sources that make this provider an application dependency.</summary>
    protected abstract Task<HealthReport> CheckActive(
        IReadOnlyCollection<string> sources,
        CancellationToken ct);

    private IReadOnlyCollection<string> GetActiveSources()
    {
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var decision = AdapterResolver.ResolveDefault(services);
            if (Matches(decision.Adapter))
            {
                sources.Add(decision.Source);
            }
        }
        catch (InvalidOperationException)
        {
            // Resolution failures are already owned by runtime facts. An unelected provider does
            // not become critical merely because another provider's election failed.
        }

        foreach (var sourceName in sourceRegistry.GetSourceNames())
        {
            var source = sourceRegistry.GetSource(sourceName);
            if (source is not null && Matches(source.Adapter))
            {
                sources.Add(source.Name);
            }
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
