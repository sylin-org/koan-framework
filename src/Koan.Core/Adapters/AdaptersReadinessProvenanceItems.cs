using System.Collections.Generic;
using System.Globalization;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Core.Adapters;

internal static class AdaptersReadinessProvenanceItems
{
    private const string DefaultPolicyKey = AdaptersReadinessOptions.SectionPath + ":DefaultPolicy";
    private const string DefaultTimeoutKey = AdaptersReadinessOptions.SectionPath + ":DefaultTimeout";
    private const string InitializationTimeoutKey = AdaptersReadinessOptions.SectionPath + ":InitializationTimeout";
    private const string MonitoringKey = AdaptersReadinessOptions.SectionPath + ":EnableMonitoring";

    private static readonly AdaptersReadinessOptions Defaults = new();

    private static readonly IReadOnlyCollection<string> Consumers = new[]
    {
        "Koan.Core.Adapters.AdapterInitializationService",
        "Koan.Core.Adapters.AdapterReadinessMonitor",
        "Koan.Core.Adapters.DefaultRetryPolicyProvider"
    };

    internal static readonly ProvenanceItem DefaultPolicy = new(
        DefaultPolicyKey,
        "Default Readiness Policy",
        "Readiness policy applied when adapters do not specify an explicit gating mode.",
        DefaultValue: Defaults.DefaultPolicy.ToString(),
        DefaultConsumers: Consumers);

    internal static readonly ProvenanceItem DefaultTimeout = new(
        DefaultTimeoutKey,
        "Default Readiness Timeout",
        "Timeout used when adapters do not override readiness wait durations.",
        DefaultValue: Defaults.DefaultTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: Consumers);

    internal static readonly ProvenanceItem InitializationTimeout = new(
        InitializationTimeoutKey,
        "Initialization Timeout",
        "Maximum duration allowed for adapter initialization before failing the startup wave.",
        DefaultValue: Defaults.InitializationTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: Consumers);

    internal static readonly ProvenanceItem MonitoringEnabled = new(
        MonitoringKey,
        "Readiness Monitoring Enabled",
        "Indicates whether adapter readiness monitoring runs after initialization completes.",
        DefaultValue: BoolString(Defaults.EnableMonitoring),
        DefaultConsumers: Consumers);

    private static string BoolString(bool value) => value ? "true" : "false";
}
