using Microsoft.Extensions.Logging;
using Sora.Data.Core;
using Sora.Flow.Infrastructure;
using Sora.Flow.Model;
using Sora.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Flow.Actions;

/// <summary>
/// Default responder for FlowAction messages.
/// Supports verbs: seed (ingest into intake), report (emit stats), ping (ack-only).
/// Model is resolved to a known Flow model type; behavior is adapter-agnostic.
/// </summary>
internal sealed class FlowActionHandler : IMessageHandler<FlowAction>
{
    private readonly ILogger<FlowActionHandler> _log;
    public FlowActionHandler(ILogger<FlowActionHandler> log) { _log = log; }

    public async Task HandleAsync(MessageEnvelope envelope, FlowAction msg, CancellationToken ct)
    {
        try
        {
            var modelType = Sora.Flow.Infrastructure.FlowRegistry.ResolveModel(msg.Model);
            if (modelType is null)
            {
                await new FlowAck(msg.Model, msg.Verb, msg.ReferenceId, "unsupported", $"Unknown model '{msg.Model}'", msg.CorrelationId).Send(ct);
                return;
            }

            var verb = (msg.Verb ?? string.Empty).Trim().ToLowerInvariant();
            switch (verb)
            {
                case "seed":
                    await HandleSeedAsync(modelType, msg, ct);
                    break;
                case "report":
                    await HandleReportAsync(modelType, msg, ct);
                    break;
                case "ping":
                    await new FlowAck(msg.Model, msg.Verb ?? "ping", msg.ReferenceId, "ok", null, msg.CorrelationId).Send(ct);
                    break;
                default:
                    await new FlowAck(msg.Model, msg.Verb ?? string.Empty, msg.ReferenceId, "unsupported", $"Unknown verb '{msg.Verb}'", msg.CorrelationId).Send(ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "FlowAction handling failed for {Model}/{Verb}", msg.Model, msg.Verb);
            try { await new FlowAck(msg.Model, msg.Verb, msg.ReferenceId, "error", ex.Message, msg.CorrelationId).Send(ct); } catch { }
        }
    }

    private static async Task HandleSeedAsync(Type modelType, FlowAction msg, CancellationToken ct)
    {
        // Create StageRecord<TModel> and save to intake set
        var recordType = typeof(StageRecord<>).MakeGenericType(modelType);
        var record = Activator.CreateInstance(recordType)!;
        recordType.GetProperty("Id")!.SetValue(record, Guid.NewGuid().ToString("n"));
        recordType.GetProperty("SourceId")!.SetValue(record, msg.Payload is IDictionary<string, object?> p && p.TryGetValue("source", out var s) ? (s?.ToString() ?? msg.Model) : (msg.ReferenceId ?? msg.Model));
        recordType.GetProperty("OccurredAt")!.SetValue(record, DateTimeOffset.UtcNow);
        // Promote model/adapter envelope if present
        var dict = ToDict(msg.Payload);
        if (dict is not null)
        {
            if (!dict.ContainsKey(Constants.Envelope.System)) dict[Constants.Envelope.System] = "flow";
            if (!dict.ContainsKey(Constants.Envelope.Adapter)) dict[Constants.Envelope.Adapter] = "actions";
        }
        recordType.GetProperty("StagePayload")!.SetValue(record, dict);

        var dataType = typeof(Data<,>).MakeGenericType(recordType, typeof(string));
        var upsert = dataType.GetMethod("UpsertAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { recordType, typeof(string), typeof(CancellationToken) })!;
        await (Task)upsert.Invoke(null, new object?[] { record, FlowSets.StageShort(FlowSets.Intake), ct })!;

        await new FlowAck(msg.Model, msg.Verb, msg.ReferenceId, "ok", null, msg.CorrelationId).Send(ct);
    }

    private static async Task HandleReportAsync(Type modelType, FlowAction msg, CancellationToken ct)
    {
        // Emit a simple stats snapshot for the model (counts per stage)
        var recordType = typeof(StageRecord<>).MakeGenericType(modelType);
        var intake = await CountAsync(recordType, FlowSets.StageShort(FlowSets.Intake), ct);
        var std = await CountAsync(recordType, FlowSets.StageShort(FlowSets.Standardized), ct);
        var keyed = await CountAsync(recordType, FlowSets.StageShort(FlowSets.Keyed), ct);

        var canType = typeof(CanonicalProjection<>).MakeGenericType(modelType);
        var linType = typeof(LineageProjection<>).MakeGenericType(modelType);
        var dynType = typeof(DynamicFlowEntity<>).MakeGenericType(modelType);
        var polType = typeof(PolicyState<>).MakeGenericType(modelType);

        var canCount = await CountAsync(canType, FlowSets.ViewShort(Constants.Views.Canonical), ct);
        var linCount = await CountAsync(linType, FlowSets.ViewShort(Constants.Views.Lineage), ct);
        var dynCount = await CountAsync(dynType, null, ct);
        var polCount = await CountAsync(polType, null, ct);

        var stats = new Dictionary<string, object?>
        {
            ["intake"] = intake,
            ["standardized"] = std,
            ["keyed"] = keyed,
            ["canonical"] = canCount,
            ["lineage"] = linCount,
            ["roots"] = dynCount,
            ["policies"] = polCount
        };
        await new FlowReport(msg.Model, msg.ReferenceId, stats, msg.CorrelationId).Send(ct);
    }

    private static async Task<int> CountAsync(Type entityType, string? set, CancellationToken ct)
    {
        var dataType = typeof(Data<,>).MakeGenericType(entityType, typeof(string));
        var page = dataType.GetMethod("FirstPage", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { typeof(int), typeof(CancellationToken) })!;
        using (DataSetContext.With(set))
        {
            var t = (Task)page.Invoke(null, new object?[] { 1, ct })!; await t.ConfigureAwait(false);
            var res = GetResult(t);
            var enumer = (System.Collections.IEnumerable)res!;
            int count = 0; foreach (var _ in enumer) count++;
            return count;
        }
    }

    private static IDictionary<string, object?>? ToDict(object? payload)
    {
        if (payload is null) return null;
        if (payload is IDictionary<string, object?> d) return d;
        if (payload is IDictionary<string, object> d2)
        {
            var conv = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in d2) conv[kv.Key] = kv.Value;
            return conv;
        }
        return new Dictionary<string, object?> { ["value"] = payload };
    }

    private static object? GetResult(Task t)
    {
        var type = t.GetType();
        if (type.IsGenericType) return type.GetProperty("Result")!.GetValue(t);
        return null;
    }
}
