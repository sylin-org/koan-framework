namespace Sora.Web.Auth.TestProvider.Options;

public sealed class TestProviderOptions
{
    public const string SectionPath = "Sora:Web:Auth:TestProvider";
    public bool Enabled { get; init; } = false; // Auto-registrar treats Development as enabled even if false
    public string RouteBase { get; init; } = "/.testoauth";
    public string ClientId { get; init; } = "test-client";
    public string ClientSecret { get; init; } = "test-secret";
    public bool ExposeInDiscoveryOutsideDevelopment { get; init; } = false;
    public string[] AllowedRedirectUris { get; init; } = Array.Empty<string>();

    // Caps and DX knobs
    public int MaxRoles { get; init; } = 256; // align with Sora.Web.Auth.Roles default
    public int MaxPermissions { get; init; } = 1024; // align with Sora.Web.Auth.Roles default
    public int MaxCustomClaimTypes { get; init; } = 64;
    public int MaxValuesPerClaimType { get; init; } = 32;

    // Dev UX defaults (UI prepopulation)
    public string[] DefaultRoles { get; init; } = new[] { "reader" };
    public bool PersistPersona { get; init; } = true; // use LocalStorage in the login UI
}
