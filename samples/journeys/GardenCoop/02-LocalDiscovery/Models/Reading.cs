using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Lifecycle;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using System.ComponentModel.DataAnnotations;

namespace GardenCoop.Models;

public sealed class Reading : Entity<Reading>
{
    public string? SensorSerial { get; set; }

    [Parent(typeof(Sensor))]
    public string SensorId { get; set; } = "";

    [Parent(typeof(Plot))]
    public string? PlotId { get; set; }

    [Range(0, 100)]
    public double SoilHumidity { get; set; }

    public double? TemperatureC { get; set; }

    public DateTimeOffset SampledAt { get; set; } = DateTimeOffset.UtcNow;

    public static async Task<Reading[]> Recent(string plotId, int take = 20, CancellationToken ct = default)
    {
        var items = await Reading.Query(r => r.PlotId == plotId, ct);
        return items
            .OrderByDescending(r => r.SampledAt)
            .Take(take)
            .ToArray();
    }

    internal static void ConfigureLifecycle()
    {
        Reading.Lifecycle
            .BeforeUpsert(async ctx =>
            {
                ctx.ProtectAll();
                ctx.AllowMutation(nameof(Reading.SensorId));
                ctx.AllowMutation(nameof(Reading.PlotId));
                var reading = ctx.Current;
                var ct = ctx.CancellationToken;

                if (!string.IsNullOrWhiteSpace(reading.SensorSerial))
                {
                    var sensor = await Sensor.Ensure(reading.SensorSerial, ct);

                    sensor.LastSeenAt = reading.SampledAt;
                    sensor.Capabilities |= SensorCapabilities.SoilHumidity;
                    if (reading.TemperatureC.HasValue)
                    {
                        sensor.Capabilities |= SensorCapabilities.Temperature;
                    }
                    await sensor.Save(ct);

                    reading.SensorId = sensor.Id;
                    reading.PlotId = sensor.PlotId;
                }

                return EntityLifecycleResult.Proceed();
            });
    }
}
