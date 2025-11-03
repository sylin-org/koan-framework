using System.Reflection;
using Koan.Core;
using Koan.Core.Provenance;
using Koan.ServiceMesh.Abstractions;
using Koan.ServiceMesh.Execution;
using Koan.ServiceMesh.Pillars;
using Koan.ServiceMesh.ServiceMesh;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.ServiceMesh.Initialization;

/// <summary>
/// Auto-discovers and registers Koan services from assemblies.
/// Scans for [KoanService] attributes and builds service descriptors.
/// </summary>
public sealed class ServicesAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.ServiceMesh";
    public string? ModuleVersion => typeof(ServicesAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Ensure Services pillar is registered
        ServicesPillarManifest.EnsureRegistered();

        // Register core services
        services.AddHttpClient();
        services.AddSingleton<IKoanServiceMesh, KoanServiceMesh>();

        // Scan assemblies for [KoanService] attributes
        var discoveredServices = DiscoverServices();

        foreach (var descriptor in discoveredServices)
        {
            RegisterService(services, descriptor);
        }

        // Register mesh coordinator as hosted service
        if (discoveredServices.Any())
        {
            services.AddHostedService<KoanServiceMeshCoordinator>();
        }
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

        // Report discovered services summary
        var discoveredServices = DiscoverServices();

        if (!discoveredServices.Any())
            return;

        // Summary in main module
        module.SetSetting("Services.Discovered", setting => setting
            .Value(string.Join(", ", discoveredServices.Select(s => s.ServiceId)))
            .Source(ProvenanceSettingSource.Auto));

        // Create per-service modules
        foreach (var descriptor in discoveredServices)
        {
            DescribeService(descriptor, cfg, env);
        }
    }

    private static void DescribeService(KoanServiceDescriptor descriptor, IConfiguration cfg, IHostEnvironment env)
    {
        // Create a dedicated module for this service
        var moduleName = $"Koan.ServiceMesh.{ToPascalCase(descriptor.ServiceId)}";
        var serviceWriter = ProvenanceRegistry.Instance.GetOrCreateModule(
            ServicesPillarManifest.PillarCode,
            moduleName
        );

        serviceWriter.Describe(
            descriptor.DisplayName,
            descriptor.Description ?? $"{descriptor.DisplayName} microservice"
        );

        // Static configuration settings
        serviceWriter.SetSetting("ServiceId", setting => setting
            .Value(descriptor.ServiceId)
            .Source(ProvenanceSettingSource.Auto));

        serviceWriter.SetSetting("Port", setting => setting
            .Value(descriptor.Port.ToString())
            .Source(ProvenanceSettingSource.AppSettings, $"Koan:Service:{descriptor.ServiceId}:Port"));

        serviceWriter.SetSetting("HeartbeatInterval", setting => setting
            .Value(descriptor.HeartbeatInterval.ToString())
            .Source(ProvenanceSettingSource.AppSettings, $"Koan:Service:{descriptor.ServiceId}:HeartbeatInterval"));

        serviceWriter.SetSetting("StaleThreshold", setting => setting
            .Value(descriptor.StaleThreshold.ToString())
            .Source(ProvenanceSettingSource.AppSettings, $"Koan:Service:{descriptor.ServiceId}:StaleThreshold"));

        if (!string.IsNullOrEmpty(descriptor.ContainerImage))
        {
            serviceWriter.SetSetting("ContainerImage", setting => setting
                .Value($"{descriptor.ContainerImage}:{descriptor.DefaultTag}")
                .Source(ProvenanceSettingSource.AppSettings, $"Koan:Service:{descriptor.ServiceId}:ContainerImage"));
        }

        serviceWriter.SetSetting("OrchestratorChannel", setting => setting
            .Value($"{descriptor.OrchestratorMulticastGroup}:{descriptor.OrchestratorMulticastPort}")
            .Source(ProvenanceSettingSource.AppSettings));

        if (descriptor.EnableServiceChannel)
        {
            serviceWriter.SetSetting("ServiceChannel", setting => setting
                .Value($"{descriptor.ServiceMulticastGroup}:{descriptor.ServiceMulticastPort}")
                .Source(ProvenanceSettingSource.AppSettings));
        }

        // Capabilities as tools
        foreach (var capability in descriptor.Capabilities)
        {
            serviceWriter.SetTool(ToTitleCase(capability), tool => tool  // "translate" → "Translate"
                .Route($"/api/{descriptor.ServiceId}/{capability}")
                .Description($"{descriptor.ServiceId} capability: {capability}")
                .Capability($"services.{descriptor.ServiceId}.{capability}"));
        }

        // Styling hint for Admin UI
        serviceWriter.SetNote("admin-style", note => note
            .Message($"[admin-style] pillar={ServicesPillarManifest.PillarCode} " +
                    $"icon={ServicesPillarManifest.PillarIcon} " +
                    $"color={ServicesPillarManifest.PillarColorHex}")
            .Kind(ProvenanceNoteKind.Info));
    }

    private static string ToPascalCase(string kebabCase)
    {
        // Convert "translation" or "detect-language" → "Translation" or "DetectLanguage"
        var parts = kebabCase.Split('-');
        var result = string.Concat(parts.Select(p =>
            p.Length > 0 ? char.ToUpperInvariant(p[0]) + p.Substring(1) : string.Empty));
        return result;
    }

    private static string ToTitleCase(string kebabCase)
    {
        // Convert "detect-language" → "Detect Language"
        var parts = kebabCase.Split('-');
        var result = string.Join(" ", parts.Select(p =>
            p.Length > 0 ? char.ToUpperInvariant(p[0]) + p.Substring(1) : string.Empty));
        return result;
    }

    private static List<KoanServiceDescriptor> DiscoverServices()
    {
        var descriptors = new List<KoanServiceDescriptor>();

        // Scan loaded assemblies
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location));

        foreach (var assembly in assemblies)
        {
            try
            {
                var serviceTypes = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract &&
                                t.GetCustomAttribute<KoanServiceAttribute>() != null);

                foreach (var serviceType in serviceTypes)
                {
                    var descriptor = BuildDescriptor(serviceType);
                    descriptors.Add(descriptor);
                }
            }
            catch
            {
                // Skip assemblies that can't be scanned
            }
        }

        return descriptors;
    }

    private static KoanServiceDescriptor BuildDescriptor(Type serviceType)
    {
        var attribute = serviceType.GetCustomAttribute<KoanServiceAttribute>()!;

        // Build descriptor from attribute defaults
        var descriptor = new KoanServiceDescriptor
        {
            ServiceId = attribute.ServiceId,
            DisplayName = attribute.DisplayName ?? ConvertToDisplayName(attribute.ServiceId),
            Description = attribute.Description,
            ServiceType = serviceType,

            // Tier 3: HTTP Endpoint
            Port = attribute.Port,
            HealthEndpoint = attribute.HealthEndpoint,
            ManifestEndpoint = attribute.ManifestEndpoint,

            // Tier 1: Orchestrator Channel
            OrchestratorMulticastGroup = attribute.OrchestratorMulticastGroup,
            OrchestratorMulticastPort = attribute.OrchestratorMulticastPort,
            HeartbeatInterval = TimeSpan.FromSeconds(attribute.HeartbeatIntervalSeconds),
            StaleThreshold = TimeSpan.FromSeconds(attribute.StaleThresholdSeconds),

            // Tier 2: Service-Specific Channel
            EnableServiceChannel = attribute.EnableServiceChannel,
            ServiceMulticastGroup = attribute.ServiceMulticastGroup,
            ServiceMulticastPort = attribute.ServiceMulticastPort,

            // Capabilities (auto-detect or explicit)
            Capabilities = attribute.Capabilities ?? DetectCapabilities(serviceType),

            // Deployment
            ContainerImage = attribute.ContainerImage,
            DefaultTag = attribute.DefaultTag
        };

        // TODO: Apply configuration hierarchy (appsettings.json, env vars)
        // Configuration path: Koan:Service:{ServiceId}:Port, etc.

        return descriptor;
    }

    private static string[] DetectCapabilities(Type serviceType)
    {
        var capabilities = new List<string>();

        // Find public Task<T> methods
        var methods = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.ReturnType.IsGenericType &&
                        m.ReturnType.GetGenericTypeDefinition() == typeof(Task<>));

        foreach (var method in methods)
        {
            // Check for explicit [KoanCapability] attribute
            var capabilityAttr = method.GetCustomAttribute<KoanCapabilityAttribute>();
            if (capabilityAttr != null)
            {
                capabilities.Add(capabilityAttr.CapabilityName);
            }
            else
            {
                // Auto-detect from method name (convert to kebab-case)
                var capability = ConvertToKebabCase(method.Name);
                capabilities.Add(capability);
            }
        }

        return capabilities.ToArray();
    }

    private static void RegisterService(IServiceCollection services, KoanServiceDescriptor descriptor)
    {
        // Register the service descriptor as singleton
        services.AddSingleton(descriptor);

        // Register the service implementation as scoped
        services.AddScoped(descriptor.ServiceType);

        // Register ServiceExecutor for this service type
        var executorType = typeof(ServiceExecutor<>).MakeGenericType(descriptor.ServiceType);
        services.AddScoped(executorType, sp =>
        {
            var mesh = sp.GetRequiredService<IKoanServiceMesh>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

            // Create logger with correct generic type
            var loggerType = typeof(ILogger<>).MakeGenericType(executorType);
            var logger = sp.GetRequiredService(loggerType);

            return Activator.CreateInstance(
                executorType,
                mesh,
                sp,
                httpClientFactory,
                logger,
                descriptor.ServiceId)!;
        });
    }

    private static string ConvertToDisplayName(string serviceId)
    {
        // Convert "translation" → "Translation"
        return char.ToUpperInvariant(serviceId[0]) + serviceId.Substring(1);
    }

    private static string ConvertToKebabCase(string methodName)
    {
        // Convert "DetectLanguage" → "detect-language"
        var result = new System.Text.StringBuilder();

        for (int i = 0; i < methodName.Length; i++)
        {
            if (i > 0 && char.IsUpper(methodName[i]))
            {
                result.Append('-');
            }
            result.Append(char.ToLowerInvariant(methodName[i]));
        }

        return result.ToString();
    }
}
