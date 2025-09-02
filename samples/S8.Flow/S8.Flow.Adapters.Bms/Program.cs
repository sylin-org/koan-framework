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
using Sora.Data.Mongo;

var builder = Host.CreateApplicationBuilder(args);

if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Adapters.Bms is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

// Minimal boot for a publisher-only process: Sora (Core + Data) + Messaging (RabbitMQ). No Flow runtime.
builder.Services.AddSora();
builder.Services.AddMessagingCore();
// Minimal RabbitMQ wiring: register the bus factory only (no health contributor / inbox discovery)
builder.Services.AddSingleton<IMessageBusFactory, RabbitMqFactory>();
// Register Mongo data adapter so StageRecord<> Upsert resolves centrally (shared DB in compose)
builder.Services.AddMongoAdapter(o =>
{
    o.ConnectionString = builder.Configuration.GetConnectionString("Default") ?? o.ConnectionString;
});

builder.Services.AddHostedService<BmsPublisher>();
// Register Flow sender + identity stamper for entity/VO Send() with server-side stamping
builder.Services.AddFlowSender();
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
    // Initial bulk seed on startup
    await SeedAllAsync(stoppingToken);

        var rng = new Random();
        var lastAnnounce = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var idx = rng.Next(0, SampleProfiles.Fleet.Length);
                var d = SampleProfiles.Fleet[idx];
                // Periodic independent announcements (devices + sensors)
                if (DateTimeOffset.UtcNow - lastAnnounce > TimeSpan.FromMinutes(5))
                {
                    // Seed device via FlowAction (plain bag)
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
                    await new FlowAction("device", "seed", bmsDeviceId, deviceBag, $"device:seed:{bmsDeviceId}", "device", bmsDeviceId).Send();

                    // Seed temperature sensor via entity Send(); DefaultSource inferred (bms)
                    foreach (var s in SampleProfiles.SensorsForBms(d))
                        await s.Send();
                    lastAnnounce = DateTimeOffset.UtcNow;
                    _log.LogInformation("BMS announced Device {Inv}/{Serial} and Sensor TEMP", d.Inventory, d.Serial);
                }

                // Fast-tracked reading VO: only key + values
                var key = $"{d.Inventory}::{d.Serial}::{SensorCodes.TEMP}";
                var reading = new SensorReadingVo
                {
                    SensorKey = key,
                    Value = Math.Round(20 + rng.NextDouble() * 10, 2),
                    Unit = Units.C,
                    Source = "bms",
                    CapturedAt = DateTimeOffset.UtcNow
                };
                await reading.Send();
                _log.LogInformation("BMS sent Reading {Key}={Value}{Unit} at {At}", key, reading.Value, Units.C, reading.CapturedAt);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "BMS publish failed");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(1.5), stoppingToken); } catch (TaskCanceledException) { }
        }
    }

    // Iterate device catalog and send normalized seeds. Keep sensors as FlowAction to preserve external reference mapping.
    private static async Task SeedAllAsync(CancellationToken ct)
    {
    // Seed devices directly; Fleet already contains Device instances.
    // occurredAt defaults to a single UtcNow captured per batch inside Send().
    await SampleProfiles.Fleet.Send(ct: ct);

        // Seed sensors via entity Send()
        foreach (var d in SampleProfiles.Fleet)
            foreach (var s in SampleProfiles.SensorsForBms(d))
                await s.Send(ct: ct);
    }
}
