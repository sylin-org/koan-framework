using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.BackgroundServices;
using Koan.Jobs.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Jobs.Execution;

internal sealed class JobWorkerService : KoanBackgroundServiceBase
{
    private readonly IJobQueue _queue;
    private readonly JobExecutor _executor;

    public JobWorkerService(
        IJobQueue queue,
        JobExecutor executor,
        ILogger<JobWorkerService> logger,
        IConfiguration configuration)
        : base(logger, configuration)
    {
        _queue = queue;
        _executor = executor;
    }

    public override string Name => "Koan.Jobs.Worker";
    public override bool IsCritical => true;

    public override Task ExecuteCoreAsync(CancellationToken cancellationToken)
        => RunAsync(cancellationToken);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var item in _queue.ReadAllAsync(cancellationToken))
        {
            try
            {
                await _executor.ExecuteAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Job execution failed for {JobId}", item.JobId);
            }
        }
    }
}
