using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Core;
using Sora.Messaging;
using Sora.Messaging.RabbitMq;
using S8.Flow.Shared;
using Sora.Core.Hosting.App;
using Sora.Flow.Actions;
using Sora.Flow.Sending;
using Sora.Flow.Attributes;
using Sora.Flow;
using Sora.Data.Core; // AddSora()
using S8.Flow.Shared.Commands;

var builder = Host.CreateApplicationBuilder(args);


if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Adapters.Oem is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

// Minimal boot: Sora + auto-registrars provide Messaging Core, RabbitMQ, Flow identity stamper,
// and auto-start this adapter (BackgroundService with [FlowAdapter]) in container environments.
builder.Services.AddSora();
    builder.Services.AddRabbitMq();


// Register FlowCommand handler for 'seed' (target: oem)
builder.Services.AddFlowCommands(reg =>
    reg.On("seed", async (ctx, args, ct) => {
        var count = args.TryGetValue("count", out var v) ? Convert.ToInt32(v) : 1;
        var subset = SampleProfiles.Fleet.Take(Math.Min(count, SampleProfiles.Fleet.Length)).ToArray();
        await AdapterSeeding.SeedCatalogAsync(FlowSampleConstants.Sources.Oem, subset, SampleProfiles.SensorsForOem, ct);
    }, target: FlowSampleConstants.Sources.Oem)
);

var app = builder.Build();
await app.RunAsync();

[FlowAdapter(system: FlowSampleConstants.Sources.Oem, adapter: FlowSampleConstants.Sources.Oem, DefaultSource = FlowSampleConstants.Sources.Oem)]
public sealed class OemPublisher : BackgroundService
{
    private readonly ILogger<OemPublisher> _log;
    public OemPublisher(ILogger<OemPublisher> log) { _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("[OEM] Starting ExecuteAsync");
        // Initial bulk seed on startup via MQ (resilient to broker warm-up)
        _log.LogInformation("[OEM] Seeding catalog with {DeviceCount} devices", SampleProfiles.Fleet.Length);
        await AdapterSeeding.SeedCatalogWithRetryAsync(
            FlowSampleConstants.Sources.Oem,
            SampleProfiles.Fleet,
            SampleProfiles.SensorsForOem,
            _log,
            stoppingToken);

        var rng = new Random();
        var lastAnnounce = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var idx = rng.Next(0, SampleProfiles.Fleet.Length);
                var d = SampleProfiles.Fleet[idx];
                _log.LogDebug("[OEM] Preparing to announce Device {DeviceId}", d.DeviceId);
                var code = rng.Next(0, 2) == 0 ? SensorCodes.PWR : SensorCodes.COOLANT_PRESSURE;
                var unit = code == SensorCodes.PWR ? Units.Watt : Units.KPa;
                var value = code == SensorCodes.PWR ? Math.Round(100 + rng.NextDouble() * 900, 2) : Math.Round(200 + rng.NextDouble() * 50, 2);
                // Periodic independent announcements
                if (DateTimeOffset.UtcNow - lastAnnounce > FlowSampleConstants.Timing.AnnouncementInterval)
                {
                    // Seed Device via FlowEvent (pure MQ)
                    var oemDeviceId = $"{d.Inventory}:{d.Serial}";
                    var deviceBag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        [Keys.Device.Inventory] = d.Inventory,
                        [Keys.Device.Serial] = d.Serial,
                        [Keys.Device.Manufacturer] = d.Manufacturer,
                        [Keys.Device.Model] = d.Model,
                        [Keys.Device.Kind] = d.Kind,
                        [Keys.Device.Code] = d.Code,
                        ["identifier.external.oem"] = oemDeviceId,
                        ["source"] = "oem",
                    };
                    var devEvent = FlowEvent.ForModel("device");
                    devEvent.SourceId = FlowSampleConstants.Sources.Oem;
                    devEvent.CorrelationId = oemDeviceId;
                    foreach (var kv in deviceBag) devEvent.With(kv.Key, kv.Value);
                    _log.LogDebug("[OEM] Sending device event for {DeviceId}", d.DeviceId);
                    await devEvent.Send(stoppingToken);

                    // Seed sensors via FlowEvent (pure MQ)
                    foreach (var s in SampleProfiles.SensorsForOem(d))
                    {
                        _log.LogDebug("[OEM] Sending sensor event for {SensorKey}", s.SensorKey);
                        var sensorEvent = FlowEvent.ForModel("sensor")
                            .With(Keys.Sensor.Key, s.SensorKey)
                            .With("DeviceId", s.DeviceId)
                            .With(Keys.Sensor.Code, s.Code)
                            .With(Keys.Sensor.Unit, s.Unit)
                            .With("source", "oem");
                        sensorEvent.SourceId = FlowSampleConstants.Sources.Oem;
                        sensorEvent.CorrelationId = s.SensorKey;
                        await sensorEvent.Send(stoppingToken);
                    }
                    lastAnnounce = DateTimeOffset.UtcNow;
                    _log.LogInformation("[OEM] Announced Device {Inv}/{Serial} and Sensors", d.Inventory, d.Serial);
                }

                // Slim reading VO
                var key = $"{d.Inventory}::{d.Serial}::{code}";
                // Publish reading via FlowEvent
                var at = DateTimeOffset.UtcNow;
                var readingEvent = FlowEvent.ForModel("reading")
                    .With(Keys.Sensor.Key, key)
                    .With(Keys.Reading.Value, value)
                    .With(Keys.Reading.CapturedAt, at.ToString("O"))
                    .With(Keys.Sensor.Unit, unit)
                    .With(Keys.Reading.Source, "oem");
                readingEvent.SourceId = FlowSampleConstants.Sources.Oem;
                readingEvent.CorrelationId = key;
                await readingEvent.Send(stoppingToken);
                _log.LogInformation("OEM sent Reading {Key}={Value}{Unit} at {At}", key, value, unit, at);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "OEM publish failed");
            }
            try { await Task.Delay(FlowSampleConstants.Timing.OemLoopDelay, stoppingToken); } catch (TaskCanceledException) { }
        }
    }
}
