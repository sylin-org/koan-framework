# Sylin.Sora.Scheduling

Lightweight in-process scheduling primitives for Sora apps.

- Target framework: net9.0
- License: Apache-2.0

## Capabilities
- In-process scheduler hosted as a BackgroundService.
- Triggers: OnStartup, FixedDelay (cron reserved for Phase 2).
- Per-job policy via options, attribute hints, and task interfaces.
- Bounded concurrency, per-run timeout, health updates via Sora.Core.

## Install

```powershell
dotnet add package Sylin.Sora.Scheduling
```

## Minimal setup

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register the scheduler via Sora.Core auto-registrar
// (SoraAutoRegistrar wires SchedulingOptions and the HostedService)
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
```

appsettings.json

```json
{
	"Sora": {
		"Scheduling": {
			"Enabled": true,
			"Jobs": {
				"cleanup": {
					"OnStartup": true,
					"FixedDelay": "00:00:10",
					"Timeout": "00:00:05",
					"MaxConcurrency": 1
				}
			}
		}
	}
}
```

## Authoring tasks

```csharp
public sealed class CleanupTask : IScheduledTask, IFixedDelay
{
		public string Id => "cleanup";
		public TimeSpan Delay => TimeSpan.FromSeconds(10);
		public Task RunAsync(CancellationToken ct) { /* work */ return Task.CompletedTask; }
}
```

Hints
- Use `IHasTimeout` for bounded work; `IHasMaxConcurrency` to allow parallel runs.
- Mark critical tasks with `IIsCritical` or `[Scheduled(Critical = true)]` to influence health/runbooks.
- Prefer idempotent work; handle cancellations.

## References
- Technical reference: `./TECHNICAL.md`
- Engineering front door: `/docs/engineering/index.md`
