using Microsoft.Extensions.Hosting;

namespace Koan.Web.Auth.Connector.Test.Options;

public sealed class TestProviderOptions
{
    public const string SectionPath = "Koan:Web:Auth:TestProvider";
    // Opt-in flag for non-Development. In Development the provider is active by default (see IsActive); set this
    // true to expose the OAuth/OIDC simulator endpoints outside Development.
    public bool Enabled { get; init; } = false;
    public string RouteBase { get; init; } = "/.testoauth";
    public string ClientId { get; init; } = "test-client";
    public string ClientSecret { get; init; } = "test-secret";
    public bool ExposeInDiscoveryOutsideDevelopment { get; init; } = false;
    public string[] AllowedRedirectUris { get; init; } = [];

    // Caps and DX knobs
    public int MaxRoles { get; init; } = 256; // align with Koan.Web.Auth.Roles default
    public int MaxPermissions { get; init; } = 1024; // align with Koan.Web.Auth.Roles default
    public int MaxCustomClaimTypes { get; init; } = 64;
    public int MaxValuesPerClaimType { get; init; } = 32;

    // Dev UX defaults (UI prepopulation)
    public string[] DefaultRoles { get; init; } = new[] { "reader" };
    public bool PersistPersona { get; init; } = true; // use LocalStorage in the login UI

    // JWT Token Configuration
    public bool UseJwtTokens { get; set; } = false;
    public string JwtSigningKey { get; init; } = ""; // Base64 encoded key; auto-generated if empty
    public string JwtAlgorithm { get; init; } = "HS256";
    public string JwtIssuer { get; set; } = "koan-test-provider";
    public string JwtAudience { get; set; } = "koan-test-client";
    public int JwtExpirationMinutes { get; init; } = 60;

    // Client Credentials Support
    public bool EnableClientCredentials { get; set; } = false;
    public Dictionary<string, ClientCredentialsClient> RegisteredClients { get; init; } = new();
    public string[] AllowedScopes { get; set; } = [];

    /// <summary>
    /// The single source of truth for whether the Test provider is active: advertised AND its endpoints mapped.
    /// Active in Development by default (the zero-config dev login — SEC-0003 §2.2); opt-in elsewhere via
    /// <see cref="Enabled"/> / <see cref="ExposeInDiscoveryOutsideDevelopment"/> (fail-closed outside Development —
    /// SEC-0001). Both the discovery contributor and the endpoint-mapping startup filter MUST use this, so the two
    /// halves cannot drift into advertising buttons whose endpoints aren't mapped.
    /// </summary>
    public bool IsActive(IHostEnvironment? env)
        => Enabled || (env?.IsDevelopment() ?? false) || ExposeInDiscoveryOutsideDevelopment;
}

