using System.Data;

namespace Koan.Data.Relational.Npgsql;

internal static class NpgsqlTelemetry
{
    public static readonly System.Diagnostics.ActivitySource Activity = new("Koan.Data.Relational.Npgsql");
}
