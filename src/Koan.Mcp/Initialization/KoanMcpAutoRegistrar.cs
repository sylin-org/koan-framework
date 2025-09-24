using System;
using Koan.Core;
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

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration configuration, IHostEnvironment environment)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        report.AddModule(ModuleName, ModuleVersion);

        var section = configuration.GetSection("Koan:Mcp");
        var enableStdio = section.GetValue("EnableStdioTransport", true);
        report.AddSetting("EnableStdioTransport", enableStdio.ToString());

        var enableHttpSse = section.GetValue("EnableHttpSseTransport", false);
        report.AddSetting("EnableHttpSseTransport", enableHttpSse.ToString());

        var requireAuth = section.GetValue<bool?>("RequireAuthentication");
        if (requireAuth.HasValue)
        {
            report.AddSetting("RequireAuthentication", requireAuth.Value.ToString());
        }

        var route = section.GetValue<string?>("HttpSseRoute");
        if (!string.IsNullOrWhiteSpace(route))
        {
            report.AddSetting("HttpSseRoute", route);
        }

        var publishCapabilities = section.GetValue("PublishCapabilityEndpoint", true);
        report.AddSetting("PublishCapabilityEndpoint", publishCapabilities.ToString());

        var allowed = section.GetSection("AllowedEntities").Get<string[]>() ?? Array.Empty<string>();
        if (allowed.Length > 0)
        {
            report.AddSetting("AllowedEntities", string.Join(',', allowed));
        }

        var denied = section.GetSection("DeniedEntities").Get<string[]>() ?? Array.Empty<string>();
        if (denied.Length > 0)
        {
            report.AddSetting("DeniedEntities", string.Join(',', denied));
        }
    }
}
