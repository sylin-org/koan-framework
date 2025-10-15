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
using static Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Admin.Initialization;

public sealed class KoanAdminAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Admin";
    public string? ModuleVersion => typeof(KoanAdminAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanAdminCore();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
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

        module.AddSetting(
            AdminProvenanceItems.Enabled,
            FromConfigurationValue(enabledOption),
            enabledOption.Value,
            sourceKey: enabledOption.ResolvedKey,
            usedDefault: enabledOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.AllowInProduction,
            FromConfigurationValue(allowProdOption),
            allowProdOption.Value,
            sourceKey: allowProdOption.ResolvedKey,
            usedDefault: allowProdOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.AllowDotPrefixInProduction,
            FromConfigurationValue(allowDotOption),
            allowDotOption.Value,
            sourceKey: allowDotOption.ResolvedKey,
            usedDefault: allowDotOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.WebEnabled,
            FromConfigurationValue(webOption),
            webOption.Value,
            sourceKey: webOption.ResolvedKey,
            usedDefault: webOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.ConsoleEnabled,
            FromConfigurationValue(consoleOption),
            consoleOption.Value,
            sourceKey: consoleOption.ResolvedKey,
            usedDefault: consoleOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.LaunchKitEnabled,
            FromConfigurationValue(launchKitOption),
            launchKitOption.Value,
            sourceKey: launchKitOption.ResolvedKey,
            usedDefault: launchKitOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.ManifestExposure,
            FromConfigurationValue(manifestOption),
            manifestOption.Value,
            sourceKey: manifestOption.ResolvedKey,
            usedDefault: manifestOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.DestructiveOperations,
            FromConfigurationValue(destructiveOption),
            destructiveOption.Value,
            sourceKey: destructiveOption.ResolvedKey,
            usedDefault: destructiveOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.PathPrefix,
            FromConfigurationValue(prefixOption),
            normalizedPrefix,
            sourceKey: prefixOption.ResolvedKey,
            usedDefault: prefixOption.UsedDefault);

        module.AddTool(
            "Admin UI",
            routes.RootPath,
            "Koan admin dashboard entry point",
            capability: "admin.web.ui");

        module.AddTool(
            "Admin API",
            routes.ApiPath,
            "Koan admin API root",
            capability: "admin.web.api");

        module.AddTool(
            "LaunchKit Export",
            routes.LaunchKitPath,
            "Download LaunchKit bundles",
            capability: "admin.launchkit");

        module.AddTool(
            "Manifest",
            routes.ManifestPath,
            "Koan admin manifest endpoint",
            capability: "admin.manifest");

        module.AddTool(
            "Admin Health",
            routes.HealthPath,
            "Admin health probe",
            capability: "admin.health");

        module.AddTool(
            "Log Stream",
            routes.LogStreamPath,
            "Admin log streaming endpoint",
            capability: "admin.logs");

        module.AddSetting(
            AdminProvenanceItems.AuthorizationPolicy,
            FromConfigurationValue(authorizationPolicyOption),
            authorizationPolicyOption.Value,
            sourceKey: authorizationPolicyOption.ResolvedKey,
            usedDefault: authorizationPolicyOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.AuthorizationAutoCreatePolicy,
            FromConfigurationValue(authorizationAutoCreateOption),
            authorizationAutoCreateOption.Value,
            sourceKey: authorizationAutoCreateOption.ResolvedKey,
            usedDefault: authorizationAutoCreateOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.AuthorizationAllowedNetworks,
            FromBootSource(allowedNetworks.Source, allowedNetworks.UsedDefault),
            allowedNetworks.Display,
            sourceKey: allowedNetworks.SourceKey,
            usedDefault: allowedNetworks.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.LoggingEnableStream,
            FromConfigurationValue(enableLogStreamOption),
            enableLogStreamOption.Value,
            sourceKey: enableLogStreamOption.ResolvedKey,
            usedDefault: enableLogStreamOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.LoggingAllowTranscriptDownload,
            FromConfigurationValue(allowTranscriptOption),
            allowTranscriptOption.Value,
            sourceKey: allowTranscriptOption.ResolvedKey,
            usedDefault: allowTranscriptOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.LoggingAllowedCategories,
            FromBootSource(allowedCategories.Source, allowedCategories.UsedDefault),
            allowedCategories.Display,
            sourceKey: allowedCategories.SourceKey,
            usedDefault: allowedCategories.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.GenerateComposeProfiles,
            FromBootSource(composeProfiles.Source, composeProfiles.UsedDefault),
            composeProfiles.Display,
            sourceKey: composeProfiles.SourceKey,
            usedDefault: composeProfiles.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.GenerateOpenApiClients,
            FromBootSource(openApiClients.Source, openApiClients.UsedDefault),
            openApiClients.Display,
            sourceKey: openApiClients.SourceKey,
            usedDefault: openApiClients.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.GenerateIncludeAppSettings,
            FromConfigurationValue(includeAppSettingsOption),
            includeAppSettingsOption.Value,
            sourceKey: includeAppSettingsOption.ResolvedKey,
            usedDefault: includeAppSettingsOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.GenerateIncludeCompose,
            FromConfigurationValue(includeComposeOption),
            includeComposeOption.Value,
            sourceKey: includeComposeOption.ResolvedKey,
            usedDefault: includeComposeOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.GenerateIncludeAspire,
            FromConfigurationValue(includeAspireOption),
            includeAspireOption.Value,
            sourceKey: includeAspireOption.ResolvedKey,
            usedDefault: includeAspireOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.GenerateIncludeManifest,
            FromConfigurationValue(includeManifestOption),
            includeManifestOption.Value,
            sourceKey: includeManifestOption.ResolvedKey,
            usedDefault: includeManifestOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.GenerateIncludeReadme,
            FromConfigurationValue(includeReadmeOption),
            includeReadmeOption.Value,
            sourceKey: includeReadmeOption.ResolvedKey,
            usedDefault: includeReadmeOption.UsedDefault);

        module.AddSetting(
            AdminProvenanceItems.GenerateComposeBasePort,
            FromConfigurationValue(composeBasePortOption),
            composeBasePortOption.Value,
            sourceKey: composeBasePortOption.ResolvedKey,
            usedDefault: composeBasePortOption.UsedDefault);

        if (!KoanEnv.IsDevelopment && normalizedPrefix.StartsWith(".", StringComparison.Ordinal) && !allowDotOption.Value)
        {
            module.AddNote("Dot-prefixed admin routes are disabled outside Development unless AllowDotPrefixInProduction=true.");
        }

        if ((env.IsProduction() || env.IsStaging()) && enabledOption.Value && !allowProdOption.Value)
        {
            module.AddNote("Koan Admin requested but AllowInProduction=false; surfaces will remain inactive.");
        }
    }

    private static ConfigurationArrayValue ReadStringArray(IConfiguration cfg, string baseKey, IReadOnlyList<string> defaults)
    {
        if (cfg is null)
        {
            return new ConfigurationArrayValue(FormatList(defaults), BootSettingSource.Auto, null, true);
        }

        var section = cfg.GetSection(baseKey);
        var children = section.GetChildren().OrderBy(c => c.Key, StringComparer.Ordinal).ToList();

        if (children.Count == 0)
        {
            return new ConfigurationArrayValue(FormatList(defaults), BootSettingSource.Auto, null, true);
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
            return new ConfigurationArrayValue(FormatList(defaults), BootSettingSource.Auto, null, true);
        }

        var displayValues = resolved
            .Select(v => (v.Value ?? string.Empty).Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .ToArray();

        var display = displayValues.Length > 0 ? string.Join(", ", displayValues) : "(none)";
        var source = resolved.Any(v => v.Source == BootSettingSource.Environment)
            ? BootSettingSource.Environment
            : resolved[0].Source;
        var sourceKey = resolved.FirstOrDefault(v => v.Source == source).ResolvedKey;

        return new ConfigurationArrayValue(display, source, sourceKey, false);
    }

    private static string FormatList(IReadOnlyCollection<string> items)
    {
        if (items is null || items.Count == 0) return "(none)";
        var trimmed = items.Select(i => (i ?? string.Empty).Trim()).Where(i => i.Length > 0).ToArray();
        return trimmed.Length == 0 ? "(none)" : string.Join(", ", trimmed);
    }

    private readonly record struct ConfigurationArrayValue(string Display, BootSettingSource Source, string? SourceKey, bool UsedDefault);
}

