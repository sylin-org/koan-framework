using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;

namespace Sora.Web.Transformers.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Web.Transformers";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // No-op: this assembly already exposes an internal ISoraInitializer (TransformerStartupInitializer)
        // which SoraInitialization will discover and run. We avoid duplicate registration here.
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var enabled = cfg.Read(Sora.Web.Transformers.Infrastructure.Constants.Configuration.Transformers.AutoDiscover, true);
        report.AddSetting("AutoDiscover", enabled.ToString());
    }
}
