# Pick an idea. Make it run.

Choose the result that sounds fun. Every sample starts as an ordinary .NET application, runs locally,
and keeps the interesting code about the thing being built.

## Feel the magic

### Entity. Controller. Agent.

[FirstUse](FirstUse/README.md) turns one `Approval` model—with a one-line controller and one agent
declaration—into persisted data, an HTTP API, and a governed tool. Start here for the smallest
complete expression of Koan.

```powershell
dotnet run --project samples/FirstUse
```

### Let a simple idea grow

[GoldenJourney](GoldenJourney/README.md) begins with a review request, then adds a business rule,
durable background work, and a bounded agent recommendation. The domain stays recognizable at every
step.

```powershell
dotnet run --project samples/GoldenJourney
```

## Choose your story

| I want to… | Run this |
|---|---|
| Turn dry garden readings into watering reminders | [Garden Journal](journeys/GardenCoop/01-GardenJournal/README.md) |
| Search local produce with ordinary words—without Docker, an API key, or a vector server | [GardenCoop: Local Discovery](journeys/GardenCoop/02-LocalDiscovery/README.md) |
| Upload a photo and watch a durable workflow organize it into a private gallery | [SnapVault](applications/SnapVault/README.md) |
| Turn messy customer arrivals into one trusted customer—or reject them with reasons | [CustomerCanon](applications/CustomerCanon/README.md) |
| Approve articles and publish them to another named data source | [DevPortal](applications/DevPortal/README.md) |
| Run a batch, verify every order, clean it up, and keep an honest receipt | [OrderIntake](applications/OrderIntake/README.md) |

[Explore the complete GardenCoop journey](journeys/GardenCoop/README.md) to watch one useful
application learn something new without losing what already worked.

## Take one small bite

- [LocalChecklist](fundamentals/LocalChecklist/README.md) saves a checklist, completes an item, and
  reloads the work in a tiny console application.
- [TaskGraph](fundamentals/TaskGraph/README.md) makes relationships readable across one Entity, a
  set, and a stream.

Run any sample from the repository root with the command on its page. On Windows, its `start.bat`
does the same thing from any directory and forwards any application arguments.

These applications target the Koan 0.20 preview. See [what works today](../docs/reference/what-works.md)
when deciding what to add to your own application.

Want to see Koan inside a standalone product? Explore
[Usagi Picks](https://github.com/lbotinelly/usagipicks), an agent-guided recommendation experience.
