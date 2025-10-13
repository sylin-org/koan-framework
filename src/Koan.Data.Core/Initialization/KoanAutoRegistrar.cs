using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Data.Core.Pillars;

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

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var ensure = cfg.Read(Infrastructure.Constants.Configuration.Runtime.EnsureSchemaOnStart, true);
        report.AddSetting("EnsureSchemaOnStart", ensure.ToString());
    }
}
