using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Options;

namespace Koan.Web.Auth.Providers;

// Extensibility point: other packages can contribute provider defaults (e.g., a TestProvider in development).

internal sealed class ProviderRegistry : IProviderRegistry
{
    private readonly AuthOptions _options;
    private readonly Dictionary<string, ProviderOptions> _effective;

    public ProviderRegistry(IOptionsSnapshot<AuthOptions> options, IEnumerable<IAuthProviderContributor> contributors, IConfiguration cfg)
    {
        _options = options.Value;
        _effective = Compose(_options, contributors, cfg);
    }

    public IReadOnlyDictionary<string, ProviderOptions> EffectiveProviders => _effective;

    public IEnumerable<ProviderDescriptor> GetDescriptors()
    {
        foreach (var (id, cfg) in _effective)
        {
            var name = string.IsNullOrWhiteSpace(cfg.DisplayName) ? id : cfg.DisplayName!;
            var protocol = cfg.Type ?? InferTypeFromId(id);
            var scopes = cfg.Scopes;
            var state = EvaluateHealth(protocol, cfg);
            var priority = cfg.Priority ?? 0;
            yield return ProviderDescriptorFactory.Create(id, name, protocol, cfg.Enabled, state, cfg.Icon, scopes, priority);
        }
    }

    private static Dictionary<string, ProviderOptions> Compose(AuthOptions root, IEnumerable<IAuthProviderContributor> contributors, IConfiguration cfg)
    {
        // Contributed defaults only (providers live in separate modules that contribute their own defaults)
        var defaults = new Dictionary<string, ProviderOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in contributors)
        {
            foreach (var kv in c.GetDefaults())
            {
                defaults[kv.Key] = kv.Value;
            }
        }
        var result = new Dictionary<string, ProviderOptions>(StringComparer.OrdinalIgnoreCase);

        // Start with defaults
        foreach (var (id, def) in defaults)
            result[id] = def;

        // Overlay with configured providers
        foreach (var (id, user) in root.Providers)
        {
            if (!result.TryGetValue(id, out var existing))
            {
                // Unknown adapter id: take user settings as-is
                result[id] = user;
                continue;
            }
            result[id] = ProviderOptions.Merge(existing, user);
        }

        // Production gating: if in Production, disable providers that come only from defaults/contributors
        // unless explicitly allowed via either Koan:Web:Auth:AllowDynamicProvidersInProduction or Koan:AllowMagicInProduction
        bool isProd = Koan.Core.KoanEnv.IsProduction;
        bool allowMagic = Koan.Core.KoanEnv.AllowMagicInProduction
                          || Koan.Core.Configuration.Read(cfg, Koan.Core.Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction, false);
        bool allowDynamic = root.AllowDynamicProvidersInProduction
                               || Koan.Core.Configuration.Read(cfg, AuthConstants.Configuration.AllowDynamicProvidersInProduction, false);
        if (isProd && !(allowMagic || allowDynamic))
        {
            // Any provider id that wasn't explicitly present in root.Providers should default to disabled
            var explicitIds = new HashSet<string>(root.Providers.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var id in result.Keys.ToList())
            {
                if (!explicitIds.Contains(id))
                {
                    result[id] = ProviderOptions.WithEnabled(result[id], false);
                }
            }
        }

        return result;
    }

    private static string InferTypeFromId(string id) => AuthConstants.Protocols.Oidc; // conservative fallback


    private static string EvaluateHealth(string protocol, ProviderOptions cfg)
    {
        // Healthy if minimally configured for the protocol; Unhealthy if enabled but missing required bits; Unknown otherwise
        var p = protocol?.ToLowerInvariant();
        if (p == AuthConstants.Protocols.Oidc)
        {
            var ok = !string.IsNullOrWhiteSpace(cfg.Authority)
                  && !string.IsNullOrWhiteSpace(cfg.ClientId)
                  && (!string.IsNullOrWhiteSpace(cfg.ClientSecret) || !string.IsNullOrWhiteSpace(cfg.SecretRef));
            return ok ? "Healthy" : (cfg.Enabled ? "Unhealthy" : "Unknown");
        }
        if (p == AuthConstants.Protocols.OAuth2)
        {
            var ok = !string.IsNullOrWhiteSpace(cfg.AuthorizationEndpoint)
                  && !string.IsNullOrWhiteSpace(cfg.TokenEndpoint)
                  && !string.IsNullOrWhiteSpace(cfg.ClientId)
                  && (!string.IsNullOrWhiteSpace(cfg.ClientSecret) || !string.IsNullOrWhiteSpace(cfg.SecretRef));
            return ok ? "Healthy" : (cfg.Enabled ? "Unhealthy" : "Unknown");
        }
        return cfg.Enabled ? "Unhealthy" : "Unknown"; // unknown protocol and enabled → suspect
    }
}
