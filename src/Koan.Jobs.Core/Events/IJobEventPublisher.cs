using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;
using Koan.Jobs.Progress;

namespace Koan.Jobs.Events;

public interface IJobEventPublisher
{
    Task PublishQueued(Job job, CancellationToken cancellationToken);
    Task PublishStarted(Job job, CancellationToken cancellationToken);
    Task PublishCompleted(Job job, CancellationToken cancellationToken);
    Task PublishFailed(Job job, string? error, CancellationToken cancellationToken);
    Task PublishCancelled(Job job, CancellationToken cancellationToken);
    Task PublishProgress(Job job, JobProgressUpdate update, CancellationToken cancellationToken);
}
