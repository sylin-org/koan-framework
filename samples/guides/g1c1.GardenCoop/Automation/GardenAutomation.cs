using System;
using System.Linq;
using System.Runtime.CompilerServices;
using g1c1.GardenCoop.Models;
using Koan.Data.Core.Events;

namespace g1c1.GardenCoop.Automation;

public static class GardenAutomation
{
    private const int WindowSize = 8;        // look at last 8 readings
    private const double DryThreshold = 20.0; // below 20% = time to water

    [ModuleInitializer]  // this runs automatically at startup - no wiring needed!
    public static void Initialize()
    {
        ConfigureReadingLifecycle();
        ConfigureReminderLifecycle();
    }

    private static void ConfigureReadingLifecycle()
    {
        Reading.Events
            .Setup(ctx => ctx.ProtectAll())  // let's prevent changes to things we don't need to change
            .AfterUpsert(async ctx =>  // every time a reading gets saved, run this
            {
                var reading = ctx.Current;
                var ct = ctx.CancellationToken;

                var plotId = reading.PlotId;
                if (string.IsNullOrWhiteSpace(plotId))
                {
                    // no plot binding yet - sensor is orphaned
                    var sensor = await Sensor.Get(reading.SensorId, ct);
                    var serial = sensor?.Id ?? "unknown";  // sensor.Id IS the serial now
                    Console.WriteLine($"[Journal] Reading {reading.Id} arrived for sensor {serial}, but no plot binding exists yet.");
                    return;
                }

                // grab recent readings to figure out average humidity
                var recent = await Reading.Recent(plotId, WindowSize, ct);
                if (recent.Length == 0)
                {
                    return;
                }

                var average = recent.Average(r => r.SoilHumidity);

                // check if we already have an active reminder for this plot
                var active = await Reminder.ActiveForPlot(plotId, ct);

                if (average < DryThreshold)
                {
                    // soil is dry - create a reminder if we don't have one
                    if (active is null)
                    {
                        var plot = await Plot.Get(plotId, ct);
                        if (plot is null)
                        {
                            return;
                        }

                        // new reminder - assign to plot's steward if they exist
                        await new Reminder
                        {
                            PlotId = plotId,
                            MemberId = plot.MemberId
                        }.ActivateAsync($"Low soil humidity (avg={average:F1}) – consider watering today.", ct);
                    }
                }
                else if (active is not null)
                {
                    // readings recovered above threshold - auto-acknowledge the reminder
                    await active.AcknowledgeAsync("Soil humidity back above threshold.", ct);
                }
            });
    }

    private static void ConfigureReminderLifecycle()
    {
        Reminder.Events
            .Setup(ctx =>
            {
                ctx.ProtectAll();  // lock it down first
                ctx.AllowMutation(nameof(Reminder.Status));  // except these two fields
                ctx.AllowMutation(nameof(Reminder.Notes));
            })
            .AfterUpsert(async ctx =>
            {
                // check if status changed to Active - that's when we'd send notifications
                var prior = await ctx.Prior.Get(ctx.CancellationToken);
                var was = prior?.Status ?? ReminderStatus.Idle;
                var now = ctx.Current.Status;

                if (was != ReminderStatus.Active && now == ReminderStatus.Active)
                {
                    // just became active - time to notify
                    Console.WriteLine($"[Journal] Sending email (fake) to steward of {ctx.Current.PlotId} about low soil humidity.");
                }
            });
    }
}
