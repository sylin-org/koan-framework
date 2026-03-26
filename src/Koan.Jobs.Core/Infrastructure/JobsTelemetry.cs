using System.Diagnostics;

namespace Koan.Jobs.Infrastructure;

internal static class JobsTelemetry
{
    internal static readonly ActivitySource Source = new("Koan.Jobs", "0.6.3");
}
