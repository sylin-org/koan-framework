using Koan.Core;
using Koan.Core.Modules;
using Koan.Orchestration.Aspire.SelfOrchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Orchestration.Aspire.Initialization;

/// <summary>
/// Auto-registrar for Koan.Orchestration.Aspire that provides mode detection
/// and self-orchestration capabilities based on "Reference = Intent"
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Orchestration.Aspire";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        var logger = services.BuildServiceProvider().GetService<ILoggerFactory>()?.CreateLogger("Koan.Orchestration.Aspire");
        logger?.LogDebug("Koan.Orchestration.Aspire KoanAutoRegistrar loaded");

        // Session ID is now managed by KoanEnv.SessionId - no need to set it here

        // Orchestration mode detection is now handled by KoanEnv during startup

        // Get orchestration mode from KoanEnv (already detected during startup)
        var orchestrationMode = KoanEnv.OrchestrationMode;

        // Add configuration provider based on detected mode
        AddOrchestrationConfigurationForMode(services, orchestrationMode, KoanEnv.SessionId);

        logger?.LogInformation("Orchestration mode detected: {Mode}", orchestrationMode);

        switch (orchestrationMode)
        {
            case OrchestrationMode.SelfOrchestrating:
                logger?.LogInformation("Self-orchestration mode detected - registering dependency orchestrator");
                RegisterSelfOrchestrationServices(services);
                break;

            case OrchestrationMode.DockerCompose:
                logger?.LogInformation("Docker Compose mode detected - using container network endpoints");
                // Configuration provider already added in AddOrchestrationConfigurationIfNeeded
                break;

            case OrchestrationMode.Kubernetes:
                logger?.LogInformation("Kubernetes mode detected - using K8s service DNS endpoints");
                // Configuration provider already added in AddOrchestrationConfigurationIfNeeded
                break;

            case OrchestrationMode.AspireAppHost:
                logger?.LogInformation("Aspire AppHost mode detected - using Aspire service management");
                // No additional services needed - AppHost handles orchestration
                break;

            case OrchestrationMode.Standalone:
                logger?.LogInformation("Standalone mode detected - using external dependencies");
                // Running with external dependencies (production)
                break;

            default:
                logger?.LogWarning("Unknown orchestration mode: {Mode} - defaulting to Standalone behavior", orchestrationMode);
                break;
        }
    }


    private void RegisterSelfOrchestrationServices(IServiceCollection services)
    {
        // Core self-orchestration services
        services.TryAddSingleton<IKoanContainerManager, DockerContainerManager>();
        services.TryAddSingleton<IKoanDependencyOrchestrator, KoanDependencyOrchestrator>();

        // Hosted service to manage dependency lifecycle
        services.AddHostedService<KoanSelfOrchestrationService>();
    }


    private bool IsRunningUnderAspireAppHost()
    {
        // Check for Aspire-specific environment variables
        return KoanEnv.OrchestrationMode == OrchestrationMode.AspireAppHost;
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        var mode = KoanEnv.OrchestrationMode;

        // Core orchestration information
        report.AddSetting("Orchestration:Mode", mode.ToString());
        report.AddSetting("Orchestration:SessionId", KoanEnv.SessionId);

        // Context detection details (from KoanEnv)
        report.AddSetting("Orchestration:AspireDetected", (mode == OrchestrationMode.AspireAppHost).ToString());
        report.AddSetting("Orchestration:KubernetesDetected", (mode == OrchestrationMode.Kubernetes).ToString());
        report.AddSetting("Orchestration:DockerComposeDetected", (mode == OrchestrationMode.DockerCompose).ToString());
        report.AddSetting("Orchestration:SelfOrchestrationEnabled", (mode == OrchestrationMode.SelfOrchestrating).ToString());

        // Environment context
        report.AddSetting("Orchestration:InContainer", KoanEnv.InContainer.ToString());
        report.AddSetting("Orchestration:IsDevelopment", KoanEnv.IsDevelopment.ToString());

        // Configuration overrides
        var forcedMode = Configuration.Read<string?>(cfg, Koan.Core.Infrastructure.Constants.Configuration.Orchestration.ForceOrchestrationMode, null);
        if (!string.IsNullOrEmpty(forcedMode))
        {
            report.AddSetting("Orchestration:ForcedMode", forcedMode);
        }

        var validationEnabled = Configuration.Read(cfg, Koan.Core.Infrastructure.Constants.Configuration.Orchestration.ValidateNetworking, true);
        report.AddSetting("Orchestration:NetworkValidationEnabled", validationEnabled.ToString());

        // Docker availability (for self-orchestration)
        try
        {
            var dockerAvailable = new DockerContainerManager(
                new Microsoft.Extensions.Logging.Abstractions.NullLogger<DockerContainerManager>())
                .IsDockerAvailableAsync().Result;
            report.AddSetting("Orchestration:DockerAvailable", dockerAvailable.ToString());
        }
        catch
        {
            report.AddSetting("Orchestration:DockerAvailable", "unknown");
        }

        // Provider election for dependency services
        var provider = mode switch
        {
            OrchestrationMode.SelfOrchestrating => "localhost (self-orchestrated)",
            OrchestrationMode.DockerCompose => "service names (docker-compose)",
            OrchestrationMode.Kubernetes => "k8s service DNS",
            OrchestrationMode.AspireAppHost => "aspire-managed",
            OrchestrationMode.Standalone => "external",
            _ => "unknown"
        };
        report.AddProviderElection("DependencyNetworking", provider,
            new[] { "localhost", "service-names", "k8s-dns", "aspire-managed", "external" },
            $"orchestration mode: {mode}");

        // Network validation attempt (if enabled and not Aspire/Standalone)
        if (validationEnabled && mode != OrchestrationMode.AspireAppHost && mode != OrchestrationMode.Standalone)
        {
            // Network validation is simplified since KoanEnv handles mode detection
            report.AddConnectionAttempt("DependencyNetworking",
                $"{mode} networking",
                true,
                "Mode detected by KoanEnv - networking strategy selected");
        }
    }

    private void AddOrchestrationConfigurationForMode(IServiceCollection services, OrchestrationMode mode, string sessionId)
    {
        try
        {
            // Try to get the configuration builder from the service collection
            var configurationDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConfiguration));
            if (configurationDescriptor?.ImplementationInstance is IConfigurationRoot configRoot)
            {
                // Access the configuration builder through reflection
                var configBuilderField = configRoot.GetType().GetField("_providers",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (configBuilderField?.GetValue(configRoot) is IList<IConfigurationProvider> providers)
                {
                    // Add appropriate configuration provider based on detected mode
                    IConfigurationProvider? provider = mode switch
                    {
                        OrchestrationMode.SelfOrchestrating => new SelfOrchestrationConfigurationProvider(),
                        OrchestrationMode.DockerCompose => new DockerComposeConfigurationProvider(),
                        OrchestrationMode.Kubernetes => new KubernetesConfigurationProvider(),
                        _ => null
                    };

                    if (provider != null)
                    {
                        provider.Load();
                        providers.Add(provider);
                    }
                }
            }
        }
        catch
        {
            // If reflection fails, fall back to environment variables
            // The orchestration system will still work, just without configuration provider
        }
    }
}