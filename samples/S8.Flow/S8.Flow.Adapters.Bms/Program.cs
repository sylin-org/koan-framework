using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Core;
using Sora.Messaging;
using Sora.Messaging.RabbitMq;
using S8.Flow.Shared;
using S8.Flow.Shared.Events;
using Sora.Core.Hosting.App;

var builder = Host.CreateApplicationBuilder(args);

if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Adapters.Bms is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

// Minimal boot for a publisher-only process: Core + Messaging (RabbitMQ). No Flow runtime.
builder.Services.AddSoraCore();
builder.Services.AddMessagingCore();
// Minimal RabbitMQ wiring: register the bus factory only (no health contributor / inbox discovery)
builder.Services.AddSingleton<IMessageBusFactory, RabbitMqFactory>();

builder.Services.AddHostedService<BmsPublisher>();

var app = builder.Build();
// Set ambient host so MessagingExtensions can resolve the bus
AppHost.Current = app.Services;
Sora.Core.SoraEnv.TryInitialize(app.Services);
await app.RunAsync();

public sealed class BmsPublisher : BackgroundService
{
    private readonly ILogger<BmsPublisher> _log;
    public BmsPublisher(ILogger<BmsPublisher> log)
    { _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                    var dev = DeviceAnnounceEvent.FromProfile(system: "bms", adapter: "bms", profile: d, source: "bms");
                    await dev.Send();
                    var sensorKey = $"{d.Inventory}::{d.Serial}::{SensorCodes.TEMP}";
                    var sensor = SensorAnnounceEvent.Create(system: "bms", adapter: "bms", sensorKey: sensorKey, code: SensorCodes.TEMP, unit: Units.C, source: "bms");
                    await sensor.Send();
                    lastAnnounce = DateTimeOffset.UtcNow;
                    _log.LogInformation("BMS announced Device {Inv}/{Serial} and Sensor {Sensor}", d.Inventory, d.Serial, sensorKey);
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
}
