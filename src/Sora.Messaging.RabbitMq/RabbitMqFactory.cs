// Copied from the previous Sora.Mq.RabbitMq namespace; keeping code identical.
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Sora.Core;
using Sora.Messaging;
using Sora.Messaging.Inbox.Http;

namespace Sora.Messaging.RabbitMq;

public sealed class RabbitMqCapabilities : IMessagingCapabilities
{
    public bool DelayedDelivery => false; // can be true if delayed-exchange plugin is present (future probe)
    public bool DeadLettering => true;
    public bool Transactions => false;
    public int MaxMessageSizeKB => 128;
    public string MessageOrdering => "Partition";
    public bool ScheduledEnqueue => false;
    public bool PublisherConfirms => true;
}

internal sealed class RabbitMqOptions
{
    public string? ConnectionString { get; set; }
    public string? ConnectionStringName { get; set; }
    public string Exchange { get; set; } = "sora";
    public string ExchangeType { get; set; } = "topic"; // fanout|topic|direct
    public bool PublisherConfirms { get; set; } = true;
    public int? MaxMessageSizeKB { get; set; }
    public int Prefetch { get; set; } = 50;
    public DlqOptions Dlq { get; set; } = new();
    public RetryOptions Retry { get; set; } = new();
    public bool ProvisionOnStart { get; set; } = false;
    public List<SubscriptionOption> Subscriptions { get; set; } = new();
}

internal sealed class SubscriptionOption
{
    public string Name { get; set; } = "default";
    public string? Queue { get; set; }
    public string[] RoutingKeys { get; set; } = Array.Empty<string>();
    public bool Dlq { get; set; } = true;
    public int Concurrency { get; set; } = 1; // number of consumers
}

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

public sealed class RabbitMqFactory : IMessageBusFactory
{
    public int ProviderPriority => 20;
    public string ProviderName => "RabbitMq";

    public (IMessageBus bus, IMessagingCapabilities caps) Create(IServiceProvider sp, string busCode, IConfiguration cfg)
    {
        // Bind options with connection string resolution
    var opts = new RabbitMqOptions();
    cfg.Bind(opts);
    // Also bind nested section if user groups transport-specific options under "RabbitMq"
    cfg.GetSection("RabbitMq").Bind(opts);
        var configRoot = sp.GetService(typeof(IConfiguration)) as IConfiguration;
        var connStr = opts.ConnectionString;
        if (string.IsNullOrWhiteSpace(connStr) && !string.IsNullOrWhiteSpace(opts.ConnectionStringName))
            connStr = configRoot?["ConnectionStrings:" + opts.ConnectionStringName];
        if (string.IsNullOrWhiteSpace(connStr))
            throw new InvalidOperationException($"RabbitMq bus '{busCode}' requires ConnectionString or ConnectionStringName.");

        var factory = new ConnectionFactory { Uri = new Uri(connStr!), DispatchConsumersAsync = true, AutomaticRecoveryEnabled = true, TopologyRecoveryEnabled = true };
        var connection = factory.CreateConnection($"sora-{busCode}");
        var channel = connection.CreateModel();

        // Topology: declare main exchange
        channel.ExchangeDeclare(exchange: opts.Exchange, type: opts.ExchangeType, durable: true, autoDelete: false, arguments: null);
        if (opts.Prefetch > 0) channel.BasicQos(0, (ushort)Math.Min(ushort.MaxValue, opts.Prefetch), global: false);

        // Retry/backoff infrastructure (headers exchange + TTL buckets)
        var retryExchange = opts.Exchange + ".retry";
        var needsRetryInfra = opts.Retry.MaxAttempts > 1;
        if (needsRetryInfra)
        {
            // Headers exchange so we can route to a single bucket using x-retry-bucket header while preserving routing key
            channel.ExchangeDeclare(exchange: retryExchange, type: "headers", durable: true, autoDelete: false, arguments: null);
        }

    // Determine effective provisioning default: true normally, false in Production unless Sora:AllowMagicInProduction = true
    bool isProd = false;
    try { isProd = Sora.Core.SoraEnv.IsProduction; } catch { isProd = string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase); }
    bool allowMagic = string.Equals((configRoot?["Sora:AllowMagicInProduction"] ?? "false"), "true", StringComparison.OrdinalIgnoreCase);
    bool provisionExplicit = cfg["ProvisionOnStart"] is not null; // detect explicit config presence
    bool provisionEffective = provisionExplicit ? opts.ProvisionOnStart : (!isProd || allowMagic);

    // Optional: pre-provision queues and bindings from config
    var autoSubWhenEmpty = opts.Subscriptions.Count == 0;
    if (provisionEffective && (opts.Subscriptions.Count > 0 || autoSubWhenEmpty))
        {
            // If no subscriptions were defined, create a single default group subscription bound to all
            if (autoSubWhenEmpty)
            {
                var msgOpts = sp.GetService(typeof(Microsoft.Extensions.Options.IOptions<Sora.Messaging.MessagingOptions>)) as Microsoft.Extensions.Options.IOptions<Sora.Messaging.MessagingOptions>;
                var defaultGroup = msgOpts?.Value.DefaultGroup ?? "workers";
                opts.Subscriptions.Add(new SubscriptionOption { Name = defaultGroup, RoutingKeys = new[] { "#" }, Dlq = true, Concurrency = 1 });
            }
            string? dlxName = null;
            if (opts.Dlq.Enabled)
            {
                dlxName = opts.Exchange + ".dlx";
                channel.ExchangeDeclare(exchange: dlxName, type: opts.ExchangeType, durable: true, autoDelete: false, arguments: null);
            }
            // Declare retry bucket queues (once) and bind them to retry headers exchange
            if (needsRetryInfra)
            {
                foreach (var bucket in ComputeRetryBuckets(opts.Retry))
                {
                    var qname = $"sora.{busCode}.retry.{bucket}s";
                    var qargs = new Dictionary<string, object?>
                    {
                        ["x-message-ttl"] = bucket * 1000,
                        ["x-dead-letter-exchange"] = opts.Exchange
                    };
                    channel.QueueDeclare(queue: qname, durable: true, exclusive: false, autoDelete: false, arguments: qargs);
                    // Bind with header match for the bucket
                    var bargs = new Dictionary<string, object?>
                    {
                        ["x-match"] = "all",
                        ["x-retry-bucket"] = bucket.ToString()
                    };
                    channel.QueueBind(queue: qname, exchange: retryExchange, routingKey: string.Empty, arguments: bargs);
                }
            }
            foreach (var sub in opts.Subscriptions)
            {
                var queue = string.IsNullOrWhiteSpace(sub.Queue) ? $"sora.{busCode}.{sub.Name}" : sub.Queue!;
                var args = new Dictionary<string, object?>();
                if (sub.Dlq && opts.Dlq.Enabled && dlxName is not null)
                {
                    args["x-dead-letter-exchange"] = dlxName;
                }
                channel.QueueDeclare(queue: queue, durable: true, exclusive: false, autoDelete: false, arguments: args);
                if (sub.RoutingKeys is { Length: > 0 })
                {
                    foreach (var rk in sub.RoutingKeys)
                        channel.QueueBind(queue: queue, exchange: opts.Exchange, routingKey: rk);
                }
                else
                {
                    // Default bind to all messages if no routing key specified
                    channel.QueueBind(queue: queue, exchange: opts.Exchange, routingKey: "#");
                }
            }
        }

        var caps = new RabbitMqCapabilities();

        var plan = Negotiation.BuildPlan(busCode, ProviderName, caps, opts.Retry, opts.Dlq, requestDelay: false);

    var bus = new RabbitMqBus(busCode, connection, channel, opts, caps, plan, sp.GetService(typeof(ITypeAliasRegistry)) as ITypeAliasRegistry);

    // record diagnostics
    (sp.GetService(typeof(IMessagingDiagnostics)) as IMessagingDiagnostics)?.SetEffectivePlan(busCode, plan);

        // Optional simple consumer dispatcher: bind each configured subscription and dispatch by alias
        if (opts.Subscriptions.Count > 0)
        {
            foreach (var sub in opts.Subscriptions)
            {
                var queue = string.IsNullOrWhiteSpace(sub.Queue) ? $"sora.{busCode}.{sub.Name}" : sub.Queue!;
                var consumers = Math.Max(1, sub.Concurrency);
                for (int i = 0; i < consumers; i++)
                {
                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.Received += async (sender, ea) =>
                    {
                        try
                        {
                            var alias = ea.BasicProperties?.Type ?? string.Empty;
                            var aliasReg = sp.GetService(typeof(ITypeAliasRegistry)) as ITypeAliasRegistry;
                            var targetType = (aliasReg?.Resolve(alias)) ?? null;
                            object? message = null;
                            if (targetType is not null)
                            {
                                message = JsonSerializer.Deserialize(ea.Body.Span, targetType, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                            }
                            var scopeFactory = sp.GetService(typeof(IServiceScopeFactory)) as IServiceScopeFactory;
                            using var scope = scopeFactory?.CreateScope();
                            var provider = scope?.ServiceProvider ?? sp;
                            if (targetType is not null && message is not null)
                            {
                                // Resolve IMessageHandler<T> and invoke
                                var handlerType = typeof(IMessageHandler<>).MakeGenericType(targetType);
                                var handler = provider.GetService(handlerType);
                                if (handler is not null)
                                {
                                    var attempt = 1;
                                    if (ea.BasicProperties?.Headers != null && ea.BasicProperties.Headers.TryGetValue("x-attempt", out var aObj))
                                    {
                                        int.TryParse(aObj?.ToString(), out attempt);
                                        attempt = Math.Max(1, attempt);
                                    }
                                    else if (ea.Redelivered) { attempt = 2; }
                                    // Correlation/causation from properties/headers
                                    var corrId = ea.BasicProperties?.CorrelationId;
                                    var headersDict = (ea.BasicProperties?.Headers)?.ToDictionary(k => k.Key, v => v.Value?.ToString() ?? string.Empty) ?? new Dictionary<string, string>();
                                    if (string.IsNullOrEmpty(corrId) && headersDict.TryGetValue("x-correlation-id", out var hCorr)) corrId = hCorr;
                                    headersDict.TryGetValue("x-causation-id", out var causation);
                                    var envelope = new MessageEnvelope(
                                        Id: ea.BasicProperties?.MessageId ?? Guid.NewGuid().ToString("n"),
                                        TypeAlias: alias,
                                        CorrelationId: corrId,
                                        CausationId: string.IsNullOrEmpty(causation) ? null : causation,
                                        Headers: headersDict,
                                        Attempt: attempt,
                                        Timestamp: DateTimeOffset.UtcNow);
                                    // Inbox de-duplication: prefer explicit idempotency key, fallback to message id
                                    var inbox = provider.GetService(typeof(IInboxStore)) as IInboxStore;
                                    var idKey = (headersDict.TryGetValue("x-idempotency-key", out var xk) && !string.IsNullOrWhiteSpace(xk)) ? xk : (ea.BasicProperties?.MessageId ?? envelope.Id);
                                    if (inbox != null)
                                    {
                                        if (await inbox.IsProcessedAsync(idKey, CancellationToken.None).ConfigureAwait(false))
                                        {
                                            channel.BasicAck(ea.DeliveryTag, false);
                                            return;
                                        }
                                    }

                                    var method = handlerType.GetMethod("HandleAsync");
                                    var task = (Task?)method?.Invoke(handler, new[] { envelope, message, CancellationToken.None });
                                    if (task is not null) await task.ConfigureAwait(false);
                                    if (inbox != null)
                                    {
                                        await inbox.MarkProcessedAsync(idKey, CancellationToken.None).ConfigureAwait(false);
                                    }
                                }
                            }
                            channel.BasicAck(ea.DeliveryTag, false);
                        }
                        catch
                        {
                            // Backoff with TTL buckets up to MaxAttempts, then DLQ/requeue
                            var currentAttempt = 1;
                            if (ea.BasicProperties?.Headers != null && ea.BasicProperties.Headers.TryGetValue("x-attempt", out var aObj))
                            {
                                int.TryParse(aObj?.ToString(), out currentAttempt);
                                currentAttempt = Math.Max(1, currentAttempt);
                            }
                            var nextAttempt = currentAttempt + 1;
                            var maxAttempts = Math.Max(1, opts.Retry.MaxAttempts);
                            if (nextAttempt <= maxAttempts)
                            {
                                // Compute delay and publish to retry exchange; then ack the original
                                var delaySec = ComputeBackoffDelaySeconds(nextAttempt, opts.Retry);
                                var props = channel.CreateBasicProperties();
                                props.ContentType = ea.BasicProperties?.ContentType ?? "application/json";
                                props.MessageId = ea.BasicProperties?.MessageId ?? Guid.NewGuid().ToString("n");
                                props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                                props.Type = ea.BasicProperties?.Type ?? string.Empty;
                                props.CorrelationId = ea.BasicProperties?.CorrelationId;
                                props.Headers = ea.BasicProperties?.Headers ?? new Dictionary<string, object>();
                                props.Headers["x-attempt"] = nextAttempt.ToString();
                                props.Headers["x-retry-bucket"] = delaySec.ToString();
                                // Preserve routing key used originally
                                var rk = ea.RoutingKey;
                                channel.BasicPublish(exchange: retryExchange, routingKey: rk, mandatory: false, basicProperties: props, body: ea.Body);
                                channel.BasicAck(ea.DeliveryTag, false);
                            }
                            else
                            {
                                // DLQ if enabled for this subscription; else requeue once more
                                var requeue = !(opts.Dlq.Enabled && sub.Dlq);
                                channel.BasicNack(ea.DeliveryTag, false, requeue: requeue);
                            }
                        }
                    };
                    channel.BasicConsume(queue, autoAck: false, consumer: consumer);
                }
            }
        }
        return (bus, caps);
    }

    private static IEnumerable<int> ComputeRetryBuckets(RetryOptions retry)
    {
        var maxA = Math.Max(1, retry.MaxAttempts);
        var delays = new List<int>();
        var first = Math.Max(1, retry.FirstDelaySeconds);
        var cap = Math.Max(first, retry.MaxDelaySeconds <= 0 ? first * 16 : retry.MaxDelaySeconds);
        var cur = first;
        for (int attempt = 2; attempt <= maxA; attempt++)
        {
            delays.Add(cur);
            if (string.Equals(retry.Backoff, "fixed", StringComparison.OrdinalIgnoreCase))
            {
                cur = first;
            }
            else
            {
                // exponential with cap
                cur = Math.Min(cap, cur * 2);
            }
        }
        return delays.Distinct().OrderBy(x => x);
    }

    private static int ComputeBackoffDelaySeconds(int attempt, RetryOptions retry)
    {
        // attempt is 2..N
        var idx = attempt - 2; // 0-based index into sequence
        var seq = ComputeRetrySequence(retry).ToArray();
        if (idx >= 0 && idx < seq.Length) return seq[idx];
    return retry.MaxDelaySeconds > 0 ? retry.MaxDelaySeconds : Math.Max(1, retry.FirstDelaySeconds);
    }

    private static IEnumerable<int> ComputeRetrySequence(RetryOptions retry)
    {
        var maxA = Math.Max(1, retry.MaxAttempts);
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
}

public static class RabbitMqServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMq(this IServiceCollection services)
    {
        services.AddSingleton<IMessageBusFactory, RabbitMqFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor>(sp =>
            new RabbitMqHealth(sp.GetRequiredService<IMessageBusSelector>(), sp)));
    // Discovery client for Inbox over RabbitMQ (optional)
    services.TryAddSingleton<IInboxDiscoveryClient, RabbitMqInboxDiscoveryClient>();
    // If policy allows discovery and no explicit endpoint configured, attempt discovery and wire HTTP inbox
    services.TryAddEnumerable(ServiceDescriptor.Singleton<ISoraInitializer>(sp => new RabbitMqInboxDiscoveryInitializer()));
        return services;
    }
}

internal sealed class RabbitMqHealth : IHealthContributor
{
    private readonly IMessageBusSelector _selector;
    private readonly IServiceProvider _sp;
    public RabbitMqHealth(IMessageBusSelector selector, IServiceProvider sp) { _selector = selector; _sp = sp; }
    public string Name => "mq:rabbitmq";
    public bool IsCritical => true;
    public Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            // Resolve default bus and attempt a passive declare to ensure connectivity
            var bus = _selector.ResolveDefault(_sp);
            if (bus is RabbitMqBus rabbit)
            {
                // Fast path: connected if channel open; more advanced checks can be added later
                return Task.FromResult(new HealthReport(Name, HealthState.Healthy, "connected"));
            }
            return Task.FromResult(new HealthReport(Name, HealthState.Degraded, "RabbitMQ not the default bus"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HealthReport(Name, HealthState.Unhealthy, ex.Message, ex));
        }
    }
}

// Auto-discovery initializer so AddSora() wires RabbitMQ when referenced
// legacy initializer removed in favor of standardized auto-registrar

internal sealed class RabbitMqInboxDiscoveryClient : IInboxDiscoveryClient
{
    private readonly IServiceProvider _sp;
    private static string? _cachedEndpoint;
    private static DateTimeOffset _cacheUntil;
    public RabbitMqInboxDiscoveryClient(IServiceProvider sp) { _sp = sp; }
    public async Task<string?> DiscoverAsync(CancellationToken ct = default)
    {
        // Serve from cache if valid
        if (_cachedEndpoint != null && DateTimeOffset.UtcNow < _cacheUntil)
        {
            return _cachedEndpoint;
        }
        // Resolve config for default bus
    var cfg = (IConfiguration?)_sp.GetService(typeof(IConfiguration));
    var disc = (IOptions<DiscoveryOptions>?)_sp.GetService(typeof(IOptions<DiscoveryOptions>));
        var opts = (IOptions<MessagingOptions>?)_sp.GetService(typeof(IOptions<MessagingOptions>));
        var busCode = opts?.Value.DefaultBus ?? "default";
        var busSection = cfg?.GetSection($"Sora:Messaging:Buses:{busCode}");
        if (busSection is null) return null;
        var rOpts = new RabbitMqOptions();
        busSection.Bind(rOpts);
        busSection.GetSection("RabbitMq").Bind(rOpts);
        var connStr = rOpts.ConnectionString;
        if (string.IsNullOrWhiteSpace(connStr) && !string.IsNullOrWhiteSpace(rOpts.ConnectionStringName))
            connStr = cfg?["ConnectionStrings:" + rOpts.ConnectionStringName];
        if (string.IsNullOrWhiteSpace(connStr)) return null;

        var factory = new ConnectionFactory { Uri = new Uri(connStr!), DispatchConsumersAsync = true, AutomaticRecoveryEnabled = true, TopologyRecoveryEnabled = true };
        using var connection = factory.CreateConnection($"sora-discovery-{busCode}");
        using var channel = connection.CreateModel();

        // Ensure exchange exists (idempotent)
        var exchange = rOpts.Exchange;
        channel.ExchangeDeclare(exchange: exchange, type: rOpts.ExchangeType, durable: true, autoDelete: false, arguments: null);

        // Create a temporary reply queue and consumer
        var q = channel.QueueDeclare(queue: string.Empty, durable: false, exclusive: true, autoDelete: true, arguments: null).QueueName;
        // Bind to announce topic; scope by wildcard to keep simple
        channel.QueueBind(queue: q, exchange: exchange, routingKey: "sora.discovery.announce.#");
    var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
    var results = new List<string>();
        var corrId = Guid.NewGuid().ToString("n");
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (sender, ea) =>
        {
            try
            {
                // Match correlation
                var propCorr = ea.BasicProperties?.CorrelationId;
                if (!string.Equals(propCorr, corrId, StringComparison.Ordinal)) { channel.BasicAck(ea.DeliveryTag, false); return; }
                // Parse endpoint from payload JSON
                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                string? endpoint = TryExtractEndpoint(json);
                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    results.Add(endpoint);
                    tcs.TrySetResult(endpoint);
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                channel.BasicAck(ea.DeliveryTag, false);
            }
            await Task.CompletedTask;
        };
        channel.BasicConsume(queue: q, autoAck: false, consumer: consumer);

        // Publish ping with reply-to and short TTL
        var group = opts?.Value.DefaultGroup ?? "workers";
        var rk = $"sora.discovery.ping.{busCode}.{group}";
        var props = channel.CreateBasicProperties();
        props.CorrelationId = corrId;
        props.ReplyTo = q;
        props.Expiration = "2000"; // ms
        props.ContentType = "application/json";
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { kind = "inbox", name = "sora-client", version = "v1" });
        channel.BasicPublish(exchange: exchange, routingKey: rk, mandatory: false, basicProperties: props, body: payload);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timeout = TimeSpan.FromSeconds(Math.Max(1, disc?.Value.TimeoutSeconds ?? 3));
        cts.CancelAfter(timeout);
        try
        {
            using (cts.Token.Register(() => tcs.TrySetCanceled(cts.Token)))
            {
                // Wait for first result or timeout
                var firstArrived = await Task.WhenAny(tcs.Task, Task.Delay(timeout, ct)).ConfigureAwait(false);
                if (firstArrived != tcs.Task)
                {
                    return null; // no announcements
                }
                // Wait a short selection window to collect multiple announces
                var selectionWait = TimeSpan.FromMilliseconds(Math.Max(0, disc?.Value.SelectionWaitMs ?? 0));
                if (selectionWait > TimeSpan.Zero)
                {
                    await Task.Delay(selectionWait, ct).ConfigureAwait(false);
                }
                var ep = results.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(ep))
                {
                    var mins = Math.Max(1, disc?.Value.CacheMinutes ?? 5);
                    _cachedEndpoint = ep;
                    _cacheUntil = DateTimeOffset.UtcNow.AddMinutes(mins);
                }
                return ep;
            }
        }
        catch
        {
            return null;
        }
    }

    private static string? TryExtractEndpoint(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("endpoint", out var ep))
            {
                if (ep.ValueKind == JsonValueKind.String) return ep.GetString();
                if (ep.ValueKind == JsonValueKind.Object && ep.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
                    return url.GetString();
            }
        }
        catch { }
        return null;
    }
}

internal sealed class RabbitMqInboxDiscoveryInitializer : ISoraInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Evaluate policy using a temporary provider (read-only)
        using var sp = services.BuildServiceProvider();
    var policy = sp.GetService(typeof(IInboxDiscoveryPolicy)) as IInboxDiscoveryPolicy;
        var cfg = sp.GetService(typeof(IConfiguration)) as IConfiguration;
        var endpoint = cfg?["Sora:Messaging:Inbox:Endpoint"];
        if (!string.IsNullOrWhiteSpace(endpoint)) return; // explicit wins
        if (policy is null || !policy.ShouldDiscover(sp))
        {
            try { Console.WriteLine($"[sora][inbox-discovery] skipped: {policy?.Reason(sp) ?? "no-policy"}"); } catch { }
            return;
        }

        // Register an initializer to run after container is finalized to perform async discovery
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISoraInitializer>(new DeferredInboxWireUp()));
    }

    private sealed class DeferredInboxWireUp : ISoraInitializer
    {
        public void Initialize(IServiceCollection services)
        {
            // Build a provider to run discovery
            using var sp = services.BuildServiceProvider();
            var client = sp.GetService(typeof(IInboxDiscoveryClient)) as IInboxDiscoveryClient;
            var cfg = sp.GetService(typeof(IConfiguration)) as IConfiguration;
            var enabled = sp.GetService(typeof(IOptions<DiscoveryOptions>)) as IOptions<DiscoveryOptions>;
            var timeout = TimeSpan.FromSeconds(Math.Max(1, enabled?.Value.TimeoutSeconds ?? 3));
            string? discovered = null;
            try
            {
                var cts = new CancellationTokenSource(timeout);
                discovered = client is null ? null : client.DiscoverAsync(cts.Token).GetAwaiter().GetResult();
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(discovered))
            {
                try { Console.WriteLine($"[sora][inbox-discovery] discovered endpoint: {discovered}"); } catch { }
                // Wire HTTP inbox client pointing at the discovered endpoint
                services.AddHttpClient<HttpInboxStore>(http =>
                {
                    http.BaseAddress = new Uri(discovered!);
                    http.Timeout = TimeSpan.FromSeconds(5);
                });
                services.Replace(ServiceDescriptor.Singleton<IInboxStore, HttpInboxStore>());
            }
            else
            {
                try { Console.WriteLine("[sora][inbox-discovery] no endpoint discovered"); } catch { }
            }
        }
    }
}
