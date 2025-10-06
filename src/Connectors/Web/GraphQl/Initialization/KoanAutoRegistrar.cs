using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Web.Connector.GraphQl;

namespace Koan.Web.Connector.GraphQl.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Connector.GraphQl";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanGraphQl();
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        var options = new GraphQlOptions();
        cfg.GetSection(Infrastructure.Constants.Configuration.Section).Bind(options);

        var enabled = options.Enabled ?? true;
        report.AddProviderElection(
            "Web.GraphQl",
            enabled ? "enabled" : "disabled",
            new[] { "enabled", "disabled" },
            "Reference = GraphQL endpoint");

        report.AddSetting("Path", options.Path);
        report.AddSetting("Debug", options.Debug.ToString());
    }
}