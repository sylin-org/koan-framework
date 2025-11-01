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
            // No services discovered yet, but mesh is enabled
            var defaultMeshConfig = new MeshConfiguration(
                "239.255.42.1",
                42001,
                "10s",
                "30s",
                null
            );

            return KoanAdminServiceMeshSurface.Empty with
            {
                Enabled = true,
                CapturedAt = capturedAt,
                Configuration = defaultMeshConfig
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
                    inst.ServiceChannelEndpoint,
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

            // Calculate health distribution with percentages for this service
            var svcHealthyCount = instanceSurfaces.Count(i => i.Status == "Healthy");
            var svcDegradedCount = instanceSurfaces.Count(i => i.Status == "Degraded");
            var svcUnhealthyCount = instanceSurfaces.Count(i => i.Status == "Unhealthy");
            var totalCount = instanceSurfaces.Count;

            var health = new ServiceHealthDistribution(
                svcHealthyCount,
                svcDegradedCount,
                svcUnhealthyCount,
                totalCount > 0 ? (int)Math.Round(svcHealthyCount * 100.0 / totalCount) : 0,
                totalCount > 0 ? (int)Math.Round(svcDegradedCount * 100.0 / totalCount) : 0,
                totalCount > 0 ? (int)Math.Round(svcUnhealthyCount * 100.0 / totalCount) : 0
            );

            // Calculate capacity metrics
            var totalConnections = instanceSurfaces.Sum(i => i.ActiveConnections);
            var averageLoad = totalCount > 0 ? (double)totalConnections / totalCount : 0;
            var maxConnectionsPerInstance = 100; // Assumed max capacity per instance
            var utilizationPercent = svcHealthyCount > 0 && totalConnections > 0
                ? Math.Min(100, (int)Math.Round(totalConnections * 100.0 / (svcHealthyCount * maxConnectionsPerInstance)))
                : 0;

            var capacity = new CapacityMetrics(
                totalCount,
                svcHealthyCount,
                utilizationPercent,
                totalConnections,
                averageLoad
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

            // Build service configuration if descriptor is available
            ServiceConfiguration? serviceConfig = descriptor != null
                ? new ServiceConfiguration(
                    descriptor.Port,
                    descriptor.HealthEndpoint,
                    descriptor.ManifestEndpoint,
                    descriptor.EnableServiceChannel,
                    descriptor.ServiceMulticastGroup,
                    descriptor.ServiceMulticastPort,
                    descriptor.ContainerImage,
                    descriptor.DefaultTag
                )
                : null;

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
                serviceConfig,
                capacity,
                instanceSurfaces
            ));
        }

        // Build mesh configuration from first service descriptor (or use defaults)
        var firstDescriptor = services.Select(s => TryGetServiceDescriptor(serviceProvider, s.ServiceId))
            .FirstOrDefault(d => d != null);

        var meshConfig = new MeshConfiguration(
            firstDescriptor?.OrchestratorMulticastGroup ?? "239.255.42.1",
            firstDescriptor?.OrchestratorMulticastPort ?? 42001,
            FormatDuration(firstDescriptor?.HeartbeatInterval ?? TimeSpan.FromSeconds(10)),
            FormatDuration(firstDescriptor?.StaleThreshold ?? TimeSpan.FromSeconds(30)),
            null  // SelfInstanceId - not available from current mesh interface
        );

        var orchestratorChannel = $"{meshConfig.OrchestratorMulticastGroup}:{meshConfig.OrchestratorMulticastPort}";

        return new KoanAdminServiceMeshSurface(
            Enabled: true,
            CapturedAt: capturedAt,
            OrchestratorChannel: orchestratorChannel,
            TotalServicesCount: services.Count,
            TotalInstancesCount: totalInstances,
            HealthyInstancesCount: healthyCount,
            DegradedInstancesCount: degradedCount,
            UnhealthyInstancesCount: unhealthyCount,
            Configuration: meshConfig,
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
