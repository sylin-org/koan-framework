using System.Threading.Tasks;
using Koan.Jobs.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Jobs.Execution;

internal static class JobRunDispatcher
{
    public static Task<TJob> Run<TJob, TContext, TResult>(JobRunRequest<TJob, TContext, TResult> request)
        where TJob : Job<TJob, TContext, TResult>, new()
    {
        var coordinator = request.Services.GetRequiredService<IJobCoordinator>();
        return coordinator.Run<TJob, TContext, TResult>(request);
    }
}
