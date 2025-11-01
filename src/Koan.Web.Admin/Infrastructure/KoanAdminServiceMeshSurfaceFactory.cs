using Koan.Services.Abstractions;
using Koan.Web.Admin.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Admin.Infrastructure;

internal static class KoanAdminServiceMeshSurfaceFactory
{
    public static async Task<KoanAdminServiceMeshSurface> CaptureAsync(
        IKoanServiceMesh mesh,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Satisfy async signature

        var capturedAt = DateTimeOffset.UtcNow;
        var serviceIds = mesh.GetDiscoveredServices();

        if (!serviceIds.Any())
        {
            return KoanAdminServiceMeshSurface.Empty with
            {
                Enabled = true,
                CapturedAt = capturedAt
            };
        }

        var services = new List<KoanAdminServiceSurface>();
        int totalInstances = 0;
        int healthyCount = 0;
        int degradedCount = 0;
        int unhealthyCount = 0;

        foreach (var serviceId in serviceIds)
        {
            var instances = mesh.GetAllInstances(serviceId);
            if (!instances.Any())
                continue;

            var instanceSurfaces = instances.Select(inst =>
            {
                var timeSince = capturedAt - inst.LastSeen;
                totalInstances++;

                switch (inst.Status)
                {
                    case ServiceInstanceStatus.Healthy:
                        healthyCount++;
                        break;
                    case ServiceInstanceStatus.Degraded:
                        degradedCount++;
                        break;
                    case ServiceInstanceStatus.Unhealthy:
                        unhealthyCount++;
                        break;
                }

                return new KoanAdminServiceInstanceSurface(
                    inst.InstanceId,
                    inst.HttpEndpoint,
                    inst.Status.ToString(),
                    inst.LastSeen,
                    FormatTimeSince(timeSince),
                    inst.ActiveConnections,
                    FormatDuration(inst.AverageResponseTime),
                    inst.DeploymentMode.ToString(),
                    inst.ContainerId,
                    inst.Capabilities
                );
            }).ToList();

            var health = new ServiceHealthDistribution(
                instanceSurfaces.Count(i => i.Status == "Healthy"),
                instanceSurfaces.Count(i => i.Status == "Degraded"),
                instanceSurfaces.Count(i => i.Status == "Unhealthy")
            );

            var responseTimes = instanceSurfaces
                .Select(i => i.AverageResponseTime)
                .Where(t => t != "N/A")
                .Select(ParseDuration)
                .Where(t => t.HasValue)
                .Select(t => t!.Value)
                .ToList();

            // Try to get descriptor for display name/description
            var descriptor = TryGetServiceDescriptor(serviceProvider, serviceId);

            // Determine load balancing policy (default to RoundRobin for now)
            // TODO: Get actual policy from service descriptor or configuration
            var loadBalancing = new LoadBalancingInfo(
                "RoundRobin",
                "Distributes requests evenly across healthy instances"
            );

            services.Add(new KoanAdminServiceSurface(
                serviceId,
                descriptor?.DisplayName ?? ToPascalCase(serviceId),
                descriptor?.Description,
                instances.First().Capabilities,  // All instances have same capabilities
                instanceSurfaces.Count,
                health,
                loadBalancing,
                responseTimes.Any() ? responseTimes.Min() : null,
                responseTimes.Any() ? responseTimes.Max() : null,
                responseTimes.Any() ? TimeSpan.FromTicks((long)responseTimes.Average(t => t.Ticks)) : null,
                instanceSurfaces
            ));
        }

        // Get orchestrator channel from configuration
        var orchestratorChannel = "239.255.42.1:42001";  // Default, could read from config

        return new KoanAdminServiceMeshSurface(
            Enabled: true,
            CapturedAt: capturedAt,
            OrchestratorChannel: orchestratorChannel,
            TotalServicesCount: services.Count,
            TotalInstancesCount: totalInstances,
            HealthyInstancesCount: healthyCount,
            DegradedInstancesCount: degradedCount,
            UnhealthyInstancesCount: unhealthyCount,
            Services: services
        );
    }

    private static KoanServiceDescriptor? TryGetServiceDescriptor(
        IServiceProvider serviceProvider,
        string serviceId)
    {
        try
        {
            // Try to get all registered descriptors
            var descriptors = serviceProvider.GetServices<KoanServiceDescriptor>();
            return descriptors.FirstOrDefault(d => d.ServiceId == serviceId);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatTimeSince(TimeSpan timeSpan)
    {
        if (timeSpan.TotalSeconds < 1)
            return "just now";
        if (timeSpan.TotalSeconds < 60)
            return $"{(int)timeSpan.TotalSeconds} second{Plural((int)timeSpan.TotalSeconds)} ago";
        if (timeSpan.TotalMinutes < 60)
            return $"{(int)timeSpan.TotalMinutes} minute{Plural((int)timeSpan.TotalMinutes)} ago";
        if (timeSpan.TotalHours < 24)
            return $"{(int)timeSpan.TotalHours} hour{Plural((int)timeSpan.TotalHours)} ago";

        return $"{(int)timeSpan.TotalDays} day{Plural((int)timeSpan.TotalDays)} ago";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration == TimeSpan.Zero)
            return "N/A";

        if (duration.TotalMilliseconds < 1)
            return $"{(int)(duration.TotalMilliseconds * 1000)}μs";
        if (duration.TotalMilliseconds < 1000)
            return $"{(int)duration.TotalMilliseconds}ms";
        if (duration.TotalSeconds < 60)
            return $"{duration.TotalSeconds:F2}s";

        return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
    }

    private static TimeSpan? ParseDuration(string formatted)
    {
        if (formatted == "N/A")
            return null;

        try
        {
            if (formatted.EndsWith("ms"))
                return TimeSpan.FromMilliseconds(double.Parse(formatted.Replace("ms", "")));
            if (formatted.EndsWith("μs"))
                return TimeSpan.FromMicroseconds(double.Parse(formatted.Replace("μs", "")));
            if (formatted.EndsWith("s") && !formatted.Contains("m"))
                return TimeSpan.FromSeconds(double.Parse(formatted.Replace("s", "")));

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string Plural(int count) => count == 1 ? "" : "s";

    private static string ToPascalCase(string kebabCase)
    {
        // Convert "translation" → "Translation"
        if (string.IsNullOrEmpty(kebabCase))
            return kebabCase;

        return char.ToUpperInvariant(kebabCase[0]) + kebabCase.Substring(1);
    }
}
