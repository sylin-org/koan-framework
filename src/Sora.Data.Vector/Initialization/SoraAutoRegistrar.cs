using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;

namespace Sora.Data.Vector.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.Vector";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register vector defaults + resolver service
        services.AddSoraDataVector();
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var def = Sora.Core.Configuration.Read(cfg, "Sora:Data:VectorDefaults:DefaultProvider", null);
        report.AddSetting("VectorDefaults:DefaultProvider", def, isSecret: false);
    }
}
