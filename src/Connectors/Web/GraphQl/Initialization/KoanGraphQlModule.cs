using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Web.Connector.GraphQl;

namespace Koan.Web.Connector.GraphQl.Initialization;

/// <summary>
/// Koan.Web.Connector.GraphQl boot module (ARCH-0086). Replaces the former <c>KoanAutoRegistrar</c> +
/// the redundant <c>KoanGraphQlInitializer</c> (both of which called <c>AddKoanGraphQl()</c> — a live
/// double-registration) with one <see cref="KoanModule"/>: <see cref="Register"/> wires the GraphQL
/// endpoint once; <see cref="Report"/> publishes the same provenance as before. Id preserves the prior
/// ModuleName so boot reports are unchanged.
/// </summary>
public sealed class KoanGraphQlModule : KoanModule
{
    public override string Id => "Koan.Web.Connector.GraphQl";

    public override void Register(IServiceCollection services)
    {
        services.AddKoanGraphQl();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
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
