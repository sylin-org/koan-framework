using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.AI.Training.Initialization;

/// <summary>
/// Auto-registers training and dataset services when Koan.AI.Training is referenced.
/// Follows the Reference = Intent pattern.
/// </summary>
public sealed class AiTrainingModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddSingleton<ITrainingService, TrainingService>();
        services.AddSingleton<IDatasetService, DatasetService>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Training orchestration registered (resolves adapters with Train capability).");
        module.AddNote("Dataset service registered (entity-to-dataset conversion active).");
    }
}
