using RabbitMQ.Client;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Sora.Messaging.Provisioning;

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
        var opts = (RabbitMqOptions)providerOptions;
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
                var qname = $"sora.{busCode}.retry.{b}s";
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
            var q = string.IsNullOrWhiteSpace(sub.Queue) ? $"sora.{busCode}.{sub.Name}" : sub.Queue!;
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
        var (conn, ch, opts) = ((IConnection connection, IModel channel, RabbitMqOptions opts))providerClient;
        // Try Management API; if not configured, fall back to empty (add-only)
        try
        {
            var (baseUrl, vhost, auth) = BuildMgmtContext(opts);
            if (baseUrl is null)
                return new CurrentTopology(Array.Empty<ExchangeSpec>(), Array.Empty<QueueSpec>(), Array.Empty<BindingSpec>());

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            if (auth is not null)
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
            }
            var vhEsc = Uri.EscapeDataString(vhost ?? "/");

            // Fetch exchanges and queues
            var exTask = http.GetAsync($"/api/exchanges/{vhEsc}");
            var qTask = http.GetAsync($"/api/queues/{vhEsc}");
            var bTask = http.GetAsync($"/api/bindings/{vhEsc}");
            HttpResponseMessage[] responses = Task.WhenAll(exTask, qTask, bTask).GetAwaiter().GetResult();

            if (responses.Any(r => r.StatusCode == HttpStatusCode.Unauthorized))
                return new CurrentTopology(Array.Empty<ExchangeSpec>(), Array.Empty<QueueSpec>(), Array.Empty<BindingSpec>());

            var exJson = responses[0].Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var qJson = responses[1].Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var bJson = responses[2].Content.ReadAsStringAsync().GetAwaiter().GetResult();

            var exDocs = JsonSerializer.Deserialize<List<RmqExchangeDto>>(exJson) ?? new();
            var qDocs = JsonSerializer.Deserialize<List<RmqQueueDto>>(qJson) ?? new();
            var bDocs = JsonSerializer.Deserialize<List<RmqBindingDto>>(bJson) ?? new();

            // Map to CurrentTopology (skip amq.* system exchanges; focus on our primary exchange and retry/dlx)
            var exchanges = exDocs
                .Where(e => !e.name.StartsWith("amq.", StringComparison.OrdinalIgnoreCase))
                .Select(e => new ExchangeSpec(e.name, e.type, e.durable, e.auto_delete, e.arguments))
                .ToArray();
            var queues = qDocs
                .Select(q => new QueueSpec(q.name, q.durable, q.exclusive, q.auto_delete, q.arguments))
                .ToArray();
            var bindings = bDocs
                .Select(b => new BindingSpec(b.source ?? string.Empty, b.destination ?? string.Empty, b.destination_type ?? "queue", b.routing_key ?? string.Empty, b.arguments))
                .ToArray();

            return new CurrentTopology(exchanges, queues, bindings);
        }
        catch
        {
            // Conservative fallback
            return new CurrentTopology(Array.Empty<ExchangeSpec>(), Array.Empty<QueueSpec>(), Array.Empty<BindingSpec>());
        }
    }

    public TopologyDiff Diff(DesiredTopology desired, CurrentTopology current)
    {
        // Exchanges
        var curExByName = current.Exchanges.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var desExByName = desired.Exchanges.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var exCreates = new List<ExchangeSpec>();
        var exUpdates = new List<(ExchangeSpec Existing, ExchangeSpec Desired)>();
        var exRemoves = new List<ExchangeSpec>();

        foreach (var de in desired.Exchanges)
        {
            if (!curExByName.TryGetValue(de.Name, out var ce))
                exCreates.Add(de);
            else
            {
                if (!string.Equals(ce.Type, de.Type, StringComparison.Ordinal)
                    || ce.Durable != de.Durable
                    || ce.AutoDelete != de.AutoDelete
                    || !ArgDictEquals(ce.Arguments, de.Arguments))
                {
                    exUpdates.Add((ce, de));
                }
            }
        }
        foreach (var ce in current.Exchanges)
        {
            if (!desExByName.ContainsKey(ce.Name))
                exRemoves.Add(ce);
        }

        // Queues
        var curQByName = current.Queues.ToDictionary(q => q.Name, StringComparer.Ordinal);
        var desQByName = desired.Queues.ToDictionary(q => q.Name, StringComparer.Ordinal);
        var qCreates = new List<QueueSpec>();
        var qUpdates = new List<(QueueSpec Existing, QueueSpec Desired)>();
        var qRemoves = new List<QueueSpec>();

        foreach (var dq in desired.Queues)
        {
            if (!curQByName.TryGetValue(dq.Name, out var cq))
                qCreates.Add(dq);
            else
            {
                if (cq.Durable != dq.Durable
                    || cq.Exclusive != dq.Exclusive
                    || cq.AutoDelete != dq.AutoDelete
                    || !ArgDictEquals(cq.Arguments, dq.Arguments))
                {
                    qUpdates.Add((cq, dq));
                }
            }
        }
        foreach (var cq in current.Queues)
        {
            if (!desQByName.ContainsKey(cq.Name))
                qRemoves.Add(cq);
        }

        // Bindings (compare by quadruple: from, toType, to, routingKey)
        var curBindsByKey = current.Bindings.ToDictionary(BindingKey, StringComparer.Ordinal);
        var desBindsByKey = desired.Bindings.ToDictionary(BindingKey, StringComparer.Ordinal);
        var bCreates = new List<BindingSpec>();
        var bRemoves = new List<BindingSpec>();

        foreach (var db in desired.Bindings)
        {
            if (!curBindsByKey.TryGetValue(BindingKey(db), out var cb))
            {
                bCreates.Add(db);
            }
            else
            {
                // If args differ, mark for removal (will be recreated in ForceRecreate apply step)
                if (!ArgDictEquals(cb.Arguments, db.Arguments))
                {
                    bRemoves.Add(cb);
                }
            }
        }
        foreach (var cb in current.Bindings)
        {
            if (!desBindsByKey.ContainsKey(BindingKey(cb)))
                bRemoves.Add(cb);
        }

        return new TopologyDiff(
            ExchangesToCreate: exCreates,
            QueuesToCreate: qCreates,
            BindingsToCreate: bCreates,
            QueueUpdates: qUpdates,
            ExchangeUpdates: exUpdates,
            ExchangesToRemove: exRemoves,
            QueuesToRemove: qRemoves,
            BindingsToRemove: bRemoves);
    }

    public void Apply(string busCode, ProvisioningMode mode, TopologyDiff diff, object providerClient)
    {
        if (mode == ProvisioningMode.Off || mode == ProvisioningMode.DryRun)
            return;

        var (conn, ch, opts) = ((IConnection connection, IModel channel, RabbitMqOptions opts))providerClient;

        bool isForce = mode == ProvisioningMode.ForceRecreate;

        if (isForce)
        {
            // Remove bindings first
            foreach (var b in diff.BindingsToRemove)
            {
                if (string.Equals(b.ToType, "queue", StringComparison.OrdinalIgnoreCase))
                    ch.QueueUnbind(b.To, b.FromExchange, b.RoutingKey, ToClientArgs(b.Arguments));
                else
                    ch.ExchangeUnbind(b.To, b.FromExchange, b.RoutingKey, ToClientArgs(b.Arguments));
            }

            // Apply destructive updates by deleting existing and re-creating
            foreach (var (existing, _) in diff.ExchangeUpdates)
            {
                try { ch.ExchangeDelete(existing.Name); } catch { /* ignore */ }
            }
            foreach (var (existing, _) in diff.QueueUpdates)
            {
                try { ch.QueueDelete(existing.Name); } catch { /* ignore */ }
            }

            // Remove extra entities
            foreach (var q in diff.QueuesToRemove)
            {
                try { ch.QueueDelete(q.Name); } catch { /* ignore */ }
            }
            foreach (var ex in diff.ExchangesToRemove)
            {
                try { ch.ExchangeDelete(ex.Name); } catch { /* ignore */ }
            }
        }

        // Create/recreate exchanges and queues
        foreach (var ex in diff.ExchangesToCreate.Concat(diff.ExchangeUpdates.Select(t => t.Desired)))
            ch.ExchangeDeclare(ex.Name, ex.Type, ex.Durable, ex.AutoDelete, ToClientArgs(ex.Arguments));

        foreach (var q in diff.QueuesToCreate.Concat(diff.QueueUpdates.Select(t => t.Desired)))
            ch.QueueDeclare(q.Name, q.Durable, q.Exclusive, q.AutoDelete, ToClientArgs(q.Arguments));

        // Bindings: in additive modes, avoid duplicating an existing quadruple by consulting current topology
        CurrentTopology? currentForAdditive = null;
        if (!isForce)
        {
            try { currentForAdditive = Inspect(busCode, providerClient); } catch { currentForAdditive = null; }
        }

        foreach (var b in diff.BindingsToCreate)
        {
            if (currentForAdditive is not null)
            {
                var key = BindingKey(b);
                if (currentForAdditive.Bindings.Any(cb => BindingKey(cb) == key))
                    continue; // skip creating duplicate binding in additive mode
            }

            if (string.Equals(b.ToType, "queue", StringComparison.OrdinalIgnoreCase))
                ch.QueueBind(b.To, b.FromExchange, b.RoutingKey, ToClientArgs(b.Arguments));
            else
                ch.ExchangeBind(b.To, b.FromExchange, b.RoutingKey, ToClientArgs(b.Arguments));
        }
    }

    private static IEnumerable<int> RabbitMqFactory_CompatComputeRetryBuckets(RetryOptions retry)
        => RabbitMqFactory.ComputeRetryBuckets_PublicShim(retry);

    private static (string? baseUrl, string? vhost, string? basicAuth) BuildMgmtContext(RabbitMqOptions opts)
    {
        // Base URL precedence: explicit ManagementUrl, else derive from connection host (http://host:15672)
        string? baseUrl = opts.ManagementUrl;
        string? vhost = null;
        string? auth = null;
        try
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
                {
                    var uri = new Uri(opts.ConnectionString);
                    var host = string.IsNullOrWhiteSpace(uri.Host) ? "localhost" : uri.Host;
                    var port = 15672; // default management port
                    baseUrl = $"http://{host}:{port}";
                    vhost = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? "/" : Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
                }
            }
            if (vhost is null)
            {
                vhost = "/";
            }
            // Basic auth from explicit management credentials, else from amqp uri userinfo
            var user = opts.ManagementUsername;
            var pass = opts.ManagementPassword;
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            {
                if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
                {
                    var uri = new Uri(opts.ConnectionString);
                    user ??= Uri.UnescapeDataString(uri.UserInfo.Split(':').FirstOrDefault() ?? "guest");
                    pass ??= Uri.UnescapeDataString(uri.UserInfo.Contains(':') ? uri.UserInfo.Split(':')[1] : "guest");
                }
            }
            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
            {
                var raw = System.Text.Encoding.UTF8.GetBytes($"{user}:{pass}");
                auth = Convert.ToBase64String(raw);
            }
        }
        catch { /* noop */ }
        return (baseUrl, vhost, auth);
    }

    // Minimal DTOs for RabbitMQ Management API responses
    private sealed class RmqExchangeDto { public string name { get; set; } = string.Empty; public string type { get; set; } = string.Empty; public bool durable { get; set; } public bool auto_delete { get; set; } public Dictionary<string, object?>? arguments { get; set; } }
    private sealed class RmqQueueDto { public string name { get; set; } = string.Empty; public bool durable { get; set; } public bool exclusive { get; set; } public bool auto_delete { get; set; } public Dictionary<string, object?>? arguments { get; set; } }
    private sealed class RmqBindingDto { public string? source { get; set; } public string? vhost { get; set; } public string? destination { get; set; } public string? destination_type { get; set; } public string? routing_key { get; set; } public Dictionary<string, object?>? arguments { get; set; } }
}
