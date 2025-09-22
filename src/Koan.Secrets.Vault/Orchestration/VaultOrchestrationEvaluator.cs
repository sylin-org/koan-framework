using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Secrets.Vault.Internal;

namespace Koan.Secrets.Vault.Orchestration;

/// <summary>
/// Vault-specific orchestration evaluator that determines if Vault containers
/// should be provisioned based on configuration and host availability.
/// </summary>
public class VaultOrchestrationEvaluator : BaseOrchestrationEvaluator
{
    private readonly IHttpClientFactory? _httpClientFactory;

    public VaultOrchestrationEvaluator(ILogger<VaultOrchestrationEvaluator>? logger = null, IHttpClientFactory? httpClientFactory = null)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory;
    }

    public override string ServiceName => "vault";
    public override int StartupPriority => 400; // Later than data services, as it's for configuration/secrets

    protected override bool IsServiceEnabled(IConfiguration configuration)
    {
        // Check if Vault is explicitly enabled in configuration
        var section = configuration.GetSection(VaultConstants.ConfigPath);
        return section.GetValue<bool>(nameof(VaultOptions.Enabled));
    }

    protected override bool HasExplicitConfiguration(IConfiguration configuration)
    {
        // Check for explicit Vault address configuration
        var explicitAddress = Configuration.ReadFirst(configuration, "",
            "Koan:Secrets:Vault:Address",
            "Vault:Address",
            "ConnectionStrings:vault",
            "ConnectionStrings:Vault");

        return !string.IsNullOrWhiteSpace(explicitAddress);
    }

    protected override int GetDefaultPort()
    {
        return 8200; // Standard Vault port
    }

    protected override string[] GetAdditionalHostCandidates(IConfiguration configuration)
    {
        var candidates = new List<string>();

        // Check legacy environment variables for backward compatibility
        var vaultAddr = Environment.GetEnvironmentVariable("VAULT_ADDR");
        if (!string.IsNullOrWhiteSpace(vaultAddr))
        {
            var host = ExtractHostFromUrl(vaultAddr);
            if (!string.IsNullOrWhiteSpace(host))
            {
                candidates.Add(host);
            }
        }

        var koanVaultUrl = Environment.GetEnvironmentVariable("Koan_VAULT_URL");
        if (!string.IsNullOrWhiteSpace(koanVaultUrl))
        {
            var host = ExtractHostFromUrl(koanVaultUrl);
            if (!string.IsNullOrWhiteSpace(host))
            {
                candidates.Add(host);
            }
        }

        return candidates.ToArray();
    }

    protected override async Task<bool> ValidateHostCredentials(IConfiguration configuration, HostDetectionResult hostResult)
    {
        try
        {
            Logger?.LogDebug("[Vault] Validating credentials for host: {Host}", hostResult.HostEndpoint);

            // Get configured token
            var section = configuration.GetSection(VaultConstants.ConfigPath);
            var token = section.GetValue<string>(nameof(VaultOptions.Token));

            if (string.IsNullOrWhiteSpace(token))
            {
                // No token configured - can't validate credentials, but host is available
                Logger?.LogDebug("[Vault] No token configured - assuming host is usable");
                return true;
            }

            // Try to authenticate with the configured token
            var vaultUrl = EnsureHttpUrl(hostResult.HostEndpoint!);
            var isValid = await TryVaultAuth(vaultUrl, token);

            Logger?.LogDebug("[Vault] Credential validation result: {IsValid}", isValid);
            return isValid;
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "[Vault] Error validating host credentials");
            return false;
        }
    }

    protected override async Task<DependencyDescriptor> CreateDependencyDescriptorAsync(IConfiguration configuration, OrchestrationContext context)
    {
        // Create environment variables for development Vault container
        var environment = new Dictionary<string, string>(context.EnvironmentVariables)
        {
            ["KOAN_DEPENDENCY_TYPE"] = "vault",
            ["VAULT_DEV_ROOT_TOKEN_ID"] = "root-token",
            ["VAULT_DEV_LISTEN_ADDRESS"] = "0.0.0.0:8200",
            ["VAULT_API_ADDR"] = "http://127.0.0.1:8200"
        };

        return await Task.FromResult(new DependencyDescriptor
        {
            Name = ServiceName,
            Image = "hashicorp/vault:latest",
            Port = GetDefaultPort(),
            StartupPriority = StartupPriority,
            HealthTimeout = TimeSpan.FromSeconds(30),
            HealthCheckCommand = null, // Vault doesn't have a simple health check command
            Environment = environment,
            Volumes = new List<string>
            {
                $"koan-vault-{context.SessionId}:/vault/data"
            }
        });
    }

    private async Task<bool> TryVaultAuth(string vaultUrl, string token)
    {
        try
        {
            using var httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-Vault-Token", token);
            httpClient.Timeout = TimeSpan.FromMilliseconds(1000);

            // Try to access the sys/auth endpoint
            var response = await httpClient.GetAsync($"{vaultUrl}/v1/sys/auth");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractHostFromUrl(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return $"{uri.Host}:{uri.Port}";
            }

            // Try parsing as host:port directly
            if (url.Contains(':'))
            {
                return url;
            }

            // Default port
            return $"{url}:8200";
        }
        catch
        {
            return null;
        }
    }

    private static string EnsureHttpUrl(string hostPort)
    {
        if (hostPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            hostPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return hostPort;
        }

        return $"http://{hostPort}";
    }
}