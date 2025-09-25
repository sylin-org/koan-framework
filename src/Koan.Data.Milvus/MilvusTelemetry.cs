
using System.Diagnostics;

namespace Koan.Data.Milvus;

internal static class MilvusTelemetry
{
    public static readonly ActivitySource Activity = new("Koan.Data.Milvus");
}
