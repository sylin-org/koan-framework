using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Jobs;
using Koan.Jobs.Infrastructure;
using Koan.Jobs.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Jobs.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Jobs.Core";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanJobs();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        var options = cfg.GetSection(ConfigurationConstants.Section).Get<JobsOptions>();
        module.Describe(ModuleVersion);
        if (options is not null)
        {
            module.AddNote($"Jobs lanes configured: {options.Lanes.Count}");
        }
    }
}

