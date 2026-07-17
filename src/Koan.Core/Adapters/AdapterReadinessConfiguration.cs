namespace Koan.Core.Adapters;

public class AdapterReadinessConfiguration : IAdapterReadinessConfiguration
{
    public ReadinessPolicy Policy { get; set; } = ReadinessPolicy.Hold;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool EnableReadinessGating { get; set; } = true;
}

public sealed class AdaptersReadinessOptions
{
    public const string SectionPath = "Koan:Adapters:Readiness";

    public ReadinessPolicy DefaultPolicy { get; set; } = ReadinessPolicy.Hold;

    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public bool EnableMonitoring { get; set; } = true;
}
