using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Messaging;
using S8.Flow.Shared;
using Sora.Flow.Attributes;
using Sora.Flow.Model;
using Sora.Data.Core;

var builder = Host.CreateApplicationBuilder(args);


if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Adapters.Oem is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Services.AddSora();

var app = builder.Build();
await app.RunAsync();


[FlowAdapter(system: FlowSampleConstants.Sources.Oem, adapter: FlowSampleConstants.Sources.Oem, DefaultSource = FlowSampleConstants.Sources.Oem)]
public sealed class OemPublisher : BackgroundService
{
    private readonly ILogger<OemPublisher> _log;
    public OemPublisher(ILogger<OemPublisher> log) { _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("[OEM] Starting ExecuteAsync");
        // Initial bulk seed on startup via MQ (resilient to broker warm-up)
        _log.LogInformation("[OEM] Seeding catalog with {DeviceCount} devices", SampleProfiles.Fleet.Length);
        await AdapterSeeding.SeedCatalogWithRetryAsync(
            FlowSampleConstants.Sources.Oem,
            SampleProfiles.Fleet,
            SampleProfiles.SensorsForOem,
            _log,
            stoppingToken);

        // Send initial manufacturer data using new dynamic capabilities
        _log.LogInformation("[OEM] Sending manufacturer support and certification data");
        await SendManufacturerData();

        var rng = new Random();
        var lastAnnounce = DateTimeOffset.MinValue;
        var lastManufacturerUpdate = DateTimeOffset.MinValue;
        var deviceIndex = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Cycle through ALL devices instead of random selection
                var d = SampleProfiles.Fleet[deviceIndex];
                deviceIndex = (deviceIndex + 1) % SampleProfiles.Fleet.Length;
                _log.LogInformation("[OEM] Processing Device {DeviceId} (Index: {Index}/{Total})", d.Id, deviceIndex, SampleProfiles.Fleet.Length);
                // ✨ BEAUTIFUL NEW MESSAGING-FIRST PATTERNS ✨
                // Periodic device and sensor announcements
                if (DateTimeOffset.UtcNow - lastAnnounce > FlowSampleConstants.Timing.AnnouncementInterval)
                {
                    // Create and send Device entity through messaging system
                    var device = new Device
                    {
                        Id = d.Id,
                        Inventory = d.Inventory,
                        Serial = d.Serial,
                        Manufacturer = d.Manufacturer,
                        Model = d.Model,
                        Kind = d.Kind,
                        Code = d.Code
                    };

                    _log.LogInformation("[OEM] 🏭 Sending Device entity: Id={DeviceId}, Serial={Serial}, Inventory={Inventory}", d.Id, device.Serial, device.Inventory);
                    await device.Send(cancellationToken: stoppingToken); // Direct entity sending with automatic transport wrapping
                    _log.LogInformation("[OEM] Device sent successfully");

                    // Send Sensor entities
                    foreach (var s in SampleProfiles.SensorsForOem(d))
                    {
                        var sensor = new Sensor
                        {
                            Id = s.Id,
                            SensorKey = s.SensorKey,
                            DeviceId = s.DeviceId,
                            Code = s.Code,
                            Unit = s.Unit
                        };

                        _log.LogTrace("[OEM] Sensor entity: {SensorKey}", s.SensorKey);
                        await sensor.Send(cancellationToken: stoppingToken); // Direct entity sending with automatic transport wrapping
                    }

                    lastAnnounce = DateTimeOffset.UtcNow;
                    _log.LogDebug("[OEM] Device {Inv}/{Serial} announced with sensors", d.Inventory, d.Serial);
                }

                // Send readings for ALL sensors of this device
                foreach (var sensor in SampleProfiles.SensorsForOem(d))
                {
                    double value;
                    switch (sensor.Code)
                    {
                        case SensorCodes.TEMP:
                            value = Math.Round(20 + rng.NextDouble() * 10, 2);
                            break;
                        case SensorCodes.PWR:
                            value = Math.Round(100 + rng.NextDouble() * 900, 2);
                            break;
                        case SensorCodes.COOLANT_PRESSURE:
                            value = Math.Round(200 + rng.NextDouble() * 50, 2);
                            break;
                        default:
                            value = 0;
                            break;
                    }

                    var reading = new Reading
                    {
                        SensorKey = sensor.SensorKey,
                        Value = value,
                        CapturedAt = DateTimeOffset.UtcNow,
                        Unit = sensor.Unit,
                        Source = "oem"
                    };

                    _log.LogTrace("[OEM] Reading: {SensorKey}={Value}{Unit}", reading.SensorKey, reading.Value, reading.Unit);
                    await reading.Send(cancellationToken: stoppingToken);
                }

                // Periodically update manufacturer data (every 5 minutes)
                if (DateTimeOffset.UtcNow - lastManufacturerUpdate > TimeSpan.FromMinutes(5))
                {
                    _log.LogDebug("[OEM] Updating manufacturer support data");
                    await SendManufacturerData();
                    lastManufacturerUpdate = DateTimeOffset.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "OEM publish failed");
            }
            try { await Task.Delay(FlowSampleConstants.Timing.OemLoopDelay, stoppingToken); } catch (TaskCanceledException) { }
        }
    }

    private async Task SendManufacturerData()
    {
        // OEM provides support, warranty, and certification data for manufacturers
        // Using nested anonymous objects for a different DX pattern
        var manufacturers = new object[]
        {
            new
            {
                identifier = new
                {
                    code = "MFG001",
                    name = "Acme Corp",
                    external = new { oem = "OEM-VENDOR-42" }
                },
                support = new
                {
                    phone = "1-800-ACME",
                    email = "support@acme.com",
                    tier = "Premium",
                    hours = "24/7",
                    sla = "4 hour response"
                },
                certifications = new
                {
                    iso9001 = true,
                    iso14001 = true,
                    ce = true,
                    ul = true
                },
                warranty = new
                {
                    standard = "2 years",
                    extended = "5 years",
                    coverage = "Parts and labor"
                }
            },
            new
            {
                identifier = new
                {
                    code = "MFG002",
                    name = "TechFlow Industries",
                    external = new { oem = "OEM-VENDOR-88" }
                },
                support = new
                {
                    phone = "+49-30-555-0100",
                    email = "hilfe@techflow.de",
                    tier = "Standard",
                    hours = "Mon-Fri 8AM-6PM CET",
                    sla = "24 hour response"
                },
                certifications = new
                {
                    iso9001 = true,
                    iso14001 = false,
                    ce = true,
                    ul = false
                },
                warranty = new
                {
                    standard = "1 year",
                    extended = "3 years",
                    coverage = "Parts only"
                }
            },
            new
            {
                identifier = new
                {
                    code = "MFG003",
                    name = "Precision Dynamics",
                    external = new { oem = "OEM-VENDOR-123" }
                },
                support = new
                {
                    phone = "+81-3-5555-0001",
                    email = "support@precision-dynamics.jp",
                    tier = "Platinum",
                    hours = "24/7 with dedicated engineer",
                    sla = "1 hour response"
                },
                certifications = new
                {
                    iso9001 = true,
                    iso14001 = true,
                    ce = true,
                    ul = true,
                    jis = true
                },
                warranty = new
                {
                    standard = "3 years",
                    extended = "10 years",
                    coverage = "Full replacement"
                },
                partnership = new
                {
                    level = "Strategic",
                    discount = "15%",
                    priority = true
                }
            }
        };

        foreach (var mfgData in manufacturers)
        {
            try
            {
                // Create DynamicFlowEntity and send via transport envelope
                var manufacturer = mfgData.ToDynamicFlowEntity<Manufacturer>();

                dynamic mfg = mfgData;
                string code = mfg.identifier.code;
                string name = mfg.identifier.name;
                _log.LogInformation("[OEM] DEBUG: Before calling manufacturer.Send() for {Code} ({Name})", code, name);
                _log.LogInformation("[OEM] DEBUG: manufacturer type: {Type}, FullName: {FullName}", manufacturer.GetType().Name, manufacturer.GetType().FullName);
                _log.LogInformation("[OEM] DEBUG: manufacturer base type: {BaseType}", manufacturer.GetType().BaseType?.FullName);
                _log.LogInformation("[OEM] DEBUG: Is IDynamicFlowEntity: {IsDynamic}", manufacturer is IDynamicFlowEntity);

                await manufacturer.Send();

                _log.LogInformation("[OEM] DEBUG: After calling manufacturer.Send() for {Code} ({Name})", code, name);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[OEM] Failed to send manufacturer data");
            }
        }
    }
}
