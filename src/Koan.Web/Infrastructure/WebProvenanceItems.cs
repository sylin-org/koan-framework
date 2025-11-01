using System.Collections.Generic;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Web.Infrastructure;

internal static class WebProvenanceItems
{
    private static readonly IReadOnlyCollection<string> SecurityConsumers = new[] { "Koan.Web.SecurityHeaders" };
    private static readonly IReadOnlyCollection<string> PipelineConsumers = new[] { "Koan.Web.HttpPipeline" };
    private static readonly IReadOnlyCollection<string> DiscoveryConsumers = new[] { "Koan.Web.ControllerDiscovery" };

    internal static readonly ProvenanceItem SecureHeadersEnabled = new(
        ConfigurationConstants.Web.Section + ":" + ConfigurationConstants.Web.Keys.EnableSecureHeaders,
        "Secure Headers",
        "Adds Koan's default security headers (HSTS, CSP, X-Frame-Options, etc.) to every response.",
        DefaultValue: "true",
        DefaultConsumers: SecurityConsumers);

    internal static readonly ProvenanceItem ProxiedApi = new(
        ConfigurationConstants.Web.Section + ":" + ConfigurationConstants.Web.Keys.IsProxiedApi,
        "Proxied API",
        "Marks the app as sitting behind a reverse proxy so forwarded headers and base URLs are honored.",
        DefaultValue: "false",
        DefaultConsumers: PipelineConsumers);

    internal static readonly ProvenanceItem AutoMapControllers = new(
        ConfigurationConstants.Web.Section + ":" + ConfigurationConstants.Web.Keys.AutoMapControllers,
        "Auto Map Controllers",
        "Automatically registers MVC controllers without requiring explicit MapControllerRoute calls.",
        DefaultValue: "true",
        DefaultConsumers: DiscoveryConsumers);

    internal static readonly ProvenanceItem ContentSecurityPolicy = new(
        ConfigurationConstants.Web.Section + ":" + ConfigurationConstants.Web.Keys.ContentSecurityPolicy,
        "Content Security Policy",
        "Explicit Content-Security-Policy directive applied to responses when non-empty.",
        MustSanitize: true,
        DefaultValue: string.Empty,
        DefaultConsumers: SecurityConsumers);

    internal static readonly ProvenanceItem ApplicationUrl = new(
        ConfigurationConstants.Web.Section + ":" + ConfigurationConstants.Web.Keys.ApplicationUrl,
        "Application Base URL",
        "Explicit override for application base URL. When set, takes precedence over ASPNETCORE_URLS detection. Used for generating absolute URLs in container environments.",
        DefaultValue: "(detected from ASPNETCORE_URLS)",
        DefaultConsumers: new[] { "Koan.Web.Urls", "Koan.Web.Admin", "Koan.Web.Connector.Swagger" });
}
