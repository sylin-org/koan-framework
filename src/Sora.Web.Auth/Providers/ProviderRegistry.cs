using Microsoft.Extensions.Options;
using Sora.Web.Auth.Infrastructure;
using Sora.Web.Auth.Options;

namespace Sora.Web.Auth.Providers;

public interface IProviderRegistry
{
    IReadOnlyDictionary<string, ProviderOptions> EffectiveProviders { get; }
    IEnumerable<ProviderDescriptor> GetDescriptors();
}

internal sealed class ProviderRegistry : IProviderRegistry
{
    private readonly AuthOptions _options;
    private readonly Dictionary<string, ProviderOptions> _effective;

    public ProviderRegistry(IOptionsSnapshot<AuthOptions> options)
    {
        _options = options.Value;
        _effective = Compose(_options);
    }

    public IReadOnlyDictionary<string, ProviderOptions> EffectiveProviders => _effective;

    public IEnumerable<ProviderDescriptor> GetDescriptors()
    {
        foreach (var (id, cfg) in _effective)
        {
            var name = string.IsNullOrWhiteSpace(cfg.DisplayName) ? id : cfg.DisplayName!;
            var protocol = cfg.Type ?? InferTypeFromId(id);
            var scopes = cfg.Scopes;
            yield return ProviderDescriptorFactory.Create(id, name, protocol, cfg.Enabled, cfg.Icon, scopes);
        }
    }

    private static Dictionary<string, ProviderOptions> Compose(AuthOptions root)
    {
        // Adapter defaults
        var defaults = GetAdapterDefaults();
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

    private static string InferTypeFromId(string id)
    {
        return id.ToLowerInvariant() switch
        {
            "google" => AuthConstants.Protocols.Oidc,
            "microsoft" => AuthConstants.Protocols.Oidc,
            "discord" => AuthConstants.Protocols.OAuth2,
            _ => AuthConstants.Protocols.Oidc // conservative default for unknowns using generic OIDC
        };
    }

    private static IReadOnlyDictionary<string, ProviderOptions> GetAdapterDefaults()
    {
        // Defaults based on common provider metadata
        return new Dictionary<string, ProviderOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["google"] = new ProviderOptions
            {
                Type = AuthConstants.Protocols.Oidc,
                DisplayName = "Google",
                Icon = "/icons/google.svg",
                Authority = "https://accounts.google.com",
                Scopes = new []{"openid","email","profile"},
                Enabled = true
            },
            ["microsoft"] = new ProviderOptions
            {
                Type = AuthConstants.Protocols.Oidc,
                DisplayName = "Microsoft",
                Icon = "/icons/microsoft.svg",
                Authority = "https://login.microsoftonline.com/common/v2.0",
                Scopes = new []{"openid","email","profile"},
                Enabled = true
            },
            ["discord"] = new ProviderOptions
            {
                Type = AuthConstants.Protocols.OAuth2,
                DisplayName = "Discord",
                Icon = "/icons/discord.svg",
                AuthorizationEndpoint = "https://discord.com/api/oauth2/authorize",
                TokenEndpoint = "https://discord.com/api/oauth2/token",
                UserInfoEndpoint = "https://discord.com/api/users/@me",
                Scopes = new []{"identify","email"},
                Enabled = true
            }
        };
    }
}
