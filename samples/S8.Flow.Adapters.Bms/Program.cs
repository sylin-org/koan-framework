using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Core;
using Sora.Messaging;
using S8.Flow.Shared;
using Sora.Data.Core;

var builder = Host.CreateApplicationBuilder(args);

if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Adapters.Bms is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.StartSora();

builder.Services.AddHostedService<BmsPublisher>();

var app = builder.Build();
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
                    inventory: d.Inventory,
                    serial: d.Serial,
                    manufacturer: d.Manufacturer,
                    model: d.Model,
                    kind: d.Kind,
                    code: d.Code,
                    sensorCode: SensorCodes.TEMP,
                    unit: Units.C,
                    value: Math.Round(20 + rng.NextDouble() * 10, 2),
                    source: "bms");
                // Publish using Sora.Messaging extension
                await evt.Send();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "BMS publish failed");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(1.5), stoppingToken); } catch (TaskCanceledException) { }
        }
    }
}
