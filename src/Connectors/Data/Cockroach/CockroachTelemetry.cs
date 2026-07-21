using System.Data;

namespace Koan.Data.Connector.Cockroach;

internal static class CockroachTelemetry
{
    public static readonly System.Diagnostics.ActivitySource Activity = new("Koan.Data.Connector.Cockroach");
}
