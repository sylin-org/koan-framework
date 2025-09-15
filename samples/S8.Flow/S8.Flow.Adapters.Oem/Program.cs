using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Messaging;
using S8.Flow.Shared;
using Koan.Flow.Attributes;
using Koan.Flow.Model;
using Koan.Flow.Extensions;
using Koan.Data.Core;
using System.Collections.Generic;

var builder = Host.CreateApplicationBuilder(args);


if (!Koan.Core.KoanEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Adapters.Oem is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Services.AddKoan();

var app = builder.Build();
await app.RunAsync();


[FlowAdapter(system: FlowSampleConstants.Sources.Oem, adapter: FlowSampleConstants.Sources.Oem, DefaultSource = FlowSampleConstants.Sources.Oem)]
public sealed class OemPublisher : BackgroundService
{
    private readonly ILogger<OemPublisher> _log;
    public OemPublisher(ILogger<OemPublisher> log) { _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("[OEM] Starting with simplified sample data");

        // Get clean sample data
        var sampleData = SampleData.CreateSampleData();
        _log.LogInformation("[OEM] Created {DeviceCount} devices with {SensorCount} sensors each",
            sampleData.Count, sampleData.First().Value.Count);

        var rng = new Random();
        var lastAnnounce = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Send all devices and sensors periodically
                if (DateTimeOffset.UtcNow - lastAnnounce > TimeSpan.FromSeconds(30))
                {
                    _log.LogInformation("[OEM] Sending complete dataset");

                    // Send manufacturers using clean dictionary approach (OEM-specific data)
                    var mfg1 = new Dictionary<string, object>
                    {
                        ["identifier.code"] = "MFG001",
                        ["identifier.name"] = "Acme Corp",
                        ["identifier.external.oem"] = "OEM-VENDOR-42",
                        ["support.phone"] = "1-800-ACME",
                        ["support.email"] = "support@acme.com",
                        ["support.tier"] = "Premium",
                        ["certifications.iso9001"] = true,
                        ["certifications.iso14001"] = true,
                        ["warranty.standard"] = "2 years",
                        ["warranty.extended"] = "5 years"
                    };
                    _log.LogDebug("[OEM] Sending Manufacturer: {Code}", mfg1["identifier.code"]);
                    await mfg1.Send<Manufacturer>();

                    var mfg2 = new Dictionary<string, object>
                    {
                        ["identifier.code"] = "MFG002",
                        ["identifier.name"] = "TechCorp Industries",
                        ["identifier.external.oem"] = "OEM-VENDOR-88",
                        ["support.phone"] = "49-123-456789",
                        ["support.email"] = "info@techcorp.de",
                        ["support.tier"] = "Standard",
                        ["certifications.iso9001"] = true,
                        ["certifications.iso14001"] = false,
                        ["warranty.standard"] = "3 years",
                        ["warranty.extended"] = "7 years"
                    };
                    _log.LogDebug("[OEM] Sending Manufacturer: {Code}", mfg2["identifier.code"]);
                    await mfg2.Send<Manufacturer>();

                    foreach (var (deviceTemplate, sensorsTemplate) in sampleData)
                    {
                        // Clone and adjust device for OEM
                        var device = new Device
                        {
                            Id = "oem" + deviceTemplate.Id, // oemD1, oemD2, etc.
                            Inventory = deviceTemplate.Inventory,
                            Serial = deviceTemplate.Serial,
                            Manufacturer = deviceTemplate.Manufacturer,
                            Model = deviceTemplate.Model,
                            Kind = deviceTemplate.Kind,
                            Code = deviceTemplate.Code
                        };

                        _log.LogDebug("[OEM] Sending Device: {DeviceId}", device.Id);
                        await device.Send(cancellationToken: stoppingToken);

                        // Send all sensors for this device
                        foreach (var sensorTemplate in sensorsTemplate)
                        {
                            var sensor = new Sensor
                            {
                                Id = "oem" + sensorTemplate.Id, // oemS1, oemS2, etc.
                                DeviceId = "oem" + sensorTemplate.DeviceId, // oemDX
                                SensorId = "oem" + sensorTemplate.SensorId,
                                Code = sensorTemplate.Code,
                                Unit = sensorTemplate.Unit
                            };

                            _log.LogDebug("[OEM] Sending Sensor: {SensorId} -> Device: {DeviceId}",
                                sensor.Id, sensor.DeviceId);
                            await sensor.Send(cancellationToken: stoppingToken);
                        }
                    }

                    lastAnnounce = DateTimeOffset.UtcNow;
                    _log.LogInformation("[OEM] Complete dataset sent");
                }

                // Send random readings
                var readings = SampleData.CreateSampleReadings(5);
                foreach (var readingTemplate in readings)
                {
                    var reading = new Reading
                    {
                        SensorId = "oem" + readingTemplate.SensorId,
                        Value = readingTemplate.Value,
                        CapturedAt = readingTemplate.CapturedAt,
                        Unit = readingTemplate.Unit
                    };

                    _log.LogDebug("[OEM] Reading: {SensorId} = {Value}", reading.SensorId, reading.Value);
                    await reading.Send(cancellationToken: stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[OEM] Error in publish loop");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch (TaskCanceledException) { }
        }
    }

}
