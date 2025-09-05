using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;

namespace Sora.Flow.Runtime.Dapr.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Flow.Runtime.Dapr";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Prefer Dapr runtime when this package is present in the app.
        services.Replace(ServiceDescriptor.Singleton<Sora.Flow.Runtime.IFlowRuntime, DaprFlowRuntime>());
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
    report.AddModule(ModuleName, ModuleVersion);
    report.AddSetting("provider", "Dapr");
    }
}
