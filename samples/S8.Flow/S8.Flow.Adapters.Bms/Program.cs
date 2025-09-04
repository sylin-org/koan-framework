using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Core;
using Sora.Messaging;
using Sora.Messaging.RabbitMq;
using S8.Flow.Shared;
using S8.Flow.Shared.Commands;
using Sora.Flow.Actions;
using Sora.Core.Hosting.App;
using Sora.Flow.Attributes;
using Sora.Flow.Configuration;
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

// ✨ BEAUTIFUL NEW MESSAGING - ZERO CONFIGURATION! ✨
// Auto-registrars wire Messaging Core, RabbitMQ, Flow identity stamper,
// and auto-start this adapter (BackgroundService with [FlowAdapter]) in container environments.
builder.Services.AddSora();

// Listen for seed commands (using new messaging system)
builder.Services.On<FlowCommandMessage>(async cmd =>
{
    if (cmd.Command == "seed")
    {
        Console.WriteLine("🌱 BMS received seed command!");
        
        // Parse count from payload (if it's a dictionary)
        var count = 1;
        if (cmd.Payload is Dictionary<string, object> dict && dict.TryGetValue("count", out var v))
        {
            count = Convert.ToInt32(v);
        }
        
        var subset = SampleProfiles.Fleet.Take(Math.Min(count, SampleProfiles.Fleet.Length)).ToArray();
        
        // Send entities via beautiful messaging patterns
        foreach (var deviceProfile in subset)
        {
            var device = new Device
            {
                DeviceId = deviceProfile.DeviceId,
                Inventory = deviceProfile.Inventory,
                Serial = deviceProfile.Serial,
                Manufacturer = deviceProfile.Manufacturer,
                Model = deviceProfile.Model,
                Kind = deviceProfile.Kind,
                Code = deviceProfile.Code
            };
            
            await Sora.Flow.Sending.FlowEntitySendExtensions.Send(device); // ✨ Beautiful messaging-first seeding
            
            // Send sensors for this device
            foreach (var sensorProfile in SampleProfiles.SensorsForBms(deviceProfile))
            {
                var sensor = new Sensor
                {
                    SensorKey = sensorProfile.SensorKey,
                    DeviceId = sensorProfile.DeviceId,
                    Code = sensorProfile.Code,
                    Unit = sensorProfile.Unit
                };
                
                await Sora.Flow.Sending.FlowEntitySendExtensions.Send(sensor);
            }
        }
        
        Console.WriteLine($"✅ BMS seeded {subset.Length} devices via messaging");
    }
});

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
                // ✨ BEAUTIFUL NEW MESSAGING-FIRST PATTERNS ✨
                // Periodic device and sensor announcements
                if (DateTimeOffset.UtcNow - lastAnnounce > FlowSampleConstants.Timing.AnnouncementInterval)
                {
                    // Create and send Device entity through messaging system
                    var device = new Device
                    {
                        DeviceId = d.DeviceId,
                        Inventory = d.Inventory,
                        Serial = d.Serial,
                        Manufacturer = d.Manufacturer,
                        Model = d.Model,
                        Kind = d.Kind,
                        Code = d.Code
                    };
                    
                    _log.LogDebug("[BMS] 🏭 Sending Device entity for {DeviceId}", d.DeviceId);
                    await Sora.Flow.Sending.FlowEntitySendExtensions.Send(device, ct); // ✨ Routes through messaging → orchestrator → Flow intake

                    // Send Sensor entities
                    foreach (var s in SampleProfiles.SensorsForBms(d))
                    {
                        var sensor = new Sensor
                        {
                            SensorKey = s.SensorKey,
                            DeviceId = s.DeviceId,
                            Code = s.Code,
                            Unit = s.Unit
                        };
                        
                        _log.LogDebug("[BMS] 📡 Sending Sensor entity for {SensorKey}", s.SensorKey);
                        await Sora.Flow.Sending.FlowEntitySendExtensions.Send(sensor, ct); // ✨ Beautiful messaging-first routing
                    }
                    
                    lastAnnounce = DateTimeOffset.UtcNow;
                    _log.LogInformation("✅ [BMS] Announced Device {Inv}/{Serial} and sensors via messaging", d.Inventory, d.Serial);
                }

                // Send Reading value object through messaging
                var reading = new Reading
                {
                    SensorKey = $"{d.Inventory}::{d.Serial}::{SensorCodes.TEMP}",
                    Value = Math.Round(20 + rng.NextDouble() * 10, 2),
                    CapturedAt = DateTimeOffset.UtcNow,
                    Unit = Units.C,
                    Source = "bms"
                };
                
                _log.LogInformation("📊 BMS sending Reading {Key}={Value}{Unit} via messaging", reading.SensorKey, reading.Value, reading.Unit);
                await Sora.Flow.Sending.FlowValueObjectSendExtensions.Send(reading, ct); // ✨ Messaging-first: routes to orchestrator automatically
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "BMS publish failed");
            }
            try { await Task.Delay(FlowSampleConstants.Timing.BmsLoopDelay, ct); } catch (TaskCanceledException) { }
        }
    }
}
