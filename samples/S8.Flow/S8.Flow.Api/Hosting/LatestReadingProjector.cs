using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using S8.Flow.Shared;
using Sora.Data.Core;
using Sora.Flow.Infrastructure;
using Sora.Flow.Model;

namespace S8.Flow.Api.Hosting;

public sealed class LatestReadingProjector : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<LatestReadingProjector> _log;
    public const string ViewName = "latest.reading";

    public LatestReadingProjector(IServiceProvider sp, ILogger<LatestReadingProjector> log)
    { _sp = sp; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                IReadOnlyList<StageRecord<SensorReadingVo>> page;
                using (DataSetContext.With(FlowSets.StageShort(FlowSets.Keyed)))
                {
                    page = await StageRecord<SensorReadingVo>.FirstPage(500, stoppingToken);
                }
                if (page.Count == 0)
                {
                    // Fallback to intake if keyed hasn't been populated yet
                    using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
                    {
                        page = await StageRecord<SensorReadingVo>.FirstPage(500, stoppingToken);
                    }
                }
                if (page.Count > 0)
                {
                    var groups = page
                        .Where(r => !string.IsNullOrWhiteSpace(r.CorrelationId))
                        .GroupBy(r => r.CorrelationId!, StringComparer.Ordinal);
                    foreach (var g in groups)
                    {
                        var latest = g.OrderByDescending(x => x.OccurredAt).First();
                        var payload = Extract(latest.StagePayload);
                        if (payload is null) continue;
                        var rulid = latest.ReferenceUlid;
                        var viewDoc = new SensorLatestReading
                        {
                            Id = $"{ViewName}::{(string.IsNullOrWhiteSpace(rulid) ? g.Key : rulid)}",
                            CanonicalId = g.Key,
                            ReferenceUlid = rulid,
                            ViewName = ViewName,
                            View = new Dictionary<string, object>
                            {
                                [Keys.Reading.CapturedAt] = payload.TryGetValue(Keys.Reading.CapturedAt, out var at) ? at : latest.OccurredAt.ToString("O"),
                                [Keys.Reading.Value] = payload.TryGetValue(Keys.Reading.Value, out var val) ? val : default!,
                                [Keys.Sensor.Unit] = payload.TryGetValue(Keys.Sensor.Unit, out var u) ? u : string.Empty,
                                [Keys.Sensor.Code] = payload.TryGetValue(Keys.Sensor.Code, out var code) ? code : string.Empty,
                            }
                        };
                        await Data<SensorLatestReading, string>.UpsertAsync(viewDoc, set: FlowSets.ViewShort(ViewName), ct: stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            { _log.LogDebug(ex, "LatestReadingProjector iteration failed"); }

            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (TaskCanceledException) { }
        }
    }

    private static Dictionary<string, object>? Extract(object? payload)
    {
        if (payload is null) return null;
        if (payload is IDictionary<string, object?> m)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in m) dict[kv.Key] = kv.Value!;
            return dict;
        }
        if (payload is Newtonsoft.Json.Linq.JObject jo)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in jo.Properties()) dict[p.Name] = p.Value?.ToString() ?? string.Empty;
            return dict;
        }
        return null;
    }
}
