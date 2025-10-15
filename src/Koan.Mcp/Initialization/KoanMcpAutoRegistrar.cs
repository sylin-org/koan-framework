using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Mcp.Extensions;
using Koan.Mcp.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Mcp.Initialization;

public sealed class KoanMcpAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Mcp";

    public string? ModuleVersion => typeof(KoanMcpAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanMcp();
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        module.Describe(ModuleVersion);
        var section = configuration.GetSection("Koan:Mcp");
        var enableStdio = Configuration.ReadWithSource(configuration, "Koan:Mcp:EnableStdioTransport", true);
        module.AddSetting(
            McpProvenanceItems.EnableStdioTransport,
            FromConfigurationValue(enableStdio),
            enableStdio.Value,
            sourceKey: enableStdio.ResolvedKey,
            usedDefault: enableStdio.UsedDefault);

        var enableHttpSse = Configuration.ReadWithSource(configuration, "Koan:Mcp:EnableHttpSseTransport", false);
        module.AddSetting(
            McpProvenanceItems.EnableHttpSseTransport,
            FromConfigurationValue(enableHttpSse),
            enableHttpSse.Value,
            sourceKey: enableHttpSse.ResolvedKey,
            usedDefault: enableHttpSse.UsedDefault);

        var requireAuth = Configuration.ReadWithSource<bool?>(configuration, "Koan:Mcp:RequireAuthentication", null);
        if (!requireAuth.UsedDefault)
        {
            module.AddSetting(
                McpProvenanceItems.RequireAuthentication,
                FromConfigurationValue(requireAuth),
                requireAuth.Value,
                sourceKey: requireAuth.ResolvedKey,
                usedDefault: requireAuth.UsedDefault);
        }

        var route = Configuration.ReadWithSource(configuration, "Koan:Mcp:HttpSseRoute", string.Empty);
        if (!string.IsNullOrWhiteSpace(route.Value))
        {
            module.AddSetting(
                McpProvenanceItems.HttpSseRoute,
                FromConfigurationValue(route),
                route.Value,
                sourceKey: route.ResolvedKey,
                usedDefault: route.UsedDefault);
        }

        var publishCapabilities = Configuration.ReadWithSource(configuration, "Koan:Mcp:PublishCapabilityEndpoint", true);
        module.AddSetting(
            McpProvenanceItems.PublishCapabilityEndpoint,
            FromConfigurationValue(publishCapabilities),
            publishCapabilities.Value,
            sourceKey: publishCapabilities.ResolvedKey,
            usedDefault: publishCapabilities.UsedDefault);

        var allowed = ReadStringArray(configuration, "Koan:Mcp:AllowedEntities");
        module.AddSetting(
            McpProvenanceItems.AllowedEntities,
            FromBootSource(allowed.Source, allowed.UsedDefault),
            allowed.Display,
            sourceKey: allowed.SourceKey,
            usedDefault: allowed.UsedDefault);

        var denied = ReadStringArray(configuration, "Koan:Mcp:DeniedEntities");
        module.AddSetting(
            McpProvenanceItems.DeniedEntities,
            FromBootSource(denied.Source, denied.UsedDefault),
            denied.Display,
            sourceKey: denied.SourceKey,
            usedDefault: denied.UsedDefault);

        var exposure = Configuration.ReadWithSource(configuration, "Koan:Mcp:Exposure", "Auto");
        module.AddSetting(
            McpProvenanceItems.ExposureMode,
            FromConfigurationValue(exposure),
            exposure.Value,
            sourceKey: exposure.ResolvedKey,
            usedDefault: exposure.UsedDefault);

        var codeEnabled = Configuration.ReadWithSource(configuration, "Koan:Mcp:CodeMode:Enabled", true);
        module.AddSetting(
            McpProvenanceItems.CodeModeEnabled,
            FromConfigurationValue(codeEnabled),
            codeEnabled.Value,
            sourceKey: codeEnabled.ResolvedKey,
            usedDefault: codeEnabled.UsedDefault);

        if (codeEnabled.Value)
        {
            var runtime = Configuration.ReadWithSource(configuration, "Koan:Mcp:CodeMode:Runtime", "Jint");
            module.AddSetting(
                McpProvenanceItems.CodeModeRuntime,
                FromConfigurationValue(runtime),
                runtime.Value,
                sourceKey: runtime.ResolvedKey,
                usedDefault: runtime.UsedDefault);

            var cpuMs = Configuration.ReadWithSource(configuration, "Koan:Mcp:CodeMode:Sandbox:CpuMilliseconds", 2000);
            module.AddSetting(
                McpProvenanceItems.SandboxCpuMs,
                FromConfigurationValue(cpuMs),
                cpuMs.Value,
                sourceKey: cpuMs.ResolvedKey,
                usedDefault: cpuMs.UsedDefault);

            var memoryMb = Configuration.ReadWithSource(configuration, "Koan:Mcp:CodeMode:Sandbox:MemoryMegabytes", 64);
            module.AddSetting(
                McpProvenanceItems.SandboxMemoryMb,
                FromConfigurationValue(memoryMb),
                memoryMb.Value,
                sourceKey: memoryMb.ResolvedKey,
                usedDefault: memoryMb.UsedDefault);

            var maxRecursion = Configuration.ReadWithSource(configuration, "Koan:Mcp:CodeMode:Sandbox:MaxRecursionDepth", 100);
            module.AddSetting(
                McpProvenanceItems.SandboxMaxRecursion,
                FromConfigurationValue(maxRecursion),
                maxRecursion.Value,
                sourceKey: maxRecursion.ResolvedKey,
                usedDefault: maxRecursion.UsedDefault);
        }
    }

    private static ConfigurationArrayValue ReadStringArray(IConfiguration configuration, string key)
    {
        if (configuration is null)
        {
            return new ConfigurationArrayValue("(none)", BootSettingSource.Auto, null, true);
        }

        var section = configuration.GetSection(key);
        var children = section.GetChildren().OrderBy(c => c.Key, StringComparer.Ordinal).ToArray();

        if (children.Length == 0)
        {
            return new ConfigurationArrayValue("(none)", BootSettingSource.Auto, null, true);
        }

        var resolved = new List<ConfigurationValue<string?>>();

        foreach (var child in children)
        {
            var option = Configuration.ReadWithSource(configuration, $"{key}:{child.Key}", default(string?));
            if (!option.UsedDefault)
            {
                resolved.Add(option);
            }
        }

        if (resolved.Count == 0)
        {
            return new ConfigurationArrayValue("(none)", BootSettingSource.Auto, null, true);
        }

        var values = resolved
            .Select(v => (v.Value ?? string.Empty).Trim())
            .Where(v => v.Length > 0)
            .ToArray();

        var display = values.Length == 0 ? "(none)" : string.Join(", ", values);
        var source = resolved.Any(r => r.Source == BootSettingSource.Environment)
            ? BootSettingSource.Environment
            : resolved[0].Source;
        var sourceKey = resolved.FirstOrDefault(r => r.Source == source).ResolvedKey;

        return new ConfigurationArrayValue(display, source, sourceKey, false);
    }

    private readonly record struct ConfigurationArrayValue(string Display, BootSettingSource Source, string? SourceKey, bool UsedDefault);
}
