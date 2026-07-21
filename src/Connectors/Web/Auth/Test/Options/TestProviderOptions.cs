using Microsoft.Extensions.Hosting;

namespace Koan.Web.Auth.Connector.Test.Options;

public sealed class TestProviderOptions
{
    public const string SectionPath = "Koan:Web:Auth:TestProvider";
    // Opt-in flag for non-Development. In Development the provider is active by default (see IsActive); set this
    // true to expose the OAuth/OIDC simulator endpoints outside Development.
    public bool Enabled { get; init; } = false;
    public string ClientId { get; init; } = "test-client";
    public string ClientSecret { get; init; } = "test-secret";
    public string[] AllowedRedirectUris { get; init; } = [];

    // Caps and DX knobs
    public int MaxRoles { get; init; } = 256;
    public int MaxPermissions { get; init; } = 1024;
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
    /// The single source of truth for whether the local provider is available for automatic election. Its stable
    /// attribute-routed endpoints remain fail-closed outside Development unless explicitly enabled.
    /// </summary>
    public bool IsActive(IHostEnvironment? env)
        => Enabled || (env?.IsDevelopment() ?? false);
}

