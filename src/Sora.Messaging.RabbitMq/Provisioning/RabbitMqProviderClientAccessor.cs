using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Sora.Messaging;
using Sora.Messaging.Provisioning;

namespace Sora.Messaging.RabbitMq.Provisioning;

/// <summary>
/// Provides RabbitMQ provider client (connection, channel, options) for the core topology orchestrator.
/// </summary>
internal sealed class RabbitMqProviderClientAccessor : IProviderClientAccessor, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly IOptions<MessagingOptions> _msgOpts;
    private readonly ILogger<RabbitMqProviderClientAccessor>? _logger;
    private readonly ConcurrentDictionary<string, object> _clients = new(StringComparer.OrdinalIgnoreCase);

    public RabbitMqProviderClientAccessor(IConfiguration configuration, IOptions<MessagingOptions> msgOpts, ILogger<RabbitMqProviderClientAccessor>? logger = null)
    {
        _configuration = configuration; _msgOpts = msgOpts; _logger = logger;
    }

    public object? GetProviderClient(string busCode)
    {
        try
        {
            return _clients.GetOrAdd(busCode, static (bc, state) => ((RabbitMqProviderClientAccessor)state).Create(bc), this);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "RabbitMqProviderClientAccessor failed to create provider client for bus {BusCode}", busCode);
            return null; // allow orchestrator fallback
        }
    }

    private object Create(string busCode)
    {
        // Build minimal RabbitMqOptions replicating factory logic (subset relevant for provisioning)
        var cfgSection = _configuration.GetSection($"Sora:Messaging:Buses:{busCode}");
        var opts = new RabbitMqOptions();

        // Connection string resolution (same precedence as factory)
        opts.ConnectionStringName = Read(cfgSection, "RabbitMq:ConnectionStringName") ?? Read(cfgSection, "ConnectionStringName") ?? opts.ConnectionStringName;
        opts.Exchange = Read(cfgSection, "RabbitMq:Exchange") ?? Read(cfgSection, "Exchange") ?? opts.Exchange;
        opts.ExchangeType = Read(cfgSection, "RabbitMq:ExchangeType") ?? Read(cfgSection, "ExchangeType") ?? opts.ExchangeType;
        opts.PublisherConfirms = ReadBool(cfgSection, "RabbitMq:PublisherConfirms", opts.PublisherConfirms);
        opts.Prefetch = ReadInt(cfgSection, "RabbitMq:Prefetch", opts.Prefetch);

        opts.Dlq.Enabled = ReadBool(cfgSection, "RabbitMq:Dlq:Enabled", opts.Dlq.Enabled);
        opts.Retry.MaxAttempts = Math.Max(1, ReadInt(cfgSection, "RabbitMq:Retry:MaxAttempts", opts.Retry.MaxAttempts));
        opts.Retry.FirstDelaySeconds = Math.Max(1, ReadInt(cfgSection, "RabbitMq:Retry:FirstDelaySeconds", opts.Retry.FirstDelaySeconds));
        opts.Retry.Backoff = Read(cfgSection, "RabbitMq:Retry:Backoff") ?? opts.Retry.Backoff;
        opts.Retry.MaxDelaySeconds = ReadInt(cfgSection, "RabbitMq:Retry:MaxDelaySeconds", opts.Retry.MaxDelaySeconds);

        // Provision mode (not strictly required here, orchestrator determines env-level mode)
        var modeStr = Read(cfgSection, "RabbitMq:ProvisionMode") ?? Read(cfgSection, "ProvisionMode");
        if (!string.IsNullOrWhiteSpace(modeStr) && Enum.TryParse<ProvisioningMode>(modeStr, true, out var pm))
            opts.ProvisionMode = pm;

        // Subscriptions single default (same heuristic as factory)
        if (opts.Subscriptions.Count == 0)
        {
            var rkCsv = Read(cfgSection, "RabbitMq:Subscriptions:0:RoutingKeys") ?? Read(cfgSection, "Subscriptions:0:RoutingKeys");
            var name = Read(cfgSection, "RabbitMq:Subscriptions:0:Name") ?? Read(cfgSection, "Subscriptions:0:Name");
            var queue = Read(cfgSection, "RabbitMq:Subscriptions:0:Queue") ?? Read(cfgSection, "Subscriptions:0:Queue");
            var dlq = ReadBool(cfgSection, "RabbitMq:Subscriptions:0:Dlq", true);
            var concurrency = Math.Max(1, ReadInt(cfgSection, "RabbitMq:Subscriptions:0:Concurrency", 1));
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

        // Connection string actual value
        var connStr = Read(cfgSection, "RabbitMq:ConnectionString") ?? Read(cfgSection, "ConnectionString") ?? opts.ConnectionString;
        if (string.IsNullOrWhiteSpace(connStr) && !string.IsNullOrWhiteSpace(opts.ConnectionStringName))
            connStr = _configuration[$"ConnectionStrings:{opts.ConnectionStringName}"];
        if (string.IsNullOrWhiteSpace(connStr))
            connStr = "amqp://guest:guest@localhost:5672/"; // dev-friendly default
        opts.ConnectionString = connStr;

        opts.ManagementUrl = Read(cfgSection, "RabbitMq:ManagementUrl") ?? Read(cfgSection, "ManagementUrl") ?? opts.ManagementUrl;
        opts.ManagementUsername = Read(cfgSection, "RabbitMq:ManagementUsername") ?? Read(cfgSection, "ManagementUsername") ?? opts.ManagementUsername;
        opts.ManagementPassword = Read(cfgSection, "RabbitMq:ManagementPassword") ?? Read(cfgSection, "ManagementPassword") ?? opts.ManagementPassword;

        // Establish lightweight connection for inspection/diff/apply (reuse retry logic from factory simplified)
        var factory = new ConnectionFactory { Uri = new Uri(connStr!), DispatchConsumersAsync = false, AutomaticRecoveryEnabled = true, TopologyRecoveryEnabled = true };
        IConnection? connection = null;
        Exception? lastErr = null;
        int maxAttempts = Math.Max(3, opts.Retry.MaxAttempts);
        var firstDelay = TimeSpan.FromSeconds(Math.Max(1, opts.Retry.FirstDelaySeconds));
        var maxDelay = TimeSpan.FromSeconds(Math.Max(opts.Retry.FirstDelaySeconds, opts.Retry.MaxDelaySeconds));
        TimeSpan NextDelay(int attempt)
        {
            var factor = Math.Pow(2, Math.Max(0, attempt - 1));
            var d = TimeSpan.FromSeconds(Math.Min(maxDelay.TotalSeconds > 0 ? maxDelay.TotalSeconds : firstDelay.TotalSeconds * 8, firstDelay.TotalSeconds * factor));
            return d <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : d;
        }
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                connection = factory.CreateConnection($"sora-orch-{busCode}");
                lastErr = null;
                break;
            }
            catch (Exception ex)
            {
                lastErr = ex;
                if (attempt >= maxAttempts) break;
                var delay = NextDelay(attempt);
                _logger?.LogDebug(ex, "RabbitMQ accessor connect attempt {Attempt}/{MaxAttempts} failed; retrying in {Delay}s", attempt, maxAttempts, (int)delay.TotalSeconds);
                try { Thread.Sleep(delay); } catch { }
            }
        }
        if (connection is null)
        {
            throw lastErr ?? new Exception("RabbitMQ accessor connection failed");
        }
        var channel = connection.CreateModel();
        return (connection, channel, opts);
    }

    private static string? Read(IConfiguration cfg, string key) => cfg[key];
    private static bool ReadBool(IConfiguration cfg, string key, bool def) => bool.TryParse(cfg[key], out var b) ? b : def;
    private static int ReadInt(IConfiguration cfg, string key, int def) => int.TryParse(cfg[key], out var i) ? i : def;

    public void Dispose()
    {
        foreach (var kv in _clients)
        {
            try
            {
                var (conn, ch, _) = ((IConnection, IModel, RabbitMqOptions))kv.Value;
                try { ch.Close(); ch.Dispose(); } catch { }
                try { conn.Close(); conn.Dispose(); } catch { }
            }
            catch { }
        }
        _clients.Clear();
    }
}