using System;
using System.Linq;
using GardenCoop.Models;

namespace GardenCoop.Automation;

public static class GardenAutomation
{
    internal const int ReadingWindowSize = 8;
    internal const double DrySoilThreshold = 20.0;

    internal static void Configure()
    {
        Reading.ConfigureLifecycle();
        Sensor.ConfigureLifecycle();
        ConfigureReadingLifecycle();
        ConfigureReminderLifecycle();
    }

    private static void ConfigureReadingLifecycle()
    {
        Reading.Lifecycle
            .AfterUpsert(async ctx =>
            {
                var reading = ctx.Current;
                var ct = ctx.CancellationToken;

                var plotId = reading.PlotId;
                if (string.IsNullOrWhiteSpace(plotId))
                {
                    var sensor = await Sensor.Get(reading.SensorId, ct);
                    var serial = sensor?.Id ?? "unknown";
                    Console.WriteLine($"[Journal] Reading {reading.Id} arrived for sensor {serial}, but no plot binding exists yet.");
                    return;
                }

                var recent = await Reading.Recent(plotId, ReadingWindowSize, ct);
                if (recent.Length == 0)
                {
                    return;
                }

                var average = recent.Average(r => r.SoilHumidity);

                var active = await Reminder.ActiveForPlot(plotId, ct);

                if (average < DrySoilThreshold)
                {
                    if (active is null)
                    {
                        var plot = await Plot.Get(plotId, ct);
                        if (plot is null)
                        {
                            return;
                        }

                        await new Reminder
                        {
                            PlotId = plotId,
                            MemberId = plot.MemberId
                        }.Activate($"Low soil humidity (avg={average:F1}) – consider watering today.", ct);
                    }
                }
                else if (active is not null)
                {
                    await active.Acknowledge("Soil humidity back above threshold.", ct);
                }
            });
    }

    private static void ConfigureReminderLifecycle()
    {
        Reminder.Lifecycle
            .AfterUpsert(ctx =>
            {
                var prior = ctx.Prior;
                var was = prior?.Status ?? ReminderStatus.Idle;
                var now = ctx.Current.Status;

                if (was != ReminderStatus.Active && now == ReminderStatus.Active)
                {
                    Console.WriteLine($"[Journal] Sending email (fake) to steward of {ctx.Current.PlotId} about low soil humidity.");
                }
            });
    }
}
