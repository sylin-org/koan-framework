using System;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Mcp.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        var enableStdio = section.GetValue("EnableStdioTransport", true);
        module.AddSetting("EnableStdioTransport", enableStdio.ToString());

        var enableHttpSse = section.GetValue("EnableHttpSseTransport", false);
        module.AddSetting("EnableHttpSseTransport", enableHttpSse.ToString());

        var requireAuth = section.GetValue<bool?>("RequireAuthentication");
        if (requireAuth.HasValue)
        {
            module.AddSetting("RequireAuthentication", requireAuth.Value.ToString());
        }

        var route = section.GetValue<string?>("HttpSseRoute");
        if (!string.IsNullOrWhiteSpace(route))
        {
            module.AddSetting("HttpSseRoute", route);
        }

        var publishCapabilities = section.GetValue("PublishCapabilityEndpoint", true);
        module.AddSetting("PublishCapabilityEndpoint", publishCapabilities.ToString());

        var allowed = section.GetSection("AllowedEntities").Get<string[]>() ?? Array.Empty<string>();
        if (allowed.Length > 0)
        {
            module.AddSetting("AllowedEntities", string.Join(',', allowed));
        }

        var denied = section.GetSection("DeniedEntities").Get<string[]>() ?? Array.Empty<string>();
        if (denied.Length > 0)
        {
            module.AddSetting("DeniedEntities", string.Join(',', denied));
        }

        // Code mode settings
        var exposure = section.GetValue<string?>("Exposure");
        module.AddSetting("Exposure", exposure ?? "Auto (default)");

        var codeSection = section.GetSection("CodeMode");
        var codeModeEnabled = codeSection.GetValue("Enabled", true);
        module.AddSetting("CodeModeEnabled", codeModeEnabled.ToString());

        if (codeModeEnabled)
        {
            var runtime = codeSection.GetValue("Runtime", "Jint");
            module.AddSetting("CodeModeRuntime", runtime);

            var sandboxSection = codeSection.GetSection("Sandbox");
            var cpuMs = sandboxSection.GetValue("CpuMilliseconds", 2000);
            var memoryMb = sandboxSection.GetValue("MemoryMegabytes", 64);
            var maxRecursion = sandboxSection.GetValue("MaxRecursionDepth", 100);

            module.AddSetting("SandboxCpuMs", cpuMs.ToString());
            module.AddSetting("SandboxMemoryMb", memoryMb.ToString());
            module.AddSetting("SandboxMaxRecursion", maxRecursion.ToString());
        }
    }
}
