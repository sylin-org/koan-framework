using Microsoft.Extensions.Logging;

namespace Koan.Orchestration.Infrastructure;

public static class OrchestrationEventIds
{
    public static readonly EventId PlanBuildStart = new(48000, nameof(PlanBuildStart));
    public static readonly EventId PlanBuildOk = new(48001, nameof(PlanBuildOk));
    public static readonly EventId PlanBuildWarn = new(48002, nameof(PlanBuildWarn));
    public static readonly EventId PlanBuildError = new(48003, nameof(PlanBuildError));

    public static readonly EventId ProviderSelectStart = new(48010, nameof(ProviderSelectStart));
    public static readonly EventId ProviderSelected = new(48011, nameof(ProviderSelected));
    public static readonly EventId ProviderUnavailable = new(48012, nameof(ProviderUnavailable));

    public static readonly EventId UpStart = new(48020, nameof(UpStart));
    public static readonly EventId UpReady = new(48021, nameof(UpReady));
    public static readonly EventId ReadinessTimeout = new(48022, nameof(ReadinessTimeout));

    public static readonly EventId DownStart = new(48030, nameof(DownStart));
    public static readonly EventId DownOk = new(48031, nameof(DownOk));

    public static readonly EventId ExportStart = new(48040, nameof(ExportStart));
    public static readonly EventId ExportOk = new(48041, nameof(ExportOk));
    public static readonly EventId ExportError = new(48042, nameof(ExportError));
}
