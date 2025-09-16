namespace Koan.Web.Auth.Services.Options;

public sealed class ServiceAuthOptions
{
    public const string SectionPath = "Koan:Auth:Services";

    // Token Management
    public TimeSpan TokenCacheDuration { get; init; } = TimeSpan.FromMinutes(55);
    public TimeSpan TokenRefreshBuffer { get; init; } = TimeSpan.FromMinutes(5);
    public bool EnableTokenCaching { get; init; } = true;

    // Service Discovery
    public bool EnableAutoDiscovery { get; init; } = true; // Dev only by default
    public ServiceDiscoveryMode DiscoveryMode { get; init; } = ServiceDiscoveryMode.Auto;
    public Dictionary<string, string> ServiceEndpoints { get; init; } = new();

    // Authentication
    public string TokenEndpoint { get; init; } = "/.testoauth/token";
    public string ClientId { get; init; } = string.Empty; // Auto-generated if empty
    public string ClientSecret { get; init; } = string.Empty; // Auto-generated in dev
    public string[] DefaultScopes { get; init; } = new[] { "koan:service" };

    // Security
    public bool ValidateServerCertificate { get; init; } = true; // False in dev
    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxRetryAttempts { get; init; } = 3;
}

public enum ServiceDiscoveryMode
{
    Auto,           // Container-aware resolution
    Manual,         // Use ServiceEndpoints dictionary
    Registry        // Use service registry (future)
}