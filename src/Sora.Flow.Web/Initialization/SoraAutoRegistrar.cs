using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;

namespace Sora.Flow.Web.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Flow.Web";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddControllers();
        // Health/metrics are assumed to be added by host; controllers expose endpoints only.
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
    report.AddModule(ModuleName, ModuleVersion);
    report.AddSetting("routes[0]", "/admin/replay");
    report.AddSetting("routes[1]", "/admin/reproject");
    report.AddSetting("routes[2]", "/models/{model}/views/{view}/{referenceId}");
    report.AddSetting("routes[3]", "/models/{model}/views/{view}");
    report.AddSetting("routes[4]", "/policies");
    }
}
