using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Flow.Infrastructure;
using Sora.Flow.Model;
using Sora.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Flow.Sending;

// Preferred name for normalized, contractless Flow ingestion payloads.
// Values must be primitives/arrays/dictionaries.
[Sora.Messaging.Message(Alias = "flow.event", Version = 1)]
public class FlowEvent
{
    // Optional routing hint so the orchestrator can resolve the target model type
    public string? Model { get; set; }
    // Optional transport metadata
    public string? SourceId { get; set; }
    public DateTimeOffset? OccurredAt { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, object?> Bag { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Instructional factory for readability; may set model metadata in future.
    public static FlowEvent ForModel(string model)
        => new FlowEvent { Model = model };

    // Unified factory for FlowEntity<T> and FlowValueObject<T> types.
    // Resolves the model name via FlowRegistry, so callers can simply write: FlowEvent.For<Device>() or FlowEvent.For<Reading>()
    public static FlowEvent For<T>()
        => For(typeof(T));

    // Non-generic counterpart
    public static FlowEvent For(Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        var name = FlowRegistry.GetModelName(type);
        // Fallback to simple type name if registry has no mapping (defensive)
        if (string.IsNullOrWhiteSpace(name)) name = type.Name;
        return new FlowEvent { Model = name };
    }

    public FlowEvent With(string path, object? value)
    { Bag[path] = value; return this; }

    // Helpers for common envelopes
    public FlowEvent WithAdapter(string system, string adapter)
    { Bag[Constants.Envelope.System] = system; Bag[Constants.Envelope.Adapter] = adapter; return this; }

    public FlowEvent WithExternal(string name, string value)
    { Bag[$"{Constants.Reserved.IdentifierExternalPrefix}{name}"] = value; return this; }

    public FlowEvent WithReference(string name, string value)
    { Bag[$"{Constants.Reserved.ReferencePrefix}{name}"] = value; return this; }

    public FlowEvent WithModel(string path, object? value)
    { Bag[$"{Constants.Reserved.ModelPrefix}{path}"] = value; return this; }
}

// Back-compat shim: prefer FlowEvent.
[System.Obsolete("Use FlowEvent instead. FlowNormalizedPayload will be removed in a future release.")]
public class FlowNormalizedPayload : FlowEvent { }

public sealed record FlowSendItem(
    Type ModelType,
    string SourceId,
    DateTimeOffset OccurredAt,
    FlowEvent Payload,
    string? CorrelationId = null)
{
    // Ergonomic factory when using FlowEvent and known model at callsite
    public static FlowSendItem Of<TModel>(FlowEvent payload, string sourceId, DateTimeOffset occurredAt, string? correlationId = null)
        => new(
            ModelType: typeof(TModel),
            SourceId: sourceId,
            OccurredAt: occurredAt,
            Payload: payload,
            CorrelationId: correlationId);
}

public sealed record FlowSendPlainItem(
    Type ModelType,
    string SourceId,
    DateTimeOffset OccurredAt,
    IDictionary<string, object?> Bag,
    string? CorrelationId = null)
{
    public static FlowSendPlainItem Of<TModel>(IDictionary<string, object?> bag, string sourceId, DateTimeOffset occurredAt, string? correlationId = null)
        => new(
            ModelType: typeof(TModel),
            SourceId: sourceId,
            OccurredAt: occurredAt,
            Bag: bag,
            CorrelationId: correlationId);
}

public interface IFlowSender
{
    Task SendAsync(IEnumerable<FlowSendItem> items, CancellationToken ct = default);
    Task SendAsync(IEnumerable<FlowSendPlainItem> items, MessageEnvelope? envelope = null, object? message = null, Type? hostType = null, CancellationToken ct = default);
}

internal sealed class FlowSender : IFlowSender
{
    private readonly IFlowIdentityStamper _stamper;

    public FlowSender(IFlowIdentityStamper stamper)
    {
        _stamper = stamper;
    }

    public async Task SendAsync(IEnumerable<FlowSendItem> items, CancellationToken ct = default)
    {
        if (items is null) return;
        var list = items.ToList(); if (list.Count == 0) return;
        // Map to StageRecord<TModel> per item and save to intake set
        foreach (var item in list)
        {
            var recordType = typeof(StageRecord<>).MakeGenericType(item.ModelType);
            var record = Activator.CreateInstance(recordType)!;
            recordType.GetProperty("Id")!.SetValue(record, Guid.NewGuid().ToString("n"));
            recordType.GetProperty("SourceId")!.SetValue(record, item.SourceId);
            recordType.GetProperty("OccurredAt")!.SetValue(record, item.OccurredAt);
            // Ensure envelope keys exist
            if (!item.Payload.Bag.ContainsKey(Constants.Envelope.System))
                item.Payload.Bag[Constants.Envelope.System] = "unknown";
            if (!item.Payload.Bag.ContainsKey(Constants.Envelope.Adapter))
                item.Payload.Bag[Constants.Envelope.Adapter] = "unknown";
            recordType.GetProperty("StagePayload")!.SetValue(record, item.Payload.Bag);
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

    public async Task SendAsync(IEnumerable<FlowSendPlainItem> items, MessageEnvelope? envelope = null, object? message = null, Type? hostType = null, CancellationToken ct = default)
    {
        if (items is null) return;
        var list = items.ToList(); if (list.Count == 0) return;
        foreach (var item in list)
        {
            // Build FlowEvent internally and apply server-side identity stamping
            var payload = new FlowEvent();
            if (item.Bag is not null)
            {
                foreach (var kv in item.Bag)
                    payload.With(kv.Key, kv.Value);
            }
            _stamper.Stamp(payload.Bag, envelope, message, hostType);

            var recordType = typeof(StageRecord<>).MakeGenericType(item.ModelType);
            var record = Activator.CreateInstance(recordType)!;
            recordType.GetProperty("Id")!.SetValue(record, Guid.NewGuid().ToString("n"));
            recordType.GetProperty("SourceId")!.SetValue(record, item.SourceId);
            recordType.GetProperty("OccurredAt")!.SetValue(record, item.OccurredAt);
            recordType.GetProperty("StagePayload")!.SetValue(record, payload.Bag);
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
    services.TryAddSingleton<IFlowSender, FlowSender>();
    // Also register the FlowEvent handler (only orchestrator processes will consume, producers won’t run consumers)
    services.TryAddSingleton<Sora.Messaging.IMessageHandler<FlowEvent>, FlowEventHandler>();
        return services;
    }
}
