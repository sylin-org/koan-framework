using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Sora.Core;
using Sora.Core.Modules;
using Sora.Web.Auth.Providers;
using Sora.Web.Auth.TestProvider.Infrastructure;
using Sora.Web.Auth.TestProvider.Options;
using Sora.Web.Auth.TestProvider.Controllers;

namespace Sora.Web.Auth.TestProvider.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Web.Auth.TestProvider";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddSingleton<DevTokenStore>();
        services.AddSoraOptions<TestProviderOptions>(TestProviderOptions.SectionPath);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthProviderContributor, TestProviderContributor>());
    // Ensure MVC discovers TestProvider controllers
    services.AddControllers().AddApplicationPart(typeof(StaticController).Assembly);
    // Map TestProvider endpoints during startup (honors RouteBase)
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, Hosting.SoraTestProviderStartupFilter>());
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var enabled = env.IsDevelopment() || cfg.GetSection(TestProviderOptions.SectionPath).GetValue<bool>(nameof(TestProviderOptions.Enabled));
        report.AddSetting("Enabled", enabled ? "true" : "false");
        if (!env.IsDevelopment() && enabled)
        {
            report.AddNote("WARNING: TestProvider is enabled outside Development. Do not use in Production.");
        }
    }
}