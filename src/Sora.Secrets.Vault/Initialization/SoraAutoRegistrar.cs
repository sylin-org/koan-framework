using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sora.Core;
using Sora.Secrets.Abstractions;
using Sora.Secrets.Vault.Internal;
using Sora.Secrets.Vault;
using Sora.Secrets.Vault.Health;

namespace Sora.Secrets.Vault.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Secrets.Vault";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddOptions<VaultOptions>()
            .BindConfiguration(VaultConstants.ConfigPath)
            .Validate(o => !o.Enabled || (o.Address is not null && !string.IsNullOrWhiteSpace(o.Token)),
                "Vault enabled but Address/Token not configured")
            .ValidateOnStart();

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

        // Health check
        services.AddHealthChecks().AddTypeActivatedCheck<VaultHealthCheck>("vault", HealthStatus.Unhealthy);
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        var section = cfg.GetSection(VaultConstants.ConfigPath);
        var enabled = section.GetValue<bool>(nameof(VaultOptions.Enabled));
        report.AddModule(ModuleName, ModuleVersion);
        report.AddSetting("Enabled", enabled.ToString());
        report.AddSetting("Mount", section.GetValue<string?>(nameof(VaultOptions.Mount)) ?? "secret");
        report.AddSetting("KvV2", section.GetValue<bool?>(nameof(VaultOptions.UseKvV2))?.ToString() ?? "true");
    }
}
