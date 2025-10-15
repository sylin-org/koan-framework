using System.Collections.Generic;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Scheduling.Infrastructure;

internal static class SchedulingProvenanceItems
{
    private static readonly SchedulingOptions Defaults = new();
    private static readonly IReadOnlyCollection<string> OrchestratorConsumers = new[] { "Koan.Scheduling.Orchestrator" };

    internal static readonly ProvenanceItem Enabled = new(
        "Koan:Scheduling:Enabled",
        "Scheduling Enabled",
        "Activates the background scheduler; disabled to prevent recurring jobs from running.",
        DefaultValue: BoolString(Defaults.Enabled),
        DefaultConsumers: OrchestratorConsumers);

    internal static readonly ProvenanceItem ReadinessGate = new(
        "Koan:Scheduling:ReadinessGate",
        "Readiness Gate",
        "If true, the scheduler only marks healthy once scheduled jobs finish their first run.",
        DefaultValue: BoolString(Defaults.ReadinessGate),
        DefaultConsumers: OrchestratorConsumers);

    private static string BoolString(bool value) => value ? "true" : "false";
}
