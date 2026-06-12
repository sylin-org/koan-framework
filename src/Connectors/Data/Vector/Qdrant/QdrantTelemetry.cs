using System.Diagnostics;

namespace Koan.Data.Vector.Connector.Qdrant;

internal static class QdrantTelemetry
{
    public static readonly ActivitySource Activity = new("Koan.Data.Vector.Connector.Qdrant");
}
