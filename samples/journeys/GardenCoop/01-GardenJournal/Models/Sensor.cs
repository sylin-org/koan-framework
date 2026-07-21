using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using System.ComponentModel.DataAnnotations;

namespace GardenCoop.Models;

[Flags]
public enum SensorCapabilities
{
    None = 0,
    SoilHumidity = 1,
    AirHumidity = 2,
    Temperature = 4,
    GpsLocation = 8
}

public sealed class Sensor : Entity<Sensor, string>
{
    public string Serial => Id;

    public string DisplayName { get; set; } = "";

    [Parent(typeof(Plot))]
    public string? PlotId { get; set; }

    public SensorCapabilities Capabilities { get; set; } = SensorCapabilities.SoilHumidity | SensorCapabilities.Temperature;

    public DateTimeOffset? LastSeenAt { get; set; }

    public static async Task<Sensor> Ensure(string serial, CancellationToken ct = default)
    {
        var normalized = (serial ?? "").Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ValidationException("Sensor serial is required.");
        }

        var existing = await Sensor.Get(normalized, ct);
        if (existing is not null)
        {
            return existing;
        }

        return await new Sensor
        {
            Id = normalized,
            DisplayName = normalized
        }.Save(ct);
    }

    internal static void ConfigureLifecycle()
    {
        Sensor.Lifecycle.AfterUpsert(async ctx =>
        {
            var current = ctx.Current;
            var ct = ctx.CancellationToken;
            var prior = ctx.Prior;

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
