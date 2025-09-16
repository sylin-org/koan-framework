using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Vector.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Vector";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        var logger = services.BuildServiceProvider().GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Koan.Data.Vector.Initialization.KoanAutoRegistrar");
    logger?.Log(LogLevel.Debug, "Koan.Data.Vector KoanAutoRegistrar loaded.");
        // Register vector defaults + resolver service
        services.AddKoanDataVector();
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var def = Configuration.Read(cfg, "Koan:Data:VectorDefaults:DefaultProvider", null);
        report.AddSetting("VectorDefaults:DefaultProvider", def, isSecret: false);
    }
}
