using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Jobs.Queue;

internal interface IJobQueue
{
    /// <summary>Enqueue an item for immediate dispatch.</summary>
    ValueTask Enqueue(JobQueueItem item, CancellationToken cancellationToken);

    /// <summary>
    /// Enqueue an item that becomes dispatchable at <paramref name="visibleAt"/> (JOBS-0002
    /// delayed-visibility). A past/now time is equivalent to immediate enqueue. Deferral paths
    /// (retry backoff, host-rate-gate, dependency-block) use this to re-queue without holding a
    /// lane permit through a sleep; it also gives scheduled/delayed jobs for free.
    /// </summary>
    ValueTask Enqueue(JobQueueItem item, DateTimeOffset visibleAt, CancellationToken cancellationToken);

    IAsyncEnumerable<JobQueueItem> ReadAll(CancellationToken cancellationToken);
}
