using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Web.Auth.Extensions;

namespace Sora.Web.Auth.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Web.Auth";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Ensure auth services are registered once
        services.AddSoraWebAuth();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.AspNetCore.Hosting.IStartupFilter, Hosting.SoraWebAuthStartupFilter>());
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        // Basic visibility: count configured providers and list enabled keys
    // Best-effort: count configured providers via raw configuration to avoid binding
    var section = cfg.GetSection(Options.AuthOptions.SectionPath);
    var providers = section.GetSection("Providers");
    var count = providers.Exists() ? providers.GetChildren().Count() : 0;
    report.AddSetting("Providers", count.ToString());
    }
}
