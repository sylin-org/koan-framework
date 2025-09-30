using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Events;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace g1c1.GardenCoop.Models;

[Flags]
public enum SensorCapabilities
{
    None = 0,
    SoilHumidity = 1,
    AirHumidity = 2,
    Temperature = 4,
    GpsLocation = 8
}

public class Sensor : Entity<Sensor>
{
    static Sensor()
    {
        ConfigureLifecycle();
    }

    [Required]
    public string Serial { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    [Parent(typeof(Plot))]
    public string? PlotId { get; set; }

    public SensorCapabilities Capabilities { get; set; } = SensorCapabilities.SoilHumidity | SensorCapabilities.Temperature;

    public DateTimeOffset? LastSeenAt { get; set; }

    public static async Task<Sensor?> GetBySerial(string serial, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        var normalized = serial.Trim();
        var matches = await Sensor.Query(s => s.Serial == normalized, ct);
        return matches.FirstOrDefault();
    }

    public static async Task<Sensor> EnsureAsync(string serial, CancellationToken ct = default)
    {
        var normalized = (serial ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ValidationException("Sensor serial is required.");
        }

        var existing = await GetBySerial(normalized, ct);
        if (existing is not null)
        {
            return existing;
        }

        var sensor = new Sensor
        {
            Serial = normalized,
            DisplayName = normalized
        };

        return await sensor.Save(ct);
    }

    private static void ConfigureLifecycle()
    {
        Sensor.Events.AfterUpsert(async ctx =>
        {
            var current = ctx.Current;
            var ct = ctx.CancellationToken;
            var prior = await ctx.Prior.Get(ct);

            if (string.IsNullOrWhiteSpace(current.PlotId))
            {
                return;
            }

            if (prior?.PlotId == current.PlotId)
            {
                return;
            }

            var orphanReadings = await Reading.Query(r => r.SensorId == current.Id && r.PlotId == null, ct);

            foreach (var reading in orphanReadings)
            {
                reading.PlotId = current.PlotId;
                await reading.Save(ct);
            }
        });
    }
}
