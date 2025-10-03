using System.Data;

namespace Koan.Data.Connector.Postgres;

internal static class PgTelemetry
{
    public static readonly System.Diagnostics.ActivitySource Activity = new("Koan.Data.Connector.Postgres");
}
