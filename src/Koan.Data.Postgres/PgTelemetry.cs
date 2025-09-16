using System.Data;

namespace Koan.Data.Postgres;

internal static class PgTelemetry
{
    public static readonly System.Diagnostics.ActivitySource Activity = new("Koan.Data.Postgres");
}