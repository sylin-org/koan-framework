using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Secrets.Core.DI;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Secrets.Core.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Secrets.Core";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register secrets services (env + config providers, resolver). Configuration injection is handled
        // in StartKoan (reflection) to avoid hard dependencies and boot-order issues.
        services.AddKoanSecrets();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        // Minimal, implementation-neutral description: chain starts with env + config; adapters may extend.
        module.AddSetting("Resolver", "env → config → adapters");
    }
}

