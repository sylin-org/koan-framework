using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;
using Koan.Jobs.Progress;
using Koan.Jobs.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Koan.Messaging;

namespace Koan.Jobs.Events;

internal sealed class JobEventPublisher : IJobEventPublisher
{
    private readonly ILogger<JobEventPublisher> _logger;
    private readonly IMessageProxy? _messageProxy;

    public JobEventPublisher(IServiceProvider services, ILogger<JobEventPublisher> logger)
    {
        _logger = logger;
        _messageProxy = services.GetService<IMessageProxy>();
    }

    public Task PublishQueued(IKoanJob job, CancellationToken cancellationToken)
        => PublishEvent(job, "queued", null, cancellationToken);

    public Task PublishStarted(IKoanJob job, CancellationToken cancellationToken)
        => PublishEvent(job, "started", null, cancellationToken);

    public Task PublishCompleted(IKoanJob job, CancellationToken cancellationToken)
        => PublishEvent(job, "completed", null, cancellationToken);

    public Task PublishFailed(IKoanJob job, string? error, CancellationToken cancellationToken)
        => PublishEvent(job, "failed", error, cancellationToken);

    public Task PublishCancelled(IKoanJob job, CancellationToken cancellationToken)
        => PublishEvent(job, "cancelled", null, cancellationToken);

    public Task PublishProgress(IKoanJob job, JobProgressUpdate update, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Job {JobId} progress {Progress:P0} - {Message}", job.Id, update.Percentage, update.Message);
        JobFlowBridge.TryPublishProgress(job, update);
        if (!JobEnvironment.Options.PublishEvents || _messageProxy == null)
            return Task.CompletedTask;

        var notification = new JobProgressNotification(
            job.Id,
            job.Status,
            update.Percentage,
            update.Message,
            update.UpdatedAt);

        return _messageProxy.SendAsync(notification, cancellationToken);
    }

    private Task PublishEvent(IKoanJob job, string eventType, string? error, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Job {JobId} {EventType} (status: {Status})", job.Id, eventType, job.Status);
        JobFlowBridge.TryPublishEvent(job, eventType, error);
        if (!JobEnvironment.Options.PublishEvents || _messageProxy == null)
            return Task.CompletedTask;

        var notification = new JobNotification(
            job.Id,
            job.Status,
            job.CorrelationId,
            eventType,
            DateTimeOffset.UtcNow,
            error);

        return _messageProxy.SendAsync(notification, cancellationToken);
    }
}
