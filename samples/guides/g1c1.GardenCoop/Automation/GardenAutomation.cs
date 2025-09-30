using System;
using System.Linq;
using System.Runtime.CompilerServices;
using g1c1.GardenCoop.Models;
using Koan.Data.Core.Events;

namespace g1c1.GardenCoop.Automation;

public static class GardenAutomation
{
    private const int WindowSize = 8;
    private const double DryThreshold = 20.0;

    [ModuleInitializer]
    public static void Initialize()
    {
        ConfigureReadingLifecycle();
        ConfigureReminderLifecycle();
    }

    private static void ConfigureReadingLifecycle()
    {
        Reading.Events
            .Setup(ctx => ctx.ProtectAll())
            .AfterUpsert(async ctx =>
            {
                var reading = ctx.Current;
                var ct = ctx.CancellationToken;

                var plotId = reading.PlotId;
                if (string.IsNullOrWhiteSpace(plotId))
                {
                    var sensor = await Sensor.Get(reading.SensorId, ct);
                    var serial = sensor?.Serial ?? "unknown";
                    Console.WriteLine($"[Journal] Reading {reading.Id} arrived for sensor {serial}, but no plot binding exists yet.");
                    return;
                }

                var recent = await Reading.Recent(plotId, WindowSize, ct);
                if (recent.Length == 0)
                {
                    return;
                }

                var average = recent.Average(r => r.SoilHumidity);

                var active = await Reminder.ActiveForPlot(plotId, ct);

                if (average < DryThreshold)
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
                        }.ActivateAsync($"Low soil humidity (avg={average:F1}) – consider watering today.", ct);
                    }
                }
                else if (active is not null)
                {
                    await active.AcknowledgeAsync("Soil humidity back above threshold.", ct);
                }
            });
    }

    private static void ConfigureReminderLifecycle()
    {
        Reminder.Events
            .Setup(ctx =>
            {
                ctx.ProtectAll();
                ctx.AllowMutation(nameof(Reminder.Status));
                ctx.AllowMutation(nameof(Reminder.Notes));
            })
            .AfterUpsert(async ctx =>
            {
                var prior = await ctx.Prior.Get(ctx.CancellationToken);
                var was = prior?.Status ?? ReminderStatus.Idle;
                var now = ctx.Current.Status;

                if (was != ReminderStatus.Active && now == ReminderStatus.Active)
                {
                    Console.WriteLine($"[Journal] Sending email (fake) to steward of {ctx.Current.PlotId} about low soil humidity.");
                }
            });
    }
}
