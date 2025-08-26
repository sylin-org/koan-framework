using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Web.Auth.Providers;

namespace Sora.Web.Auth.Discord.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Web.Auth.Discord";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthProviderContributor, DiscordProviderContributor>());
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        report.AddSetting("Provides", "discord (OAuth2)");
    }
}
