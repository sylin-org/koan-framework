using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Observability.Health;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Vector.Connector.SqliteVec.Initialization;

/// <summary>Registers the embedded stable sqlite-vec provider.</summary>
public sealed class SqliteVectorModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<SqliteVecOptions>(SqliteVecOptions.Section);
        services.TryAddSingleton<SqliteVecNative>();
        services.AddSingleton<SqliteVecAdapterFactory>();
        services.AddSingleton<IVectorAdapterFactory>(provider =>
            provider.GetRequiredService<SqliteVecAdapterFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqliteVecHealthContributor>());
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var route = SqliteVecRoute.ResolveDefault(cfg);
        module.AddSetting("Vector", $"sqlite-vec {Infrastructure.Constants.Native.ReportedVersion} exact");
        module.AddSetting("Store", Koan.Core.Redaction.DeIdentify(route.ConnectionString));
        module.AddSetting("Placement", route.Origin);
        module.AddNote("Durable embedded exact vectors; native payload is pinned and verified before use.");
    }
}
