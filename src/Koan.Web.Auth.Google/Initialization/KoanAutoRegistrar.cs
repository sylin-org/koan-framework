using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Web.Auth.Google.Providers;
using Koan.Web.Auth.Providers;

namespace Koan.Web.Auth.Google.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Auth.Google";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthProviderContributor, GoogleProviderContributor>());
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        report.AddSetting("Provides", "google (OIDC)");
    }
}
