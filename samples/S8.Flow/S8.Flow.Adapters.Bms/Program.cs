using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Core;
using Sora.Messaging;
using Sora.Messaging.RabbitMq;
using S8.Flow.Shared;
using S8.Flow.Shared.Commands;
// removed events; use plain-bag seeds and direct VO Send()
using Sora.Flow.Actions;
using Sora.Core.Hosting.App;
using Sora.Flow.Attributes;
using Sora.Flow.Sending;
using Sora.Data.Core;
using Sora.Flow;

var builder = Host.CreateApplicationBuilder(args);


if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Adapters.Bms is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

// Minimal boot: Sora + auto-registrars wire Messaging Core, RabbitMQ, Flow identity stamper,
// and auto-start this adapter (BackgroundService with [FlowAdapter]) in container environments.
builder.Services.AddSora();
    builder.Services.AddRabbitMq();


// Register FlowCommand handler for 'seed' (target: bms)
builder.Services.AddFlowCommands(reg =>
    reg.On("seed", async (ctx, args, ct) => {
        var count = args.TryGetValue("count", out var v) ? Convert.ToInt32(v) : 1;
        var subset = SampleProfiles.Fleet.Take(Math.Min(count, SampleProfiles.Fleet.Length)).ToArray();
        await AdapterSeeding.SeedCatalogAsync(FlowSampleConstants.Sources.Bms, subset, SampleProfiles.SensorsForBms, ct);
    }, target: FlowSampleConstants.Sources.Bms)
);

var app = builder.Build();
await app.RunAsync();

[FlowAdapter(system: FlowSampleConstants.Sources.Bms, adapter: FlowSampleConstants.Sources.Bms, DefaultSource = FlowSampleConstants.Sources.Bms)]
public sealed class BmsPublisher : BackgroundService
{
    private readonly ILogger<BmsPublisher> _log;
    public BmsPublisher(ILogger<BmsPublisher> log)
    { _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("[BMS] Starting ExecuteAsync");
        // Initial bulk seed on startup via MQ (resilient to broker warm-up)
        _log.LogInformation("[BMS] Seeding catalog with {DeviceCount} devices", SampleProfiles.Fleet.Length);
        await AdapterSeeding.SeedCatalogWithRetryAsync(
            FlowSampleConstants.Sources.Bms,
            SampleProfiles.Fleet,
            SampleProfiles.SensorsForBms,
            _log,
            ct);

        var rng = new Random();
        var lastAnnounce = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var idx = rng.Next(0, SampleProfiles.Fleet.Length);
                var d = SampleProfiles.Fleet[idx];
                _log.LogDebug("[BMS] Preparing to announce Device {DeviceId}", d.DeviceId);
                // Periodic independent announcements (devices + sensors)
                if (DateTimeOffset.UtcNow - lastAnnounce > FlowSampleConstants.Timing.AnnouncementInterval)
                {
                    // Seed device via FlowEvent (plain bag)
                    var bmsDeviceId = $"{d.Inventory}:{d.Serial}";
                    var deviceBag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        [Keys.Device.Inventory] = d.Inventory,
                        [Keys.Device.Serial] = d.Serial,
                        [Keys.Device.Manufacturer] = d.Manufacturer,
                        [Keys.Device.Model] = d.Model,
                        [Keys.Device.Kind] = d.Kind,
                        [Keys.Device.Code] = d.Code,
                        ["identifier.external.bms"] = bmsDeviceId,
                        ["source"] = "bms",
                    };
                    var devEvent = FlowEvent.ForModel("device");
                    devEvent.SourceId = FlowSampleConstants.Sources.Bms;
                    devEvent.CorrelationId = bmsDeviceId;
                    foreach (var kv in deviceBag) devEvent.With(kv.Key, kv.Value);
                    _log.LogDebug("[BMS] Sending device event for {DeviceId}", d.DeviceId);
                    await devEvent.Send(ct);

                    // Seed temperature sensor via FlowEvent
                    foreach (var s in SampleProfiles.SensorsForBms(d))
                    {
                        _log.LogDebug("[BMS] Sending sensor event for {SensorKey}", s.SensorKey);
                        var sensorEvent = FlowEvent.ForModel("sensor")
                            .With(Keys.Sensor.Key, s.SensorKey)
                            .With("DeviceId", s.DeviceId)
                            .With(Keys.Sensor.Code, s.Code)
                            .With(Keys.Sensor.Unit, s.Unit)
                            .With("source", "bms");
                        sensorEvent.SourceId = FlowSampleConstants.Sources.Bms;
                        sensorEvent.CorrelationId = s.SensorKey;
                        await sensorEvent.Send(ct);
                    }
                    lastAnnounce = DateTimeOffset.UtcNow;
                    _log.LogInformation("[BMS] Announced Device {Inv}/{Serial} and Sensor TEMP", d.Inventory, d.Serial);
                }

                // Publish reading via FlowEvent
                var key = $"{d.Inventory}::{d.Serial}::{SensorCodes.TEMP}";
                var at = DateTimeOffset.UtcNow;
                var val = Math.Round(20 + rng.NextDouble() * 10, 2);
                var readingEvent = FlowEvent.ForModel("reading")
                    .With(Keys.Sensor.Key, key)
                    .With(Keys.Reading.Value, val)
                    .With(Keys.Reading.CapturedAt, at.ToString("O"))
                    .With(Keys.Sensor.Unit, Units.C)
                    .With(Keys.Reading.Source, "bms");
                readingEvent.SourceId = FlowSampleConstants.Sources.Bms;
                readingEvent.CorrelationId = key;
                await readingEvent.Send(ct);
                _log.LogInformation("BMS sent Reading {Key}={Value}{Unit} at {At}", key, val, Units.C, at);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "BMS publish failed");
            }
            try { await Task.Delay(FlowSampleConstants.Timing.BmsLoopDelay, ct); } catch (TaskCanceledException) { }
        }
    }
}
