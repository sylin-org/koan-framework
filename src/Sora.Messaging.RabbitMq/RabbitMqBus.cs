using RabbitMQ.Client;
using Sora.Messaging.Infrastructure;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Sora.Messaging.RabbitMq;

internal sealed class RabbitMqBus : IMessageBus, IDisposable
{
    private readonly string _busCode;
    private readonly IConnection _conn;
    private readonly IModel _channel;
    private readonly RabbitMqOptions _opts;
    private readonly IMessagingCapabilities _caps;
    private readonly EffectiveMessagingPlan _plan;
    private readonly JsonSerializerSettings _json;
    private readonly ITypeAliasRegistry? _aliases;

    public RabbitMqBus(string busCode, IConnection conn, IModel channel, RabbitMqOptions opts, IMessagingCapabilities caps, EffectiveMessagingPlan plan, ITypeAliasRegistry? aliases)
    {
        _busCode = busCode; _conn = conn; _channel = channel; _opts = opts; _caps = caps; _plan = plan; _aliases = aliases;
    _json = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver(), Formatting = Formatting.None };
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
            var partitionSuffix = MessageMeta.ResolvePartitionSuffix(type, m);
            var routingKey = (alias + partitionSuffix).Replace(' ', '.');
            var json = JsonConvert.SerializeObject(m, _json);
            var body = System.Text.Encoding.UTF8.GetBytes(json);

            if (_opts.MaxMessageSizeKB is int maxKb && body.Length > maxKb * 1024)
                throw new InvalidOperationException($"Message exceeds MaxMessageSizeKB ({_opts.MaxMessageSizeKB} KB).");

            var props = _channel.CreateBasicProperties();
            props.ContentType = "application/json";
            props.MessageId = Guid.NewGuid().ToString("n");
            props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            props.Type = alias;
            // Promote [Header] properties
            props.Headers = MessageMeta.ExtractHeaders(type, m);
            // Promote [IdempotencyKey] property when present
            var idk = MessageMeta.ResolveIdempotencyKey(type, m);
            if (!string.IsNullOrEmpty(idk))
            {
                props.Headers ??= new Dictionary<string, object>();
                props.Headers[HeaderNames.IdempotencyKey] = idk!;
            }
            // Correlation/Causation: map x-correlation-id header to AMQP CorrelationId; set x-causation-id to MessageId when absent
            if (props.Headers != null && props.Headers.TryGetValue(HeaderNames.CorrelationId, out var corr) && props.CorrelationId is null)
            {
                props.CorrelationId = corr?.ToString();
            }
            if (props.Headers != null && !props.Headers.ContainsKey(HeaderNames.CausationId))
            {
                props.Headers[HeaderNames.CausationId] = props.MessageId;
            }

            // Scheduled delivery fallback via TTL buckets when [DelaySeconds] present
            var delay = MessageMeta.ResolveDelaySeconds(type, m);
            if (delay > 0)
            {
                var bucket = RetryMath.ChooseBucket(delay, _opts.Retry);
                props.Headers ??= new Dictionary<string, object>();
                props.Headers[HeaderNames.Attempt] = "1";
                props.Headers[HeaderNames.RetryBucket] = bucket.ToString();
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

    // Extracted to MessageMeta in Core

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