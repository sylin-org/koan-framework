using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Extensions;

namespace Koan.Data.Core.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Core";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Core wiring is triggered by AddKoan()/AddKoanDataCore via host code; nothing to do here.
        // We intentionally avoid double-registering since AddKoan() is the canonical entry point.
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var ensure = cfg.Read(Infrastructure.Constants.Configuration.Runtime.EnsureSchemaOnStart, true);
        report.AddSetting("EnsureSchemaOnStart", ensure.ToString());
    }
}
