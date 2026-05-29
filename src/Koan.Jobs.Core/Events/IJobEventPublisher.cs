using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;
using Koan.Jobs.Progress;

namespace Koan.Jobs.Events;

public interface IJobEventPublisher
{
    Task PublishQueued(IKoanJob job, CancellationToken cancellationToken);
    Task PublishStarted(IKoanJob job, CancellationToken cancellationToken);
    Task PublishCompleted(IKoanJob job, CancellationToken cancellationToken);
    Task PublishFailed(IKoanJob job, string? error, CancellationToken cancellationToken);
    Task PublishCancelled(IKoanJob job, CancellationToken cancellationToken);
    Task PublishProgress(IKoanJob job, JobProgressUpdate update, CancellationToken cancellationToken);
}
