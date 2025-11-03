using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;
using Koan.Jobs.Progress;

namespace Koan.Jobs.Execution;

internal static class JobRunner<TJob, TContext, TResult>
    where TJob : Job<TJob, TContext, TResult>, new()
{
    public static async Task<JobExecutionOutcome> Run(Job job, JobProgressTracker tracker, CancellationToken cancellationToken)
    {
        if (job is not TJob typedJob)
            throw new InvalidOperationException($"Job {job.Id} is not of type {typeof(TJob).FullName}.");

        var context = typedJob.Context ?? throw new InvalidOperationException($"Job {job.Id} has no execution context.");

        try
        {
            var result = await typedJob.InvokeExecute(context, tracker, cancellationToken);
            typedJob.Result = result;
            return new JobExecutionOutcome(JobExecutionStatus.Succeeded, result, null);
        }
        catch (OperationCanceledException oce) when (tracker.CancellationRequested || cancellationToken.IsCancellationRequested)
        {
            return new JobExecutionOutcome(JobExecutionStatus.Cancelled, default, oce);
        }
        catch (Exception ex)
        {
            return new JobExecutionOutcome(JobExecutionStatus.Faulted, default, ex);
        }
    }
}
