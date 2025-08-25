namespace Sora.Data.Core;

public interface IDataDiagnostics
{
    // Returns a snapshot of known entity configurations in this ServiceProvider.
    IReadOnlyList<EntityConfigInfo> GetEntityConfigsSnapshot();
}