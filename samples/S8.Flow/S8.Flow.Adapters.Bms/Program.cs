using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Core;
using Sora.Messaging;
using Sora.Messaging.RabbitMq;
using S8.Flow.Shared;
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

// Minimal boot for a publisher-only process: Sora (Core) + Messaging (RabbitMQ). No Flow runtime, no DB.
builder.Services.AddSora();
builder.Services.AddMessagingCore();
// Minimal RabbitMQ wiring: register the bus factory only (no health contributor / inbox discovery)
builder.Services.AddSingleton<IMessageBusFactory, RabbitMqFactory>();
// No storage naming for producer-only process

builder.Services.AddHostedService<BmsPublisher>();
// Only identity stamper used to enrich FlowEvent payloads (optional)
builder.Services.AddFlowIdentityStamper();

var app = builder.Build();
// Set ambient host so MessagingExtensions can resolve the bus
AppHost.Current = app.Services;
Sora.Core.SoraEnv.TryInitialize(app.Services);
await app.RunAsync();

[FlowAdapter(system: "bms", adapter: "bms", DefaultSource = "bms")]
public sealed class BmsPublisher : BackgroundService
{
    private readonly ILogger<BmsPublisher> _log;
    public BmsPublisher(ILogger<BmsPublisher> log)
    { _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
    // Initial bulk seed on startup via MQ
    await SeedAllAsync(ct);

        var rng = new Random();
        var lastAnnounce = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var idx = rng.Next(0, SampleProfiles.Fleet.Length);
                var d = SampleProfiles.Fleet[idx];
                // Periodic independent announcements (devices + sensors)
                if (DateTimeOffset.UtcNow - lastAnnounce > TimeSpan.FromMinutes(5))
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
                    devEvent.SourceId = "bms";
                    devEvent.CorrelationId = bmsDeviceId;
                    foreach (var kv in deviceBag) devEvent.With(kv.Key, kv.Value);
                    await devEvent.Send(ct);

                    // Seed temperature sensor via FlowEvent
                    foreach (var s in SampleProfiles.SensorsForBms(d))
                    {
                        var sensorEvent = FlowEvent.ForModel("sensor")
                            .With(Keys.Sensor.Key, s.SensorKey)
                            .With("DeviceId", s.DeviceId)
                            .With(Keys.Sensor.Code, s.Code)
                            .With(Keys.Sensor.Unit, s.Unit)
                            .With("source", "bms");
                        sensorEvent.SourceId = "bms";
                        sensorEvent.CorrelationId = s.SensorKey;
                        await sensorEvent.Send(ct);
                    }
                    lastAnnounce = DateTimeOffset.UtcNow;
                    _log.LogInformation("BMS announced Device {Inv}/{Serial} and Sensor TEMP", d.Inventory, d.Serial);
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
                readingEvent.SourceId = "bms";
                readingEvent.CorrelationId = key;
                await readingEvent.Send(ct);
                _log.LogInformation("BMS sent Reading {Key}={Value}{Unit} at {At}", key, val, Units.C, at);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "BMS publish failed");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(1.5), ct); } catch (TaskCanceledException) { }
        }
    }

    // Iterate device catalog and publish normalized seeds via MQ
    private static async Task SeedAllAsync(CancellationToken ct)
    {
        foreach (var d in SampleProfiles.Fleet)
        {
            var bmsDeviceId = $"{d.Inventory}:{d.Serial}";
            var deviceEvent = FlowEvent.ForModel("device")
                .With(Keys.Device.Inventory, d.Inventory)
                .With(Keys.Device.Serial, d.Serial)
                .With(Keys.Device.Manufacturer, d.Manufacturer)
                .With(Keys.Device.Model, d.Model)
                .With(Keys.Device.Kind, d.Kind)
                .With(Keys.Device.Code, d.Code)
                .With("identifier.external.bms", bmsDeviceId)
                .With("source", "bms");
            deviceEvent.SourceId = "bms";
            deviceEvent.CorrelationId = bmsDeviceId;
            await deviceEvent.Send(ct);

            foreach (var s in SampleProfiles.SensorsForBms(d))
            {
                var sensorEvent = FlowEvent.ForModel("sensor")
                    .With(Keys.Sensor.Key, s.SensorKey)
                    .With("DeviceId", s.DeviceId)
                    .With(Keys.Sensor.Code, s.Code)
                    .With(Keys.Sensor.Unit, s.Unit)
                    .With("source", "bms");
                sensorEvent.SourceId = "bms";
                sensorEvent.CorrelationId = s.SensorKey;
                await sensorEvent.Send(ct);
            }
        }
    }
}
