using System.Collections.Generic;

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
                    SensorKey = $"S{globalSensorId}",
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
                SensorKey = $"S{randomSensorId}",
                Value = random.NextDouble() * 100,
                CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-random.Next(0, 60)),
                Unit = "unit"
            };
            readings.Add(reading);
        }
        
        return readings;
    }
}