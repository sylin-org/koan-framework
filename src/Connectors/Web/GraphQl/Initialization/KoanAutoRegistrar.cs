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

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var options = new GraphQlOptions();
        cfg.GetSection(Infrastructure.Constants.Configuration.Section).Bind(options);

        var enabled = options.Enabled ?? true;
        module.AddSetting("GraphQl.State", enabled ? "enabled" : "disabled");
        module.AddSetting("GraphQl.Candidates", "enabled, disabled");
        module.AddSetting("GraphQl.Rationale", "Reference = GraphQL endpoint");

        module.AddSetting("Path", options.Path);
        module.AddSetting("Debug", options.Debug.ToString());
    }
}