namespace Koan.Data.Connector.Mongo;

internal static class MongoTelemetry
{
    public static readonly System.Diagnostics.ActivitySource Activity = new("Koan.Data.Connector.Mongo");
}

// Self-registration so the adapter participates in discovery
