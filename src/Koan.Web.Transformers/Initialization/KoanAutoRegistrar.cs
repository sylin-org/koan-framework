using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Extensions;

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

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var enabled = cfg.Read(Infrastructure.Constants.Configuration.Transformers.AutoDiscover, true);
        report.AddSetting("AutoDiscover", enabled.ToString());
    }
}
