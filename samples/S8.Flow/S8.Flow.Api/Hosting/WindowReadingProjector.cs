using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using S8.Flow.Shared;
using Sora.Data.Core;
using Sora.Flow.Infrastructure;
using Sora.Flow.Model;

namespace S8.Flow.Api.Hosting;

public sealed class WindowReadingProjector : BackgroundService
{
    private readonly ILogger<WindowReadingProjector> _log;
    public const string ViewName = "window.5m";
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public WindowReadingProjector(ILogger<WindowReadingProjector> log) { _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<StageRecord<SensorReadingVo>> page;
                using (DataSetContext.With(FlowSets.StageShort(FlowSets.Keyed)))
                {
                    page = await StageRecord<SensorReadingVo>.FirstPage(500, stoppingToken);
                }
                if (page.Count == 0)
                {
                    using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
                    {
                        page = await StageRecord<SensorReadingVo>.FirstPage(500, stoppingToken);
                    }
                }
                if (page.Count > 0)
                {
                        var cutoff = DateTimeOffset.UtcNow - Window;
                        var groups = page
                            .Where(r => !string.IsNullOrWhiteSpace(r.CorrelationId))
                            .GroupBy(r => r.CorrelationId!, StringComparer.Ordinal);
                        foreach (var g in groups)
                        {
                            var readings = new List<(DateTimeOffset At, double Val)>();
                            foreach (var r in g)
                            {
                                var dict = Extract(r.StagePayload);
                                if (dict is null) continue;
                                if (!dict.TryGetValue(Keys.Reading.CapturedAt, out var atObj)) continue;
                                if (!dict.TryGetValue(Keys.Reading.Value, out var valObj)) continue;
                                if (!DateTimeOffset.TryParse(atObj?.ToString(), out var at)) continue;
                                if (!double.TryParse(valObj?.ToString(), out var val)) continue;
                                if (at >= cutoff) readings.Add((at, val));
                            }
                            readings.Sort((a,b) => a.At.CompareTo(b.At));
                            if (readings.Count == 0) continue;
                            var view = new Dictionary<string, object>
                            {
                                ["from"] = readings.First().At.ToString("O"),
                                ["to"] = readings.Last().At.ToString("O"),
                                ["count"] = readings.Count,
                                ["min"] = readings.Min(x => x.Val),
                                ["max"] = readings.Max(x => x.Val),
                                ["avg"] = readings.Average(x => x.Val),
                                ["series"] = readings.Select(x => new object[]{ x.At.ToString("O"), x.Val }).ToArray(),
                            };
                            var rulid = g.First().ReferenceUlid;
                            var doc = new SensorWindowReading
                            {
                                Id = $"{ViewName}::{(string.IsNullOrWhiteSpace(rulid) ? g.Key : rulid)}",
                                CanonicalId = g.Key,
                                ReferenceUlid = rulid,
                                ViewName = ViewName,
                                View = view
                            };
                            await Data<SensorWindowReading, string>.UpsertAsync(doc, set: FlowSets.ViewShort(ViewName), ct: stoppingToken);
                        }
                }
            }
            catch (Exception ex)
            { _log.LogDebug(ex, "WindowReadingProjector iteration failed"); }

            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
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
