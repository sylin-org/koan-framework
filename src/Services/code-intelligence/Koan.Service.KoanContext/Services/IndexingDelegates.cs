using System;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Context.Services;

/// <summary>
/// Delegate abstraction for triggering a full project indexing operation.
/// </summary>
/// <param name="projectId">Target project identifier.</param>
/// <param name="force">When true, cancels any active job before starting.</param>
/// <param name="cancellationToken">Cancellation token for the operation.</param>
/// <param name="progress">Optional progress reporter for indexing telemetry.</param>
/// <returns>Indexing result produced by the indexing pipeline.</returns>
public delegate Task<IndexingResult> IndexProjectAsync(
    string projectId,
    bool force,
    CancellationToken cancellationToken,
    IProgress<IndexingProgress>? progress = null);
