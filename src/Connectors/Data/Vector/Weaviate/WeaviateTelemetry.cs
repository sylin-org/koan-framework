using System.Diagnostics;

namespace Koan.Data.Vector.Connector.Weaviate;

internal static class WeaviateTelemetry
{
    public static readonly ActivitySource Activity = new("Koan.Data.Vector.Connector.Weaviate");
}
