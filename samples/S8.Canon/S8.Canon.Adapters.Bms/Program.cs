using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Messaging;
using Koan.Messaging.Connector.RabbitMq;
using S8.Canon.Shared;
using Koan.Canon.Actions;
using Koan.Canon.Extensions;
using Koan.Core.Hosting.App;
using Koan.Canon.Attributes;
using Koan.Data.Core;
using Koan.Canon.Model;
using System.Collections.Generic;

var builder = Host.CreateApplicationBuilder(args);


if (!Koan.Core.KoanEnv.InContainer)
{
    Console.Error.WriteLine("S8.Canon.Adapters.Bms is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

// Koan framework with auto-configuration
builder.Services.AddKoan();


var app = builder.Build();
await app.RunAsync();

[CanonAdapter(system: FlowSampleConstants.Sources.Bms, adapter: FlowSampleConstants.Sources.Bms, DefaultSource = FlowSampleConstants.Sources.Bms)]
public sealed class BmsPublisher : BackgroundService
{
    private readonly ILogger<BmsPublisher> _log;
    public BmsPublisher(ILogger<BmsPublisher> log)
    { _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("[BMS] Starting with simplified sample data");

        // Get clean sample data
        var sampleData = SampleData.CreateSampleData();
        _log.LogInformation("[BMS] Created {DeviceCount} devices with {SensorCount} sensors each",
            sampleData.Count, sampleData.First().Value.Count);

        var rng = new Random();
        var lastAnnounce = DateTimeOffset.MinValue;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Send all devices and sensors periodically
                if (DateTimeOffset.UtcNow - lastAnnounce > TimeSpan.FromSeconds(30))
                {
                    _log.LogInformation("[BMS] Sending complete dataset");

                    // Send manufacturers using clean dictionary approach
                    var mfg1 = new Dictionary<string, object>
                    {
                        ["identifier.code"] = "MFG001",
                        ["identifier.name"] = "Acme Corp",
                        ["identifier.external.bms"] = "BMS-MFG-001",
                        ["manufacturing.country"] = "USA",
                        ["manufacturing.established"] = "1985",
                        ["manufacturing.facilities"] = new[] { "Plant A", "Plant B" },
                        ["products.categories"] = new[] { "sensors", "actuators" }
                    };
                    _log.LogDebug("[BMS] Sending Manufacturer: {Code}", mfg1["identifier.code"]);
                    await mfg1.Send<Manufacturer>();

                    var mfg2 = new Dictionary<string, object>
                    {
                        ["identifier.code"] = "MFG002",
                        ["identifier.name"] = "TechCorp Industries",
                        ["identifier.external.bms"] = "BMS-MFG-002",
                        ["manufacturing.country"] = "Germany",
                        ["manufacturing.established"] = "1992",
                        ["manufacturing.facilities"] = new[] { "Factory 1", "Factory 2", "Factory 3" },
                        ["products.categories"] = new[] { "controllers", "displays" }
                    };
                    _log.LogDebug("[BMS] Sending Manufacturer: {Code}", mfg2["identifier.code"]);
                    await mfg2.Send<Manufacturer>();

                    foreach (var (deviceTemplate, sensorsTemplate) in sampleData)
                    {
                        // Clone and adjust device for BMS
                        var device = new Device
                        {
                            Id = "bms" + deviceTemplate.Id, // bmsD1, bmsD2, etc.
                            Inventory = deviceTemplate.Inventory,
                            Serial = deviceTemplate.Serial,
                            Manufacturer = deviceTemplate.Manufacturer,
                            Model = deviceTemplate.Model,
                            Kind = deviceTemplate.Kind,
                            Code = deviceTemplate.Code
                        };

                        _log.LogDebug("[BMS] Sending Device: {DeviceId}", device.Id);
                        await device.Send();

                        // Send all sensors for this device
                        foreach (var sensorTemplate in sensorsTemplate)
                        {
                            var sensor = new Sensor
                            {
                                Id = "bms" + sensorTemplate.Id, // bmsS1, bmsS2, etc.
                                DeviceId = "bms" + sensorTemplate.DeviceId, // bmsDX
                                SensorId = "bms" + sensorTemplate.SensorId,
                                Code = sensorTemplate.Code,
                                Unit = sensorTemplate.Unit
                            };

                            _log.LogDebug("[BMS] Sending Sensor: {SensorId} -> Device: {DeviceId}",
                                sensor.Id, sensor.DeviceId);
                            await sensor.Send();
                        }
                    }

                    lastAnnounce = DateTimeOffset.UtcNow;
                    _log.LogInformation("[BMS] Complete dataset sent");
                }

                // Send random readings
                var readings = SampleData.CreateSampleReadings(5);
                foreach (var readingTemplate in readings)
                {
                    var reading = new Reading
                    {
                        SensorId = "bms" + readingTemplate.SensorId,
                        Value = readingTemplate.Value,
                        CapturedAt = readingTemplate.CapturedAt,
                        Unit = readingTemplate.Unit
                    };

                    _log.LogDebug("[BMS] Reading: {SensorId} = {Value}", reading.SensorId, reading.Value);
                    await reading.Send();
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[BMS] Error in publish loop");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch (TaskCanceledException) { }
        }
    }

}


