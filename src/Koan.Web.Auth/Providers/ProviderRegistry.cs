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
            result[id] = Merge(existing, user);
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
                    var p = result[id];
                    result[id] = new ProviderOptions
                    {
                        // copy with Enabled=false
                        Type = p.Type,
                        DisplayName = p.DisplayName,
                        Icon = p.Icon,
                        Enabled = false,
                        Priority = p.Priority,
                        Authority = p.Authority,
                        ClientId = p.ClientId,
                        ClientSecret = p.ClientSecret,
                        SecretRef = p.SecretRef,
                        Scopes = p.Scopes,
                        CallbackPath = p.CallbackPath,
                        AuthorizationEndpoint = p.AuthorizationEndpoint,
                        TokenEndpoint = p.TokenEndpoint,
                        UserInfoEndpoint = p.UserInfoEndpoint,
                        EntityId = p.EntityId,
                        IdpMetadataUrl = p.IdpMetadataUrl,
                        IdpMetadataXml = p.IdpMetadataXml,
                        SigningCertRef = p.SigningCertRef,
                        DecryptionCertRef = p.DecryptionCertRef,
                        AllowIdpInitiated = p.AllowIdpInitiated,
                        ClockSkewSeconds = p.ClockSkewSeconds
                    };
                }
            }
        }

        return result;
    }

    private static ProviderOptions Merge(ProviderOptions a, ProviderOptions b)
    {
        return new ProviderOptions
        {
            // Common
            Type = b.Type ?? a.Type,
            DisplayName = b.DisplayName ?? a.DisplayName,
            Icon = b.Icon ?? a.Icon,
            Enabled = b.Enabled && a.Enabled,
            Priority = b.Priority ?? a.Priority,

            // OIDC
            Authority = b.Authority ?? a.Authority,
            ClientId = b.ClientId ?? a.ClientId,
            ClientSecret = b.ClientSecret ?? a.ClientSecret,
            SecretRef = b.SecretRef ?? a.SecretRef,
            Scopes = b.Scopes ?? a.Scopes,
            CallbackPath = b.CallbackPath ?? a.CallbackPath,

            // OAuth2
            AuthorizationEndpoint = b.AuthorizationEndpoint ?? a.AuthorizationEndpoint,
            TokenEndpoint = b.TokenEndpoint ?? a.TokenEndpoint,
            UserInfoEndpoint = b.UserInfoEndpoint ?? a.UserInfoEndpoint,

            // SAML
            EntityId = b.EntityId ?? a.EntityId,
            IdpMetadataUrl = b.IdpMetadataUrl ?? a.IdpMetadataUrl,
            IdpMetadataXml = b.IdpMetadataXml ?? a.IdpMetadataXml,
            SigningCertRef = b.SigningCertRef ?? a.SigningCertRef,
            DecryptionCertRef = b.DecryptionCertRef ?? a.DecryptionCertRef,
            AllowIdpInitiated = b.AllowIdpInitiated || a.AllowIdpInitiated,
            ClockSkewSeconds = b.ClockSkewSeconds != 120 ? b.ClockSkewSeconds : a.ClockSkewSeconds
        };
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
        if (p == AuthConstants.Protocols.Saml)
        {
            var ok = !string.IsNullOrWhiteSpace(cfg.EntityId)
                  && (!string.IsNullOrWhiteSpace(cfg.IdpMetadataUrl) || !string.IsNullOrWhiteSpace(cfg.IdpMetadataXml));
            return ok ? "Healthy" : (cfg.Enabled ? "Unhealthy" : "Unknown");
        }
        return cfg.Enabled ? "Unhealthy" : "Unknown"; // unknown protocol and enabled → suspect
    }
}
