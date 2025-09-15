using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Koan.Core;

namespace Koan.Data.Relational.Orchestration;

internal sealed class RelationalMaterializationOptionsConfigurator(IConfiguration cfg) : IConfigureOptions<RelationalMaterializationOptions>
{
    public void Configure(RelationalMaterializationOptions options)
    {
        // Bind loosely from multiple potential keys; default values already set above.
        var section = cfg.GetSection("Koan:Data:Relational:Materialization");
        section.Bind(options);
        // Sensible default: when policy != None, fail on mismatch by default.
        if (options.Materialization != RelationalMaterializationPolicy.None && !options.FailOnMismatch)
            options.FailOnMismatch = true;
        // Production safety gate
        var allowMagic = Configuration.Read(cfg, Koan.Core.Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction, false);
        options.AllowProductionDdl = options.AllowProductionDdl || allowMagic;
    }
}