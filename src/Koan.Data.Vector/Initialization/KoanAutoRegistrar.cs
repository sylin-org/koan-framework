using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Logging;

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

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var def = Configuration.Read(cfg, "Koan:Data:VectorDefaults:DefaultProvider", null);
        report.AddSetting("VectorDefaults:DefaultProvider", def, isSecret: false);
    }

    private static class LogActions
    {
        public const string Init = "registrar.init";
    }
}
