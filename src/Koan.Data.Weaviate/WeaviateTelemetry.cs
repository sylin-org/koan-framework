using System.Diagnostics;

namespace Koan.Data.Weaviate;

internal static class WeaviateTelemetry
{
    public static readonly ActivitySource Activity = new("Koan.Data.Weaviate");
}