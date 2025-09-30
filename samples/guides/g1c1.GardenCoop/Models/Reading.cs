using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Events;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace g1c1.GardenCoop.Models;

public class Reading : Entity<Reading>  // inheriting from Entity<Reading> gives me auto GUID v7 for Id
{
    static Reading()  // runs once when the type loads
    {
        ConfigureLifecycle();
    }

    // the Pi can send "sensorSerial" instead of looking up the GUID - we'll resolve it
    public string? SensorSerial { get; set; }

    [Parent(typeof(Sensor))]
    public string SensorId { get; set; } = string.Empty;  // this will be set to the serial after lookup

    [Parent(typeof(Plot))]
    public string? PlotId { get; set; }  // gets copied from sensor binding, might be null if sensor not bound yet

    [Range(0, 100)]  // humidity is 0-100%
    public double SoilHumidity { get; set; }

    public double? TemperatureC { get; set; }  // optional - not all sensors have temp

    public DateTimeOffset SampledAt { get; set; } = DateTimeOffset.UtcNow;

    public static async Task<Reading[]> Recent(string plotId, int take = 20, CancellationToken ct = default)
    {
        // grab recent readings for a plot - useful for averaging
        var items = await Reading.Query(r => r.PlotId == plotId, ct);
        return items
            .OrderByDescending(r => r.SampledAt)
            .Take(take)
            .ToArray();
    }

    private static void ConfigureLifecycle()
    {
        Reading.Events
            .Setup(ctx =>
            {
                // let's prevent changes to most things, but allow setting the IDs we resolve
                ctx.ProtectAll();
                ctx.AllowMutation(nameof(Reading.SensorId));  // we set this from the serial
                ctx.AllowMutation(nameof(Reading.PlotId));    // we copy this from the sensor
            })
            .BeforeUpsert(async ctx =>
            {
                var reading = ctx.Current;
                var ct = ctx.CancellationToken;

                // if the Pi sent a serial instead of an ID, let's look up the sensor
                if (!string.IsNullOrWhiteSpace(reading.SensorSerial))
                {
                    // find or create the sensor - one call does it all
                    var sensor = await Sensor.EnsureAsync(reading.SensorSerial, ct);

                    // update sensor's last seen and capabilities
                    sensor.LastSeenAt = reading.SampledAt;
                    sensor.Capabilities |= SensorCapabilities.SoilHumidity;  // mark that this sensor reports humidity
                    if (reading.TemperatureC.HasValue)
                    {
                        sensor.Capabilities |= SensorCapabilities.Temperature;  // OR the flags together
                    }
                    await sensor.Save(ct);  // Save() works on both Entity<T> and Entity<T,K>

                    // now set the reading's IDs from the sensor
                    reading.SensorId = sensor.Id;  // sensor.Id IS the serial
                    reading.PlotId = sensor.PlotId;  // might be null if sensor not bound to a plot yet
                }

                return EntityEventResult.Proceed();  // BeforeUpsert needs to return a result
            });
    }
}
