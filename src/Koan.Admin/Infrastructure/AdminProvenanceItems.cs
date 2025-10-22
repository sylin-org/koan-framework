using System.Collections.Generic;
using System.Globalization;
using Koan.Admin.Options;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Admin.Infrastructure;

internal static class AdminProvenanceItems
{
    private static readonly KoanAdminOptions Defaults = new();
    private static readonly KoanAdminAuthorizationOptions AuthorizationDefaults = Defaults.Authorization;
    private static readonly KoanAdminLoggingOptions LoggingDefaults = Defaults.Logging;
    private static readonly KoanAdminGenerateOptions GenerateDefaults = Defaults.Generate;

    private static readonly IReadOnlyCollection<string> FeatureManagerConsumers = new[] { "Koan.Admin.FeatureManager" };
    private static readonly IReadOnlyCollection<string> FeatureManagerAndWebConsumers = new[] { "Koan.Admin.FeatureManager", "Koan.Web.Admin" };
    private static readonly IReadOnlyCollection<string> RouteProviderConsumers = new[] { "Koan.Admin.RouteProvider" };
    private static readonly IReadOnlyCollection<string> RouteAndWebConsumers = new[] { "Koan.Admin.RouteProvider", "Koan.Web.Admin" };
    private static readonly IReadOnlyCollection<string> LaunchKitConsumers = new[] { "Koan.Admin.LaunchKitService" };
    private static readonly IReadOnlyCollection<string> ManifestConsumers = new[] { "Koan.Admin.ManifestService" };
    private static readonly IReadOnlyCollection<string> AuthorizationConsumers = new[] { "Koan.Web.Admin.Authorization" };
    private static readonly IReadOnlyCollection<string> LoggingConsumers = new[] { "Koan.Admin.Logging" };

    internal static readonly ProvenanceItem Enabled = new(
        ConfigurationConstants.Admin.Section + ":" + ConfigurationConstants.Admin.Keys.Enabled,
        "Koan Admin Enabled",
        "Turns Koan Admin surfaces on or off.",
        DefaultValue: AsBool(Defaults.Enabled),
        DefaultConsumers: FeatureManagerConsumers);

    internal static readonly ProvenanceItem AllowInProduction = new(
        ConfigurationConstants.Admin.Section + ":" + ConfigurationConstants.Admin.Keys.AllowInProduction,
        "Allow In Production",
        "Permits Koan Admin endpoints to activate in staging or production environments.",
        DefaultValue: AsBool(Defaults.AllowInProduction),
        DefaultConsumers: FeatureManagerConsumers);

    internal static readonly ProvenanceItem AllowDotPrefixInProduction = new(
        ConfigurationConstants.Admin.Section + ":" + ConfigurationConstants.Admin.Keys.AllowDotPrefixInProduction,
        "Allow Dot Prefix Outside Development",
        "If disabled, dot-prefixed admin routes stay inactive beyond Development.",
        DefaultValue: AsBool(Defaults.AllowDotPrefixInProduction),
        DefaultConsumers: RouteProviderConsumers);

    internal static readonly ProvenanceItem WebEnabled = new(
        ConfigurationConstants.Admin.Section + ":" + ConfigurationConstants.Admin.Keys.EnableWeb,
        "Admin Web Enabled",
        "Enables the Koan Admin web UI surfaces when true.",
        DefaultValue: AsBool(Defaults.EnableWeb),
        DefaultConsumers: FeatureManagerAndWebConsumers);

    internal static readonly ProvenanceItem ConsoleEnabled = new(
        ConfigurationConstants.Admin.Section + ":" + ConfigurationConstants.Admin.Keys.EnableConsoleUi,
        "Admin Console Enabled",
        "Enables the lightweight console UI for Koan Admin.",
        DefaultValue: AsBool(Defaults.EnableConsoleUi),
        DefaultConsumers: FeatureManagerConsumers);

    internal static readonly ProvenanceItem LaunchKitEnabled = new(
        ConfigurationConstants.Admin.Section + ":" + ConfigurationConstants.Admin.Keys.EnableLaunchKit,
        "LaunchKit Enabled",
        "Allows LaunchKit bundle export endpoints to activate.",
        DefaultValue: AsBool(Defaults.EnableLaunchKit),
        DefaultConsumers: LaunchKitConsumers);

    internal static readonly ProvenanceItem ManifestExposure = new(
        ConfigurationConstants.Admin.Section + ":" + ConfigurationConstants.Admin.Keys.ExposeManifest,
        "Expose Manifest",
        "Controls whether the admin manifest endpoint is publicly exposed.",
        DefaultValue: AsBool(Defaults.ExposeManifest),
        DefaultConsumers: ManifestConsumers);

    internal static readonly ProvenanceItem DestructiveOperations = new(
        ConfigurationConstants.Admin.Section + ":" + ConfigurationConstants.Admin.Keys.DestructiveOps,
        "Destructive Operations",
        "Gates high-risk maintenance operations surfaced by Koan Admin.",
        DefaultValue: AsBool(Defaults.DestructiveOps),
        DefaultConsumers: FeatureManagerConsumers);

    internal static readonly ProvenanceItem PathPrefix = new(
        ConfigurationConstants.Admin.Section + ":" + ConfigurationConstants.Admin.Keys.PathPrefix,
        "Admin Path Prefix",
        "Base route segment used when mapping Koan Admin endpoints.",
        DefaultValue: Defaults.PathPrefix,
        DefaultConsumers: RouteAndWebConsumers);

    internal static readonly ProvenanceItem AuthorizationPolicy = new(
        ConfigurationConstants.Admin.Authorization.Section + ":" + ConfigurationConstants.Admin.Authorization.Keys.Policy,
        "Authorization Policy",
        "Name of the ASP.NET Core authorization policy guarding admin endpoints.",
        DefaultValue: AuthorizationDefaults.Policy,
        DefaultConsumers: AuthorizationConsumers);

    internal static readonly ProvenanceItem AuthorizationAutoCreatePolicy = new(
        ConfigurationConstants.Admin.Authorization.Section + ":" + ConfigurationConstants.Admin.Authorization.Keys.AutoCreateDevelopmentPolicy,
        "Auto-Create Development Policy",
        "Automatically registers a permissive development policy when none exists.",
        DefaultValue: AsBool(AuthorizationDefaults.AutoCreateDevelopmentPolicy),
        DefaultConsumers: AuthorizationConsumers);

    internal static readonly ProvenanceItem AuthorizationAllowedNetworks = new(
        ConfigurationConstants.Admin.Authorization.Section + ":" + ConfigurationConstants.Admin.Authorization.Keys.AllowedNetworks,
        "Allowed Networks",
        "CIDR ranges permitted to access Koan Admin routes when network restrictions are enabled.",
        DefaultValue: FormatList(AuthorizationDefaults.AllowedNetworks),
        DefaultConsumers: AuthorizationConsumers);

    internal static readonly ProvenanceItem LoggingEnableStream = new(
        ConfigurationConstants.Admin.Logging.Section + ":" + ConfigurationConstants.Admin.Logging.Keys.EnableLogStream,
        "Enable Log Stream",
        "Allows clients to stream server logs through Koan Admin.",
        DefaultValue: AsBool(LoggingDefaults.EnableLogStream),
        DefaultConsumers: LoggingConsumers);

    internal static readonly ProvenanceItem LoggingAllowTranscriptDownload = new(
        ConfigurationConstants.Admin.Logging.Section + ":" + ConfigurationConstants.Admin.Logging.Keys.AllowTranscriptDownload,
        "Allow Transcript Download",
        "Permits downloading captured request transcripts from Koan Admin.",
        DefaultValue: AsBool(LoggingDefaults.AllowTranscriptDownload),
        DefaultConsumers: LoggingConsumers);

    internal static readonly ProvenanceItem LoggingAllowedCategories = new(
        ConfigurationConstants.Admin.Logging.Section + ":" + ConfigurationConstants.Admin.Logging.Keys.AllowedCategories,
        "Allowed Log Categories",
        "Restricts log streaming to these categories when provided.",
        DefaultValue: FormatList(LoggingDefaults.AllowedCategories),
        DefaultConsumers: LoggingConsumers);

    internal static readonly ProvenanceItem GenerateComposeProfiles = new(
        ConfigurationConstants.Admin.Generate.Section + ":" + ConfigurationConstants.Admin.Generate.Keys.ComposeProfiles,
        "Compose Profiles",
        "Docker Compose profiles included when exporting LaunchKit bundles.",
        DefaultValue: FormatList(GenerateDefaults.ComposeProfiles),
        DefaultConsumers: LaunchKitConsumers);

    internal static readonly ProvenanceItem GenerateOpenApiClients = new(
        ConfigurationConstants.Admin.Generate.Section + ":" + ConfigurationConstants.Admin.Generate.Keys.OpenApiClients,
        "OpenAPI Clients",
        "List of OpenAPI client generators to run during exports.",
        DefaultValue: FormatList(GenerateDefaults.OpenApiClients),
        DefaultConsumers: LaunchKitConsumers);

    internal static readonly ProvenanceItem GenerateIncludeAppSettings = new(
        ConfigurationConstants.Admin.Generate.Section + ":" + ConfigurationConstants.Admin.Generate.Keys.IncludeAppSettings,
        "Include AppSettings",
        "Includes appsettings.json snapshots in LaunchKit exports.",
        DefaultValue: AsBool(GenerateDefaults.IncludeAppSettings),
        DefaultConsumers: LaunchKitConsumers);

    internal static readonly ProvenanceItem GenerateIncludeCompose = new(
        ConfigurationConstants.Admin.Generate.Section + ":" + ConfigurationConstants.Admin.Generate.Keys.IncludeCompose,
        "Include Compose Files",
        "Includes Docker Compose artifacts in LaunchKit exports.",
        DefaultValue: AsBool(GenerateDefaults.IncludeCompose),
        DefaultConsumers: LaunchKitConsumers);

    internal static readonly ProvenanceItem GenerateIncludeAspire = new(
        ConfigurationConstants.Admin.Generate.Section + ":" + ConfigurationConstants.Admin.Generate.Keys.IncludeAspire,
        "Include Aspire Assets",
        "Packages Aspire-specific assets with LaunchKit exports.",
        DefaultValue: AsBool(GenerateDefaults.IncludeAspire),
        DefaultConsumers: LaunchKitConsumers);

    internal static readonly ProvenanceItem GenerateIncludeManifest = new(
        ConfigurationConstants.Admin.Generate.Section + ":" + ConfigurationConstants.Admin.Generate.Keys.IncludeManifest,
        "Include Manifest",
        "Attaches the Koan manifest to LaunchKit exports when enabled.",
        DefaultValue: AsBool(GenerateDefaults.IncludeManifest),
        DefaultConsumers: LaunchKitConsumers);

    internal static readonly ProvenanceItem GenerateIncludeReadme = new(
        ConfigurationConstants.Admin.Generate.Section + ":" + ConfigurationConstants.Admin.Generate.Keys.IncludeReadme,
        "Include README",
        "Generates a README alongside LaunchKit export bundles.",
        DefaultValue: AsBool(GenerateDefaults.IncludeReadme),
        DefaultConsumers: LaunchKitConsumers);

    internal static readonly ProvenanceItem GenerateComposeBasePort = new(
        ConfigurationConstants.Admin.Generate.Section + ":" + ConfigurationConstants.Admin.Generate.Keys.ComposeBasePort,
        "Compose Base Port",
        "Base port offset applied when generating Docker Compose artifacts.",
        DefaultValue: GenerateDefaults.ComposeBasePort.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: LaunchKitConsumers);

    private static string AsBool(bool value) => value ? "true" : "false";

    private static string FormatList(IReadOnlyCollection<string> items)
    {
        if (items is null || items.Count == 0)
        {
            return "(none)";
        }

        var trimmed = new List<string>(items.Count);
        foreach (var item in items)
        {
            var candidate = (item ?? string.Empty).Trim();
            if (candidate.Length > 0)
            {
                trimmed.Add(candidate);
            }
        }

        return trimmed.Count == 0 ? "(none)" : string.Join(", ", trimmed);
    }
}
