namespace S16.PantryPal.Infrastructure;

public static class PantryRoutes
{
    public const string IngestionBase = "api/pantry-ingestion";
    public const string InsightsBase = "api/pantry-insights";
    public const string Upload = IngestionBase + "/upload";
    public const string Confirm = IngestionBase + "/confirm"; // +/{photoId}
    public const string Stats = InsightsBase + "/stats";
}
