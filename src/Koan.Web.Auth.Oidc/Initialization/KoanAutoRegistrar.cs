using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;

namespace Koan.Web.Auth.Oidc.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Auth.Oidc";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // No defaults contributed; this module exists to keep handler logic separate when implemented later.
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        report.AddSetting("Provides", "Generic OIDC handler (no defaults)");
    }
}
