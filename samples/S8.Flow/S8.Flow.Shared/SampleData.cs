using System.Collections.Generic;
using Koan.Flow.Model;

namespace S8.Flow.Shared;

public static class SampleData
{
    /// <summary>
    /// Creates clean, simplified sample data for debugging ParentKey resolution.
    /// 5 devices, 5 sensors each = 25 total sensors.
    /// </summary>
    public static Dictionary<Device, List<Sensor>> CreateSampleData()
    {
        var data = new Dictionary<Device, List<Sensor>>();

        for (int deviceNum = 1; deviceNum <= 5; deviceNum++)
        {
            var device = new Device
            {
                Id = $"D{deviceNum}",
                Inventory = $"INV-{deviceNum:D3}",
                Serial = $"SN{deviceNum:D3}",
                Manufacturer = "TestMfg",
                Model = "TestModel",
                Kind = "TEST",
                Code = $"TEST-{deviceNum}"
            };

            var sensors = new List<Sensor>();
            for (int sensorNum = 1; sensorNum <= 5; sensorNum++)
            {
                var globalSensorId = ((deviceNum - 1) * 5) + sensorNum;
                var sensor = new Sensor
                {
                    Id = $"S{globalSensorId}",
                    DeviceId = $"D{deviceNum}", // Parent reference
                    SensorId = $"S{globalSensorId}",
                    Code = $"SENSOR{sensorNum}",
                    Unit = sensorNum switch
                    {
                        1 => "°C",
                        2 => "V",
                        3 => "A",
                        4 => "W",
                        5 => "Hz",
                        _ => "unit"
                    }
                };
                sensors.Add(sensor);
            }

            data[device] = sensors;
        }

        return data;
    }

    /// <summary>
    /// Creates sample readings for random sensors
    /// </summary>
    public static List<Reading> CreateSampleReadings(int count = 10)
    {
        var readings = new List<Reading>();
        var random = new Random();

        for (int i = 1; i <= count; i++)
        {
            var randomSensorId = random.Next(1, 26); // S1 to S25
            var reading = new Reading
            {
                SensorId = $"S{randomSensorId}",
                Value = random.NextDouble() * 100,
                CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-random.Next(0, 60)),
                Unit = "unit"
            };
            readings.Add(reading);
        }

        return readings;
    }

    /// <summary>
    /// Creates sample manufacturer entities with dynamic data structure
    /// </summary>
    public static List<Manufacturer> CreateSampleManufacturers()
    {
        var manufacturers = new List<Manufacturer>();

        // Create manufacturers with BMS-specific data using JSON paths as keys
        var mfg1 = new Dictionary<string, object>
        {
            ["identifier.code"] = "MFG001",
            ["identifier.name"] = "Acme Corp",
            ["identifier.external.bms"] = "BMS-MFG-001",
            ["manufacturing.country"] = "USA",
            ["manufacturing.established"] = "1985",
            ["manufacturing.facilities"] = new[] { "Plant A", "Plant B" },
            ["products.categories"] = new[] { "sensors", "actuators" }
        }.ToDynamicFlowEntity<Manufacturer>();
        manufacturers.Add(mfg1);

        var mfg2 = new Dictionary<string, object>
        {
            ["identifier.code"] = "MFG002",
            ["identifier.name"] = "TechCorp Industries",
            ["identifier.external.bms"] = "BMS-MFG-002",
            ["manufacturing.country"] = "Germany",
            ["manufacturing.established"] = "1992",
            ["manufacturing.facilities"] = new[] { "Factory 1", "Factory 2", "Factory 3" },
            ["products.categories"] = new[] { "controllers", "displays" }
        }.ToDynamicFlowEntity<Manufacturer>();
        manufacturers.Add(mfg2);

        return manufacturers;
    }

    /// <summary>
    /// Creates OEM-specific manufacturer data (support and certification info)
    /// </summary>
    public static List<Manufacturer> CreateOemManufacturers()
    {
        var manufacturers = new List<Manufacturer>();

        // Create manufacturers with OEM-specific data using JSON paths as keys
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
        }.ToDynamicFlowEntity<Manufacturer>();
        manufacturers.Add(mfg1);

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
        }.ToDynamicFlowEntity<Manufacturer>();
        manufacturers.Add(mfg2);

        return manufacturers;
    }
}