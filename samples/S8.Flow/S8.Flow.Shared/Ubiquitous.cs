namespace S8.Flow.Shared;

public static class Keys
{
    public static class Device
    {
        public const string Inventory = "identifier.inventory";
        public const string Serial = "identifier.serial";
        public const string Manufacturer = "manufacturer";
        public const string Model = "model";
        public const string Kind = "kind";
        public const string Code = "code";
    }

    public static class Sensor
    {
        public const string Key = "key";
        public const string Code = "code";
        public const string Unit = "unit";
        public const string Reliability = "reliability";
    }

    public static class Reading
    {
        public const string CapturedAt = "capturedAt";
        public const string Value = "value";
        public const string Source = "source";
    }
}

public static class Units
{
    public const string C = "C";
    public const string F = "F";
    public const string MilliTesla = "mT";
    public const string Watt = "W";
    public const string KPa = "kPa";
    public const string Rpm = "RPM";
}

public static class SensorCodes
{
    public const string TEMP = "TEMP";
    public const string MAG = "MAG";
    public const string PWR = "PWR";
    public const string COOLANT_PRESSURE = "COOLANT_PRESSURE";
    public const string GANTRY_RPM = "GANTRY_RPM";
}

public static class SampleProfiles
{
    public static readonly Device[] Fleet = new[]
    {
        new Device { DeviceId = "INV-1001:SN-MRI-7000-001", Inventory = "INV-1001", Serial = "SN-MRI-7000-001", Manufacturer = "BrandA", Model = "MRI-7000", Kind = "MRI", Code = "MRI-7000-A" },
        new Device { DeviceId = "INV-1002:SN-MRI-7000-002", Inventory = "INV-1002", Serial = "SN-MRI-7000-002", Manufacturer = "BrandB", Model = "MRI-7000", Kind = "MRI", Code = "MRI-7000-B" },
        new Device { DeviceId = "INV-2001:SN-CT-300-001", Inventory = "INV-2001", Serial = "SN-CT-300-001", Manufacturer = "BrandC", Model = "CT-300", Kind = "CT", Code = "CT-300-1" },
        new Device { DeviceId = "INV-2002:SN-CT-300-002", Inventory = "INV-2002", Serial = "SN-CT-300-002", Manufacturer = "BrandC", Model = "CT-300", Kind = "CT", Code = "CT-300-2" },
        new Device { DeviceId = "INV-3001:SN-CRYO-200-001", Inventory = "INV-3001", Serial = "SN-CRYO-200-001", Manufacturer = "BrandA", Model = "CRYO-200", Kind = "CRYO", Code = "CRYO-200-1" },
    };

    // BMS publishes temperature sensors only
    public static IEnumerable<Sensor> SensorsForBms(Device d)
    {
        var sensorKey = $"{d.Inventory}::{d.Serial}::{SensorCodes.TEMP}";
        yield return new Sensor
        {
            DeviceId = d.DeviceId,
            SensorKey = sensorKey,
            Code = SensorCodes.TEMP,
            Unit = Units.C,
        };
    }

    // OEM publishes power and coolant pressure sensors
    public static IEnumerable<Sensor> SensorsForOem(Device d)
    {
        yield return new Sensor
        {
            DeviceId = d.DeviceId,
            SensorKey = $"{d.Inventory}::{d.Serial}::{SensorCodes.PWR}",
            Code = SensorCodes.PWR,
            Unit = Units.Watt,
        };
        yield return new Sensor
        {
            DeviceId = d.DeviceId,
            SensorKey = $"{d.Inventory}::{d.Serial}::{SensorCodes.COOLANT_PRESSURE}",
            Code = SensorCodes.COOLANT_PRESSURE,
            Unit = Units.KPa,
        };
    }

    // Procedural generation for seeding: generates 'total' devices and sensors
    public static IEnumerable<Device> GenerateDevices(int total = 1000)
    {
        for (int i = 1; i <= total; i++)
        {
            var inv = $"INV-{1000 + i}";
            var serial = $"SN-FAKE-{i:D6}";
            yield return new Device
            {
                DeviceId = $"{inv}:{serial}",
                Inventory = inv,
                Serial = serial,
                Manufacturer = $"Brand{(char)('A' + (i % 5))}",
                Model = $"MODEL-{i % 10}",
                Kind = (i % 2 == 0) ? "MRI" : "CT",
                Code = $"CODE-{i % 20}",
            };
        }
    }

    public static IEnumerable<Sensor> GenerateSensorsForBms(Device d)
    {
        var sensorKey = $"{d.Inventory}::{d.Serial}::{SensorCodes.TEMP}";
        yield return new Sensor
        {
            DeviceId = d.DeviceId,
            SensorKey = sensorKey,
            Code = SensorCodes.TEMP,
            Unit = Units.C,
        };
    }

    public static IEnumerable<Sensor> GenerateSensorsForOem(Device d)
    {
        yield return new Sensor
        {
            DeviceId = d.DeviceId,
            SensorKey = $"{d.Inventory}::{d.Serial}::{SensorCodes.PWR}",
            Code = SensorCodes.PWR,
            Unit = Units.Watt,
        };
        yield return new Sensor
        {
            DeviceId = d.DeviceId,
            SensorKey = $"{d.Inventory}::{d.Serial}::{SensorCodes.COOLANT_PRESSURE}",
            Code = SensorCodes.COOLANT_PRESSURE,
            Unit = Units.KPa,
        };
    }
}

public sealed class SensorReading
{
    public required string SensorKey { get; init; }
    public required double Value { get; init; }
    public required string Unit { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? Source { get; init; }
}