using Koan.Canon.Diagnostics;
using Koan.Core;
using Koan.Core.Composition;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Hosting.Registry;
using Koan.Core.Provenance;
using Koan.Jobs;
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

        SeedStageJobs();

        // The stage receipts are jobs (canon-rides-jobs), but their closed job types cannot ride
        // KoanRegistry discovery: registry statics are rebuilt per boot AFTER module registration,
        // and open generics are never discovered anyway. So Canon owns the registry factory: built
        // from the composition plan (which survives), not from KoanRegistry. RemoveAll makes this
        // deterministic regardless of module registration order.
        services.RemoveAll<JobTypeRegistry>();
        services.AddSingleton<JobTypeRegistry>(static sp =>
        {
            var plan = sp.GetRequiredService<CanonCompositionPlan>();
            var stageJobTypes = plan.Models
                .Select(static model => typeof(CanonStage<>).MakeGenericType(model.ModelType))
                .ToArray();
            return new JobTypeRegistry(stageJobTypes);
        });

        services.AddCanonRuntime();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, CanonHoldsHealthContributor>());
    }

    /// <summary>
    /// The receipt is the job (canon-rides-jobs): seed one closed <see cref="CanonStage{TModel}"/>
    /// job type per discovered canon model, so the Jobs engine can claim and process staged
    /// receipts. Open generics are never discovered — Canon closes them over its own models.
    /// Idempotent; called at register <em>and</em> again at first enqueue, because a host's own
    /// assembly manifest (where its models live) can load after module registration.
    /// </summary>
    internal static void SeedStageJobs()
    {
        var stageJobTypes = KoanRegistry.GetDiscoveredImplementors(typeof(ICanonModel))
            .Where(static t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                               && typeof(CanonEntity<>).IsAssignableFrom(t))
            .Select(static model => typeof(CanonStage<>).MakeGenericType(model))
            .Distinct()
            .ToArray();

        if (stageJobTypes.Length > 0)
        {
            KoanRegistry.RegisterDiscoveredImplementors(typeof(IKoanJob), stageJobTypes);
        }
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
            "defaults=reconcile/newest-wins; commit=canonical->match-indexes->audit (non-atomic).",
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
                "default-reconcile-rule",
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
