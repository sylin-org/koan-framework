using System.Reflection;
using System.Text.Json;
using RabbitMQ.Client;

namespace Sora.Messaging.RabbitMq;

internal sealed class RabbitMqBus : IMessageBus, IDisposable
{
    private readonly string _busCode;
    private readonly IConnection _conn;
    private readonly IModel _channel;
    private readonly RabbitMqOptions _opts;
    private readonly IMessagingCapabilities _caps;
    private readonly EffectiveMessagingPlan _plan;
    private readonly JsonSerializerOptions _json;
    private readonly ITypeAliasRegistry? _aliases;

    public RabbitMqBus(string busCode, IConnection conn, IModel channel, RabbitMqOptions opts, IMessagingCapabilities caps, EffectiveMessagingPlan plan, ITypeAliasRegistry? aliases)
    {
        _busCode = busCode; _conn = conn; _channel = channel; _opts = opts; _caps = caps; _plan = plan; _aliases = aliases;
        _json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
        if (_opts.PublisherConfirms)
        {
            _channel.ConfirmSelect();
        }
    }

    public Task SendAsync(object message, CancellationToken ct = default)
        => SendManyAsync(new[] { message }, ct);

    public Task SendManyAsync(IEnumerable<object> messages, CancellationToken ct = default)
    {
        var exchange = _opts.Exchange;
        var retryExchange = _opts.Exchange + ".retry";
        foreach (var m in messages)
        {
            ct.ThrowIfCancellationRequested();
            var type = m.GetType();
            var alias = _aliases?.GetAlias(type) ?? type.FullName ?? type.Name;
            var partitionSuffix = ResolvePartitionSuffix(type, m);
            var routingKey = (alias + partitionSuffix).Replace(' ', '.');
            var body = JsonSerializer.SerializeToUtf8Bytes(m, type, _json);

            if (_opts.MaxMessageSizeKB is int maxKb && body.Length > maxKb * 1024)
                throw new InvalidOperationException($"Message exceeds MaxMessageSizeKB ({_opts.MaxMessageSizeKB} KB).");

            var props = _channel.CreateBasicProperties();
            props.ContentType = "application/json";
            props.MessageId = Guid.NewGuid().ToString("n");
            props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            props.Type = alias;
            // Promote [Header] properties
            props.Headers = ExtractHeaders(type, m);
            // Promote [IdempotencyKey] property when present
            var idk = ResolveIdempotencyKey(type, m);
            if (!string.IsNullOrEmpty(idk))
            {
                props.Headers ??= new Dictionary<string, object>();
                props.Headers["x-idempotency-key"] = idk!;
            }
            // Correlation/Causation: map x-correlation-id header to AMQP CorrelationId; set x-causation-id to MessageId when absent
            if (props.Headers != null && props.Headers.TryGetValue("x-correlation-id", out var corr) && props.CorrelationId is null)
            {
                props.CorrelationId = corr?.ToString();
            }
            if (props.Headers != null && !props.Headers.ContainsKey("x-causation-id"))
            {
                props.Headers["x-causation-id"] = props.MessageId;
            }

            // Scheduled delivery fallback via TTL buckets when [DelaySeconds] present
            var delay = ResolveDelaySeconds(type, m);
            if (delay > 0)
            {
                var bucket = ChooseRetryBucket(delay, _opts.Retry);
                props.Headers ??= new Dictionary<string, object>();
                props.Headers["x-attempt"] = "1";
                props.Headers["x-retry-bucket"] = bucket.ToString();
                _channel.BasicPublish(exchange: retryExchange, routingKey: routingKey, mandatory: false, basicProperties: props, body: body);
            }
            else
            {
                _channel.BasicPublish(exchange: exchange, routingKey: routingKey, mandatory: false, basicProperties: props, body: body);
            }
            if (_opts.PublisherConfirms)
            {
                // Wait for broker ack; small timeout to avoid hanging the publisher
                if (!_channel.WaitForConfirms(TimeSpan.FromSeconds(5)))
                    throw new InvalidOperationException("RabbitMQ did not confirm publish within timeout.");
            }
        }

        return Task.CompletedTask;
    }

    private static string ResolvePartitionSuffix(Type type, object message)
    {
        var partProp = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(p => p.GetCustomAttribute<PartitionKeyAttribute>(inherit: true) != null);
        if (partProp is null) return string.Empty;
        var value = partProp.GetValue(message)?.ToString();
        if (string.IsNullOrEmpty(value)) return string.Empty;
        // simple stable hash to a small shard space
        var hash = Math.Abs(value.GetHashCode()) % 16;
        return $".p{hash}";
    }

    private static IDictionary<string, object> ExtractHeaders(Type type, object message)
    {
        var dict = new Dictionary<string, object>();
        foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var h = p.GetCustomAttribute<HeaderAttribute>(inherit: true);
            if (h is null) continue;
            // Do not promote sensitive properties
            var isSensitive = p.GetCustomAttribute<SensitiveAttribute>(inherit: true) != null;
            if (isSensitive) continue;
            var val = p.GetValue(message);
            if (val is null) continue;
            // Simple string conversion; could extend for other primitives
            dict[h.Name] = val.ToString() ?? string.Empty;
        }
        return dict;
    }

    private static int ResolveDelaySeconds(Type type, object message)
    {
        foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var d = p.GetCustomAttribute<DelaySecondsAttribute>(inherit: true);
            if (d is null) continue;
            var val = p.GetValue(message);
            if (val is null) continue;
            if (int.TryParse(val.ToString(), out var seconds) && seconds > 0) return seconds;
        }
        return 0;
    }

    private static string? ResolveIdempotencyKey(Type type, object message)
    {
        foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var d = p.GetCustomAttribute<IdempotencyKeyAttribute>(inherit: true);
            if (d is null) continue;
            var val = p.GetValue(message);
            var s = val?.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }

    private static int ChooseRetryBucket(int requestedSeconds, RetryOptions retry)
    {
        var seq = ComputeRetrySequenceLocal(retry).ToArray();
        if (seq.Length == 0) return requestedSeconds; // no buckets declared; publish as-is (may require infra)
        foreach (var s in seq)
            if (s >= requestedSeconds) return s;
        return seq[^1];
    }

    private static IEnumerable<int> ComputeRetrySequenceLocal(RetryOptions retry)
    {
        var maxA = Math.Max(2, retry.MaxAttempts);
        var first = Math.Max(1, retry.FirstDelaySeconds);
        var cap = Math.Max(first, retry.MaxDelaySeconds <= 0 ? first * 16 : retry.MaxDelaySeconds);
        var cur = first;
        for (int attempt = 2; attempt <= maxA; attempt++)
        {
            yield return cur;
            if (string.Equals(retry.Backoff, "fixed", StringComparison.OrdinalIgnoreCase))
                cur = first;
            else
                cur = Math.Min(cap, cur * 2);
        }
    }

    public void Dispose()
    {
        try { _channel.Close(); _channel.Dispose(); } catch { }
        try { _conn.Close(); _conn.Dispose(); } catch { }
    }
}