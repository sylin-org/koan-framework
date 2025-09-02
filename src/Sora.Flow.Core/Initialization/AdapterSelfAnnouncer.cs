using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Flow.Model;
using Sora.Flow.Options;
using Sora.Messaging;

namespace Sora.Flow.Initialization;

internal sealed class AdapterSelfAnnouncer : BackgroundService
{
    private readonly ILogger<AdapterSelfAnnouncer> _log;
    private readonly AdapterRegistryOptions _opts;
    private readonly IAdapterIdentity _id;

    public AdapterSelfAnnouncer(IOptions<AdapterRegistryOptions> opts, IAdapterIdentity id, ILogger<AdapterSelfAnnouncer> log)
    {
        _opts = opts.Value; _id = id; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
    if (!_opts.AutoAnnounce || string.IsNullOrWhiteSpace(_id.System) || string.IsNullOrWhiteSpace(_id.Adapter))
        { _log.LogDebug("Self-announcer disabled or FlowAdapter metadata missing"); return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var msg = new AdapterAnnouncement
                {
            System = _id.System!,
            Adapter = _id.Adapter!,
            InstanceId = _id.InstanceId,
            Version = _id.Version,
                    Capabilities = _id.Capabilities,
            Bus = _id.Bus,
            Group = _id.Group,
            Host = _id.Host,
            Pid = _id.Pid,
            StartedAt = _id.StartedAt,
                    LastSeenAt = now,
            HeartbeatSeconds = _id.HeartbeatSeconds,
                };
                await msg.Send(stoppingToken);
            }
            catch (Exception ex)
            { _log.LogDebug(ex, "Adapter self-announcement failed"); }

        try { await Task.Delay(TimeSpan.FromSeconds(_id.HeartbeatSeconds), stoppingToken); } catch (TaskCanceledException) { }
        }
    }
}
