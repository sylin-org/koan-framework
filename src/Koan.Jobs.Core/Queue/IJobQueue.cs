using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Jobs.Queue;

internal interface IJobQueue
{
    ValueTask EnqueueAsync(JobQueueItem item, CancellationToken cancellationToken);
    IAsyncEnumerable<JobQueueItem> ReadAllAsync(CancellationToken cancellationToken);
}
