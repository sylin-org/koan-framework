# AutoConfigureFlow - Examples

The `AutoConfigureFlow` extension method eliminates boilerplate code for Flow entity and value object handlers by automatically discovering and registering standard handlers.

## ✨ Basic Usage

**Before (26 lines of repetitive boilerplate):**
```csharp
builder.Services.ConfigureFlow(flow =>
{
    flow.On<Reading>(async reading =>
    {
        Console.WriteLine($"📊 Received Reading: {reading.SensorKey} = {reading.Value}{reading.Unit}");
        await reading.SendToFlowIntake();
    });

    flow.On<Device>(async device =>
    {
        Console.WriteLine($"🏭 Device registered: {device.DeviceId} ({device.Manufacturer} {device.Model})");
        await device.SendToFlowIntake();
    });

    flow.On<Sensor>(async sensor =>
    {
        Console.WriteLine($"📡 Sensor registered: {sensor.SensorKey} ({sensor.Code}) - Unit: {sensor.Unit}");
        await sensor.SendToFlowIntake();
    });
});
```

**After (1 line with automatic discovery and guaranteed consistency):**
```csharp
builder.Services.AutoConfigureFlow(typeof(Device).Assembly);
```

## 📋 Usage Patterns

### 1. Auto-discover from calling assembly
```csharp
services.AutoConfigureFlow();
```

### 2. Auto-discover from specific assemblies  
```csharp
services.AutoConfigureFlow(typeof(Device).Assembly, typeof(Reading).Assembly);
```

### 3. Custom logging configuration
```csharp
services.AutoConfigureFlow(options =>
{
    options.EntityLogFormat = "🔥 Entity {0} received: {1}";
    options.ValueObjectLogFormat = "⚡ ValueObject {0} received: {1}";
}, typeof(Device).Assembly);
```

### 4. Disable logging (silent mode)
```csharp
services.AutoConfigureFlow(options =>
{
    options.EnableLogging = false;
}, typeof(Device).Assembly);
```

### 5. Type filtering (only specific types)
```csharp
services.AutoConfigureFlow(options =>
{
    options.TypeFilter = type => type.Name.StartsWith("Critical");
}, typeof(Device).Assembly);
```

### 6. Hybrid usage with custom handlers
```csharp
// Auto-register standard handlers for most types
services.AutoConfigureFlow(typeof(Device).Assembly)
    
// Add custom handlers for specific types (overrides auto-registered handlers)
.ConfigureFlow(flow =>
{
    flow.On<CriticalReading>(async reading =>
    {
        // Custom processing for critical readings
        logger.LogWarning("Critical reading: {SensorKey} = {Value}", reading.SensorKey, reading.Value);
        await SendAlertAsync(reading);
        await reading.SendToFlowIntake();
    });
});
```

## 🎯 What Gets Auto-Registered

- **FlowEntity&lt;T&gt; types**: Device, Sensor, etc.
  - Default log: `🏭 {TypeName} {KeyInfo}`
  - Routes to `entity.SendToFlowIntake()`

- **FlowValueObject&lt;T&gt; types**: Reading, etc.  
  - Default log: `📊 {TypeName} {KeyInfo}`
  - Routes to `valueObject.SendToFlowIntake()`

- **Smart key detection**: Automatically finds `[Key]` properties, `*Key` properties, `*Id` properties for logging
- **Special handling**: Reading type shows `SensorKey = Value+Unit` format

## 🔧 Configuration Options

```csharp
public sealed class AutoFlowOptions
{
    // Whether to enable console logging (default: true)
    public bool EnableLogging { get; set; } = true;

    // Custom format for entity logs: {0} = TypeName, {1} = KeyInfo
    public string EntityLogFormat { get; set; } = "🏭 {0} {1}";

    // Custom format for value object logs: {0} = TypeName, {1} = KeyInfo  
    public string ValueObjectLogFormat { get; set; } = "📊 {0} {1}";

    // Filter which types get auto-registered
    public Func<Type, bool>? TypeFilter { get; set; }
}
```

## ✅ Benefits

1. **Eliminates boilerplate**: 26 lines → 1 line
2. **Guaranteed consistency**: All handlers follow the same pattern
3. **Automatic discovery**: New FlowEntity/FlowValueObject types are automatically included
4. **Customizable**: Override logging, filtering, and specific handlers as needed
5. **Type safety**: Compile-time verification of handler registration
6. **Performance**: Uses reflection only at startup, runtime performance is identical