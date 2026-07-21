using Koan.Core;
using Koan.Core.Observability.Health;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector;

/// <summary>Bases vector-adapter readiness on runtime selection rather than package presence.</summary>
public abstract class VectorAdapterHealthContributorBase(
    string provider,
    IVectorAdapterParticipation participation) : IHealthContributor
{
    private const string ComponentPrefix = "data:";

    /// <summary>The provider identifier used by Vector provider election.</summary>
    protected string Provider { get; } = provider;

    /// <inheritdoc />
    public string Name => ComponentPrefix + Provider;

    /// <inheritdoc />
    public bool IsCritical => participation.ActiveSources(Provider).Count > 0;

    /// <inheritdoc />
    public async Task<HealthReport> Check(CancellationToken ct = default)
    {
        var sources = participation.ActiveSources(Provider);
        if (sources.Count == 0)
        {
            return new HealthReport(
                Name,
                HealthState.Unknown,
                "Vector adapter is available but not active",
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
                    $"Vector source '{source}' is unavailable",
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
            new Dictionary<string, object?>
            {
                ["active"] = true,
                ["provider"] = Provider,
                ["sources"] = string.Join(",", sources)
            });
    }

    /// <summary>Probes one logical source after this provider has become a runtime dependency.</summary>
    protected abstract Task ProbeSource(string source, CancellationToken ct);
}
