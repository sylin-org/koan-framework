using Koan.AI.Contracts.Sources;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.AI.Health;

/// <summary>
/// Contributes AI subsystem health to the framework health aggregator.
/// Reports Healthy when all source members are healthy, Degraded when some are unhealthy,
/// and Unhealthy when no sources have any healthy members.
/// </summary>
internal sealed class AiSourcesHealthContributor(IAiSourceRegistry sourceRegistry) : IHealthContributor
{
    public string Name => "Koan.AI";

    public bool IsCritical => false;

    public Task<HealthReport> Check(CancellationToken ct = default)
    {
        var sources = sourceRegistry.GetAllSources();

        if (sources.Count == 0)
        {
            // No sources registered — nothing to report on.
            return Task.FromResult(new HealthReport(Name, HealthState.Healthy, "No AI sources registered", null, null));
        }

        var totalMembers = 0;
        var healthyMembers = 0;

        foreach (var source in sources)
        {
            foreach (var member in source.Members)
            {
                totalMembers++;

                if (member.HealthState is MemberHealthState.Healthy or MemberHealthState.Unknown)
                {
                    healthyMembers++;
                }
            }
        }

        if (totalMembers == 0)
        {
            return Task.FromResult(new HealthReport(Name, HealthState.Unhealthy, "All sources have zero members", null, null));
        }

        var data = new Dictionary<string, object?>
        {
            ["totalMembers"] = totalMembers,
            ["healthyMembers"] = healthyMembers,
            ["sources"] = sources.Count
        };

        if (healthyMembers == totalMembers)
        {
            return Task.FromResult(new HealthReport(Name, HealthState.Healthy, $"{healthyMembers}/{totalMembers} members healthy", null, data));
        }

        if (healthyMembers > 0)
        {
            return Task.FromResult(new HealthReport(Name, HealthState.Degraded, $"{healthyMembers}/{totalMembers} members healthy", null, data));
        }

        return Task.FromResult(new HealthReport(Name, HealthState.Unhealthy, $"0/{totalMembers} members healthy", null, data));
    }
}
