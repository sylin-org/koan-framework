using Sora.Messaging;
using Sora.Flow.Infrastructure;
using Sora.Flow.Model;
using Sora.Data.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Flow.Sending;

internal sealed class FlowEventHandler : IMessageHandler<FlowEvent>
{
    public async Task HandleAsync(MessageEnvelope envelope, FlowEvent msg, CancellationToken ct)
    {
        // Resolve model type from msg.Model if provided, else infer from bag via FlowRegistry (if supported)
        var modelType = !string.IsNullOrWhiteSpace(msg.Model) ? FlowRegistry.ResolveModel(msg.Model!) : null;
        if (modelType is null)
        {
            // Fallback: attempt best-effort resolution using known defaults; if unknown, drop into a generic intake?
            // For now, require Model for determinism
            return;
        }

        var recordType = typeof(StageRecord<>).MakeGenericType(modelType);
        var record = Activator.CreateInstance(recordType)!;
        recordType.GetProperty("Id")!.SetValue(record, Guid.NewGuid().ToString("n"));
        recordType.GetProperty("SourceId")!.SetValue(record, msg.SourceId ?? envelope.Headers?.GetValueOrDefault("source") ?? "events");
        recordType.GetProperty("OccurredAt")!.SetValue(record, msg.OccurredAt ?? DateTimeOffset.UtcNow);
        if (!string.IsNullOrWhiteSpace(msg.CorrelationId)) recordType.GetProperty("CorrelationId")?.SetValue(record, msg.CorrelationId);
        // Ensure envelope system/adapter present; orchestrator will often run IdentityStamper upstream, but we can patch here too
        var bag = msg.Bag ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!bag.ContainsKey(Constants.Envelope.System) && !string.IsNullOrWhiteSpace(envelope.Headers?.GetValueOrDefault(Constants.Envelope.System)))
            bag[Constants.Envelope.System] = envelope.Headers![Constants.Envelope.System];
        if (!bag.ContainsKey(Constants.Envelope.Adapter) && !string.IsNullOrWhiteSpace(envelope.Headers?.GetValueOrDefault(Constants.Envelope.Adapter)))
            bag[Constants.Envelope.Adapter] = envelope.Headers![Constants.Envelope.Adapter];
        recordType.GetProperty("StagePayload")!.SetValue(record, bag);

        var dataType = typeof(Data<,>).MakeGenericType(recordType, typeof(string));
        var upsert = dataType.GetMethod("UpsertAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { recordType, typeof(string), typeof(CancellationToken) })!;
        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
        {
            await (Task)upsert.Invoke(null, new object?[] { record, FlowSets.StageShort(FlowSets.Intake), ct })!;
        }
    }
}
