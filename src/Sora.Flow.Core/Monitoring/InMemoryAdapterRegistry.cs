using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Flow.Options;

namespace Sora.Flow.Monitoring;

internal sealed class InMemoryAdapterRegistry : IAdapterRegistry, IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<string, AdapterEntry> _entries = new();
    private readonly ILogger<InMemoryAdapterRegistry> _log;
    private readonly AdapterRegistryOptions _opts;
    private Timer? _timer;

    public InMemoryAdapterRegistry(IOptions<AdapterRegistryOptions> opts, ILogger<InMemoryAdapterRegistry> log)
    { _opts = opts.Value; _log = log; }

    public void Upsert(AdapterEntry entry)
    {
        var key = Key(entry.System, entry.Adapter, entry.InstanceId);
        _entries.AddOrUpdate(key, entry, (k, old) => { old.LastSeenAt = entry.LastSeenAt; return old; });
    }

    public IReadOnlyList<AdapterEntry> All() => _entries.Values.ToList();

    public IReadOnlyList<AdapterEntry> ForSystem(string system)
        => _entries.Values.Where(x => string.Equals(x.System, system, StringComparison.OrdinalIgnoreCase)).ToList();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(Sweep, null, TimeSpan.FromSeconds(_opts.TtlSeconds), TimeSpan.FromSeconds(Math.Max(5, _opts.TtlSeconds / 2)));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    { _timer?.Dispose(); _timer = null; return Task.CompletedTask; }

    private void Sweep(object? state)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var ttl = TimeSpan.FromSeconds(Math.Max(10, _opts.TtlSeconds));
            foreach (var kv in _entries)
            {
                if (now - kv.Value.LastSeenAt > ttl)
                {
                    _entries.TryRemove(kv.Key, out _);
                }
            }
        }
        catch (Exception ex)
        { _log.LogDebug(ex, "Adapter registry sweep failed"); }
    }

    private static string Key(string system, string adapter, string instanceId) => $"{system}:{adapter}:{instanceId}";

    public void Dispose() => _timer?.Dispose();
}
