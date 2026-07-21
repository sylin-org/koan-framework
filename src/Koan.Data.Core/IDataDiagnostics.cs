namespace Koan.Data.Core;

public interface IDataDiagnostics
{
    // Returns a snapshot of known entity configurations in this ServiceProvider.
    IReadOnlyList<EntityConfigInfo> GetEntityConfigsSnapshot();

    // Returns the adapters and logical sources actually requested through this ServiceProvider.
    // The default preserves compatibility for third-party diagnostic implementations.
    IReadOnlyList<DataAdapterParticipationInfo> GetAdapterParticipationsSnapshot() => [];

    /// <summary>Returns lifecycle behavior declared by this host composition.</summary>
    IReadOnlyList<Lifecycle.EntityLifecycleInfo> GetLifecyclePlansSnapshot() => [];
}
