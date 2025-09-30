
using System.Diagnostics;

namespace Koan.Data.Vector.Connector.Milvus;

internal static class MilvusTelemetry
{
    public static readonly ActivitySource Activity = new("Koan.Data.Vector.Connector.Milvus");
}

