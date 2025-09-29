---
type: GUIDE
domain: data
title: "Garden Cooperative Journal"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.2
validation:
  date_last_tested: 2025-09-28
  status: verified
  scope: docs/guides/garden-cooperative-journal.md
---

# Garden Cooperative Journal

Spend a day with the Maple Street cooperative and learn Koan by following the people who tend its raised beds. This how-to keeps the architecture bound to the story—no abstract best-practice detours—so every code sample pays off a moment from the garden journal. The cast stays light (`Plot`, `Reading`, `Reminder`, `Member`), letting the narrative do the teaching while you explore what Koan can do.

## What you’ll build

- A single Koan slice where moisture sensors post readings, Flow tallies hydration scores, and members get nudged when soil dries out.
- Story-first controllers that mirror the cooperative’s language: `POST /api/garden/readings`, `GET /api/garden/reminders`, and a lightweight acknowledgment loop.
- Lifecycle hooks that narrate state changes instead of burying them in infrastructure.

### What you’ll learn

- How Koan’s entity statics, relationship helpers, and Flow batches fit together without leaving the story’s point of view.
- How to enrich responses (`?with=`) and page or stream (`FirstPage`, `Page`, `AllStream`) without inventing extra layers.
- How to keep Chapter 1 grounded in SQLite while leaving room for a future Mongo chapter.

### Field reminders from the co-op

- **Sensor jitter happens** – Clamp or toss duplicate readings so reminders only fire when soil is truly dry.
- **Keep configuration external** – Swapping adapters later should feel like changing hoses, not rewriting code.
- **Members miss pings** – Let reminders reactivate when acknowledgements lag; the co-op forgives late-night watering.
- **Flow batches can lag** – Pass cancellation tokens through handlers so long jobs can yield when the crew closes the gate.

---

## Storyboard at a Glance

| Moment | API Surface | What happens |
| --- | --- | --- |
| Dawn moisture check | `POST /api/garden/readings` | A sensor reports moisture for Plot A. Reading saves immediately via entity controller. |
| Midday hydration review | Flow pipeline | Batched readings compute hydration score; pipeline flips `Reminder.Status` to `Active` when the soil is dry. |
| Evening journal entry | `GET /api/garden/reminders` | Member sees active reminders, logs a watering acknowledgement, and the lifecycle hook records the status return to `Idle`. |

Use the sections below to implement each beat.

---

## 1. Configure SQLite for the Garden

*Morning check-in: Riley unlocks the tool shed and confirms the Raspberry Pi is still whispering readings to Koan.*

```csharp
// appsettings.Development.json
{
  "Koan": {
    "Data": {
      "DefaultAdapter": "sqlite",
      "Adapters": {
        "sqlite": {
          "ConnectionString": "Data Source=./data/garden.db"
        }
      }
    }
  }
}
```

`Program.cs` stays minimal—Koan discovers the adapter and Flow components automatically.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

var app = builder.Build();
app.Run();
```

---

## 2. Model the Cooperative

*Late morning planning: Mara sketches plots, stewards, and reminder cards on the whiteboard before anyone writes code.*

```csharp
using Koan.Data.Core.Relationships;

public class Plot : Entity<Plot>
{
    public string Name { get; set; } = "";

    [Parent(typeof(Member))]
    public string MemberId { get; set; } = "";
}

public class Member : Entity<Member>
{
    public string DisplayName { get; set; } = "";
}

public class Reading : Entity<Reading>
{
    [Parent(typeof(Plot))]
    public string PlotId { get; set; } = "";

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
    [Parent(typeof(Plot))]
    public string PlotId { get; set; } = "";

    [Parent(typeof(Member))]
    public string MemberId { get; set; } = "";

    public ReminderStatus Status { get; private set; } = ReminderStatus.Idle;
    public string Notes { get; private set; } = "";

    public Task<Reminder> Activate(string notes)
    {
        Status = ReminderStatus.Active;
        Notes = notes;
        return Save();
    }

    public Task<Reminder> Acknowledge(string notes)
    {
        Status = ReminderStatus.Acknowledged;
        Notes = notes;
        return Save();
    }
}
```

`GetParent<TParent>()` now works everywhere—`await plot.GetParent<Member>(ct)` fetches the steward, `await reading.GetParent<Plot>(ct)` returns the plot, and `await reminder.GetParent<Member>(ct)` links the reminder back to its owner. To walk down the graph, rely on `GetChildren<TChild>()`:

```csharp
var reminders = await member.GetChildren<Reminder>(ct);
var active = reminders.Where(r => r.Status == ReminderStatus.Active).ToArray();
```

For dashboards, batch-resolve relationships with the `Relatives` extension from `Koan.Data.Core.Extensions` and keep UIs from issuing N+1 queries:

```csharp
using Koan.Data.Core.Extensions;

var plots = await Plot.All(ct);
var withMembers = await plots.Relatives<Plot, string>(ct);
```

---

## 3. Accept Sensor Readings

*Lunch hour upload: The Pi posts another moisture snapshot while the crew shares sandwiches on the picnic tables.*

```csharp
[Route("api/garden/readings")]
public sealed class ReadingsController : EntityController<Reading> { }
```

Koan wires up CRUD endpoints automatically. Sensors can submit data without bespoke handlers.

```http
POST /api/garden/readings
Content-Type: application/json

{
  "plotId": "plots/raspberry-bed",
  "moisture": 14.2,
  "sampledAt": "2025-09-28T09:15:00Z"
}
```

Use `Reading.Recent(plotId)` for dashboards or Flow jobs that need context around each sensor update.

> **Field note** – When the cooperative grows beyond a handful of plots, switch to the built-in pagers and streamers to stay responsive.

```csharp
var firstPage = await Reading.FirstPage(size: 25, ct);
var secondPage = await Reading.Page(page: 2, size: 25, ct);

await foreach (var reading in Reading.AllStream(batchSize: 500, ct))
{
    // Process long-running analytics without loading everything into memory.
}
```

These helpers mirror the guidance in the [paging and streaming playbook](../guides/data/all-query-streaming-and-pager.md) and keep dashboards or Flow analytics from overfetching.

---

## 4. Score Hydration with Flow

*Afternoon analysis: After lunch, the Flow pipeline chews through the last batch and flags thirsty plots before anyone forgets.*

```csharp
public static class HydrationPipeline
{
    public static void Configure(FlowPipeline pipeline)
    {
        pipeline.Batch("garden-hydration")
                .FromEntity<Reading>()
                .Every(TimeSpan.FromMinutes(15))
                .Run(async (batch, ct) =>
                {
                    var byPlot = batch.GroupBy(r => r.PlotId);
                    foreach (var group in byPlot)
                    {
                        var average = group.Average(r => r.Moisture);
                        if (average < 20)
                        {
                            await EnsureReminder(group.Key, ct);
                        }
                    }
                });
    }

    private static async Task EnsureReminder(string plotId, CancellationToken ct)
    {
        var reminder = await Reminder.Query()
                                     .Where(r => r.PlotId == plotId && r.Status == ReminderStatus.Active)
                                     .FirstOrDefaultAsync(ct);

        if (reminder is null)
        {
            var steward = await Plot.ById(plotId, ct);
            if (steward is null) return;

            await new Reminder
            {
                PlotId = plotId,
                MemberId = steward.MemberId
            }.Activate("Soil moisture low – consider watering today.");
        }
    }
}
```

The pipeline batches readings every 15 minutes, checks moisture averages, and activates a reminder when soil is too dry. All adapters are accessed through entity statics so swapping providers later is low-risk.

---

## 5. React to Reminder Status Changes

*Evening wrap-up: Reminders flip states as the stewards log who watered what, and Koan keeps the journal tidy.*

```csharp
public static class ReminderLifecycle
{
    public static void Configure(EntityLifecycleBuilder<Reminder> pipeline)
    {
        pipeline.ProtectAll()
                .Allow(r => r.Status, r => r.Notes)
                .AfterUpsert(async (ctx, next) =>
                {
                    var previous = ctx.Previous?.Status ?? ReminderStatus.Idle;
                    var current = ctx.Entity.Status;

                    await next();

                    if (previous != ReminderStatus.Active && current == ReminderStatus.Active)
                    {
                        await Flow.Emit(new ReminderActivated
                        {
                            ReminderId = ctx.Entity.Id,
                            PlotId = ctx.Entity.PlotId,
                            MemberId = ctx.Entity.MemberId
                        });
                    }
                });
    }
}

public sealed class ReminderActivated : Entity<ReminderActivated>
{
    public string ReminderId { get; set; } = "";
    public string PlotId { get; set; } = "";
    public string MemberId { get; set; } = "";
}
```

The hook compares the previous and current status, emits an event when a reminder becomes active, and otherwise stays quiet. Consumers can subscribe to `ReminderActivated` for analytics or optional notifications.

> **Optional extension** – Add a background worker that listens for `ReminderActivated` events and sends a daily digest email. Keep it disabled by default so the how-to remains focused.

---

## 6. Journal-Friendly APIs

*Nightly journal entry: The co-op lead runs a few curl commands to publish the day’s snapshot before locking the gate.*

Give members three canonical endpoints:

```bash
# Record a new sensor reading
curl -X POST https://localhost:5001/api/garden/readings ^
  -H "Content-Type: application/json" ^
  -d '{"plotId":"plots/raspberry-bed","moisture":18.6}'

# See active reminders for the dashboard
curl https://localhost:5001/api/garden/reminders?status=Active

# Acknowledge a reminder after watering
curl -X PATCH https://localhost:5001/api/garden/reminders/reminders/42 ^
  -H "Content-Type: application/json" ^
  -d '{"status":"Acknowledged","notes":"Evening watering complete"}'
```

### Enriching responses on the fly

`EntityController<T>` already speaks the relationship enrichment dialect: append `?with=` to any GET and Koan will invoke `GetRelatives()` internally.

```bash
# Pull every reminder with its plot + member already expanded
curl "https://localhost:5001/api/garden/reminders?status=Active&with=plot,member"

# Or grab a single reminder with the blunt instrument
curl "https://localhost:5001/api/garden/reminders/reminders/42?with=all"

# Paging still works with enrichment
curl "https://localhost:5001/api/garden/reminders?status=Active&page=1&pageSize=10&with=all"
```

Use targeted lists (`with=plot,member`) when the UI only needs specific relatives; reach for `with=all` during diagnostics or when previewing the full relationship graph. Because the controller delegates to the same relationship helpers we outlined earlier, you can mix `with=` with streaming endpoints or paging without crafting bespoke transformers.

Use transformers (see the [API Delivery Playbook](./building-apis.md)) if you need to enrich reminder responses with plot or member metadata before rendering the journal UI.

---

## Next Steps

- **Chapter 2 preview** – Swap the adapter to MongoDB, keeping the same entity names while exploring document modeling and vector-friendly projections.
- **AI add-on** – Pair the reminders with semantic search using the [AI Integration Playbook](./ai-integration.md).
- **Troubleshooting** – If hydration scores stop moving, start with the [Troubleshooting Hub](../support/troubleshooting.md) to confirm Flow scheduling and adapter readiness.
