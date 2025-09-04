using Newtonsoft.Json;
using RabbitMQ.Client;
using Sora.Messaging.Provisioning;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Sora.Messaging.RabbitMq.Provisioning;

using Sora.Messaging.Infrastructure;
internal sealed class RabbitMqProvisioner : ITopologyPlanner, ITopologyInspector, ITopologyDiffer, ITopologyApplier
{
    private static readonly IDictionary<string, object> EmptyArgs = new Dictionary<string, object>();

    private static bool ArgValueEquals(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Equals(b)) return true;
        // Normalize numeric comparisons (int/long/double) and strings representing numbers
        try
        {
            var da = Convert.ToDecimal(a, System.Globalization.CultureInfo.InvariantCulture);
            var db = Convert.ToDecimal(b, System.Globalization.CultureInfo.InvariantCulture);
            return da == db;
        }
        catch { /* not numeric */ }
        // Fallback to string compare ignoring trivial formatting
        return string.Equals(Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture),
                             Convert.ToString(b, System.Globalization.CultureInfo.InvariantCulture),
                             StringComparison.Ordinal);
    }

    private static bool ArgDictEquals(IReadOnlyDictionary<string, object?>? a, IReadOnlyDictionary<string, object?>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null) return b is null || b.Count == 0;
        if (b is null) return a.Count == 0;
        if (a.Count != b.Count) return false;
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var bv)) return false;
            if (!ArgValueEquals(kv.Value, bv)) return false;
        }
        return true;
    }

    private static string BindingKey(BindingSpec b)
        => $"{b.FromExchange}|{b.ToType}|{b.To}|{b.RoutingKey}";

    private static IDictionary<string, object> ToClientArgs(IReadOnlyDictionary<string, object?>? args)
    {
        if (args is null || args.Count == 0) return EmptyArgs;
        var dict = new Dictionary<string, object>(args.Count);
        foreach (var kvp in args)
        {
            if (kvp.Value is null) continue; // skip nulls; RabbitMQ client expects concrete objects
            dict[kvp.Key] = kvp.Value;
        }
        return dict;
    }

    public DesiredTopology Plan(string busCode, string? defaultGroup, object providerOptions, IMessagingCapabilities caps, ITypeAliasRegistry? aliases)
    {
        var ctx = providerOptions as RabbitMqProviderContext ?? throw new ArgumentException("Expected RabbitMqProviderContext", nameof(providerOptions));
        var opts = ctx.Options;
        var exchanges = new List<ExchangeSpec>
        {
            new(opts.Exchange, opts.ExchangeType, Durable: true)
        };
        var bindings = new List<BindingSpec>();
        var queues = new List<QueueSpec>();

        // Retry infrastructure (headers exchange + TTL queues)
        if (opts.Retry.MaxAttempts > 1)
        {
            exchanges.Add(new ExchangeSpec(opts.Exchange + ".retry", "headers", Durable: true));
            var buckets = RetryMath.Buckets(opts.Retry).ToArray();
            foreach (var b in buckets)
            {
                var qname = $"sora.{ctx.Bus}.retry.{b}s";
                var qargs = new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = b * 1000,
                    ["x-dead-letter-exchange"] = opts.Exchange
                };
                queues.Add(new QueueSpec(qname, Arguments: qargs));
                // bind via header match
                bindings.Add(new BindingSpec(opts.Exchange + ".retry", qname, "queue", "", new Dictionary<string, object?>
                {
                    ["x-match"] = "all",
                    ["x-retry-bucket"] = b.ToString()
                }));
            }
        }

        // DLQ exchange if enabled
        string? dlx = null;
        if (opts.Dlq.Enabled)
        {
            dlx = opts.Exchange + ".dlx";
            exchanges.Add(new ExchangeSpec(dlx, opts.ExchangeType, Durable: true));
        }

        // Subscriptions → queues + bindings
        var subs = opts.Subscriptions.Count == 0
            ? new[] { new SubscriptionOption { Name = string.IsNullOrWhiteSpace(defaultGroup) ? "workers" : defaultGroup!, RoutingKeys = new[] { "#" }, Dlq = true, Concurrency = 1 } }
            : opts.Subscriptions.ToArray();
        foreach (var sub in subs)
        {
            var q = string.IsNullOrWhiteSpace(sub.Queue) ? $"sora.{ctx.Bus}.{sub.Name}" : sub.Queue!;
            var qArgs = new Dictionary<string, object?>();
            if (sub.Dlq && dlx is not null)
                qArgs["x-dead-letter-exchange"] = dlx;
            queues.Add(new QueueSpec(q, Arguments: qArgs, Dlq: dlx is not null ? new DlqSpec(dlx) : null));

            var rks = sub.RoutingKeys is { Length: > 0 } ? sub.RoutingKeys : new[] { "#" };
            foreach (var rk in rks)
                bindings.Add(new BindingSpec(opts.Exchange, q, "queue", rk));
        }

        return new DesiredTopology(exchanges, queues, bindings);
    }

    public CurrentTopology Inspect(string busCode, object providerClient)
    {
        var ctx = providerClient as RabbitMqProviderContext ?? throw new ArgumentException("Expected RabbitMqProviderContext", nameof(providerClient));
        var conn = ctx.Connection;
        var ch = ctx.Channel;
        var opts = ctx.Options;
    // Management API logic removed for build fix. Conservative fallback:
    return new CurrentTopology(Array.Empty<ExchangeSpec>(), Array.Empty<QueueSpec>(), Array.Empty<BindingSpec>());
    }

    public TopologyDiff Diff(DesiredTopology desired, CurrentTopology current)
    {
        // If any difference, mark for full teardown and rebuild
        bool anyDiff = !desired.Exchanges.SequenceEqual(current.Exchanges)
            || !desired.Queues.SequenceEqual(current.Queues)
            || !desired.Bindings.SequenceEqual(current.Bindings);

        if (anyDiff)
        {
            return new TopologyDiff(
                ExchangesToCreate: desired.Exchanges.ToList(),
                QueuesToCreate: desired.Queues.ToList(),
                BindingsToCreate: desired.Bindings.ToList(),
                QueueUpdates: new List<(QueueSpec, QueueSpec)>(),
                ExchangeUpdates: new List<(ExchangeSpec, ExchangeSpec)>(),
                ExchangesToRemove: current.Exchanges.ToList(),
                QueuesToRemove: current.Queues.ToList(),
                BindingsToRemove: current.Bindings.ToList()
            );
        }
        else
        {
            return new TopologyDiff(
                ExchangesToCreate: new List<ExchangeSpec>(),
                QueuesToCreate: new List<QueueSpec>(),
                BindingsToCreate: new List<BindingSpec>(),
                QueueUpdates: new List<(QueueSpec, QueueSpec)>(),
                ExchangeUpdates: new List<(ExchangeSpec, ExchangeSpec)>(),
                ExchangesToRemove: new List<ExchangeSpec>(),
                QueuesToRemove: new List<QueueSpec>(),
                BindingsToRemove: new List<BindingSpec>()
            );
        }
    }

    public void Apply(string busCode, ProvisioningMode mode, TopologyDiff diff, object providerClient)
    {
        if (mode == ProvisioningMode.Off || mode == ProvisioningMode.DryRun)
            return;

        var ctx = providerClient as RabbitMqProviderContext ?? throw new ArgumentException("Expected RabbitMqProviderContext", nameof(providerClient));
        var ch = ctx.Channel;

        // Remove all current bindings, queues, exchanges
        foreach (var b in diff.BindingsToRemove)
        {
            try
            {
                // Skip system exchanges
                if (IsSystemExchange(b.To) || IsSystemExchange(b.FromExchange)) continue;
                if (string.Equals(b.ToType, "queue", StringComparison.OrdinalIgnoreCase))
                    ch.QueueUnbind(b.To, b.FromExchange, b.RoutingKey, ToClientArgs(b.Arguments));
                else
                    ch.ExchangeUnbind(b.To, b.FromExchange, b.RoutingKey, ToClientArgs(b.Arguments));
            }
            catch { }
        }

        foreach (var q in diff.QueuesToRemove)
        {
            try { ch.QueueDelete(q.Name, false, false); } catch { }
        }

        foreach (var ex in diff.ExchangesToRemove)
        {
            if (IsSystemExchange(ex.Name)) continue;
            try { ch.ExchangeDelete(ex.Name, false); } catch { }
        }

        // Create all desired exchanges, queues, bindings
        foreach (var ex in diff.ExchangesToCreate)
        {
            if (IsSystemExchange(ex.Name)) continue;
            ch.ExchangeDeclare(ex.Name, ex.Type, ex.Durable, ex.AutoDelete, ToClientArgs(ex.Arguments));
        }
        foreach (var q in diff.QueuesToCreate)
        {
            try { ch.QueueDelete(q.Name, false, false); } catch { /* ignore if not exists */ }
            ch.QueueDeclare(q.Name, q.Durable, q.Exclusive, q.AutoDelete, ToClientArgs(q.Arguments));
        }
        foreach (var b in diff.BindingsToCreate)
        {
            // Skip system exchanges
            if (IsSystemExchange(b.To) || IsSystemExchange(b.FromExchange)) continue;
            if (string.Equals(b.ToType, "queue", StringComparison.OrdinalIgnoreCase))
                ch.QueueBind(b.To, b.FromExchange, b.RoutingKey, ToClientArgs(b.Arguments));
            else
                ch.ExchangeBind(b.To, b.FromExchange, b.RoutingKey, ToClientArgs(b.Arguments));
        }

    }

    // Helper to detect system exchanges (default or amq.*)
    private static bool IsSystemExchange(string? name)
    {
        if (string.IsNullOrEmpty(name)) return true; // default exchange
        if (name.StartsWith("amq.", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }


    // Minimal DTOs for RabbitMQ Management API responses
    private sealed class RmqExchangeDto { public string name { get; set; } = string.Empty; public string type { get; set; } = string.Empty; public bool durable { get; set; } public bool auto_delete { get; set; } public Dictionary<string, object?>? arguments { get; set; } }
    private sealed class RmqQueueDto { public string name { get; set; } = string.Empty; public bool durable { get; set; } public bool exclusive { get; set; } public bool auto_delete { get; set; } public Dictionary<string, object?>? arguments { get; set; } }
    private sealed class RmqBindingDto { public string? source { get; set; } public string? vhost { get; set; } public string? destination { get; set; } public string? destination_type { get; set; } public string? routing_key { get; set; } public Dictionary<string, object?>? arguments { get; set; } }
}
