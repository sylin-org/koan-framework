using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Secrets.Abstractions;
using Koan.Secrets.Connector.Vault.Internal;
using Koan.Secrets.Connector.Vault;
using Koan.Secrets.Connector.Vault.Health;
using Koan.Secrets.Connector.Vault.Orchestration;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Secrets.Connector.Vault.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Secrets.Connector.Vault";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<VaultOptions, VaultOptionsConfigurator>(
                VaultConstants.ConfigPath,
                configuratorLifetime: ServiceLifetime.Singleton)
            .Validate(o => !o.Enabled || (o.Address is not null && !string.IsNullOrWhiteSpace(o.Token)),
                "Vault enabled but Address/Token not configured");

        services.AddHttpClient(VaultConstants.HttpClientName, (sp, client) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<VaultOptions>>().Value;
            if (opts.Address is not null)
            {
                client.BaseAddress = opts.Address;
            }
            if (!string.IsNullOrWhiteSpace(opts.Token))
            {
                client.DefaultRequestHeaders.Remove("X-Vault-Token");
                client.DefaultRequestHeaders.Add("X-Vault-Token", opts.Token);
            }
            if (!string.IsNullOrWhiteSpace(opts.Namespace))
            {
                client.DefaultRequestHeaders.Remove("X-Vault-Namespace");
                client.DefaultRequestHeaders.Add("X-Vault-Namespace", opts.Namespace);
            }
            client.Timeout = opts.Timeout;
        });

        // Register provider only when enabled
        services.AddSingleton<ISecretProvider, VaultSecretProvider>(sp =>
        {
            var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<VaultOptions>>().Value;
            if (!o.Enabled)
            {
                // Disabled: return a provider that always yields NotFound so chain proceeds
                return new VaultSecretProvider(sp, sp.GetRequiredService<System.Net.Http.IHttpClientFactory>(),
                    Microsoft.Extensions.Options.Options.Create(o), sp.GetService<Microsoft.Extensions.Logging.ILogger<VaultSecretProvider>>(), disabled: true);
            }
            return new VaultSecretProvider(sp, sp.GetRequiredService<System.Net.Http.IHttpClientFactory>(),
                Microsoft.Extensions.Options.Options.Create(o), sp.GetService<Microsoft.Extensions.Logging.ILogger<VaultSecretProvider>>());
        });

        // Register orchestration evaluator for dependency management
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, VaultOrchestrationEvaluator>());

        // Health check
        services.AddHealthChecks().AddTypeActivatedCheck<VaultHealthCheck>("vault", HealthStatus.Unhealthy);
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        var section = cfg.GetSection(VaultConstants.ConfigPath);
        var enabled = section.GetValue<bool>(nameof(VaultOptions.Enabled));

        module.Describe(ModuleVersion);
        module.AddSetting("Enabled", enabled.ToString());
        module.AddSetting("Mount", section.GetValue<string?>(nameof(VaultOptions.Mount)) ?? "secret");
        module.AddSetting("KvV2", section.GetValue<bool?>(nameof(VaultOptions.UseKvV2))?.ToString() ?? "true");
        module.AddSetting("OrchestrationMode", KoanEnv.OrchestrationMode.ToString());
        module.AddSetting("Configuration", "Orchestration-aware service discovery enabled");

        // Only perform discovery reporting if Vault is enabled
        if (enabled)
        {
            try
            {
                // Use centralized orchestration-aware service discovery for reporting
                var serviceDiscovery = new OrchestrationAwareServiceDiscovery(cfg);

                // Create Vault-specific discovery options
                var discoveryOptions = ServiceDiscoveryExtensions.ForVault();

                // Add legacy environment variable support
                var envCandidates = GetLegacyEnvironmentCandidates();
                if (envCandidates.Length > 0)
                {
                    discoveryOptions = discoveryOptions with
                    {
                        AdditionalCandidates = envCandidates
                    };
                }

                // Discover Vault service
                var discoveryTask = serviceDiscovery.DiscoverServiceAsync("vault", discoveryOptions);
                var result = discoveryTask.GetAwaiter().GetResult();

                var method = $"orchestration-{result.DiscoveryMethod}";
                var endpoint = Koan.Core.Redaction.DeIdentify(result.ServiceUrl ?? string.Empty);

                module.AddSetting("Discovery.Method", method);
                module.AddSetting("Discovery.Endpoint", endpoint);
                module.AddSetting("Discovery.Healthy", result.IsHealthy.ToString());

                var status = result.IsHealthy ? "accessible" : "unreachable";
                module.AddNote($"Vault endpoint {status}: {endpoint}");
            }
            catch (Exception ex)
            {
                var fallbackEndpoint = Koan.Core.Redaction.DeIdentify("http://localhost:8200");
                module.AddSetting("Discovery.Method", "orchestration-fallback");
                module.AddSetting("Discovery.Endpoint", fallbackEndpoint);
                module.AddSetting("Discovery.Healthy", bool.FalseString);
                module.AddNote($"Vault endpoint unreachable: {fallbackEndpoint}");
                module.AddSetting("Discovery.Error", ex.Message);
            }
        }
    }

    private static string[] GetLegacyEnvironmentCandidates()
    {
        var candidates = new List<string>();

        // Legacy environment variable support for backward compatibility
        var vaultAddr = Environment.GetEnvironmentVariable("VAULT_ADDR");
        if (!string.IsNullOrWhiteSpace(vaultAddr))
        {
            candidates.Add(vaultAddr);
        }

        var koanVaultUrl = Environment.GetEnvironmentVariable("Koan_VAULT_URL");
        if (!string.IsNullOrWhiteSpace(koanVaultUrl))
        {
            candidates.Add(koanVaultUrl);
        }

        return candidates.ToArray();
    }
}


