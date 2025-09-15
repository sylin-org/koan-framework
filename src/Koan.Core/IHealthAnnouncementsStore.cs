using Koan.Core.Observability.Health;

namespace Koan.Core;

internal interface IHealthAnnouncementsStore
{
    IReadOnlyList<HealthReport> Snapshot();
}