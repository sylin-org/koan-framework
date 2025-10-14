using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Admin.Extensions;
using Koan.Admin.Infrastructure;
using Koan.Admin.Options;
using Koan.Admin.Services;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Admin.Initialization;

public sealed class KoanAdminAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Admin";
    public string? ModuleVersion => typeof(KoanAdminAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanAdminCore();
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        var configuration = cfg ?? new ConfigurationBuilder().Build();
        var defaults = new KoanAdminOptions();
        var authorizationDefaults = defaults.Authorization;
        var loggingDefaults = defaults.Logging;
        var generateDefaults = defaults.Generate;

        var enabledOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.Enabled}", defaults.Enabled);
        var allowProdOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.AllowInProduction}", defaults.AllowInProduction);
        var webOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.EnableWeb}", defaults.EnableWeb);
        var consoleOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.EnableConsoleUi}", defaults.EnableConsoleUi);
        var launchKitOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.EnableLaunchKit}", defaults.EnableLaunchKit);
        var manifestOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.ExposeManifest}", defaults.ExposeManifest);
        var destructiveOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.DestructiveOps}", defaults.DestructiveOps);
        var allowDotOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.AllowDotPrefixInProduction}", defaults.AllowDotPrefixInProduction);
        var prefixOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.PathPrefix}", defaults.PathPrefix);

        var normalizedPrefix = KoanAdminPathUtility.NormalizePrefix(prefixOption.Value ?? KoanAdminDefaults.Prefix);
        var routes = KoanAdminRouteProvider.CreateMap(new KoanAdminOptions { PathPrefix = normalizedPrefix });

        var authorizationPolicyOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Authorization.Section}:{ConfigurationConstants.Admin.Authorization.Keys.Policy}", authorizationDefaults.Policy);
        var authorizationAutoCreateOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Authorization.Section}:{ConfigurationConstants.Admin.Authorization.Keys.AutoCreateDevelopmentPolicy}", authorizationDefaults.AutoCreateDevelopmentPolicy);
        var allowedNetworks = ReadStringArray(configuration, $"{ConfigurationConstants.Admin.Authorization.Section}:{ConfigurationConstants.Admin.Authorization.Keys.AllowedNetworks}", authorizationDefaults.AllowedNetworks);

        var enableLogStreamOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Logging.Section}:{ConfigurationConstants.Admin.Logging.Keys.EnableLogStream}", loggingDefaults.EnableLogStream);
        var allowTranscriptOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Logging.Section}:{ConfigurationConstants.Admin.Logging.Keys.AllowTranscriptDownload}", loggingDefaults.AllowTranscriptDownload);
        var allowedCategories = ReadStringArray(configuration, $"{ConfigurationConstants.Admin.Logging.Section}:{ConfigurationConstants.Admin.Logging.Keys.AllowedCategories}", loggingDefaults.AllowedCategories);

        var composeProfiles = ReadStringArray(configuration, $"{ConfigurationConstants.Admin.Generate.Section}:{ConfigurationConstants.Admin.Generate.Keys.ComposeProfiles}", generateDefaults.ComposeProfiles);
        var openApiClients = ReadStringArray(configuration, $"{ConfigurationConstants.Admin.Generate.Section}:{ConfigurationConstants.Admin.Generate.Keys.OpenApiClients}", generateDefaults.OpenApiClients);
        var includeAppSettingsOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Generate.Section}:{ConfigurationConstants.Admin.Generate.Keys.IncludeAppSettings}", generateDefaults.IncludeAppSettings);
        var includeComposeOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Generate.Section}:{ConfigurationConstants.Admin.Generate.Keys.IncludeCompose}", generateDefaults.IncludeCompose);
        var includeAspireOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Generate.Section}:{ConfigurationConstants.Admin.Generate.Keys.IncludeAspire}", generateDefaults.IncludeAspire);
        var includeManifestOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Generate.Section}:{ConfigurationConstants.Admin.Generate.Keys.IncludeManifest}", generateDefaults.IncludeManifest);
        var includeReadmeOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Generate.Section}:{ConfigurationConstants.Admin.Generate.Keys.IncludeReadme}", generateDefaults.IncludeReadme);
        var composeBasePortOption = Configuration.ReadWithSource(configuration, $"{ConfigurationConstants.Admin.Generate.Section}:{ConfigurationConstants.Admin.Generate.Keys.ComposeBasePort}", generateDefaults.ComposeBasePort);

        report.AddSetting(
            "enabled",
            BoolString(enabledOption.Value),
            source: enabledOption.Source,
            consumers: new[] { "Koan.Admin.FeatureManager" },
            sourceKey: enabledOption.ResolvedKey);

        report.AddSetting(
            "allow.production",
            BoolString(allowProdOption.Value),
            source: allowProdOption.Source,
            consumers: new[] { "Koan.Admin.FeatureManager" },
            sourceKey: allowProdOption.ResolvedKey);

        report.AddSetting(
            "allow.dotPrefix.production",
            BoolString(allowDotOption.Value),
            source: allowDotOption.Source,
            consumers: new[] { "Koan.Admin.RouteProvider" },
            sourceKey: allowDotOption.ResolvedKey);

        report.AddSetting(
            "web.enabled",
            BoolString(webOption.Value),
            source: webOption.Source,
            consumers: new[] { "Koan.Admin.FeatureManager", "Koan.Web.Admin" },
            sourceKey: webOption.ResolvedKey);

        report.AddSetting(
            "console.enabled",
            BoolString(consoleOption.Value),
            source: consoleOption.Source,
            consumers: new[] { "Koan.Admin.FeatureManager" },
            sourceKey: consoleOption.ResolvedKey);

        report.AddSetting(
            "launchkit.enabled",
            BoolString(launchKitOption.Value),
            source: launchKitOption.Source,
            consumers: new[] { "Koan.Admin.LaunchKitService" },
            sourceKey: launchKitOption.ResolvedKey);

        report.AddSetting(
            "manifest.expose",
            BoolString(manifestOption.Value),
            source: manifestOption.Source,
            consumers: new[] { "Koan.Admin.ManifestService" },
            sourceKey: manifestOption.ResolvedKey);

        report.AddSetting(
            "destructive.ops",
            BoolString(destructiveOption.Value),
            source: destructiveOption.Source,
            consumers: new[] { "Koan.Admin.FeatureManager" },
            sourceKey: destructiveOption.ResolvedKey);

        report.AddSetting(
            "prefix",
            normalizedPrefix,
            source: prefixOption.Source,
            consumers: new[] { "Koan.Admin.RouteProvider", "Koan.Web.Admin" },
            sourceKey: prefixOption.ResolvedKey);

        report.AddTool(
            "Admin UI",
            routes.RootPath,
            "Koan admin dashboard entry point",
            capability: "admin.web.ui");

        report.AddTool(
            "Admin API",
            routes.ApiPath,
            "Koan admin API root",
            capability: "admin.web.api");

        report.AddTool(
            "LaunchKit Export",
            routes.LaunchKitPath,
            "Download LaunchKit bundles",
            capability: "admin.launchkit");

        report.AddTool(
            "Manifest",
            routes.ManifestPath,
            "Koan admin manifest endpoint",
            capability: "admin.manifest");

        report.AddTool(
            "Admin Health",
            routes.HealthPath,
            "Admin health probe",
            capability: "admin.health");

        report.AddTool(
            "Log Stream",
            routes.LogStreamPath,
            "Admin log streaming endpoint",
            capability: "admin.logs");

        report.AddSetting(
            "authorization.policy",
            authorizationPolicyOption.Value ?? string.Empty,
            source: authorizationPolicyOption.Source,
            consumers: new[] { "Koan.Web.Admin.Authorization" },
            sourceKey: authorizationPolicyOption.ResolvedKey);

        report.AddSetting(
            "authorization.autoCreateDevelopmentPolicy",
            BoolString(authorizationAutoCreateOption.Value),
            source: authorizationAutoCreateOption.Source,
            consumers: new[] { "Koan.Web.Admin.Authorization" },
            sourceKey: authorizationAutoCreateOption.ResolvedKey);

        report.AddSetting(
            "authorization.allowedNetworks",
            allowedNetworks.Display,
            source: allowedNetworks.Source,
            consumers: new[] { "Koan.Web.Admin.Authorization" },
            sourceKey: allowedNetworks.SourceKey);

        report.AddSetting(
            "logging.enableLogStream",
            BoolString(enableLogStreamOption.Value),
            source: enableLogStreamOption.Source,
            consumers: new[] { "Koan.Admin.Logging" },
            sourceKey: enableLogStreamOption.ResolvedKey);

        report.AddSetting(
            "logging.allowTranscriptDownload",
            BoolString(allowTranscriptOption.Value),
            source: allowTranscriptOption.Source,
            consumers: new[] { "Koan.Admin.Logging" },
            sourceKey: allowTranscriptOption.ResolvedKey);

        report.AddSetting(
            "logging.allowedCategories",
            allowedCategories.Display,
            source: allowedCategories.Source,
            consumers: new[] { "Koan.Admin.Logging" },
            sourceKey: allowedCategories.SourceKey);

        report.AddSetting(
            "generate.composeProfiles",
            composeProfiles.Display,
            source: composeProfiles.Source,
            consumers: new[] { "Koan.Admin.LaunchKitService" },
            sourceKey: composeProfiles.SourceKey);

        report.AddSetting(
            "generate.openApiClients",
            openApiClients.Display,
            source: openApiClients.Source,
            consumers: new[] { "Koan.Admin.LaunchKitService" },
            sourceKey: openApiClients.SourceKey);

        report.AddSetting(
            "generate.includeAppSettings",
            BoolString(includeAppSettingsOption.Value),
            source: includeAppSettingsOption.Source,
            consumers: new[] { "Koan.Admin.LaunchKitService" },
            sourceKey: includeAppSettingsOption.ResolvedKey);

        report.AddSetting(
            "generate.includeCompose",
            BoolString(includeComposeOption.Value),
            source: includeComposeOption.Source,
            consumers: new[] { "Koan.Admin.LaunchKitService" },
            sourceKey: includeComposeOption.ResolvedKey);

        report.AddSetting(
            "generate.includeAspire",
            BoolString(includeAspireOption.Value),
            source: includeAspireOption.Source,
            consumers: new[] { "Koan.Admin.LaunchKitService" },
            sourceKey: includeAspireOption.ResolvedKey);

        report.AddSetting(
            "generate.includeManifest",
            BoolString(includeManifestOption.Value),
            source: includeManifestOption.Source,
            consumers: new[] { "Koan.Admin.LaunchKitService" },
            sourceKey: includeManifestOption.ResolvedKey);

        report.AddSetting(
            "generate.includeReadme",
            BoolString(includeReadmeOption.Value),
            source: includeReadmeOption.Source,
            consumers: new[] { "Koan.Admin.LaunchKitService" },
            sourceKey: includeReadmeOption.ResolvedKey);

        report.AddSetting(
            "generate.composeBasePort",
            composeBasePortOption.Value.ToString(),
            source: composeBasePortOption.Source,
            consumers: new[] { "Koan.Admin.LaunchKitService" },
            sourceKey: composeBasePortOption.ResolvedKey);

    if (!KoanEnv.IsDevelopment && normalizedPrefix.StartsWith(".", StringComparison.Ordinal) && !allowDotOption.Value)
        {
            report.AddNote("Dot-prefixed admin routes are disabled outside Development unless AllowDotPrefixInProduction=true.");
        }

        if ((env.IsProduction() || env.IsStaging()) && enabledOption.Value && !allowProdOption.Value)
        {
            report.AddNote("Koan Admin requested but AllowInProduction=false; surfaces will remain inactive.");
        }
    }

    private static ConfigurationArrayValue ReadStringArray(IConfiguration cfg, string baseKey, IReadOnlyList<string> defaults)
    {
        if (cfg is null)
        {
            return new ConfigurationArrayValue(FormatList(defaults), BootSettingSource.Auto, baseKey, true);
        }

        var section = cfg.GetSection(baseKey);
        var children = section.GetChildren().OrderBy(c => c.Key, StringComparer.Ordinal).ToList();

        if (children.Count == 0)
        {
            return new ConfigurationArrayValue(FormatList(defaults), BootSettingSource.Auto, baseKey, true);
        }

        var resolved = new List<ConfigurationValue<string?>>();

        foreach (var child in children)
        {
            var option = Configuration.ReadWithSource(cfg, $"{baseKey}:{child.Key}", default(string?));
            if (!option.UsedDefault)
            {
                resolved.Add(option);
            }
        }

        if (resolved.Count == 0)
        {
            return new ConfigurationArrayValue(FormatList(defaults), BootSettingSource.Auto, baseKey, true);
        }

        var displayValues = resolved
            .Select(v => (v.Value ?? string.Empty).Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .ToArray();

        var display = displayValues.Length > 0 ? string.Join(", ", displayValues) : "(none)";
        var source = resolved.Any(v => v.Source == BootSettingSource.Environment)
            ? BootSettingSource.Environment
            : resolved[0].Source;
        var sourceKey = resolved.First(v => v.Source == source).ResolvedKey;

        return new ConfigurationArrayValue(display, source, sourceKey, false);
    }

    private static string BoolString(bool value) => value ? "true" : "false";

    private static string FormatList(IReadOnlyCollection<string> items)
    {
        if (items is null || items.Count == 0) return "(none)";
        var trimmed = items.Select(i => (i ?? string.Empty).Trim()).Where(i => i.Length > 0).ToArray();
        return trimmed.Length == 0 ? "(none)" : string.Join(", ", trimmed);
    }

    private readonly record struct ConfigurationArrayValue(string Display, BootSettingSource Source, string SourceKey, bool UsedDefault);
}
