namespace Sora.Core;

internal interface IHealthAnnouncementsStore
{
    IReadOnlyList<HealthReport> Snapshot();
}