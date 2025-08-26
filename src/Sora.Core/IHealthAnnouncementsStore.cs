using Sora.Core.Observability.Health;

namespace Sora.Core;

internal interface IHealthAnnouncementsStore
{
    IReadOnlyList<HealthReport> Snapshot();
}