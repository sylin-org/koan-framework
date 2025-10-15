using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Logging;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Vector.Infrastructure;

namespace Koan.Data.Vector.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<KoanAutoRegistrar>();

    public string ModuleName => "Koan.Data.Vector";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        Log.BootDebug(LogActions.Init, "loaded", ("module", ModuleName));
        // Register vector defaults + resolver service
        services.AddKoanDataVector();
        Log.BootDebug(LogActions.Init, "services-registered", ("module", ModuleName));
    }

    public void Describe(global::Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var defaultOptions = new VectorDefaultsOptions();
        var defaultProvider = Configuration.ReadWithSource<string?>(
            cfg,
            Constants.Configuration.Keys.DefaultProvider,
            defaultOptions.DefaultProvider);

        var display = string.IsNullOrWhiteSpace(defaultProvider.Value)
            ? "(auto)"
            : defaultProvider.Value;

        module.AddSetting(
            "VectorDefaults:DefaultProvider",
            display,
            source: defaultProvider.Source,
            sourceKey: defaultProvider.ResolvedKey,
            consumers: DefaultProviderConsumers);
    }

    private static readonly string[] DefaultProviderConsumers =
    {
        "Koan.Data.Vector.VectorService"
    };

    private static class LogActions
    {
        public const string Init = "registrar.init";
    }
}

