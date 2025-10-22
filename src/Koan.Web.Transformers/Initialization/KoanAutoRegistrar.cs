using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Web.Transformers.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Transformers";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // No-op: this assembly already exposes an internal IKoanInitializer (TransformerStartupInitializer)
        // which AppBootstrapper will discover and run. We avoid duplicate registration here.
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var enabled = Koan.Core.Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Transformers.AutoDiscover,
            true);
        module.AddSetting(
            "AutoDiscover",
            enabled.Value.ToString(),
            source: enabled.Source,
            consumers: new[] { "Koan.Web.Transformers.Registry" });
    }
}

