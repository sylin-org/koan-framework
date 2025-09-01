using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Core;
using Sora.Messaging;
using Sora.Messaging.RabbitMq;
using S8.Flow.Shared;
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
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var idx = rng.Next(0, SampleProfiles.Fleet.Length);
                var d = SampleProfiles.Fleet[idx];
                var evt = TelemetryEvent.Reading(
                    system: "bms",
                    adapter: "bms",
                    sensorExternalId: $"{d.Inventory}::{d.Serial}::{SensorCodes.TEMP}",
                    unit: Units.C,
                    value: Math.Round(20 + rng.NextDouble() * 10, 2),
                    source: "bms");
                // Publish using Sora.Messaging extension
                await evt.Send();
                _log.LogInformation("BMS sent TelemetryEvent {Inv}/{Serial} {Sensor}={Value}{Unit} at {At}", d.Inventory, d.Serial, SensorCodes.TEMP, evt.Value, Units.C, evt.CapturedAt);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "BMS publish failed");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(1.5), stoppingToken); } catch (TaskCanceledException) { }
        }
    }
}
