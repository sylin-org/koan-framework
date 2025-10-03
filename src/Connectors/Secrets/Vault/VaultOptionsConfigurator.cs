using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Orchestration;

namespace Koan.Secrets.Connector.Vault;

/// <summary>
/// Orchestration-aware Vault configuration using centralized service discovery.
/// Automatically discovers Vault server address across different orchestration environments.
/// </summary>
internal sealed class VaultOptionsConfigurator(IConfiguration config, ILogger<VaultOptionsConfigurator> logger) : IConfigureOptions<VaultOptions>
{
    public void Configure(VaultOptions options)
    {
        logger.LogInformation("Vault Orchestration-Aware Configuration Started");
        logger.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        logger.LogInformation("Initial options - Enabled: {Enabled}, Address: '{Address}'",
            options.Enabled, options.Address);

        // Only perform auto-discovery if Vault is enabled and no Address is configured
        if (!options.Enabled)
        {
            logger.LogInformation("Vault is disabled, skipping address configuration");
            return;
        }

        // Use centralized orchestration-aware service discovery
        var serviceDiscovery = new OrchestrationAwareServiceDiscovery(config, null);

        // Check for explicit address configuration first
        var explicitAddress = Configuration.ReadFirst(config, "",
            "Koan:Secrets:Vault:Address",
            "Vault:Address",
            "ConnectionStrings:vault",
            "ConnectionStrings:Vault");

        if (!string.IsNullOrWhiteSpace(explicitAddress))
        {
            logger.LogInformation("Using explicit address from configuration");
            options.Address = new Uri(explicitAddress);
        }
        else if (options.Address == null || IsDefaultAddress(options.Address))
        {
            logger.LogInformation("Auto-detection mode - using orchestration-aware service discovery");
            var discoveredAddress = ResolveOrchestrationAwareAddress(serviceDiscovery, logger);
            if (!string.IsNullOrWhiteSpace(discoveredAddress))
            {
                options.Address = new Uri(discoveredAddress);
            }
        }
        else
        {
            logger.LogInformation("Using pre-configured address");
        }

        // Final address logging
        logger.LogInformation("Final Vault Configuration");
        logger.LogInformation("Enabled: {Enabled}, Address: '{Address}'", options.Enabled, options.Address);
        logger.LogInformation("Vault Orchestration-Aware Configuration Complete");
    }

    private string? ResolveOrchestrationAwareAddress(
        IOrchestrationAwareServiceDiscovery serviceDiscovery,
        ILogger logger)
    {
        try
        {
            // Check if auto-detection is explicitly disabled
            if (IsAutoDetectionDisabled())
            {
                logger.LogInformation("Auto-detection disabled via configuration - using localhost");
                return "http://localhost:8200"; // Standard Vault port
            }

            // Create service discovery options with Vault-specific health checking
            var discoveryOptions = ServiceDiscoveryExtensions.ForVault();

            // Add legacy environment variable support
            var envCandidates = GetAdditionalCandidatesFromEnvironment();
            if (envCandidates.Length > 0)
            {
                discoveryOptions = discoveryOptions with
                {
                    AdditionalCandidates = envCandidates
                };
            }

            // Add Vault-specific health checking with custom validation
            discoveryOptions = discoveryOptions with
            {
                HealthCheck = new HealthCheckOptions
                {
                    HealthCheckPath = "/v1/sys/health",
                    Timeout = TimeSpan.FromMilliseconds(500),
                    Required = !KoanEnv.IsProduction // Less strict in production
                }
            };

            // Use centralized service discovery
            var discoveryTask = serviceDiscovery.DiscoverServiceAsync("vault", discoveryOptions);
            var result = discoveryTask.GetAwaiter().GetResult();

            logger.LogInformation("Vault discovered via {Method}: {ServiceUrl}",
                result.DiscoveryMethod, result.ServiceUrl);

            if (!result.IsHealthy && discoveryOptions.HealthCheck?.Required == true)
            {
                logger.LogWarning("Discovered Vault service failed health check but proceeding anyway");
            }

            return result.ServiceUrl;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in orchestration-aware Vault discovery, falling back to localhost");
            return "http://localhost:8200";
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Configuration.Read(config, "Koan:Secrets:Vault:DisableAutoDetection", false)
               || Configuration.Read(config, "Koan_SECRETS_VAULT_DISABLE_AUTO_DETECTION", false);
    }

    private string[] GetAdditionalCandidatesFromEnvironment()
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

    private static bool IsDefaultAddress(Uri address)
    {
        // Consider common default addresses as "not configured"
        var addressString = address.ToString().TrimEnd('/');
        return addressString == "http://localhost:8200" ||
               addressString == "https://localhost:8200" ||
               addressString == "http://127.0.0.1:8200";
    }
}
