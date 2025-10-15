using System.Collections.Generic;
using Koan.Core.Hosting.Bootstrap;
using Koan.Web.Auth.Services.Options;

namespace Koan.Web.Auth.Services.Infrastructure;

internal static class WebAuthServicesProvenanceItems
{
    private static readonly IReadOnlyCollection<string> HostingConsumers = new[] { "Koan.Web.Auth.Services.Hosting" };
    private static readonly IReadOnlyCollection<string> DiscoveryConsumers = new[] { "Koan.Web.Auth.Services.Discovery" };
    private static readonly IReadOnlyCollection<string> TokenConsumers = new[] { "Koan.Web.Auth.Services.TokenCache" };
    private static readonly IReadOnlyCollection<string> DirectoryConsumers = new[] { "Koan.Web.Auth.Services.Directory" };

    internal static readonly ProvenanceItem Mode = new(
        "Koan:Web:Auth:Services:Mode",
        "Auth Services Mode",
        "Indicates the hosting environment mode used by the service authenticator bootstrap.",
        DefaultValue: "Development",
        DefaultConsumers: HostingConsumers);

    internal static readonly ProvenanceItem AutoDiscovery = new(
        ServiceAuthOptions.SectionPath + ":EnableAutoDiscovery",
        "Auto Discovery",
        "Enables automatic discovery and registration of service clients based on controller attributes.",
        DefaultValue: "true",
        DefaultConsumers: DiscoveryConsumers);

    internal static readonly ProvenanceItem TokenCaching = new(
        ServiceAuthOptions.SectionPath + ":EnableTokenCaching",
        "Token Caching",
        "Controls whether service authentication tokens are cached for reuse between requests.",
        DefaultValue: "true",
        DefaultConsumers: TokenConsumers);

    internal static readonly ProvenanceItem ServicesDiscovered = new(
        ServiceAuthOptions.SectionPath + ":DiscoveredServices",
        "Services Discovered",
        "Number of services discovered via attribute scanning during startup.",
        DefaultValue: "0",
        DefaultConsumers: DirectoryConsumers);

    internal static ProvenanceItem ServiceDetail(string serviceId)
        => new(
            ServiceAuthOptions.SectionPath + ":DiscoveredServices:" + serviceId,
            $"Service {serviceId}",
            "Details for a discovered service including provided scopes and dependency count.",
            DefaultValue: "(none)",
            DefaultConsumers: DirectoryConsumers);
}
