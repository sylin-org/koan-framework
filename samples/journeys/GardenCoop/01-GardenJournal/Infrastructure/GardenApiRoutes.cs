namespace GardenCoop.Infrastructure;

/// <summary>Stable HTTP contracts exposed by the GardenCoop application.</summary>
public static class GardenApiRoutes
{
    public const string Base = "api/garden";
    public const string Members = Base + "/members";
    public const string Plots = Base + "/plots";
    public const string Readings = Base + "/readings";
    public const string Reminders = Base + "/reminders";
    public const string Sensors = Base + "/sensors";
    public const string RecentReadings = "recent/{plotId}";
}
