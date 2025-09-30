---
type: GUIDE
domain: data
title: "Garden Cooperative Journal"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-29
framework_version: v0.6.2
validation:
  date_last_tested: 2025-09-29
  status: pending
  scope: docs/guides/garden-cooperative-journal.md
---

# Garden Cooperative Journal

_There is no tech yet._ The Maple Street co-op runs on a corkboard and goodwill. Riley keeps a pencil behind her ear; Mara trusts memory more than she should. Beds get watered twice or not at all.

**Riley’s pitch:** “Let’s put up a tiny web API that speaks garden. We’ll keep the Pis dumb—just posting numbers—and keep the journal human.”

This chapter walks from **no tech** to **one Koan slice** using **only the data framework**: entity statics, relationship helpers, and lifecycle hooks. No config files.

---

## What you’ll build

- **A garden slice** that accepts readings and surfaces reminders in the co-op’s own language.
- **Dumb sensors, honest write path**: Pis `POST`; the service decides—immediately—whether a bed is dry enough to nudge.
- **Three member moves**: see what’s dry, water it, mark it done.

> Companion sample: `samples/guides/g1c1.GardenCoop/`
>
> ```pwsh
> cd samples/guides/g1c1.GardenCoop
> dotnet run
> ```
>
> The console host launches Koan.Web and serves the AngularJS dashboard straight from `wwwroot/`—no extra scaffolding. Flip the “sensor loop” toggle to let the Pi simulator post readings while you watch reminders light up.

### Before you start (truthfully low-tech)

- People with watering cans.
- Paper labels on beds.
- A few Raspberry Pis you can script (or simulate with `curl`).
- .NET 9 and a blank Koan web slice.

---

## Zero-Tech Reality → Single-Slice Moves

| Garden moment                      | Without tech                             | What we’ll do                                                                                          |
| ---------------------------------- | ---------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| **Is Bed 3 actually dry?**         | Intuition and arguments.                 | On each `Reading` upsert, compute a short rolling average with entity statics; set/clear a `Reminder`. |
| **Too many pings / radio silence** | Group chats spiral, or nobody speaks.    | Enforce **one active reminder per plot** via a simple upsert; no batching, no timers.                  |
| **Forgot to “mark it done”**       | Watering happens; the corkboard doesn’t. | `PATCH` reminder to `Acknowledged`; lifecycle notes the change so the journal stays trustworthy.       |

---

## 1) Ground the journal in SQLite (code-first, config-free)

_Morning: one laptop, one command. No servers to nurse._

```csharp
using Koan.Data.Sqlite;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();

var app = builder.Build();
app.Run();
```

---

## 2) Name the things people actually say

_Late morning: four boxes on the whiteboard. No hieroglyphics._

```csharp
using Koan.Data.Core.Relationships;

public class Plot : Entity<Plot>
{
    public string Name { get; set; } = "";
    [Parent(typeof(Member))] public string MemberId { get; set; } = "";
}

public class Member : Entity<Member>
{
    public string DisplayName { get; set; } = "";
}

public class Reading : Entity<Reading>
{
    [Parent(typeof(Plot))] public string PlotId { get; set; } = "";
    public double Moisture { get; set; }
    public DateTimeOffset SampledAt { get; set; } = DateTimeOffset.UtcNow;

    public static Task<Reading[]> Recent(string plotId, int take = 20, CancellationToken ct = default) =>
        Query().Where(r => r.PlotId == plotId)
               .OrderByDescending(r => r.SampledAt)
               .Take(take)
               .ToArrayAsync(ct);
}

public enum ReminderStatus { Idle, Active, Acknowledged }

public class Reminder : Entity<Reminder>
{
    [Parent(typeof(Plot))] public string PlotId { get; set; } = "";
    [Parent(typeof(Member))] public string MemberId { get; set; } = "";

    public ReminderStatus Status { get; private set; } = ReminderStatus.Idle;
    public string Notes { get; private set; } = "";

    public Task<Reminder> Activate(string notes) { Status = ReminderStatus.Active; Notes = notes; return Save(); }
    public Task<Reminder> Acknowledge(string notes) { Status = ReminderStatus.Acknowledged; Notes = notes; return Save(); }
}
```

**Narrative truth**

- `Plot` is a bed with a steward (`Member`).
- Sensors produce `Reading` snapshots, tied to a plot.
- `Reminder` is the polite nudge, not a siren.

---

## 3) Give the Pis one sentence to say

_Lunch: Riley tapes labels to the Pis—Bed-1, Bed-2, Bed-3. “You only need one phrase,” she tells them. “`POST /api/garden/readings`.”_

```csharp
[Route("api/garden/readings")]
public sealed class ReadingsController : EntityController<Reading> { }
```

A Pi speaks plainly:

```http
POST /api/garden/readings
Content-Type: application/json

{
  "plotId": "plots/bed-3",
  "moisture": 14.2,
  "sampledAt": "2025-09-28T09:15:00Z"
}
```

**What happens**
Koan validates the parent (`Plot`), stamps an ID (v7), stores the snapshot. No CSV archaeology.

**Field sense**
If hardware is chatty, clamp identical `(plotId, sampledAt)` in the Pi script; keep the server logic simple.

---

## 4) Decide “dry” on the write path

_Afternoon: Bed 3 dips; Bed 1 wobbles. Mara squints. “Feelings aren’t data.” Riley nods. “So we decide when the reading arrives.”_

We do the “is it dry?” decision **right after** each upsert—using entity statics only. Register the automation once so every process shares the same story.

```csharp
using System.Runtime.CompilerServices;
using Koan.Data.Core.Events;

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
                if (recent.Length == 0) return;

                var average = recent.Average(r => r.Moisture);

                var active = await Reminder.Query()
                    .Where(r => r.PlotId == reading.PlotId && r.Status == ReminderStatus.Active)
                    .FirstOrDefaultAsync(ct);

                if (average < DryThreshold)
                {
                    if (active is null)
                    {
                        var plot = await Plot.Get(reading.PlotId, ct);
                        if (plot is null) return;

                        await new Reminder
                        {
                            PlotId = reading.PlotId,
                            MemberId = plot.MemberId
                        }.Activate($"Low moisture (avg={average:F1}) – consider watering today.");
                    }
                }
                else if (active is not null)
                {
                    await active.Acknowledge("Moisture back above threshold.");
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
                var prior = await ctx.Prior.GetAsync(ctx.CancellationToken);
                var was = prior?.Status ?? ReminderStatus.Idle;
                var now = ctx.Current.Status;

                // Journal-friendly: narrate first activation; stay quiet on routine edits.
                if (was != ReminderStatus.Active && now == ReminderStatus.Active)
                {
                    Console.WriteLine($"[Journal] Reminder {ctx.Current.Id} activated for {ctx.Current.PlotId}");
                }
            });
    }
}
```

**What’s happening**

- Every `Reading` upsert triggers a small, local computation (no background jobs).
- We average a short, recent window and **upsert** the plot’s reminder accordingly.
- Net effect: simple, predictable, and immediate feedback.

**Tuning notes**

- Adjust `WindowSize` and `DryThreshold` for your soil/sensor combo.
- Add hysteresis later (for example, activate below 20, retire above 24) if you see flapping.
- Per-plot thresholds? Add a `DryThreshold` field on `Plot` and read it instead of the constant.

---

## 5) Three member moves, expressed as verbs

_Night: the gate squeaks. Two commands later, tomorrow’s plan is clear._ The lifecycles above keep the journal honest; these requests let people stay in the loop.

```bash
# 1) Record a reading (Pi or human)
curl -X POST https://localhost:5001/api/garden/readings \
  -H "Content-Type: application/json" \
  -d '{"plotId":"plots/bed-3","moisture":18.6}'

# 2) See what needs attention (pre-expanded)
curl "https://localhost:5001/api/garden/reminders?status=Active&with=plot,member"

# 3) Mark it done after watering (correct route)
curl -X PATCH https://localhost:5001/api/garden/reminders/{id} \
  -H "Content-Type: application/json" \
  -d '{"status":"Acknowledged","notes":"Evening watering complete"}'
```

**Why this is enough**

- The system decides dryness the moment data arrives.
- The list stays clean: one active reminder per plot, retired automatically when readings recover or manually when people water.

---

## Field wisdom (paper → pixels, without drama)

- **Start dumb at the edges**: Pis publish numbers, not meaning.
- **Decide on write**: small computations beat background machinery for v1.
- **Limit noise**: enforce one active reminder per plot; no alert storms.
- **Let mistakes breathe**: manual acknowledgements are always allowed; the next good reading retires a stale nudge.
- **Keep adapters swappable**: SQLite today, Mongo tomorrow—move it in config/DI, not code shape.

---

## Next steps

- Add **hysteresis** (`activate < 20`, `retire > 24`) to avoid edge jitter.
- Add **per-plot thresholds** (herbs vs. tomatoes) as fields on `Plot`.
- Swap SQLite → Mongo later by config; your entities and lifecycles stay put.

**Garden journal recap**
You began with pencils and habit. You now have a tiny web API, dumb Pis that say one sentence, and a write-path that decides, gently and immediately, what needs water—using **only** the data framework.

---

Want me to append a tiny Pi script (Python) that posts readings and clamps duplicate timestamps so teams can copy-paste and be done?
