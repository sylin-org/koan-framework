using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Jobs;
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

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        var options = cfg.GetSection("Koan:Jobs").Get<JobsOptions>();
        report.AddModule(ModuleName, ModuleVersion);
        if (options is not null)
        {
            report.AddNote($"Jobs default store: {options.DefaultStore}");
        }
    }
}
