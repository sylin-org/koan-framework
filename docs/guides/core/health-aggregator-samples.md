# Health aggregator — samples

This page shows minimal, copy-pasteable usage for the Health Aggregator.

## 1) Configure via appsettings.json

```json
{
  "Sora": {
    "Health": {
      "Aggregator": {
        "Enabled": true,
        "Scheduler": {
          "EnableTtlScheduling": true,
          "QuantizationWindow": "00:00:02",
          "JitterPercent": 0.10,
          "JitterAbsoluteMin": "00:00:00.100"
        },
        "Ttl": { "MinTtl": "00:00:01", "MaxTtl": "01:00:00" },
        "Policy": { "SnapshotStalenessWindow": "00:00:30", "RequiredComponents": ["core","data","mq"] }
      }
    }
  }
}
```

## 2) Register the aggregator in DI (Program.cs)

```csharp
var builder = WebApplication.CreateBuilder(args);

// Bind options
builder.Services.Configure<HealthAggregatorOptions>(builder.Configuration.GetSection("Sora:Health:Aggregator"));

// Add the aggregator service
builder.Services.AddSingleton<IHealthAggregator, HealthAggregator>();
// Optional: hosted scheduler
builder.Services.AddHostedService<HealthAggregatorScheduler>();

var app = builder.Build();
```

## 3) Minimal contributor: push on probe invite (broadcast or targeted)

```csharp
public sealed class AiHealthContributor
{
    private readonly IHealthAggregator _health;

  public AiHealthContributor(IHealthAggregator health)
  {
    _health = health;
    // Either generic event...
    _health.ProbeRequested += OnProbeRequested;
    // ...or component-scoped subscription to avoid fan-out when targeted
    _health.Subscribe("ai", _ => PushAi());
  }

    private void OnProbeRequested(object? sender, ProbeRequestedEventArgs e)
    {
        if (e.Component != null && !string.Equals(e.Component, "ai", StringComparison.OrdinalIgnoreCase))
            return; // ignore probes for other components

    PushAi();
    }

  private void PushAi()
  {
    // Perform a quick, non-blocking check if needed, then push.
    _health.Push(
      component: "ai",
      status: HealthStatus.Healthy,
      message: "ok",
      ttl: TimeSpan.FromSeconds(30),
      facts: new Dictionary<string,string> { ["adaptersReady"] = "2/2" }
    );
  }
}
```

## 4) Invite contributors at startup and on demand

```csharp
// On app start
app.Services.GetRequiredService<IHealthAggregator>()
    .RequestProbe(ProbeReason.Startup);

// On demand (e.g., admin controller) — broadcast
[HttpPost("/ops/health/probe")] 
public IActionResult Probe([FromServices] IHealthAggregator agg)
{
    agg.RequestProbe(ProbeReason.Manual);
    return Accepted();
}

// On demand — targeted component only
[HttpPost("/ops/health/probe/{component}")]
public IActionResult Probe([FromServices] IHealthAggregator agg, string component)
{
  agg.RequestProbe(ProbeReason.Manual, component);
  return Accepted();
}
```

## 5) Read in a controller

```csharp
[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly IHealthAggregator _health;

    public HealthController(IHealthAggregator health) => _health = health;

    [HttpGet]
    public ActionResult<HealthSnapshot> Get() => Ok(_health.GetSnapshot());
}
```

Notes
- The aggregator keeps reads cheap and out-of-band. Handlers should be fast; offload slow work.
- TTLs apply only when supplied by the component in `Push`.
- With the scheduler running, TTL-driven components will be re-invited near expiry.
