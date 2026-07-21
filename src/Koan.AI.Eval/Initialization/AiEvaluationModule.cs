using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.AI.Eval.Initialization;

/// <summary>
/// Auto-registers the evaluation service when Koan.AI.Eval is referenced.
/// Follows the Reference = Intent pattern.
/// </summary>
public sealed class AiEvaluationModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddSingleton<IEvalService, EvalService>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Eval service registered (delegates to adapters with MetricCompute capability).");
    }
}
