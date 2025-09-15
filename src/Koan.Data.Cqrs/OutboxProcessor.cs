using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Koan.Core.BackgroundServices;
using Koan.Data.Core;

namespace Koan.Data.Cqrs;

/// <summary>
/// Background processor that drains the outbox and applies simple 1:1 projections when implicit CQRS is enabled.
/// This mirrors Upsert/Delete events into the same entity repository resolved for reads.
/// </summary>
[KoanBackgroundService(RunInProduction = true)]
[ServiceEvent(Koan.Core.Events.KoanServiceEvents.Outbox.Processed, EventArgsType = typeof(OutboxProcessedEventArgs))]
[ServiceEvent(Koan.Core.Events.KoanServiceEvents.Outbox.Failed, EventArgsType = typeof(OutboxFailedEventArgs))]
internal sealed class OutboxProcessor : KoanFluentServiceBase
{
    private readonly IOutboxStore _outbox;
    private readonly CqrsOptions _options;
    private readonly ICqrsRouting _routing;
    private readonly IServiceProvider _serviceProvider;

    public OutboxProcessor(
        ILogger<OutboxProcessor> logger, 
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        IOutboxStore outbox, 
        IOptions<CqrsOptions> options)
        : base(logger, configuration)
    { 
        _serviceProvider = serviceProvider;
        _outbox = outbox; 
        _options = options.Value; 
        _routing = serviceProvider.GetRequiredService<ICqrsRouting>(); 
    }

    public override async Task ExecuteCoreAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("OutboxProcessor started - processing CQRS outbox entries");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await _outbox.DequeueAsync(100, stoppingToken);
                if (batch.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
                    continue;
                }

                var processedCount = 0;
                var failedCount = 0;

                foreach (var entry in batch)
                {
                    try
                    {
                        await ApplyProjectionAsync(entry, stoppingToken);
                        await _outbox.MarkProcessedAsync(entry.Id, stoppingToken);
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Failed processing outbox entry {Id} {Type} {Op}", entry.Id, entry.EntityType, entry.Operation);
                        failedCount++;
                        
                        await EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Outbox.Failed, new OutboxFailedEventArgs
                        {
                            EntryId = entry.Id,
                            EntityType = entry.EntityType,
                            Operation = entry.Operation,
                            Error = ex.Message,
                            FailedAt = DateTimeOffset.UtcNow,
                            Exception = ex
                        });
                        
                        // Leave unmarked; it will retry on next loop.
                    }
                }
                
                if (processedCount > 0)
                {
                    await EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Outbox.Processed, new OutboxProcessedEventArgs
                    {
                        ProcessedCount = processedCount,
                        FailedCount = failedCount,
                        BatchSize = batch.Count,
                        ProcessedAt = DateTimeOffset.UtcNow
                    });
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "OutboxProcessor loop error");
                
                await EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Outbox.Failed, new OutboxFailedEventArgs
                {
                    EntryId = "",
                    EntityType = "System",
                    Operation = "BatchProcess",
                    Error = ex.Message,
                    FailedAt = DateTimeOffset.UtcNow,
                    Exception = ex
                });
                
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        
        Logger.LogInformation("OutboxProcessor stopped");
    }
    
    [ServiceAction(Koan.Core.Actions.KoanServiceActions.Outbox.ProcessBatch)]
    public async Task ProcessBatchAction(int? batchSize, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Manual outbox batch processing requested with batch size: {BatchSize}", batchSize ?? 100);
        
        var batch = await _outbox.DequeueAsync(batchSize ?? 100, cancellationToken);
        var processedCount = 0;
        
        foreach (var entry in batch)
        {
            try
            {
                await ApplyProjectionAsync(entry, cancellationToken);
                await _outbox.MarkProcessedAsync(entry.Id, cancellationToken);
                processedCount++;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed processing outbox entry {Id} {Type} {Op} in manual batch", entry.Id, entry.EntityType, entry.Operation);
            }
        }
        
        Logger.LogInformation("Manual batch processing completed: {ProcessedCount}/{TotalCount}", processedCount, batch.Count);
    }

    private async Task ApplyProjectionAsync(OutboxEntry entry, CancellationToken ct)
    {
        // Minimal 1:1 mirror: same entity type assembly-qualified
        var entityType = Type.GetType(entry.EntityType, throwOnError: false);
        if (entityType is null)
        {
            Logger.LogDebug("Unknown entity type '{Type}', skipping", entry.EntityType);
            return;
        }
        // Resolve key type via AggregateMetadata
        var idSpec = AggregateMetadata.GetIdSpec(entityType);
        if (idSpec is null)
        {
            Logger.LogDebug("No Identifier on entity '{Type}', skipping", entry.EntityType);
            return;
        }
        var keyType = idSpec.Prop.PropertyType;

        // Resolve read/write repos via routing (mirror applies to read repo)
        var repo = GetReadRepository(entityType, keyType);

        // Only mirror if the entity participates in a profile
        var profile = _routing.GetProfileNameFor(entityType);
        if (string.IsNullOrWhiteSpace(profile)) return;

        if (string.Equals(entry.Operation, "Upsert", StringComparison.OrdinalIgnoreCase))
        {
            var model = JsonConvert.DeserializeObject(entry.PayloadJson, entityType);
            if (model is null) return;
            var upsertAsync = repo.GetType().GetMethod("UpsertAsync");
            await (Task)upsertAsync!.Invoke(repo, new object?[] { model, ct })!;
            return;
        }
        if (string.Equals(entry.Operation, "Delete", StringComparison.OrdinalIgnoreCase))
        {
            var id = ConvertId(entry.EntityId, keyType);
            var deleteAsync = repo.GetType().GetMethod("DeleteAsync");
            await (Task)deleteAsync!.Invoke(repo, new object?[] { id, ct })!;
            return;
        }
    }

    private object GetReadRepository(Type entityType, Type keyType)
    {
        var mi = typeof(ICqrsRouting).GetMethod(nameof(ICqrsRouting.GetReadRepository))!;
        var gm = mi.MakeGenericMethod(entityType, keyType);
        return gm.Invoke(_routing, null)!;
    }

    private static object? ConvertId(string raw, Type keyType)
    {
        if (keyType == typeof(string)) return raw;
        if (keyType == typeof(Guid)) return Guid.Parse(raw);
        if (keyType == typeof(int)) return int.Parse(raw);
        if (keyType == typeof(long)) return long.Parse(raw);
        if (keyType == typeof(short)) return short.Parse(raw);
        if (keyType == typeof(uint)) return uint.Parse(raw);
        if (keyType == typeof(ulong)) return ulong.Parse(raw);
        if (keyType == typeof(ushort)) return ushort.Parse(raw);
        return Convert.ChangeType(raw, keyType);
    }
}
