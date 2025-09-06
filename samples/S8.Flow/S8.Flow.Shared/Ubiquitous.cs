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
        new Device { Id = "D1", Inventory = "INV-1001", Serial = "SN-MRI-7000-001", Manufacturer = "BrandA", Model = "MRI-7000", Kind = "MRI", Code = "MRI-7000-A" },
        new Device { Id = "D2", Inventory = "INV-1002", Serial = "SN-MRI-7000-002", Manufacturer = "BrandB", Model = "MRI-7000", Kind = "MRI", Code = "MRI-7000-B" },
        new Device { Id = "D3", Inventory = "INV-2001", Serial = "SN-CT-300-001", Manufacturer = "BrandC", Model = "CT-300", Kind = "CT", Code = "CT-300-1" },
        new Device { Id = "D4", Inventory = "INV-2002", Serial = "SN-CT-300-002", Manufacturer = "BrandC", Model = "CT-300", Kind = "CT", Code = "CT-300-2" },
        new Device { Id = "D5", Inventory = "INV-3001", Serial = "SN-CRYO-200-001", Manufacturer = "BrandA", Model = "CRYO-200", Kind = "CRYO", Code = "CRYO-200-1" },
    };

    // BMS publishes ALL sensors (temperature, power, coolant pressure) for ALL devices
    public static IEnumerable<Sensor> SensorsForBms(Device d)
    {
        // Get device index for consistent sensor numbering
        var deviceIndex = Array.IndexOf(Fleet, d);
        var baseIndex = deviceIndex * 3; // 3 sensors per device
        
        yield return new Sensor
        {
            Id = $"S{baseIndex + 1}",
            DeviceId = d.Id,
            SensorKey = $"S{baseIndex + 1}",
            Code = SensorCodes.TEMP,
            Unit = Units.C,
        };
        yield return new Sensor
        {
            Id = $"S{baseIndex + 2}",
            DeviceId = d.Id,
            SensorKey = $"S{baseIndex + 2}",
            Code = SensorCodes.PWR,
            Unit = Units.Watt,
        };
        yield return new Sensor
        {
            Id = $"S{baseIndex + 3}",
            DeviceId = d.Id,
            SensorKey = $"S{baseIndex + 3}",
            Code = SensorCodes.COOLANT_PRESSURE,
            Unit = Units.KPa,
        };
    }

    // OEM also publishes ALL sensors for ALL devices
    public static IEnumerable<Sensor> SensorsForOem(Device d)
    {
        // Use same sensor IDs as BMS to test merging capabilities
        var deviceIndex = Array.IndexOf(Fleet, d);
        var baseIndex = deviceIndex * 3; // 3 sensors per device
        
        yield return new Sensor
        {
            Id = $"S{baseIndex + 1}",
            DeviceId = d.Id,
            SensorKey = $"S{baseIndex + 1}",
            Code = SensorCodes.TEMP,
            Unit = Units.C,
        };
        yield return new Sensor
        {
            Id = $"S{baseIndex + 2}",
            DeviceId = d.Id,
            SensorKey = $"S{baseIndex + 2}",
            Code = SensorCodes.PWR,
            Unit = Units.Watt,
        };
        yield return new Sensor
        {
            Id = $"S{baseIndex + 3}",
            DeviceId = d.Id,
            SensorKey = $"S{baseIndex + 3}",
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