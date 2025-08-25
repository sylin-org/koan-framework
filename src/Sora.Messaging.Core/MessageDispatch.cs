using System.Text.Json;

namespace Sora.Messaging;

public enum DispatchResultKind { Success, DuplicateSkipped, NoHandler, DeserializationSkipped, Failure }

public sealed record DispatchOutcome(DispatchResultKind Kind, int Attempt, string IdempotencyKey);

// Central consumer dispatcher: builds envelope, de-dups, resolves handler, and invokes it.
public static class MessageDispatch
{
    public static async Task<DispatchOutcome> DispatchAsync(
        IServiceProvider sp,
        string alias,
        ReadOnlyMemory<byte> body,
        IDictionary<string, object>? rawHeaders,
        string? messageId,
        string? correlationId,
        bool redelivered,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken ct = default)
    {
        var attempt = 1;
        if (rawHeaders != null && rawHeaders.TryGetValue(Infrastructure.HeaderNames.Attempt, out var aObj))
        {
            int.TryParse(aObj?.ToString(), out attempt);
            attempt = Math.Max(1, attempt);
        }
        else if (redelivered) { attempt = 2; }

        var aliasReg = sp.GetService(typeof(ITypeAliasRegistry)) as ITypeAliasRegistry;
        var targetType = (aliasReg?.Resolve(alias)) ?? null;
        if (targetType is null)
        {
            return new DispatchOutcome(DispatchResultKind.DeserializationSkipped, attempt, messageId ?? string.Empty);
        }

        object? message = null;
        try
        {
            message = JsonSerializer.Deserialize(body.Span, targetType, jsonOptions ?? new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
        catch
        {
            return new DispatchOutcome(DispatchResultKind.DeserializationSkipped, attempt, messageId ?? string.Empty);
        }
        if (message is null)
            return new DispatchOutcome(DispatchResultKind.DeserializationSkipped, attempt, messageId ?? string.Empty);

        // Convert headers to string dict for the envelope
        var headersDict = new Dictionary<string, string>();
        if (rawHeaders != null)
        {
            foreach (var kv in rawHeaders)
                headersDict[kv.Key] = kv.Value?.ToString() ?? string.Empty;
        }
        var corrId = correlationId;
        if (string.IsNullOrEmpty(corrId) && headersDict.TryGetValue(Infrastructure.HeaderNames.CorrelationId, out var hCorr))
            corrId = hCorr;
        headersDict.TryGetValue(Infrastructure.HeaderNames.CausationId, out var causation);

        var envelope = new MessageEnvelope(
            Id: messageId ?? Guid.NewGuid().ToString("n"),
            TypeAlias: alias,
            CorrelationId: corrId,
            CausationId: string.IsNullOrEmpty(causation) ? null : causation,
            Headers: headersDict,
            Attempt: attempt,
            Timestamp: DateTimeOffset.UtcNow);

        // Inbox de-dup
        var inbox = sp.GetService(typeof(IInboxStore)) as IInboxStore;
        var idKey = (headersDict.TryGetValue(Infrastructure.HeaderNames.IdempotencyKey, out var xk) && !string.IsNullOrWhiteSpace(xk))
            ? xk : (messageId ?? envelope.Id);
        if (inbox != null)
        {
            if (await inbox.IsProcessedAsync(idKey, ct).ConfigureAwait(false))
                return new DispatchOutcome(DispatchResultKind.DuplicateSkipped, attempt, idKey);
        }

        // Resolve handler and invoke
        var handlerType = typeof(IMessageHandler<>).MakeGenericType(targetType);
        var handler = sp.GetService(handlerType);
        if (handler is null)
        {
            return new DispatchOutcome(DispatchResultKind.NoHandler, attempt, idKey);
        }

        try
        {
            var method = handlerType.GetMethod("HandleAsync");
            var task = (Task?)method?.Invoke(handler, new[] { envelope, message, ct });
            if (task is not null) await task.ConfigureAwait(false);
            if (inbox != null)
            {
                await inbox.MarkProcessedAsync(idKey, ct).ConfigureAwait(false);
            }
            return new DispatchOutcome(DispatchResultKind.Success, attempt, idKey);
        }
        catch
        {
            return new DispatchOutcome(DispatchResultKind.Failure, attempt, idKey);
        }
    }
}
