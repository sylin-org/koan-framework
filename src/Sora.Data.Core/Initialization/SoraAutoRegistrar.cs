using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;

namespace Sora.Data.Core.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.Core";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Core wiring is triggered by AddSora()/AddSoraDataCore via host code; nothing to do here.
        // We intentionally avoid double-registering since AddSora() is the canonical entry point.
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var ensure = cfg.Read(Sora.Data.Core.Infrastructure.Constants.Configuration.Runtime.EnsureSchemaOnStart, true);
        report.AddSetting("EnsureSchemaOnStart", ensure.ToString());
    }
}
