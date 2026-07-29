namespace Koan.Testing;

/// <summary>Standard modules used by every adapter fixture; providers supply mechanics and exact receipts.</summary>
public static class DataScenarioCatalog
{
    public static IReadOnlyList<DataScenarioDefinition> All { get; } =
    [
        new("fault", DataScenarioKind.Fault, ["G-01", "G-02", "G-04", "H-06"], false, false, false, 1),
        new("cancellation", DataScenarioKind.Cancellation, ["G-02", "G-04"], false, false, false, 1),
        new("pool-saturation", DataScenarioKind.PoolSaturation, ["G-03"], true, false, false, 2),
        new("two-host", DataScenarioKind.TwoHost, ["P-03", "H-04"], false, true, false, 2),
        new("restart", DataScenarioKind.Restart, ["G-07"], true, false, true, 2),
        new("durability", DataScenarioKind.Durability, ["G-07"], true, false, true, 2),
        new("isolation", DataScenarioKind.Isolation, ["G-09"], true, true, false, 2),
        new("soak", DataScenarioKind.Soak, ["G-08", "P-03", "P-05"], false, false, false, 100)
    ];

    public static DataScenarioDefinition Require(DataScenarioKind kind) =>
        All.Single(definition => definition.Kind == kind);
}
