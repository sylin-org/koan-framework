using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Execution;
using Koan.Jobs.Progress;
using Koan.Jobs.Store;
using Koan.Jobs.Support;

namespace Koan.Jobs.Model;

public abstract partial class Job<TJob, TContext, TResult> : Job
    where TJob : Job<TJob, TContext, TResult>, new()
{
    [NotMapped]
    public TContext? Context
    {
        get => JobSerialization.Deserialize<TContext>(ContextJson);
        set => ContextJson = JobSerialization.Serialize(value);
    }

    [NotMapped]
    public TResult? Result
    {
        get => JobSerialization.Deserialize<TResult>(ResultJson);
        set => ResultJson = JobSerialization.Serialize(value);
    }

    protected abstract Task<TResult> Execute(TContext context, IJobProgress progress, CancellationToken cancellationToken);

    internal Task<TResult> InvokeExecute(TContext context, IJobProgress progress, CancellationToken cancellationToken)
        => Execute(context, progress, cancellationToken);

    public static JobRunBuilder<TJob, TContext, TResult> Start(
        TContext context,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
        => JobEnvironment.CreateBuilder<TJob, TContext, TResult>(context, correlationId, cancellationToken);

    public async Task<TResult> Wait(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var waitTimeout = timeout ?? TimeSpan.FromMinutes(30);
        var deadline = DateTimeOffset.UtcNow.Add(waitTimeout);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await Refresh(cancellationToken).ConfigureAwait(false);
            if (snapshot.Status == JobStatus.Completed)
            {
                return snapshot.Result ?? default!;
            }

            if (snapshot.Status == JobStatus.Failed)
                throw new JobFailedException(snapshot.Id, snapshot.LastError);

            if (snapshot.Status == JobStatus.Cancelled)
                throw new JobCancelledException(snapshot.Id);

            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException($"Job {Id} did not complete within {waitTimeout}");

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<TJob> Refresh(CancellationToken cancellationToken = default)
    {
        var refreshed = await JobEnvironment.Coordinator.Refresh<TJob, TContext, TResult>(Id, cancellationToken).ConfigureAwait(false);
        return refreshed ?? (TJob)this;
    }

    public IDisposable OnProgress(Func<JobProgressUpdate, Task> handler, CancellationToken cancellationToken = default)
        => JobEnvironment.ProgressBroker.Subscribe(Id, handler, cancellationToken);

    public Task Cancel(CancellationToken cancellationToken = default)
        => JobEnvironment.Coordinator.Cancel<TJob, TContext, TResult>(Id, cancellationToken);
}
