namespace Koan.Data.Mongo;

internal static class MongoTelemetry
{
    public static readonly System.Diagnostics.ActivitySource Activity = new("Koan.Data.Mongo");
}

// Self-registration so the adapter participates in discovery