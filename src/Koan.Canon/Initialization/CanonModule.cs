using Koan.Core;
using Koan.Core.Composition;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Canon;

/// <summary>
/// Activates the Canon runtime and compiles discovered model pipelines.
/// </summary>
public sealed class CanonModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        var plan = CanonCompositionCompiler.Discover();
        services.AddSingleton(plan);

        foreach (var contributorType in plan.Models
                     .SelectMany(static model => model.ContributorTypes)
                     .Distinct())
        {
            services.TryAddSingleton(contributorType);
        }

        services.AddCanonRuntime();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version, "Entity-first canonicalization runtime");
        module.SetSetting("Canon", setting => setting.Value(
            "host-owned model plan; automatic built-in and custom pipelines; ordered non-atomic Data commit"));
        module.SetSetting("Canon exclusions", setting => setting.Value(
            "no distributed locking, delivery, rollback, blind-retry safety, durable replay, or automatic recovery"));
    }

    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        var plan = services.GetRequiredService<CanonCompositionPlan>();
        var contributors = plan.Models.Sum(static model => model.ContributorTypes.Count);
        services.GetService<ILoggerFactory>()?.CreateLogger("Koan.Canon").LogInformation(
            "Canon composition active: models={Models}; custom-contributors={Contributors}; " +
            "defaults=aggregation/policy; commit=canonical->indexes->audit (non-atomic).",
            plan.Models.Count,
            contributors);
        return Task.CompletedTask;
    }

    public override void ReportComposition(KoanCompositionBuilder composition, IServiceProvider services)
    {
        var plan = services.GetRequiredService<CanonCompositionPlan>();
        var customModels = plan.Models.Count(static model => model.HasCustomContributors);
        composition.AddCapability(
            Infrastructure.Constants.Diagnostics.CapabilityCode,
            [
                $"models:{plan.Models.Count}",
                $"custom-pipelines:{customModels}",
                "default-aggregation-policy",
                "ordered-non-atomic-commit",
            ]);
        composition.AddGuarantee(
            Infrastructure.Constants.Diagnostics.CapabilityCode,
            Infrastructure.Constants.Diagnostics.CapabilitySubject,
            $"Every discovered CanonEntity has one compiled pipeline; models={plan.Models.Count}; " +
            $"custom-pipelines={customModels}; commit order is canonical -> indexes -> audit and is not atomic. " +
            "No rollback, blind-retry safety, durable replay, or automatic recovery.",
            Infrastructure.Constants.Diagnostics.CapabilityReason,
            source: "Koan.Canon");
    }
}
