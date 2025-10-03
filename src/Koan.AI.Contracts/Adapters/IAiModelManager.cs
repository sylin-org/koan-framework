using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Adapters;

/// <summary>Optional contract implemented by adapters that can provision or retire models on demand.</summary>
public interface IAiModelManager
{
    /// <summary>Ensures that the requested model is available (installing it if required).</summary>
    Task<AiModelOperationResult> EnsureInstalledAsync(AiModelOperationRequest request, CancellationToken ct = default);

    /// <summary>Forces a re-pull/update of a model regardless of current availability.</summary>
    Task<AiModelOperationResult> RefreshAsync(AiModelOperationRequest request, CancellationToken ct = default);

    /// <summary>Removes a model from the provider.</summary>
    Task<AiModelOperationResult> FlushAsync(AiModelOperationRequest request, CancellationToken ct = default);

    /// <summary>Lists models managed by this adapter (may be a subset of reported capabilities).</summary>
    Task<IReadOnlyList<AiModelDescriptor>> ListManagedModelsAsync(CancellationToken ct = default);
}
