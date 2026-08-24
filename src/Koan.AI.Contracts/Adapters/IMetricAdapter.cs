using Koan.AI.Contracts.Shared;

namespace Koan.AI.Contracts.Adapters;

/// <summary>
/// Implemented by AI adapters that compute evaluation metrics for Koan.AI.Eval.
/// Capability is structural: declaring <see cref="AiCapability.MetricCompute"/> in
/// <see cref="IAiAdapter.Capabilities"/> without implementing this interface fails
/// correctively when a measurement is requested, naming the adapter and the remedy.
/// </summary>
public interface IMetricAdapter
{
    /// <summary>
    /// Compute one named metric (well-known names in <c>Koan.AI.Eval.Metric</c>) for a model over a
    /// dataset. The references are identities the adapter resolves against its own model and dataset
    /// stores; when it cannot compute the requested metric it must fail with the reason, never return
    /// a placeholder value.
    /// </summary>
    Task<double> ComputeMetricAsync(ModelRef model, DatasetRef data, string metric, CancellationToken cancellationToken = default);
}
