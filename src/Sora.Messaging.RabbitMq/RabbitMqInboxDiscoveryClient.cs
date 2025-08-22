using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Sora.Core;

namespace Sora.Messaging.RabbitMq;

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
        // Read connection string from known keys (bus-specific)
        var connStr = Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.ConnectionString(busCode), null);
        var connName = Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.ConnectionStringName(busCode), null);
        if (string.IsNullOrWhiteSpace(connStr) && !string.IsNullOrWhiteSpace(connName))
            connStr = cfg?["ConnectionStrings:" + connName];
        if (string.IsNullOrWhiteSpace(connStr)) return null;

        var factory = new ConnectionFactory { Uri = new Uri(connStr!), DispatchConsumersAsync = true, AutomaticRecoveryEnabled = true, TopologyRecoveryEnabled = true };
        using var connection = factory.CreateConnection($"sora-discovery-{busCode}");
        using var channel = connection.CreateModel();

        // Ensure exchange exists (idempotent)
        var exchange = Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Exchange(busCode), "sora") ?? "sora";
        var exchangeType = Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.ExchangeType(busCode), "topic") ?? "topic";
        channel.ExchangeDeclare(exchange: exchange, type: exchangeType, durable: true, autoDelete: false, arguments: null);

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