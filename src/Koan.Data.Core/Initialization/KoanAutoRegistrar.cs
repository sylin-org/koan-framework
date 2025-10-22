using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Data.Core.Pillars;
using Koan.Core.Hosting.Bootstrap;
using DataCoreItems = Koan.Data.Core.Infrastructure.DataCoreProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Data.Core.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Core";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        DataPillarManifest.EnsureRegistered();
        // Registration handled by KoanDataCoreInitializer; explicit AddKoanDataCore() keeps compatibility for manual layering.
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var ensureSetting = Koan.Core.Configuration.ReadWithSource(
            cfg,
            DataCoreItems.EnsureSchemaOnStart.Key,
            true);

        module.AddSetting(
            DataCoreItems.EnsureSchemaOnStart,
            ProvenanceModes.FromConfigurationValue(ensureSetting),
            ensureSetting.Value,
            sourceKey: ensureSetting.ResolvedKey,
            usedDefault: ensureSetting.UsedDefault);
    }
}

