using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Web.Auth.Extensions;
using Koan.Web.Extensions;

namespace Koan.Web.Auth.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Auth";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Ensure auth services are registered once
        services.AddKoanWebAuth();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.AspNetCore.Hosting.IStartupFilter, Hosting.KoanWebAuthStartupFilter>());

        // Ensure MVC discovers controllers from this assembly
        services.AddKoanControllersFrom<Controllers.DiscoveryController>();
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Best-effort discovery summary without binding or DI: list provider display names and protocol
        // Strategy: if configured providers exist, list those; otherwise fall back to well-known defaults.
        var section = cfg.GetSection(Options.AuthOptions.SectionPath);
        var providers = section.GetSection("Providers");

        static string PrettyProtocol(string? type)
            => string.IsNullOrWhiteSpace(type) ? "OIDC"
               : type!.ToLowerInvariant() switch
               {
                   "oidc" => "OIDC",
                   "oauth2" => "OAuth",
                   "oauth" => "OAuth",
                   "saml" => "SAML",
                   "ldap" => "LDAP",
                   _ => type
               };

        static string Titleize(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return id;
            var parts = id.Replace('-', ' ').Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                parts[i] = char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p.Substring(1) : string.Empty);
            }
            return string.Join(' ', parts);
        }

        var configured = providers.Exists() ? providers.GetChildren().ToList() : new List<IConfigurationSection>();
        var detected = new List<string>();

        if (configured.Count > 0)
        {
            foreach (var child in configured)
            {
                var id = child.Key;
                var display = child.GetValue<string>(nameof(Options.ProviderOptions.DisplayName));
                if (string.IsNullOrWhiteSpace(display)) display = Titleize(id);
                var type = child.GetValue<string>(nameof(Options.ProviderOptions.Type));
                // Keep type as-is if configured; otherwise leave null and PrettyProtocol will titleize raw value later.
                detected.Add($"{display} ({PrettyProtocol(type)})");
            }
        }
        else
        {
            // No explicit config: nothing to list unless contributors add providers at runtime
        }

        report.AddSetting("Providers", detected.Count.ToString());
        report.AddSetting("DetectedProviders", string.Join(", ", detected));

        // Production gating for dynamic providers (adapter/contributor defaults without explicit config)
        var allowDynamic = Koan.Core.Configuration.Read(cfg, Infrastructure.AuthConstants.Configuration.AllowDynamicProvidersInProduction, false)
                           || Koan.Core.KoanEnv.AllowMagicInProduction
                           || Koan.Core.Configuration.Read(cfg, Koan.Core.Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction, false);
        if (Koan.Core.KoanEnv.IsProduction && !allowDynamic)
        {
            report.AddSetting("DynamicProvidersInProduction", "disabled (set Koan:Web:Auth:AllowDynamicProvidersInProduction=true or Koan:AllowMagicInProduction=true)");
        }
        else
        {
            report.AddSetting("DynamicProvidersInProduction", "enabled");
        }
    }
}
