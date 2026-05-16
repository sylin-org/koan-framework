using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Jobs.Queue;

internal interface IJobQueue
{
    ValueTask Enqueue(JobQueueItem item, CancellationToken cancellationToken);
    IAsyncEnumerable<JobQueueItem> ReadAll(CancellationToken cancellationToken);
}
