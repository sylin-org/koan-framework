using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.BackgroundServices;
using Koan.Jobs.Options;
using Koan.Jobs.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Jobs.Execution;

internal sealed class InMemoryJobSweeper : KoanBackgroundServiceBase
{
    private readonly InMemoryJobStore _store;
    private readonly JobsOptions _options;

    public InMemoryJobSweeper(
        InMemoryJobStore store,
        IOptions<JobsOptions> options,
        ILogger<InMemoryJobSweeper> logger,
        IConfiguration configuration)
        : base(logger, configuration)
    {
        _store = store;
        _options = options.Value;
    }

    public override string Name => "Koan.Jobs.InMemorySweep";
    public override bool IsCritical => false;

    public override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        if (_options.InMemory.SweepIntervalSeconds <= 0)
        {
            Logger.LogInformation("In-memory job sweeper disabled (interval <= 0)");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.InMemory.SweepIntervalSeconds));
        var completedRetention = TimeSpan.FromMinutes(Math.Max(1, _options.InMemory.CompletedRetentionMinutes));
        var faultedRetention = TimeSpan.FromMinutes(Math.Max(1, _options.InMemory.FaultedRetentionMinutes));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _store.Sweep(completedRetention, faultedRetention);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error sweeping in-memory jobs");
            }

            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
