using System.Runtime.CompilerServices;
using g1c1.GardenCoop.Models;
using Koan.Data.Core.Events;
using System.Linq;

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

                var recent = await Reading.Recent(reading.PlotId, WindowSize, ct);
                if (recent.Length == 0)
                {
                    return;
                }

                var average = recent.Average(r => r.Moisture);

                var active = await Reminder.ActiveForPlot(reading.PlotId, ct);

                if (average < DryThreshold)
                {
                    if (active is null)
                    {
                        var plot = await Plot.Get(reading.PlotId, ct);
                        if (plot is null)
                        {
                            return;
                        }

                        await new Reminder
                        {
                            PlotId = reading.PlotId,
                            MemberId = plot.MemberId
                        }.ActivateAsync($"Low moisture (avg={average:F1}) – consider watering today.", ct);
                    }
                }
                else if (active is not null)
                {
                    await active.AcknowledgeAsync("Moisture back above threshold.", ct);
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
                    Console.WriteLine($"[Journal] Sending email (fake) to steward of {ctx.Current.PlotId} about low moisture.");
                }
            });
    }
}
