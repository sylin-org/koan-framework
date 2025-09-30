namespace Koan.Web.Auth.Connector.Test.Options;

public sealed class TestProviderOptions
{
    public const string SectionPath = "Koan:Web:Auth:TestProvider";
    public bool Enabled { get; init; } = false; // Auto-registrar treats Development as enabled even if false
    public string RouteBase { get; init; } = "/.testoauth";
    public string ClientId { get; init; } = "test-client";
    public string ClientSecret { get; init; } = "test-secret";
    public bool ExposeInDiscoveryOutsideDevelopment { get; init; } = false;
    public string[] AllowedRedirectUris { get; init; } = Array.Empty<string>();

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
    public string JwtSigningKey { get; init; } = string.Empty; // Base64 encoded key; auto-generated if empty
    public string JwtAlgorithm { get; init; } = "HS256";
    public string JwtIssuer { get; set; } = "koan-test-provider";
    public string JwtAudience { get; set; } = "koan-test-client";
    public int JwtExpirationMinutes { get; init; } = 60;

    // Client Credentials Support
    public bool EnableClientCredentials { get; set; } = false;
    public Dictionary<string, ClientCredentialsClient> RegisteredClients { get; init; } = new();
    public string[] AllowedScopes { get; set; } = Array.Empty<string>();
}

