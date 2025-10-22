using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;

namespace Koan.Jobs.Execution;

internal interface IJobCoordinator
{
    Task<TJob> Run<TJob, TContext, TResult>(JobRunRequest<TJob, TContext, TResult> request)
        where TJob : Job<TJob, TContext, TResult>, new();

    Task<TJob?> Refresh<TJob, TContext, TResult>(string jobId, CancellationToken cancellationToken)
        where TJob : Job<TJob, TContext, TResult>, new();

    Task Cancel<TJob, TContext, TResult>(string jobId, CancellationToken cancellationToken)
        where TJob : Job<TJob, TContext, TResult>, new();

    Task<IReadOnlyList<JobExecution>> GetExecutionsAsync(string jobId, CancellationToken cancellationToken);
}
