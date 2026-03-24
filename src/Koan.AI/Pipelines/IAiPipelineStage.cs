using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.AI.Pipelines;

/// <summary>
/// Represents a stage in the AI transformation pipeline.
/// Supports both immediate execution and streaming.
/// </summary>
/// <typeparam name="TOutput">The output type of this pipeline stage</typeparam>
public interface IAiPipelineStage<TOutput>
{
    /// <summary>
    /// Execute the pipeline stage and return the result.
    /// </summary>
    Task<TOutput> Execute(CancellationToken ct = default);

    /// <summary>
    /// Stream results from the pipeline stage (if supported).
    /// </summary>
    IAsyncEnumerable<TOutput> Stream(CancellationToken ct = default);
}
