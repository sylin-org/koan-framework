using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using System.Linq;

namespace g1c1.GardenCoop.Models;

public enum ReminderStatus
{
    Idle,
    Active,
    Acknowledged
}

public class Reminder : Entity<Reminder>
{
    [Parent(typeof(Plot))]
    public string PlotId { get; set; } = string.Empty;

    [Parent(typeof(Member))]
    public string? MemberId { get; set; }

    public ReminderStatus Status { get; set; } = ReminderStatus.Idle;

    public string Notes { get; set; } = string.Empty;

    public static async Task<Reminder?> ActiveForPlot(string plotId, CancellationToken ct = default)
    {
        var reminders = await Reminder.Query(r => r.PlotId == plotId && r.Status == ReminderStatus.Active, ct);
        return reminders.FirstOrDefault();
    }

    public Task<Reminder> ActivateAsync(string notes, CancellationToken ct = default)
    {
        Status = ReminderStatus.Active;
        Notes = notes;
        return this.Save(ct);
    }

    public Task<Reminder> AcknowledgeAsync(string notes, CancellationToken ct = default)
    {
        Status = ReminderStatus.Acknowledged;
        Notes = notes;
        return this.Save(ct);
    }
}
