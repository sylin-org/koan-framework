using System.Threading;
using System.Threading.Tasks;
using Koan.Core.BackgroundServices;
using Koan.Jobs.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Jobs.Execution;

/// <summary>
/// On startup, re-enqueues every persisted non-terminal job across all job types (JOBS-0003 boot
/// recovery). Replaces the old in-memory recovery sweep: because each job type is its own set, this
/// is a registry-driven fan-out rather than a query over a unified set. Jobs are at-least-once +
/// idempotent, so re-queuing a row that was mid-run is safe.
/// </summary>
internal sealed class JobRecoveryService : KoanBackgroundServiceBase
{
    private readonly JobTypeRegistry _registry;
    private readonly IJobQueue _queue;

    public JobRecoveryService(JobTypeRegistry registry, IJobQueue queue, ILogger<JobRecoveryService> logger, IConfiguration configuration)
        : base(logger, configuration)
    {
        _registry = registry;
        _queue = queue;
    }

    public override string Name => "Koan.Jobs.Recovery";
    public override bool IsCritical => false;

    public override async Task ExecuteCore(CancellationToken cancellationToken)
    {
        try
        {
            await _registry.RecoverAll(_queue, Logger, cancellationToken);
        }
        catch (System.Exception ex)
        {
            Logger.LogError(ex, "Job boot recovery failed; non-terminal jobs may not resume until re-submitted.");
        }
    }
}
