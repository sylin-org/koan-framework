using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Composition;
using Koan.Core.Provenance;
using Koan.Observability.Infrastructure;

namespace Koan.Observability.Initialization;

/// <summary>
/// Reference = Intent (ARCH-0088): referencing the <c>Koan.Observability</c> package auto-enables the
/// OpenTelemetry wiring (traces + metrics + optional OTLP export) at boot. Configure via the
/// <c>Koan:Observability</c> config section (or <c>OTEL_*</c> environment variables). Advanced customization
/// composes through the standard OpenTelemetry builder. Health/probe primitives stay in <c>Koan.Core</c>.
/// </summary>
public sealed class ObservabilityModule : KoanModule
{
    public override void Register(IServiceCollection services) => ObservabilityPipeline.Register(services);

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        var plan = ObservabilityPlan.Compile(cfg, env);
        module.Describe(Version, "Reference-activated OpenTelemetry traces and metrics")
            .SetStatus(plan.Active ? "active" : "inactive", plan.StatusDetail)
            .SetSetting("Signals", setting => setting.Value(
                $"traces={plan.TracesEnabled.ToString().ToLowerInvariant()}, metrics={plan.MetricsEnabled.ToString().ToLowerInvariant()}"))
            .SetSetting("Export", setting => setting.Value(plan.Exporter));
    }

    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        var plan = services.GetRequiredService<ObservabilityPlan>();
        services.GetService<ILoggerFactory>()?.CreateLogger("Koan.Observability").LogInformation(
            "Koan Observability {State}: traces={Traces}, metrics={Metrics}, exporter={Exporter}.",
            plan.Active ? "active" : "inactive",
            plan.TracesEnabled,
            plan.MetricsEnabled,
            plan.Exporter);
        return Task.CompletedTask;
    }

    public override void ReportComposition(KoanCompositionBuilder composition, IServiceProvider services)
    {
        var plan = services.GetRequiredService<ObservabilityPlan>();
        composition.AddCapability(
            Constants.Diagnostics.CapabilityCode,
            [
                $"active:{plan.Active.ToString().ToLowerInvariant()}",
                $"traces:{plan.TracesEnabled.ToString().ToLowerInvariant()}",
                $"metrics:{plan.MetricsEnabled.ToString().ToLowerInvariant()}",
                $"exporter:{plan.Exporter}",
                "sources:Koan.*",
                "meters:Koan.*",
            ]);
        composition.AddGuarantee(
            Constants.Diagnostics.CapabilityCode,
            Constants.Diagnostics.CapabilitySubject,
            plan.Active
                ? $"One host OpenTelemetry pipeline collects Koan.* signals; traces={plan.TracesEnabled}; metrics={plan.MetricsEnabled}; exporter={plan.Exporter}."
                : $"Koan owns no active OpenTelemetry provider: {plan.StatusDetail}.",
            Constants.Diagnostics.CapabilityReason,
            source: "Koan.Observability");
    }
}
