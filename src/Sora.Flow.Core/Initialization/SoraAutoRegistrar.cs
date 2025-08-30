using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Flow.Options;

namespace Sora.Flow.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Flow.Core";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddSoraFlow();
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        var opts = cfg.GetSection("Sora:Flow").Get<FlowOptions>() ?? new FlowOptions();
    report.AddModule(ModuleName, ModuleVersion);
    report.AddSetting("runtime", "InMemory");
    report.AddSetting("batch", opts.BatchSize.ToString());
    report.AddSetting("concurrency.Standardize", opts.StandardizeConcurrency.ToString());
    report.AddSetting("concurrency.Key", opts.KeyConcurrency.ToString());
    report.AddSetting("concurrency.Associate", opts.AssociateConcurrency.ToString());
    report.AddSetting("concurrency.Project", opts.ProjectConcurrency.ToString());
    report.AddSetting("ttl.Intake", opts.IntakeTtl.ToString());
    report.AddSetting("ttl.Standardized", opts.StandardizedTtl.ToString());
    report.AddSetting("ttl.Keyed", opts.KeyedTtl.ToString());
    report.AddSetting("ttl.ProjectionTask", opts.ProjectionTaskTtl.ToString());
    report.AddSetting("ttl.RejectionReport", opts.RejectionReportTtl.ToString());
    report.AddSetting("purge.Enabled", opts.PurgeEnabled.ToString().ToLowerInvariant());
    report.AddSetting("purge.Interval", opts.PurgeInterval.ToString());
    report.AddSetting("dlq", opts.DeadLetterEnabled.ToString().ToLowerInvariant());
    report.AddSetting("defaultView", opts.DefaultViewName);
    if (opts.AggregationTags?.Length > 0)
        report.AddSetting("aggregation.tags", string.Join(",", opts.AggregationTags));
    }
}
