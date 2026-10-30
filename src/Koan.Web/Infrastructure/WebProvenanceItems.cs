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
        "Marks the app as sitting behind a reverse proxy that emits the security headers, so Koan omits its own.",
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
        DefaultValue: "",
        DefaultConsumers: SecurityConsumers);

    internal static readonly ProvenanceItem ApplicationUrl = new(
        ConfigurationConstants.Web.Section + ":" + ConfigurationConstants.Web.Keys.ApplicationUrl,
        "Application Base URL",
        "The application's public base URL (scheme, host, optional path). When set it is authoritative: the pipeline serves under its path only, and every absolute URL Koan writes derives from it. When unset, URLs derive from each request.",
        DefaultValue: "(derived from each request)",
        DefaultConsumers: new[] { "Koan.Web.Urls", "Koan.Web.HttpPipeline", "Koan.Web.Auth.Server", "Koan.Mcp", "Koan.Web.OpenGraph", "Koan.Web.Auth.Connector.Atproto" });

    internal static readonly ProvenanceItem KeyRingDirectory = new(
        ConfigurationConstants.Web.Section + ":" + ConfigurationConstants.Web.Keys.KeyRingDirectory,
        "Key Ring Directory",
        "The folder the ASP.NET Data Protection key ring persists in, under the application name the folder remembers, so a rebuilt container, a renamed application and every instance over it share the keys. Unset leaves the ring where ASP.NET puts it.",
        DefaultValue: "(ASP.NET default location)",
        DefaultConsumers: new[] { "Koan.Web.Auth", "Koan.Web.Auth.Connector.Atproto" });

    internal static readonly ProvenanceItem KeyRingApplicationName = new(
        ConfigurationConstants.Web.Section + ":" + ConfigurationConstants.Web.Keys.KeyRingApplicationName,
        "Key Ring Application Name",
        "The application name the key ring folder's keys are scoped to, pinned. Unset, the folder remembers the name it was first written under; a pinned name the folder does not hold refuses the start.",
        DefaultValue: "(remembered by the folder)",
        DefaultConsumers: new[] { "Koan.Web.Auth", "Koan.Web.Auth.Connector.Atproto" });

    internal static readonly ProvenanceItem TrustedProxies = new(
        ConfigurationConstants.Web.Section + ":" + ConfigurationConstants.Web.Keys.TrustedProxies,
        "Trusted Proxies",
        "Reverse proxies (IP or CIDR) whose X-Forwarded-Proto, X-Forwarded-Host and X-Forwarded-For are honoured, walking back to the first address nobody declared. Empty honours none.",
        DefaultValue: "(none)",
        DefaultConsumers: PipelineConsumers);
}
