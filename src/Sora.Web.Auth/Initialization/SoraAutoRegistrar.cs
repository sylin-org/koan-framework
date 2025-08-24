using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Web.Auth.Extensions;

namespace Sora.Web.Auth.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Web.Auth";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Ensure auth services are registered once
        services.AddSoraWebAuth();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.AspNetCore.Hosting.IStartupFilter, Hosting.SoraWebAuthStartupFilter>());
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Best-effort discovery summary without binding or DI: list provider display names and protocol
        // Strategy: if configured providers exist, list those; otherwise fall back to well-known defaults.
        var section = cfg.GetSection(Options.AuthOptions.SectionPath);
        var providers = section.GetSection("Providers");

        // Well-known adapter defaults for display purposes
        var defaults = new Dictionary<string, (string Name, string Type)>(StringComparer.OrdinalIgnoreCase)
        {
            ["google"] = ("Google", "oidc"),
            ["microsoft"] = ("Microsoft", "oidc"),
            ["discord"] = ("Discord", "oauth2")
        };

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
                if (string.IsNullOrWhiteSpace(display))
                {
                    display = defaults.TryGetValue(id, out var meta) ? meta.Name : Titleize(id);
                }
                var type = child.GetValue<string>(nameof(Options.ProviderOptions.Type));
                if (string.IsNullOrWhiteSpace(type) && defaults.TryGetValue(id, out var meta2))
                {
                    type = meta2.Type;
                }
                detected.Add($"{display} ({PrettyProtocol(type)})");
            }
        }
        else
        {
            // No explicit config: show well-known defaults as detected
            foreach (var kvp in defaults)
            {
                detected.Add($"{kvp.Value.Name} ({PrettyProtocol(kvp.Value.Type)})");
            }
        }

        report.AddSetting("Providers", detected.Count.ToString());
        report.AddSetting("DetectedProviders", string.Join(", ", detected));
    }
}
