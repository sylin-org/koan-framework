using System.Data;

namespace Sora.Data.Postgres;

internal static class PgTelemetry
{
    public static readonly System.Diagnostics.ActivitySource Activity = new("Sora.Data.Postgres");
}