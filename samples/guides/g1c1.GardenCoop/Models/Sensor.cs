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

[Flags]  // need this so I can combine capabilities with |
public enum SensorCapabilities
{
    None = 0,
    SoilHumidity = 1,      // using this now
    AirHumidity = 2,       // maybe later for rain logic
    Temperature = 4,       // got this working too
    GpsLocation = 8        // future idea - exact bed locations
}

// using Entity<Sensor, string> means the serial IS the ID - no separate lookup needed!
public class Sensor : Entity<Sensor, string>
{
    static Sensor()  // runs once when the type loads - neat place for setup
    {
        ConfigureLifecycle();
    }

    // Serial returns Id - keeps API contract simple (frontend expects 'serial' field)
    public string Serial => Id;

    // Pi's friendly name - defaults to its serial until someone changes it
    public string DisplayName { get; set; } = string.Empty;

    [Parent(typeof(Plot))]
    public string? PlotId { get; set; }  // nullable = sensor might not be assigned to a plot yet

    public SensorCapabilities Capabilities { get; set; } = SensorCapabilities.SoilHumidity | SensorCapabilities.Temperature;

    public DateTimeOffset? LastSeenAt { get; set; }  // track when we last heard from this Pi

    public static async Task<Sensor> EnsureAsync(string serial, CancellationToken ct = default)
    {
        // "ensure" = get it if exists, create if doesn't
        var normalized = (serial ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ValidationException("Sensor serial is required.");
        }

        // since serial IS the ID, just try to Get() it directly
        var existing = await Sensor.Get(normalized, ct);
        if (existing is not null)
        {
            return existing;
        }

        // first time seeing this serial - create it with the serial as the ID
        var sensor = new Sensor
        {
            Id = normalized,  // the serial IS the identity
            DisplayName = normalized  // using serial as name until someone gives it something better
        };

        return await sensor.Save(ct);  // Save() works on both Entity<T> and Entity<T,K>
    }

    private static void ConfigureLifecycle()
    {
        Sensor.Events.AfterUpsert(async ctx =>
        {
            var current = ctx.Current;
            var ct = ctx.CancellationToken;
            var prior = await ctx.Prior.Get(ct);

            // did we just bind this sensor to a plot?
            if (string.IsNullOrWhiteSpace(current.PlotId))
            {
                return;
            }

            // same plot as before? nothing to do
            if (prior?.PlotId == current.PlotId)
            {
                return;
            }

            // plot binding changed! let's backfill readings that arrived before binding
            var orphanReadings = await Reading.Query(r => r.SensorId == current.Id && r.PlotId == null, ct);

            foreach (var reading in orphanReadings)
            {
                reading.PlotId = current.PlotId;
                await reading.Save(ct);  // each save triggers the reminder logic - smart!
            }
        });
    }
}
