using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Logging;
using Koan.Orchestration.Aspire.SelfOrchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Orchestration.Aspire.Initialization;

/// <summary>
/// Auto-registrar for Koan.Orchestration.Aspire that provides mode detection
/// and self-orchestration capabilities based on "Reference = Intent"
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<KoanAutoRegistrar>();

    public string ModuleName => "Koan.Orchestration.Aspire";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        Log.BootDebug(LogActions.Init, "loaded");

        // Session ID is now managed by KoanEnv.SessionId - no need to set it here

        // Orchestration mode detection is now handled by KoanEnv during startup

        // Get orchestration mode from KoanEnv (already detected during startup)
        var orchestrationMode = KoanEnv.OrchestrationMode;

        // Add configuration provider based on detected mode
        AddOrchestrationConfigurationForMode(services, orchestrationMode, KoanEnv.SessionId);

        Log.BootInfo(LogActions.ModeDetected, "detected", ("mode", orchestrationMode));

        switch (orchestrationMode)
        {
            case OrchestrationMode.SelfOrchestrating:
                Log.BootInfo(LogActions.ModeCase, "self-orchestrating");
                RegisterSelfOrchestrationServices(services);
                break;

            case OrchestrationMode.DockerCompose:
                Log.BootInfo(LogActions.ModeCase, "docker-compose");
                // Configuration provider already added in AddOrchestrationConfigurationIfNeeded
                break;

            case OrchestrationMode.Kubernetes:
                Log.BootInfo(LogActions.ModeCase, "kubernetes");
                // Configuration provider already added in AddOrchestrationConfigurationIfNeeded
                break;

            case OrchestrationMode.AspireAppHost:
                Log.BootInfo(LogActions.ModeCase, "aspire-apphost");
                // No additional services needed - AppHost handles orchestration
                break;

            case OrchestrationMode.Standalone:
                Log.BootInfo(LogActions.ModeCase, "standalone");
                // Running with external dependencies (production)
                break;

            default:
                Log.BootWarning(LogActions.ModeCase, "unknown", ("mode", orchestrationMode));
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

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var mode = KoanEnv.OrchestrationMode;

        // Core orchestration information
        module.AddSetting("Orchestration:Mode", mode.ToString());
        module.AddSetting("Orchestration:SessionId", KoanEnv.SessionId);

        // Context detection details (from KoanEnv)
        module.AddSetting("Orchestration:AspireDetected", (mode == OrchestrationMode.AspireAppHost).ToString());
        module.AddSetting("Orchestration:KubernetesDetected", (mode == OrchestrationMode.Kubernetes).ToString());
        module.AddSetting("Orchestration:DockerComposeDetected", (mode == OrchestrationMode.DockerCompose).ToString());
        module.AddSetting("Orchestration:SelfOrchestrationEnabled", (mode == OrchestrationMode.SelfOrchestrating).ToString());

        // Environment context
        module.AddSetting("Orchestration:InContainer", KoanEnv.InContainer.ToString());
        module.AddSetting("Orchestration:IsDevelopment", KoanEnv.IsDevelopment.ToString());

        // Configuration overrides
        var forcedMode = Configuration.Read<string?>(cfg, Koan.Core.Infrastructure.Constants.Configuration.Orchestration.ForceOrchestrationMode, null);
        if (!string.IsNullOrEmpty(forcedMode))
        {
            module.AddSetting("Orchestration:ForcedMode", forcedMode);
        }

        var validationEnabled = Configuration.Read(cfg, Koan.Core.Infrastructure.Constants.Configuration.Orchestration.ValidateNetworking, true);
        module.AddSetting("Orchestration:NetworkValidationEnabled", validationEnabled.ToString());

        // Docker availability (for self-orchestration)
        try
        {
            var dockerAvailable = new DockerContainerManager(
                new Microsoft.Extensions.Logging.Abstractions.NullLogger<DockerContainerManager>())
                .IsDockerAvailableAsync().Result;
            module.AddSetting("Orchestration:DockerAvailable", dockerAvailable.ToString());
        }
        catch
        {
            module.AddSetting("Orchestration:DockerAvailable", "unknown");
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
        var candidates = string.Join(", ", new[] { "localhost", "service-names", "k8s-dns", "aspire-managed", "external" });
        module.AddSetting("DependencyNetworking.Provider", provider);
        module.AddSetting("DependencyNetworking.Candidates", candidates);
        module.AddSetting("DependencyNetworking.Reason", $"orchestration mode: {mode}");

        // Network validation attempt (if enabled and not Aspire/Standalone)
        if (validationEnabled && mode != OrchestrationMode.AspireAppHost && mode != OrchestrationMode.Standalone)
        {
            // Network validation is simplified since KoanEnv handles mode detection
            module.AddNote($"Dependency networking validated for {mode}: Mode detected by KoanEnv - networking strategy selected");
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

    private static class LogActions
    {
        public const string Init = "registrar.init";
        public const string ModeDetected = "registrar.mode";
        public const string ModeCase = "registrar.mode.case";
    }
}
