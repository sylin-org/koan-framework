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
    Console.Error.WriteLine("S8.Flow.Adapters.Oem is container-only. Use samples/S8.Compose/docker-compose.yml.");
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

builder.Services.AddHostedService<OemPublisher>();

var app = builder.Build();
// Set ambient host so MessagingExtensions can resolve the bus
AppHost.Current = app.Services;
Sora.Core.SoraEnv.TryInitialize(app.Services);
await app.RunAsync();

public sealed class OemPublisher : BackgroundService
{
    private readonly ILogger<OemPublisher> _log;
    public OemPublisher(ILogger<OemPublisher> log) { _log = log; }

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
                var code = rng.Next(0, 2) == 0 ? SensorCodes.PWR : SensorCodes.COOLANT_PRESSURE;
                var unit = code == SensorCodes.PWR ? Units.Watt : Units.KPa;
                var value = code == SensorCodes.PWR ? Math.Round(100 + rng.NextDouble() * 900, 2) : Math.Round(200 + rng.NextDouble() * 50, 2);
                // Periodic independent announcements
                if (DateTimeOffset.UtcNow - lastAnnounce > TimeSpan.FromMinutes(5))
                {
                    var dev = DeviceAnnounceEvent.FromProfile(system: "oem", adapter: "oem", profile: d, source: "oem");
                    await dev.Send();
                    var sensorKeyTemp = $"{d.Inventory}::{d.Serial}::{SensorCodes.PWR}";
                    await SensorAnnounceEvent.Create(system: "oem", adapter: "oem", sensorKey: sensorKeyTemp, code: SensorCodes.PWR, unit: Units.Watt, source: "oem").Send();
                    var sensorKeyCool = $"{d.Inventory}::{d.Serial}::{SensorCodes.COOLANT_PRESSURE}";
                    await SensorAnnounceEvent.Create(system: "oem", adapter: "oem", sensorKey: sensorKeyCool, code: SensorCodes.COOLANT_PRESSURE, unit: Units.KPa, source: "oem").Send();
                    lastAnnounce = DateTimeOffset.UtcNow;
                    _log.LogInformation("OEM announced Device {Inv}/{Serial} and Sensors {A}/{B}", d.Inventory, d.Serial, sensorKeyTemp, sensorKeyCool);
                }

                // Slim reading VO
                var key = $"{d.Inventory}::{d.Serial}::{code}";
                var reading = ReadingEvent.Create(sensorKey: key, value: value, unit: unit, source: "oem");
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
}
