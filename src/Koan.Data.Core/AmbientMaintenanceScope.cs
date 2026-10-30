using Koan.Data.Abstractions;

namespace Koan.Data.Core;

/// <summary>The one scope of a host no axis partitions: the ambient one.</summary>
internal sealed class AmbientMaintenanceScope : IMaintenanceScopes
{
    public Task ForEach(Func<CancellationToken, Task> work, CancellationToken ct = default) => work(ct);
}
