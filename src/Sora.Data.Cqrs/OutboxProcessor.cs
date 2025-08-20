using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.Cqrs;

/// <summary>
/// Background processor that drains the outbox and applies simple 1:1 projections when implicit CQRS is enabled.
/// This mirrors Upsert/Delete events into the same entity repository resolved for reads.
/// </summary>
internal sealed class OutboxProcessor : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly IOutboxStore _outbox;
    private readonly CqrsOptions _options;
    private readonly ICqrsRouting _routing;

    public OutboxProcessor(IServiceProvider sp, ILogger<OutboxProcessor> logger, IOutboxStore outbox, IOptions<CqrsOptions> options)
    { _sp = sp; _logger = logger; _outbox = outbox; _options = options.Value; _routing = sp.GetRequiredService<ICqrsRouting>(); }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

                foreach (var entry in batch)
                {
                    try
                    {
                        await ApplyProjectionAsync(entry, stoppingToken);
                        await _outbox.MarkProcessedAsync(entry.Id, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed processing outbox entry {Id} {Type} {Op}", entry.Id, entry.EntityType, entry.Operation);
                        // Leave unmarked; it will retry on next loop.
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxProcessor loop error");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    private async Task ApplyProjectionAsync(OutboxEntry entry, CancellationToken ct)
    {
        // Minimal 1:1 mirror: same entity type assembly-qualified
        var entityType = Type.GetType(entry.EntityType, throwOnError: false);
        if (entityType is null)
        {
            _logger.LogDebug("Unknown entity type '{Type}', skipping", entry.EntityType);
            return;
        }
        // Resolve key type via AggregateMetadata
        var idSpec = Sora.Data.Core.Metadata.AggregateMetadata.GetIdSpec(entityType);
        if (idSpec is null)
        {
            _logger.LogDebug("No Identifier on entity '{Type}', skipping", entry.EntityType);
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
            var model = JsonSerializer.Deserialize(entry.PayloadJson, entityType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
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
