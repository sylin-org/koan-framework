// Copied from the previous Sora.Mq.RabbitMq namespace; keeping code identical.
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Sora.Core;
using Sora.Messaging;
using Sora.Messaging.Infrastructure;
using Sora.Messaging.Provisioning;
using Sora.Messaging.RabbitMq.Provisioning;
using System.Text;

namespace Sora.Messaging.RabbitMq;

public sealed class RabbitMqFactory : IMessageBusFactory
{
    public int ProviderPriority => 20;
    public string ProviderName => "RabbitMq";

    public (IMessageBus bus, IMessagingCapabilities caps) Create(IServiceProvider sp, string busCode, IConfiguration cfg)
    {
        // Bind options with connection string resolution (prefer explicit keys via helper)
        var opts = new RabbitMqOptions();
        // ConnectionStringName (optional indirection to root ConnectionStrings)
        opts.ConnectionStringName = Configuration.Read<string?>(cfg, "RabbitMq:ConnectionStringName", opts.ConnectionStringName)
            ?? Configuration.Read<string?>(cfg, "ConnectionStringName", opts.ConnectionStringName);
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
        // ProvisionMode (optional explicit override)
        var modeStr = Configuration.Read<string?>(cfg, "RabbitMq:ProvisionMode", null) ?? Configuration.Read<string?>(cfg, "ProvisionMode", null);
        if (!string.IsNullOrWhiteSpace(modeStr) && Enum.TryParse<ProvisioningMode>(modeStr, ignoreCase: true, out var parsedMode))
        {
            opts.ProvisionMode = parsedMode;
        }
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
        // keep on options for inspector parsing
        opts.ConnectionString = connStr;

        // Optional management overrides
        opts.ManagementUrl = Configuration.Read<string?>(cfg, "RabbitMq:ManagementUrl", opts.ManagementUrl)
            ?? Configuration.Read<string?>(cfg, "ManagementUrl", opts.ManagementUrl);
        opts.ManagementUsername = Configuration.Read<string?>(cfg, "RabbitMq:ManagementUsername", opts.ManagementUsername)
            ?? Configuration.Read<string?>(cfg, "ManagementUsername", opts.ManagementUsername);
        opts.ManagementPassword = Configuration.Read<string?>(cfg, "RabbitMq:ManagementPassword", opts.ManagementPassword)
            ?? Configuration.Read<string?>(cfg, "ManagementPassword", opts.ManagementPassword);

        var factory = new ConnectionFactory { Uri = new Uri(connStr!), DispatchConsumersAsync = true, AutomaticRecoveryEnabled = true, TopologyRecoveryEnabled = true };
        var connection = factory.CreateConnection($"sora-{busCode}");
        var channel = connection.CreateModel();

        if (opts.Prefetch > 0) channel.BasicQos(0, (ushort)Math.Min(ushort.MaxValue, opts.Prefetch), global: false);

        // Determine effective provisioning default: true normally, false in Production unless Sora:AllowMagicInProduction = true
        bool isProd = false;
        try { isProd = SoraEnv.IsProduction; }
        catch { isProd = string.Equals(Configuration.ReadFirst(null, Core.Infrastructure.Constants.Configuration.Env.AspNetCoreEnvironment, Core.Infrastructure.Constants.Configuration.Env.DotnetEnvironment) ?? string.Empty, "Production", StringComparison.OrdinalIgnoreCase); }
        bool allowMagic = Configuration.Read(configRoot, Core.Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction, false);
        // Provisioning mode selection per MESS-0028
        ProvisioningMode mode;
        if (opts.ProvisionMode.HasValue)
        {
            mode = opts.ProvisionMode.Value;
        }
        else
        {
            var provisionExplicit = cfg["ProvisionOnStart"] is not null; // detect explicit boolean
            var provisionEffective = provisionExplicit ? opts.ProvisionOnStart : (!isProd || allowMagic);
            mode = provisionEffective ? ProvisioningMode.CreateIfMissing : ProvisioningMode.Off;
        }
        if (mode == ProvisioningMode.ForceRecreate && isProd && !allowMagic)
        {
            // Guard ForceRecreate in production without override
            mode = ProvisioningMode.Off;
        }

        // Provisioning: plan/inspect/diff/apply
        var msgOptsAccessor = sp.GetService(typeof(IOptions<MessagingOptions>)) as IOptions<MessagingOptions>;
        var msgOpts = msgOptsAccessor?.Value ?? new MessagingOptions();
        var provisioner = new RabbitMqProvisioner();
        var desired = provisioner.Plan(busCode, msgOpts.DefaultGroup, opts, new RabbitMqCapabilities(), sp.GetService(typeof(ITypeAliasRegistry)) as ITypeAliasRegistry);
        CurrentTopology current = new(Array.Empty<ExchangeSpec>(), Array.Empty<QueueSpec>(), Array.Empty<BindingSpec>());
        if (mode != ProvisioningMode.Off && mode != ProvisioningMode.CreateIfMissing)
        {
            current = provisioner.Inspect(busCode, (connection, channel, opts));
        }
        var diff = provisioner.Diff(desired, current);
        var diagSvc = sp.GetService(typeof(IMessagingDiagnostics)) as IMessagingDiagnostics;
        if (mode == ProvisioningMode.DryRun)
        {
            // Record effective plan and provisioning diagnostics; no apply
            var capsTmp = new RabbitMqCapabilities();
            var planTmp = Negotiation.BuildPlan(busCode, ProviderName, capsTmp, opts.Retry, opts.Dlq, requestDelay: false);
            diagSvc?.SetEffectivePlan(busCode, planTmp);
            diagSvc?.SetProvisioning(busCode, new ProvisioningDiagnostics(
                BusCode: busCode,
                Provider: ProviderName,
                Mode: mode,
                Desired: desired,
                Current: current,
                Diff: diff,
                Timestamp: DateTimeOffset.UtcNow));
        }
        else
        {
            provisioner.Apply(busCode, mode, diff, (connection, channel, opts));
            diagSvc?.SetProvisioning(busCode, new ProvisioningDiagnostics(
                BusCode: busCode,
                Provider: ProviderName,
                Mode: mode,
                Desired: desired,
                Current: current,
                Diff: diff,
                Timestamp: DateTimeOffset.UtcNow));
        }

        // Ensure base exchange exists (idempotent); provisioner already declared in most modes 
        channel.ExchangeDeclare(exchange: opts.Exchange, type: opts.ExchangeType, durable: true, autoDelete: false, arguments: null);

        var caps = new RabbitMqCapabilities();

        var plan = Negotiation.BuildPlan(busCode, ProviderName, caps, opts.Retry, opts.Dlq, requestDelay: false);

        var bus = new RabbitMqBus(busCode, connection, channel, opts, caps, plan, sp.GetService(typeof(ITypeAliasRegistry)) as ITypeAliasRegistry);

        // record diagnostics
        (sp.GetService(typeof(IMessagingDiagnostics)) as IMessagingDiagnostics)?.SetEffectivePlan(busCode, plan);

        // Optional simple consumer dispatcher: bind each configured subscription and dispatch by alias
        if (opts.Subscriptions.Count > 0)
        {
            var retryExchange = Naming.RetryExchange(opts.Exchange);
            foreach (var sub in opts.Subscriptions)
            {
                var queue = string.IsNullOrWhiteSpace(sub.Queue) ? Naming.Queue(busCode, sub.Name ?? "default") : sub.Queue!;
                var consumers = Math.Max(1, sub.Concurrency);
                for (int i = 0; i < consumers; i++)
                {
                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.Received += async (sender, ea) =>
                    {
                        var alias = ea.BasicProperties?.Type ?? string.Empty;
                        // Map headers: convert byte[] to UTF8 string; keep others as ToString()
                        IDictionary<string, object>? mappedHeaders = null;
                        var rawHeaders = ea.BasicProperties?.Headers;
                        if (rawHeaders is not null)
                        {
                            mappedHeaders = new Dictionary<string, object>(rawHeaders.Count);
                            foreach (var kv in rawHeaders)
                            {
                                object val = kv.Value is byte[] b ? Encoding.UTF8.GetString(b) : (kv.Value?.ToString() ?? string.Empty);
                                mappedHeaders[kv.Key] = val;
                            }
                        }

                        var scopeFactory = sp.GetService(typeof(IServiceScopeFactory)) as IServiceScopeFactory;
                        using var scope = scopeFactory?.CreateScope();
                        var provider = scope?.ServiceProvider ?? sp;

                        var outcome = await MessageDispatch.DispatchAsync(
                            provider,
                            alias,
                            ea.Body,
                            mappedHeaders,
                            ea.BasicProperties?.MessageId,
                            ea.BasicProperties?.CorrelationId,
                            ea.Redelivered,
                            null,
                            CancellationToken.None);

                        switch (outcome.Kind)
                        {
                            case DispatchResultKind.Success:
                            case DispatchResultKind.DuplicateSkipped:
                            case DispatchResultKind.NoHandler:
                            case DispatchResultKind.DeserializationSkipped:
                                channel.BasicAck(ea.DeliveryTag, false);
                                break;
                            case DispatchResultKind.Failure:
                            default:
                                // Backoff with TTL buckets up to MaxAttempts, then DLQ/requeue
                                var nextAttempt = Math.Max(1, outcome.Attempt) + 1;
                                var maxAttempts = Math.Max(1, opts.Retry.MaxAttempts);
                                if (nextAttempt <= maxAttempts && mode != ProvisioningMode.Off)
                                {
                                    var seq = RetryMath.Sequence(opts.Retry).ToArray();
                                    var idx = Math.Max(0, nextAttempt - 2);
                                    var delaySec = idx < seq.Length ? seq[idx] : (opts.Retry.MaxDelaySeconds > 0 ? opts.Retry.MaxDelaySeconds : Math.Max(1, opts.Retry.FirstDelaySeconds));
                                    var props = channel.CreateBasicProperties();
                                    props.ContentType = ea.BasicProperties?.ContentType ?? "application/json";
                                    props.MessageId = ea.BasicProperties?.MessageId ?? Guid.NewGuid().ToString("n");
                                    props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                                    props.Type = ea.BasicProperties?.Type ?? string.Empty;
                                    props.CorrelationId = ea.BasicProperties?.CorrelationId;
                                    props.Headers = ea.BasicProperties?.Headers ?? new Dictionary<string, object>();
                                    props.Headers[HeaderNames.Attempt] = nextAttempt.ToString();
                                    props.Headers[HeaderNames.RetryBucket] = delaySec.ToString();
                                    var rk = ea.RoutingKey;
                                    channel.BasicPublish(exchange: retryExchange, routingKey: rk, mandatory: false, basicProperties: props, body: ea.Body);
                                    channel.BasicAck(ea.DeliveryTag, false);
                                }
                                else
                                {
                                    var requeue = !(opts.Dlq.Enabled && sub.Dlq);
                                    channel.BasicNack(ea.DeliveryTag, false, requeue: requeue);
                                }
                                break;
                        }
                    };
                    channel.BasicConsume(queue, autoAck: false, consumer: consumer);
                }
            }
        }
        return (bus, caps);
    }

    // Internal shim remains for back-compat; delegate to core RetryMath
    internal static IEnumerable<int> ComputeRetryBuckets_PublicShim(RetryOptions retry) => RetryMath.Buckets(retry);
}

// Auto-discovery initializer so AddSora() wires RabbitMQ when referenced
// legacy initializer removed in favor of standardized auto-registrar