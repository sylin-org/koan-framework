using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using S8.Location.Core.Models;
using Sora.Flow.Core;
using Sora.Flow.Core.Extensions;
using Sora.Flow.Model;
using Sora.Flow.Infrastructure;
using Sora.Flow.Actions;
using Sora.Data.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace S8.Location.Core.Services;

/// <summary>
/// Background service that monitors Sora.Flow's native parked collection 
/// and resolves addresses that were parked with "WAITING_ADDRESS_RESOLVE" status.
/// Uses ONLY Flow's native parking mechanisms - no custom parking entities.
/// </summary>
public class BackgroundResolutionService : BackgroundService
{
    private readonly ILogger<BackgroundResolutionService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IFlowActions _flowActions;

    public BackgroundResolutionService(
        ILogger<BackgroundResolutionService> logger,
        IServiceProvider serviceProvider,
        IFlowActions flowActions)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _flowActions = flowActions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[BackgroundResolutionService] Starting address resolution background service");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessParkedAddresses(stoppingToken);
                
                // Run every 5 minutes
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("[BackgroundResolutionService] Background service cancellation requested");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BackgroundResolutionService] Error in background resolution cycle");
                
                // Wait before retrying on error
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
    
    private async Task ProcessParkedAddresses(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IAddressResolutionService>();
        
        _logger.LogInformation("[BackgroundResolutionService] Starting parked address resolution cycle");
        
        try
        {
            // Query Flow's native parked collection for locations waiting for address resolution
            using var context = DataSetContext.With(FlowSets.StageShort(FlowSets.Parked));
            var parkedRecords = await Data<ParkedRecord<Models.Location>, string>.FirstPage(100, ct);
            var waitingRecords = parkedRecords.Where(pr => pr.ReasonCode == "WAITING_ADDRESS_RESOLVE").ToList();
            
            _logger.LogInformation("[BackgroundResolutionService] Found {Count} parked addresses to resolve", waitingRecords.Count);
            
            foreach (var parkedRecord in waitingRecords)
            {
                try
                {
                    if (parkedRecord.Data == null) continue;
                    
                    // Extract address from the parked data
                    var address = parkedRecord.Data.TryGetValue("address", out var addr) ? addr?.ToString() : null;
                    if (string.IsNullOrEmpty(address)) continue;
                    
                    _logger.LogDebug("[BackgroundResolutionService] Resolving address: {Address}", address);
                    
                    // Resolve using the AddressResolutionService
                    var agnosticLocationId = await resolver.ResolveToCanonicalIdAsync(address, ct);
                    
                    _logger.LogInformation("[BackgroundResolutionService] Resolved address to AgnosticLocationId: {AgnosticId}", agnosticLocationId);
                    
                    // Heal the parked record using the semantic Flow extension method
                    await parkedRecord.HealAsync(_flowActions, new
                    {
                        AgnosticLocationId = agnosticLocationId,
                        Resolved = true
                    }, 
                    healingReason: $"Address resolved to canonical location {agnosticLocationId}", 
                    ct: ct);
                    
                    _logger.LogDebug("[BackgroundResolutionService] Successfully resolved and reinjected location {Id}", parkedRecord.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[BackgroundResolutionService] Failed to resolve address from parked record {Id}", parkedRecord.Id);
                    
                    // Could implement retry logic here by updating the parked record
                    // For now, leave it parked for the next cycle
                }
            }
            
            _logger.LogInformation("[BackgroundResolutionService] Completed parked address resolution cycle");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BackgroundResolutionService] Error querying parked addresses from Flow");
        }
    }
}