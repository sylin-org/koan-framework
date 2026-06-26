using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Core.Observability.Health;

/// <summary>
/// The shared shape every backing-service health contributor repeats (ARCH-0103 cross-cutting promotion): a named,
/// critical probe that pings the service and reports <see cref="HealthState.Healthy"/> (+ optional metadata) or
/// <see cref="HealthState.Unhealthy"/> with the error message. A subclass supplies only the probe and its metadata.
///
/// <para><b>Probe through the shared connection.</b> The probe should ping via the adapter's EXISTING readiness/
/// connection provider — never open a fresh client per probe (the Mongo <c>new MongoClient(...)</c>-per-tick leak this
/// base removes; Couchbase already reused its provider, so it is the reference shape).</para>
/// </summary>
public abstract class AdapterHealthContributorBase : IHealthContributor
{
    public abstract string Name { get; }

    public virtual bool IsCritical => true;

    /// <summary>Ping the backing service through its shared connection. Throw on failure.</summary>
    protected abstract Task ProbeAsync(CancellationToken ct);

    /// <summary>Metadata attached to a Healthy report (e.g. database name, a redacted connection string); null for none.</summary>
    protected virtual IReadOnlyDictionary<string, object?>? HealthyData() => null;

    public async Task<HealthReport> Check(CancellationToken ct = default)
    {
        try
        {
            await ProbeAsync(ct).ConfigureAwait(false);
            return new HealthReport(Name, HealthState.Healthy, null, null, HealthyData());
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, null, null);
        }
    }
}
