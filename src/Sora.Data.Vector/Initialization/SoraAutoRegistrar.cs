using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Microsoft.Extensions.Logging;

namespace Sora.Data.Vector.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.Vector";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        var logger = services.BuildServiceProvider().GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Sora.Data.Vector.Initialization.SoraAutoRegistrar");
    logger?.Log(LogLevel.Debug, "Sora.Data.Vector SoraAutoRegistrar loaded.");
        // Register vector defaults + resolver service
        services.AddSoraDataVector();
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var def = Configuration.Read(cfg, "Sora:Data:VectorDefaults:DefaultProvider", null);
        report.AddSetting("VectorDefaults:DefaultProvider", def, isSecret: false);
    }
}
