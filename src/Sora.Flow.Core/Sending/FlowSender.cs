using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Flow.Infrastructure;
using Sora.Flow.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Flow.Sending;

// Internal-only record for direct Flow intake operations
// Used by the orchestrator and internal messaging handlers
internal sealed record FlowSendPlainItem(
    Type ModelType,
    string SourceId,
    DateTimeOffset OccurredAt,
    IDictionary<string, object?> Bag,
    string? CorrelationId = null)
{
    internal static FlowSendPlainItem Of<TModel>(IDictionary<string, object?> bag, string sourceId, DateTimeOffset occurredAt, string? correlationId = null)
        => new(
            ModelType: typeof(TModel),
            SourceId: sourceId,
            OccurredAt: occurredAt,
            Bag: bag,
            CorrelationId: correlationId);
}

// Internal interface for direct Flow intake operations
// Used by orchestrator and messaging handlers - not for public use
internal interface IFlowSender
{
    Task SendAsync(IEnumerable<FlowSendPlainItem> items, object? envelope = null, object? message = null, Type? hostType = null, CancellationToken ct = default);
}

internal sealed class FlowSender : IFlowSender
{
    private readonly IFlowIdentityStamper _stamper;

    public FlowSender(IFlowIdentityStamper stamper)
    {
        _stamper = stamper;
    }

    public async Task SendAsync(IEnumerable<FlowSendPlainItem> items, object? envelope = null, object? message = null, Type? hostType = null, CancellationToken ct = default)
    {
        if (items is null) return;
        var list = items.ToList(); if (list.Count == 0) return;
        foreach (var item in list)
        {
            // Apply server-side identity stamping to the bag
            var bag = item.Bag ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            _stamper.Stamp(bag, envelope, message, hostType);

            var recordType = typeof(StageRecord<>).MakeGenericType(item.ModelType);
            var record = Activator.CreateInstance(recordType)!;
            recordType.GetProperty("Id")!.SetValue(record, Guid.NewGuid().ToString("n"));
            recordType.GetProperty("SourceId")!.SetValue(record, item.SourceId);
            recordType.GetProperty("OccurredAt")!.SetValue(record, item.OccurredAt);
            recordType.GetProperty("StagePayload")!.SetValue(record, bag);
            if (!string.IsNullOrWhiteSpace(item.CorrelationId))
                recordType.GetProperty("CorrelationId")?.SetValue(record, item.CorrelationId);

            var dataType = typeof(Sora.Data.Core.Data<,>).MakeGenericType(recordType, typeof(string));
            var upsert = dataType.GetMethod("UpsertAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { recordType, typeof(string), typeof(CancellationToken) })!;
            using (Sora.Data.Core.DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
            {
                await (Task)upsert.Invoke(null, new object?[] { record, FlowSets.StageShort(FlowSets.Intake), ct })!;
            }
        }
    }
}

public static class FlowSenderRegistration
{
    public static IServiceCollection AddFlowSender(this IServiceCollection services)
    {
        // Register the real sender as an internal type
        services.TryAddSingleton<FlowSender>();
        // Removed old messaging readiness system - new system handles this automatically
        // Register the sender directly as IFlowSender
        services.TryAddSingleton<IFlowSender, FlowSender>();
        // Removed old message handler registration - new system uses .On<T>() pattern
        return services;
    }
}
