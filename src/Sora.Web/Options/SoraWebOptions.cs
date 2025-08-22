namespace Sora.Web.Options;

/// <summary>
/// Options that control Sora's default web pipeline wiring.
/// Prefer configuring via appsettings or AddSoraWeb().
/// </summary>
public sealed class SoraWebOptions
{
    // If true, the module will automatically hook middleware via an IStartupFilter.
    public bool AutoUse { get; set; } = true;

    // When set, a very light in-pipeline handler responds to GET {HealthPath} with { status: "ok" }.
    public string HealthPath { get; set; } = Sora.Web.Infrastructure.SoraWebConstants.Routes.ApiHealth;

    // Opt-in static files wiring. If true, UseDefaultFiles()+UseStaticFiles() will be applied.
    public bool EnableStaticFiles { get; set; } = true;

    // If true, controllers are mapped automatically by the startup filter.
    public bool AutoMapControllers { get; set; } = true;

    // Apply a minimal set of secure response headers via middleware.
    public bool EnableSecureHeaders { get; set; } = true;

    // If true, assumes the app sits behind a reverse proxy (e.g., nginx) that applies
    // security headers. When set, Sora will omit emitting its own security headers
    // to avoid duplicates and potential conflicts.
    // Configure via: Sora:Web:IsProxiedApi=true (env: Sora__Web__IsProxiedApi=true)
    public bool IsProxiedApi { get; set; } = false;

    // Optional Content-Security-Policy value. If null/empty, CSP is not set.
    // Example safe default for simple static content:
    // "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'"
    public string? ContentSecurityPolicy { get; set; }

    // Controls exposure of the observability snapshot endpoint.
    // Default: false; can be enabled via config. In Development, controller may allow it regardless.
    public bool ExposeObservabilitySnapshot { get; set; } = false;
}
