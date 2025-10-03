using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;
using Koan.Jobs.Progress;

namespace Koan.Jobs.Events;

public interface IJobEventPublisher
{
    Task PublishQueuedAsync(Job job, CancellationToken cancellationToken);
    Task PublishStartedAsync(Job job, CancellationToken cancellationToken);
    Task PublishCompletedAsync(Job job, CancellationToken cancellationToken);
    Task PublishFailedAsync(Job job, string? error, CancellationToken cancellationToken);
    Task PublishCancelledAsync(Job job, CancellationToken cancellationToken);
    Task PublishProgressAsync(Job job, JobProgressUpdate update, CancellationToken cancellationToken);
}
