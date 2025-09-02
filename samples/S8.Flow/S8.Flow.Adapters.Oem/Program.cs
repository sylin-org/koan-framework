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
using Sora.Data.Core;
using Sora.Data.Mongo;

var builder = Host.CreateApplicationBuilder(args);

if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Adapters.Oem is container-only. Use samples/S8.Compose/docker-compose.yml.");
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

builder.Services.AddHostedService<OemPublisher>();
// Register Flow sender + identity stamper for entity/VO Send() with server-side stamping
builder.Services.AddFlowSender();
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
                var code = rng.Next(0, 2) == 0 ? SensorCodes.PWR : SensorCodes.COOLANT_PRESSURE;
                var unit = code == SensorCodes.PWR ? Units.Watt : Units.KPa;
                var value = code == SensorCodes.PWR ? Math.Round(100 + rng.NextDouble() * 900, 2) : Math.Round(200 + rng.NextDouble() * 50, 2);
                // Periodic independent announcements
                if (DateTimeOffset.UtcNow - lastAnnounce > TimeSpan.FromMinutes(5))
                {
                    // Plain-bag seed: device
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
                    var devKey = $"device:seed:{oemDeviceId}";
                    await new FlowAction(
                        Model: "device",
                        Verb: "seed",
                        ReferenceId: oemDeviceId,
                        Payload: deviceBag,
                        IdempotencyKey: devKey,
                        PartitionKey: "device",
                        CorrelationId: oemDeviceId)
                        .Send();

                    // Seed sensors via entity Send(); DefaultSource inferred (oem)
                    foreach (var s in SampleProfiles.SensorsForOem(d))
                        await s.Send();
                    lastAnnounce = DateTimeOffset.UtcNow;
                    _log.LogInformation("OEM announced Device {Inv}/{Serial} and OEM sensors", d.Inventory, d.Serial);
                }

                // Slim reading VO
                var key = $"{d.Inventory}::{d.Serial}::{code}";
                var reading = new SensorReadingVo { SensorKey = key, Value = value, Unit = unit, Source = "oem", CapturedAt = DateTimeOffset.UtcNow };
                await reading.Send();
                _log.LogInformation("OEM sent Reading {Key}={Value}{Unit} at {At}", key, value, unit, reading.CapturedAt);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "OEM publish failed");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(2.5), stoppingToken); } catch (TaskCanceledException) { }
        }
    }

    // Iterate device catalog and send normalized seeds. Keep sensors via FlowAction for explicit external refs.
    private static async Task SeedAllAsync(CancellationToken ct)
    {
    // occurredAt defaults to a single UtcNow captured per batch inside Send().
    await SampleProfiles.Fleet.Send(ct: ct);

        foreach (var d in SampleProfiles.Fleet)
            foreach (var s in SampleProfiles.SensorsForOem(d))
                await s.Send(ct: ct);
    }
}
