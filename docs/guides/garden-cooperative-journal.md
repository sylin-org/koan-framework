---
type: GUIDE
domain: data
title: "Garden Cooperative Journal"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: tested
  scope: four-line host, Entity lifecycle automation, SQLite, REST, facts, and NativeAOT companion
---

# Garden Cooperative Journal

The Maple Street co-op wants a small application that speaks garden: sensors report soil humidity,
plots belong to stewards, and a dry bed creates one watering reminder. The Pis stay simple; the
application owns the meaning.

The complete, executable companion is
[`samples/guides/g1c1.GardenCoop`](../../samples/guides/g1c1.GardenCoop/).

## 1. Start with the whole host

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

That is not abbreviated tutorial code. It is the complete application host. Project/package references
provide SQLite, Web, OpenAPI, Admin, Auth, and the Development test-auth connector. `AddKoan()` discovers
and composes them with the application module.

From a checkout:

```pwsh
Set-Location samples/guides/g1c1.GardenCoop
dotnet run -- --urls http://localhost:5000
```

Open <http://localhost:5000>. A fresh local SQLite database is enough; no external service or required
configuration exists.

## 2. Name the garden

The models map garden language directly onto Entity:

```csharp
public sealed class Plot : Entity<Plot>
{
    public string Name { get; set; } = "";

    [Parent(typeof(Member))]
    public string? MemberId { get; set; }

    public string Notes { get; set; } = "";
}

public sealed class Sensor : Entity<Sensor, string>
{
    public string Serial => Id;
    public string DisplayName { get; set; } = "";

    [Parent(typeof(Plot))]
    public string? PlotId { get; set; }

    public SensorCapabilities Capabilities { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
}

public sealed class Reading : Entity<Reading>
{
    public string? SensorSerial { get; set; }

    [Parent(typeof(Sensor))]
    public string SensorId { get; set; } = "";

    [Parent(typeof(Plot))]
    public string? PlotId { get; set; }

    [Range(0, 100)]
    public double SoilHumidity { get; set; }
    public double? TemperatureC { get; set; }
    public DateTimeOffset SampledAt { get; set; } = DateTimeOffset.UtcNow;
}
```

The sensor serial is its natural key, so `Entity<Sensor, string>` avoids a second lookup identity. A
reading may arrive with only `SensorSerial`; lifecycle policy resolves the sensor and copies its plot
binding before persistence.

## 3. Put write meaning at the write boundary

Garden automation is persistence policy, so it uses `Entity.Lifecycle` rather than a controller or
background job:

```csharp
Reading.Lifecycle.BeforeUpsert(async context =>
{
    context.ProtectAll();
    context.AllowMutation(nameof(Reading.SensorId));
    context.AllowMutation(nameof(Reading.PlotId));

    var reading = context.Current;
    if (!string.IsNullOrWhiteSpace(reading.SensorSerial))
    {
        var sensor = await Sensor.Ensure(reading.SensorSerial, context.CancellationToken);
        sensor.LastSeenAt = reading.SampledAt;
        await sensor.Save(context.CancellationToken);

        reading.SensorId = sensor.Id;
        reading.PlotId = sensor.PlotId;
    }

    return context.Proceed();
});
```

An `AfterUpsert` rule reads the recent bounded window for that plot. Below the dry threshold it creates
one active reminder; after recovery it acknowledges that reminder. Another handler compares
`context.Prior`—the stable pre-write snapshot—with `context.Current` and narrates only the transition
into `Active`.

This placement has one useful guarantee: a reading written through Entity statics, `Data<T>`, generated
REST, or a generated agent tool receives the same policy.

## 4. Give composition one application owner

The application has real startup responsibilities, so one ordinary module owns them:

```csharp
public sealed class GardenCoopModule : KoanModule
{
    public override void Register(IServiceCollection services) => GardenAutomation.Configure();

    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<GardenCoopModule>();
        return GardenSeeder.EnsureSampleData(logger, ct);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version, "Garden sensor binding and watering-reminder automation.");
    }
}
```

There is no module descriptor, manual registrar, `AppHost` assignment, seeder runner, or callback in
`Program.cs`. The standard module base supplies the lifecycle; the standard .NET type and assembly supply
identity unless a real exception requires an override.

## 5. Observe the same story

A fresh start seeds three plots and three readings. Bed 3 is dry, so one active reminder is immediately
visible:

```pwsh
Invoke-RestMethod http://localhost:5000/api/garden/plots
Invoke-RestMethod http://localhost:5000/api/garden/reminders
Invoke-RestMethod http://localhost:5000/.well-known/Koan/facts
```

The dashboard, HTTP caller, startup report, facts endpoint, and executable sample test all observe the
same application. The focused contract also posts a recovery reading and proves that the active reminder
becomes acknowledged.

## 6. Deployment claim

GardenCoop has a measured win-x64 NativeAOT path. It publishes a self-contained native deployment
directory—not a falsely advertised physical single file—containing the executable, dashboard assets,
and SQLite native library. The exact command and current boundary live in the
[sample README](../../samples/guides/g1c1.GardenCoop/README.md) and the
[NativeAOT guide](nativeaot-howto.md).
