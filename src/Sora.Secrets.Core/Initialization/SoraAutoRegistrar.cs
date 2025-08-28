using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Secrets.Core.DI;

namespace Sora.Secrets.Core.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Secrets.Core";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register secrets services (env + config providers, resolver). Configuration injection is handled
        // in StartSora (reflection) to avoid hard dependencies and boot-order issues.
        services.AddSoraSecrets();
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        // Minimal, implementation-neutral description: chain starts with env + config; adapters may extend.
        report.AddSetting("Resolver", "env → config → adapters");
    }
}
