// Copied from the previous Sora.Mq.RabbitMq namespace; keeping code identical.
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Sora.Core;
using System.Text.Json;

namespace Sora.Messaging.RabbitMq;

public sealed class RabbitMqFactory : IMessageBusFactory
{
    public int ProviderPriority => 20;
    public string ProviderName => "RabbitMq";

    public (IMessageBus bus, IMessagingCapabilities caps) Create(IServiceProvider sp, string busCode, IConfiguration cfg)
    {
        // Bind options with connection string resolution (prefer explicit keys via helper)
        var opts = new RabbitMqOptions();
        // Exchange
        opts.Exchange = Configuration.Read<string?>(cfg, "RabbitMq:Exchange", null)
            ?? Configuration.Read<string?>(cfg, "Exchange", null)
            ?? opts.Exchange;
        // ExchangeType
        opts.ExchangeType = Configuration.Read<string?>(cfg, "RabbitMq:ExchangeType", null)
            ?? Configuration.Read<string?>(cfg, "ExchangeType", null)
            ?? opts.ExchangeType;
        // PublisherConfirms
        opts.PublisherConfirms = Configuration.Read(cfg, "RabbitMq:PublisherConfirms", opts.PublisherConfirms);
        // Prefetch
        opts.Prefetch = Configuration.Read(cfg, "RabbitMq:Prefetch", opts.Prefetch);
        // DLQ
        opts.Dlq.Enabled = Configuration.Read(cfg, "RabbitMq:Dlq:Enabled", opts.Dlq.Enabled);
        // Retry
        opts.Retry.MaxAttempts = Math.Max(1, Configuration.Read(cfg, "RabbitMq:Retry:MaxAttempts", opts.Retry.MaxAttempts));
        opts.Retry.FirstDelaySeconds = Math.Max(1, Configuration.Read(cfg, "RabbitMq:Retry:FirstDelaySeconds", opts.Retry.FirstDelaySeconds));
        opts.Retry.Backoff = Configuration.Read<string?>(cfg, "RabbitMq:Retry:Backoff", opts.Retry.Backoff) ?? opts.Retry.Backoff;
        opts.Retry.MaxDelaySeconds = Configuration.Read(cfg, "RabbitMq:Retry:MaxDelaySeconds", opts.Retry.MaxDelaySeconds);
        // ProvisionOnStart
        opts.ProvisionOnStart = Configuration.Read(cfg, "RabbitMq:ProvisionOnStart", opts.ProvisionOnStart);
        // Subscriptions (minimal: read comma separated routing keys for default group if provided)
        // Keep existing list if already populated by other means
        if (opts.Subscriptions.Count == 0)
        {
            var rkCsv = Configuration.Read<string?>(cfg, "RabbitMq:Subscriptions:0:RoutingKeys", null);
            var name = Configuration.Read<string?>(cfg, "RabbitMq:Subscriptions:0:Name", null);
            var queue = Configuration.Read<string?>(cfg, "RabbitMq:Subscriptions:0:Queue", null);
            var dlq = Configuration.Read(cfg, "RabbitMq:Subscriptions:0:Dlq", true);
            var concurrency = Math.Max(1, Configuration.Read(cfg, "RabbitMq:Subscriptions:0:Concurrency", 1));
            if (!string.IsNullOrWhiteSpace(rkCsv) || !string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(queue))
            {
                opts.Subscriptions.Add(new SubscriptionOption
                {
                    Name = string.IsNullOrWhiteSpace(name) ? "default" : name!,
                    Queue = queue,
                    RoutingKeys = string.IsNullOrWhiteSpace(rkCsv) ? Array.Empty<string>() : rkCsv!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                    Dlq = dlq,
                    Concurrency = concurrency
                });
            }
        }

        var configRoot = sp.GetService(typeof(IConfiguration)) as IConfiguration;
        // Connection string resolution
        var connStr = Configuration.Read<string?>(cfg, "RabbitMq:ConnectionString", null)
            ?? Configuration.Read<string?>(cfg, "ConnectionString", null)
            ?? opts.ConnectionString;
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
        try { isProd = SoraEnv.IsProduction; }
        catch { isProd = string.Equals(Configuration.ReadFirst(null, Core.Infrastructure.Constants.Configuration.Env.AspNetCoreEnvironment, Core.Infrastructure.Constants.Configuration.Env.DotnetEnvironment) ?? string.Empty, "Production", StringComparison.OrdinalIgnoreCase); }
        bool allowMagic = Configuration.Read(configRoot, Core.Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction, false);
        bool provisionExplicit = cfg["ProvisionOnStart"] is not null; // detect explicit config presence
        bool provisionEffective = provisionExplicit ? opts.ProvisionOnStart : (!isProd || allowMagic);

        // Optional: pre-provision queues and bindings from config
        var autoSubWhenEmpty = opts.Subscriptions.Count == 0;
        if (provisionEffective && (opts.Subscriptions.Count > 0 || autoSubWhenEmpty))
        {
            // If no subscriptions were defined, create a single default group subscription bound to all
            if (autoSubWhenEmpty)
            {
                var msgOpts = sp.GetService(typeof(IOptions<MessagingOptions>)) as IOptions<MessagingOptions>;
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

// Auto-discovery initializer so AddSora() wires RabbitMQ when referenced
// legacy initializer removed in favor of standardized auto-registrar