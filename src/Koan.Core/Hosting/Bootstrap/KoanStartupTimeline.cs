using System;
using System.Collections.Generic;

namespace Koan.Core.Hosting.Bootstrap;

internal enum KoanStartupStage
{
    BootstrapStart,
    ConfigReady,
    DataReady,
    ServicesReady,
    AppReady
}

internal static class KoanStartupTimeline
{
    private static readonly object _sync = new();
    private static readonly Dictionary<KoanStartupStage, DateTimeOffset> _marks = new();

    public static void Mark(KoanStartupStage stage)
    {
        lock (_sync)
        {
            if (!_marks.ContainsKey(stage))
            {
                _marks[stage] = DateTimeOffset.UtcNow;
            }
        }
    }

    public static StartupTimelineSummary GetSummary()
    {
        Dictionary<KoanStartupStage, DateTimeOffset> snapshot;
        lock (_sync)
        {
            snapshot = new Dictionary<KoanStartupStage, DateTimeOffset>(_marks);
        }

        DateTimeOffset? Get(KoanStartupStage stage)
            => snapshot.TryGetValue(stage, out var value) ? value : null;

        var boot = Get(KoanStartupStage.BootstrapStart);
        var config = Get(KoanStartupStage.ConfigReady);
        var data = Get(KoanStartupStage.DataReady);
        var services = Get(KoanStartupStage.ServicesReady);
        var ready = Get(KoanStartupStage.AppReady);

        return new StartupTimelineSummary(
            Boot: Diff(boot, config ?? data ?? services ?? ready),
            Config: Diff(config, data ?? services ?? ready),
            Data: Diff(data, services ?? ready),
            Services: Diff(services, ready),
            Total: Diff(boot, ready));
    }

    private static TimeSpan? Diff(DateTimeOffset? start, DateTimeOffset? end)
    {
        if (start is null || end is null)
        {
            return null;
        }

        var duration = end.Value - start.Value;
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }
}

internal readonly record struct StartupTimelineSummary(
    TimeSpan? Boot,
    TimeSpan? Config,
    TimeSpan? Data,
    TimeSpan? Services,
    TimeSpan? Total)
{
    public bool HasValues
        => Boot.HasValue || Config.HasValue || Data.HasValue || Services.HasValue || Total.HasValue;
}
