using Koan.AI.Contracts.Sources;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.AI.Health;

/// <summary>
/// Contributes AI subsystem health to the framework health aggregator.
/// Reports Healthy when all source members are healthy, Degraded when a healthy member remains alongside another
/// state, Unhealthy when explicit failures leave no healthy member, and Unknown while health is wholly unestablished.
/// </summary>
internal sealed class AiSourcesHealthContributor(IAiSourceRegistry sourceRegistry) : IHealthContributor
{
    public string Name => "Koan.AI";

    public bool IsCritical => false;

    public Task<HealthReport> Check(CancellationToken ct = default)
    {
        var allSources = sourceRegistry.GetAllSources();
        var sources = allSources
            .Where(source => source.IsEnabled)
            .ToArray();

        if (sources.Length == 0)
        {
            var detail = allSources.Count == 0 ? "No AI sources registered" : "No enabled AI sources";
            return Task.FromResult(new HealthReport(Name, HealthState.Healthy, detail, null, null));
        }

        var totalMembers = 0;
        var healthyMembers = 0;
        var unhealthyMembers = 0;
        var unknownMembers = 0;
        var recoveringMembers = 0;

        foreach (var source in sources)
        {
            foreach (var member in source.Members)
            {
                totalMembers++;

                switch (member.HealthState)
                {
                    case MemberHealthState.Healthy:
                        healthyMembers++;
                        break;
                    case MemberHealthState.Unhealthy:
                        unhealthyMembers++;
                        break;
                    case MemberHealthState.Recovering:
                        recoveringMembers++;
                        break;
                    default:
                        unknownMembers++;
                        break;
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
            ["unhealthyMembers"] = unhealthyMembers,
            ["unknownMembers"] = unknownMembers,
            ["recoveringMembers"] = recoveringMembers,
            ["sources"] = sources.Length
        };

        if (healthyMembers == totalMembers)
        {
            return Task.FromResult(new HealthReport(Name, HealthState.Healthy, $"{healthyMembers}/{totalMembers} members healthy", null, data));
        }

        if (healthyMembers > 0)
        {
            return Task.FromResult(new HealthReport(
                Name,
                HealthState.Degraded,
                $"{healthyMembers}/{totalMembers} members healthy; {unhealthyMembers} unhealthy, {unknownMembers} unknown, {recoveringMembers} recovering",
                null,
                data));
        }

        if (unhealthyMembers > 0)
        {
            return Task.FromResult(new HealthReport(
                Name,
                HealthState.Unhealthy,
                $"0/{totalMembers} members healthy; {unhealthyMembers} unhealthy, {unknownMembers} unknown, {recoveringMembers} recovering",
                null,
                data));
        }

        return Task.FromResult(new HealthReport(
            Name,
            HealthState.Unknown,
            $"0/{totalMembers} members healthy; health has not been established",
            null,
            data));
    }
}
