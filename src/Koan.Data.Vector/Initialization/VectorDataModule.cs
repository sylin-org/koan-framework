using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Vector.Infrastructure;
using VectorItems = Koan.Data.Vector.Infrastructure.VectorProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Data.Vector.Initialization;

public sealed class VectorDataModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanDataVector();
    }

    public override void Report(global::Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var defaultOptions = new VectorDefaultsOptions();
        var defaultProvider = Configuration.ReadWithSource<string?>(
            cfg,
            VectorItems.DefaultProvider.Key,
            defaultOptions.DefaultProvider);

        var display = string.IsNullOrWhiteSpace(defaultProvider.Value)
            ? "(auto)"
            : defaultProvider.Value;

        module.AddSetting(
            VectorItems.DefaultProvider,
            ProvenanceModes.FromConfigurationValue(defaultProvider),
            display,
            sourceKey: defaultProvider.ResolvedKey,
            usedDefault: defaultProvider.UsedDefault);
    }
}
