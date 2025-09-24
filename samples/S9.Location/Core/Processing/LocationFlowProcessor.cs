using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Flow.Actions;
using Koan.Flow.Core.Extensions;
using Koan.Flow.Infrastructure;
using Koan.Flow.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using S9.Location.Core.Diagnostics;
using S9.Location.Core.Models;
using S9.Location.Core.Services;

namespace S9.Location.Core.Processing;

public sealed class LocationFlowProcessor : BackgroundService
{
    private const int BatchSize = 50;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LocationFlowProcessor> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);

    public LocationFlowProcessor(IServiceProvider serviceProvider, ILogger<LocationFlowProcessor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LocationFlowProcessor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while processing harmonization queue");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("LocationFlowProcessor stopped");
    }

    private async Task ProcessPendingAsync(CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var flowActions = scope.ServiceProvider.GetRequiredService<IFlowActions>();
        var pipeline = scope.ServiceProvider.GetRequiredService<IResolutionPipeline>();

        var processed = 0;
        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Parked)))
        {
            while (!ct.IsCancellationRequested)
            {
                var batch = await ParkedRecord<RawLocation>.FirstPage(BatchSize, ct);
                if (batch.Count == 0)
                {
                    break;
                }

                foreach (var parked in batch.Where(p => string.Equals(p.ReasonCode, LocationFlowConstants.ParkedWaitingForResolution, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        if (parked.Data is null)
                        {
                            _logger.LogWarning("Skipping parked location {ParkedId}: no data payload present", parked.Id);
                            await parked.Delete(ct);
                            continue;
                        }

                        var outcome = await pipeline.HarmonizeAsync(parked.Data, ct);

                        await parked.HealAsync(flowActions, parked.Data, healingReason: $"Resolved to {outcome.CanonicalLocationId}", ct: ct);
                        processed++;
                        _logger.LogInformation("Healed parked location {ParkedId} -> {CanonicalId} (cacheHit: {CacheHit}, confidence: {Confidence:0.00})",
                            parked.Id,
                            outcome.CanonicalLocationId,
                            outcome.CacheHit,
                            outcome.Confidence);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to harmonize parked location {ParkedId}", parked.Id);
                    }
                }

                if (batch.Count < BatchSize)
                {
                    break;
                }
            }
        }

        if (processed == 0)
        {
            _logger.LogDebug("No parked locations required harmonization during this cycle");
        }
    }
}
