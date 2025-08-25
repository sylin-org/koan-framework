using System.Diagnostics;

namespace Sora.Data.Weaviate;

internal static class WeaviateTelemetry
{
    public static readonly ActivitySource Activity = new("Sora.Data.Weaviate");
}