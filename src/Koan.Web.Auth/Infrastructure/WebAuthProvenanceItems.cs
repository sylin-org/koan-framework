using System.Collections.Generic;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Web.Auth.Infrastructure;

internal static class WebAuthProvenanceItems
{
    private static readonly IReadOnlyCollection<string> ProviderConsumers = new[] { "Koan.Web.Auth.ProviderRegistry" };

    internal static readonly ProvenanceItem ProviderRegistryCount = new(
        Options.AuthOptions.SectionPath + ":Providers",
        "Auth Providers Registered",
        "Number of authentication providers available after configuration and contributor defaults merge.",
        DefaultValue: "0",
        DefaultConsumers: ProviderConsumers);

    internal static readonly ProvenanceItem ProviderRegistryDetails = new(
        Options.AuthOptions.SectionPath + ":Providers:Detected",
        "Auth Providers Detected",
        "Display names and protocols for configured and contributor-provided authentication providers.",
        DefaultValue: "(none)",
        DefaultConsumers: ProviderConsumers);

    internal static readonly ProvenanceItem DynamicProvidersInProduction = new(
        AuthConstants.Configuration.AllowDynamicProvidersInProduction,
        "Dynamic Providers In Production",
        "Indicates whether contributor-provided authentication providers are allowed to auto-enable in production environments.",
        DefaultValue: "false",
        DefaultConsumers: ProviderConsumers);
}
