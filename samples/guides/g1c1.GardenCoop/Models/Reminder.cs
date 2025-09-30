using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using System.Linq;

namespace g1c1.GardenCoop.Models;

public enum ReminderStatus
{
    Idle,           // no action needed
    Active,         // hey, water this!
    Acknowledged    // got it, we watered
}

public class Reminder : Entity<Reminder>
{
    [Parent(typeof(Plot))]
    public string PlotId { get; set; } = string.Empty;

    [Parent(typeof(Member))]
    public string? MemberId { get; set; }  // who should we nudge?

    public ReminderStatus Status { get; set; } = ReminderStatus.Idle;

    public string Notes { get; set; } = string.Empty;

    public static async Task<Reminder?> ActiveForPlot(string plotId, CancellationToken ct = default)
    {
        // check if this plot already has an active reminder - keep it to one per plot
        var reminders = await Reminder.Query(r => r.PlotId == plotId && r.Status == ReminderStatus.Active, ct);
        return reminders.FirstOrDefault();
    }

    public Task<Reminder> ActivateAsync(string notes, CancellationToken ct = default)
    {
        // turn on the reminder
        Status = ReminderStatus.Active;
        Notes = notes;
        return this.Save(ct);
    }

    public Task<Reminder> AcknowledgeAsync(string notes, CancellationToken ct = default)
    {
        // mark it done
        Status = ReminderStatus.Acknowledged;
        Notes = notes;
        return this.Save(ct);
    }
}
