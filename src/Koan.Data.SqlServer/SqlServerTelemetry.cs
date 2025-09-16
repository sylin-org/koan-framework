using System.Data;

namespace Koan.Data.SqlServer;

internal static class SqlServerTelemetry
{
    public static readonly System.Diagnostics.ActivitySource Activity = new("Koan.Data.SqlServer");
}