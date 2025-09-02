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

var builder = Host.CreateApplicationBuilder(args);

if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Adapters.Oem is container-only. Use samples/S8.Compose/docker-compose.yml.");
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
// No storage naming needed; this producer publishes only messages

builder.Services.AddHostedService<OemPublisher>();
// Only identity stamper used to enrich FlowEvent payloads (optional)
builder.Services.AddFlowIdentityStamper();

var app = builder.Build();
// Set ambient host so MessagingExtensions can resolve the bus
AppHost.Current = app.Services;
Sora.Core.SoraEnv.TryInitialize(app.Services);
await app.RunAsync();

[FlowAdapter(system: "oem", adapter: "oem", DefaultSource = "oem")]
public sealed class OemPublisher : BackgroundService
{
    private readonly ILogger<OemPublisher> _log;
    public OemPublisher(ILogger<OemPublisher> log) { _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
    // Initial bulk seed on startup via MQ
    await SeedAllAsync(stoppingToken);

        var rng = new Random();
        var lastAnnounce = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var idx = rng.Next(0, SampleProfiles.Fleet.Length);
                var d = SampleProfiles.Fleet[idx];
                var code = rng.Next(0, 2) == 0 ? SensorCodes.PWR : SensorCodes.COOLANT_PRESSURE;
                var unit = code == SensorCodes.PWR ? Units.Watt : Units.KPa;
                var value = code == SensorCodes.PWR ? Math.Round(100 + rng.NextDouble() * 900, 2) : Math.Round(200 + rng.NextDouble() * 50, 2);
                // Periodic independent announcements
                if (DateTimeOffset.UtcNow - lastAnnounce > TimeSpan.FromMinutes(5))
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
                    devEvent.SourceId = "oem";
                    devEvent.CorrelationId = oemDeviceId;
                    foreach (var kv in deviceBag) devEvent.With(kv.Key, kv.Value);
                    await devEvent.Send(stoppingToken);

                    // Seed sensors via FlowEvent (pure MQ)
                    foreach (var s in SampleProfiles.SensorsForOem(d))
                    {
                        var sensorEvent = FlowEvent.ForModel("sensor")
                            .With(Keys.Sensor.Key, s.SensorKey)
                            .With("DeviceId", s.DeviceId)
                            .With(Keys.Sensor.Code, s.Code)
                            .With(Keys.Sensor.Unit, s.Unit)
                            .With("source", "oem");
                        sensorEvent.SourceId = "oem";
                        sensorEvent.CorrelationId = s.SensorKey;
                        await sensorEvent.Send(stoppingToken);
                    }
                    lastAnnounce = DateTimeOffset.UtcNow;
                    _log.LogInformation("OEM announced Device {Inv}/{Serial} and OEM sensors", d.Inventory, d.Serial);
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
                readingEvent.SourceId = "oem";
                readingEvent.CorrelationId = key;
                await readingEvent.Send(stoppingToken);
                _log.LogInformation("OEM sent Reading {Key}={Value}{Unit} at {At}", key, value, unit, at);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "OEM publish failed");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(2.5), stoppingToken); } catch (TaskCanceledException) { }
        }
    }

    // Iterate device catalog and publish normalized seeds via MQ
    private static async Task SeedAllAsync(CancellationToken ct)
    {
        foreach (var d in SampleProfiles.Fleet)
        {
            var oemDeviceId = $"{d.Inventory}:{d.Serial}";
            var deviceEvent = FlowEvent.ForModel("device")
                .With(Keys.Device.Inventory, d.Inventory)
                .With(Keys.Device.Serial, d.Serial)
                .With(Keys.Device.Manufacturer, d.Manufacturer)
                .With(Keys.Device.Model, d.Model)
                .With(Keys.Device.Kind, d.Kind)
                .With(Keys.Device.Code, d.Code)
                .With("identifier.external.oem", oemDeviceId)
                .With("source", "oem");
            deviceEvent.SourceId = "oem";
            deviceEvent.CorrelationId = oemDeviceId;
            await deviceEvent.Send(ct);

            foreach (var s in SampleProfiles.SensorsForOem(d))
            {
                var sensorEvent = FlowEvent.ForModel("sensor")
                    .With(Keys.Sensor.Key, s.SensorKey)
                    .With("DeviceId", s.DeviceId)
                    .With(Keys.Sensor.Code, s.Code)
                    .With(Keys.Sensor.Unit, s.Unit)
                    .With("source", "oem");
                sensorEvent.SourceId = "oem";
                sensorEvent.CorrelationId = s.SensorKey;
                await sensorEvent.Send(ct);
            }
        }
    }
}
