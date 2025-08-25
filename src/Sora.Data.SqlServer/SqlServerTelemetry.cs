using System.Data;

namespace Sora.Data.SqlServer;

internal static class SqlServerTelemetry
{
    public static readonly System.Diagnostics.ActivitySource Activity = new("Sora.Data.SqlServer");
}