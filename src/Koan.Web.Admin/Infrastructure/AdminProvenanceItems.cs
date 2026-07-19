using Koan.Core.Hosting.Bootstrap;
using Koan.Web.Admin.Options;

namespace Koan.Web.Admin.Infrastructure;

internal static class AdminProvenanceItems
{
    private static readonly KoanAdminOptions Defaults = new();
    private static readonly IReadOnlyCollection<string> Consumers = ["Koan.Web.Admin"];

    internal static readonly ProvenanceItem Enabled = new(
        ConfigurationConstants.Admin.Section + ":" + ConfigurationConstants.Admin.Keys.Enabled,
        "Koan Admin Enabled",
        "Enables the authenticated, read-only dashboard in Development.",
        DefaultValue: Defaults.Enabled ? "true" : "false",
        DefaultConsumers: Consumers);

    internal static readonly ProvenanceItem PathPrefix = new(
        ConfigurationConstants.Admin.Section + ":" + ConfigurationConstants.Admin.Keys.PathPrefix,
        "Admin Path Prefix",
        "Startup-owned route prefix for the Admin UI and API.",
        DefaultValue: Defaults.PathPrefix,
        DefaultConsumers: Consumers);

    internal static readonly ProvenanceItem AuthorizationPolicy = new(
        ConfigurationConstants.Admin.Authorization.Section + ":" + ConfigurationConstants.Admin.Authorization.Keys.Policy,
        "Authorization Policy",
        "ASP.NET Core authorization policy required by every Admin request.",
        DefaultValue: Defaults.Authorization.Policy,
        DefaultConsumers: Consumers);

    internal static readonly ProvenanceItem AuthorizationAutoCreatePolicy = new(
        ConfigurationConstants.Admin.Authorization.Section + ":" + ConfigurationConstants.Admin.Authorization.Keys.AutoCreateDevelopmentPolicy,
        "Create Development Policy",
        "Creates the named authenticated-user policy only when the application has not defined it.",
        DefaultValue: Defaults.Authorization.AutoCreateDevelopmentPolicy ? "true" : "false",
        DefaultConsumers: Consumers);
}
