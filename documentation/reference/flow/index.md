---
type: REFERENCE
domain: flow
title: "Flow Pillar Reference"
audience: [developers, architects, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# Flow Pillar Reference

**Document Type**: REFERENCE
**Target Audience**: Developers, Architects
**Last Updated**: 2025-01-17
**Framework Version**: v0.2.18+

---

## Installation

```bash
dotnet add package Koan.Flow
```

```csharp
// Program.cs
builder.Services.AddKoan();
app.UseKoan();
```

## Flow Entities

### Basic Flow Entity

```csharp
public sealed class Device : FlowEntity<Device>
{
    [AggregationKey]
    public string SerialNumber { get; set; } = "";
    public string Model { get; set; } = "";
    public string Status { get; set; } = "";
}
```

### Dynamic Flow Entity (Contractless)

```csharp
// Send raw JSON data through the pipeline
var dynamicDevice = new DynamicFlowEntity<Device>
{
    Id = "dev:123",
    Model = new
    {
        inventory = new { serial = "DEV-001" },
        person = new { identifier = new { username = "john.doe" } },
        model = "Laptop",
        status = "Active"
    }
};

await dynamicDevice.Save(FlowSets.Stage<Device>("intake"));
```

## Data Pipeline Stages

### Intake → Processing

```csharp
// 1. Intake: Raw data from adapters
await new DynamicFlowEntity<Device>
{
    Id = "dev:123",
    Model = deviceData
}.Save(FlowSets.Stage<Device>("intake"));

// 2. Standardized: Normalized format
var standardized = await StageRecord<Device>.Query()
    .Where(r => r.Stage == "standardized")
    .ToArrayAsync();

// 3. Keyed: Aggregation keys extracted
var keyed = await StageRecord<Device>.Query()
    .Where(r => r.Stage == "keyed")
    .ToArrayAsync();

// 4. Processed: Final stage after projection
var processed = await StageRecord<Device>.Query()
    .Where(r => r.Stage == "processed")
    .ToArrayAsync();
```

### Projection Views

```csharp
// Query canonical projection
using (DataSetContext.With(FlowSets.View<Device>("canonical")))
{
    var devices = await CanonicalProjection<Device>.FirstPage(50);
}

// Query lineage projection
using (DataSetContext.With(FlowSets.View<Device>("lineage")))
{
    var lineage = await LineageProjection<Device>.FirstPage(50);
}
```

## Flow Adapters

### Background Service Adapter

```csharp
[FlowAdapter("oem", "device-sync", DefaultSource = "oem-hub")]
public class DeviceAdapter : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var devices = await FetchDevicesFromOEM();

            foreach (var device in devices)
            {
                await device.Send("oem-hub", DateTimeOffset.UtcNow);
            }

            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }

    private async Task<Device[]> FetchDevicesFromOEM()
    {
        // Fetch from external system
        var oemDevices = await _oemClient.GetDevicesAsync();

        return oemDevices.Select(d => new Device
        {
            SerialNumber = d.Serial,
            Model = d.ModelName,
            Status = d.CurrentStatus
        }).ToArray();
    }
}
```

### HTTP Adapter

```csharp
[Route("api/[controller]")]
public class DeviceIngestController : ControllerBase
{
    [HttpPost("devices")]
    public async Task<IActionResult> IngestDevices([FromBody] DeviceData[] devices)
    {
        foreach (var device in devices)
        {
            var flowEntity = new DynamicFlowEntity<Device>
            {
                Id = $"device:{device.Id}",
                Model = new
                {
                    inventory = new { serial = device.SerialNumber },
                    model = device.ModelName,
                    status = device.Status
                }
            };

            await flowEntity.Save(FlowSets.Stage<Device>("intake"));
        }

        return Ok(new { processed = devices.Length });
    }
}
```

## External ID Management

### Entity with External References

```csharp
public sealed class Sensor : FlowEntity<Sensor>
{
    [ParentKey(typeof(Device))]
    public string DeviceId { get; set; } = "";

    public string Type { get; set; } = "";
    public double Value { get; set; }
}
```

### Resolving External References

```csharp
// Send sensor data with external device reference
var sensorData = new DynamicFlowEntity<Sensor>
{
    Id = "sensor:temp-01",
    Model = new
    {
        sensor = new { identifier = "TEMP-001" },
        type = "Temperature",
        value = 22.5,
        // Reference to device using external ID
        reference = new
        {
            device = new
            {
                external = new { oem = "DEV-001" }
            }
        }
    }
};

await sensorData.Save(FlowSets.Stage<Sensor>("intake"));
```

## Flow Controllers

### Model-Specific Controllers

```csharp
// Auto-generated controller for Device flow operations
[Route("api/flow/[controller]")]
public class DeviceController : FlowEntityController<Device>
{
    // Inherits standard flow operations:
    // GET /api/flow/device - list devices
    // GET /api/flow/device/{id} - get device
    // GET /api/flow/device/views/canonical/{id} - canonical view
    // GET /api/flow/device/views/lineage/{id} - lineage view
    // POST /api/flow/device/admin/reproject/{id} - trigger reprojection
}
```

### Custom Flow Operations

```csharp
[Route("api/flow/[controller]")]
public class DeviceController : FlowEntityController<Device>
{
    [HttpPost("admin/reprocess-failed")]
    public async Task<IActionResult> ReprocessFailed()
    {
        var failed = await StageRecord<Device>.Query()
            .Where(r => r.Stage == "failed")
            .ToArrayAsync();

        foreach (var record in failed)
        {
            await record.Requeue();
        }

        return Ok(new { requeued = failed.Length });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var stats = new
        {
            Intake = await StageRecord<Device>.CountAsync(r => r.Stage == "intake"),
            Processed = await StageRecord<Device>.CountAsync(r => r.Stage == "processed"),
            Failed = await StageRecord<Device>.CountAsync(r => r.Stage == "failed")
        };

        return Ok(stats);
    }
}
```

## Flow Events and Messaging

### Flow Event Publishing

```csharp
// Send flow events for processing
await FlowEvent.For<Device>()
    .WithSource("oem-hub")
    .WithData(deviceData)
    .Send();

// Batch flow events
var events = devices.Select(d => FlowEvent.For<Device>()
    .WithSource("oem-hub")
    .WithData(d)).ToArray();

await events.Send();
```

### Flow Event Handling

```csharp
public class DeviceFlowHandler : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await this.On<FlowEvent<Device>>(async flowEvent =>
        {
            // Process device flow event
            var device = flowEvent.Data;

            // Custom business logic
            if (device.Status == "Critical")
            {
                await _alertService.SendAlert($"Device {device.SerialNumber} is critical");
            }

            // Continue flow processing
            await flowEvent.Continue();
        });
    }
}
```

## Flow Interceptors

### Pre-Processing Interceptors

```csharp
public static class DeviceFlowInterceptors
{
    public static void Configure()
    {
        FlowInterceptors.For<Device>()
            .BeforeIntake(async device =>
            {
                // Validate device data
                if (string.IsNullOrEmpty(device.SerialNumber))
                {
                    return FlowIntakeAction.Reject("Serial number required");
                }

                return FlowIntakeAction.Continue;
            })
            .AfterKeying(async device =>
            {
                // Enrich with additional data
                device.Model = await _deviceCatalog.GetModelName(device.SerialNumber);

                return FlowStageAction.Continue;
            });
    }
}
```

### Post-Processing Interceptors

```csharp
FlowInterceptors.For<Device>()
    .AfterProjection(async device =>
    {
        // Trigger downstream processes
        await new DeviceProcessed
        {
            DeviceId = device.Id,
            SerialNumber = device.SerialNumber,
            ProcessedAt = DateTimeOffset.UtcNow
        }.Send();

        return FlowStageAction.Continue;
    });
```

## Configuration

### Basic Flow Configuration

```json
{
  "Koan": {
    "Flow": {
      "AutoRegister": true,
      "Adapters": {
        "AutoStart": true,
        "Include": ["oem:device-sync"],
        "Exclude": []
      }
    }
  }
}
```

### Adapter-Specific Configuration

```json
{
  "Koan": {
    "Flow": {
      "Adapters": {
        "oem:device-sync": {
          "Enabled": true,
          "PollInterval": "00:05:00",
          "BatchSize": 100,
          "DefaultSource": "oem-production"
        }
      }
    }
  }
}
```

## Monitoring and Diagnostics

### Flow Health Checks

```csharp
public class FlowHealthCheck : IHealthContributor
{
    public string Name => "Flow Pipeline";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        var pendingCount = await StageRecord<Device>.CountAsync(r => r.Stage == "intake");
        var failedCount = await StageRecord<Device>.CountAsync(r => r.Stage == "failed");

        var isHealthy = pendingCount < 1000 && failedCount < 10;
        var message = isHealthy ? null : $"Pending: {pendingCount}, Failed: {failedCount}";

        return new HealthReport(Name, isHealthy, message);
    }
}
```

### Flow Monitoring

```csharp
[Route("api/[controller]")]
public class FlowMonitorController : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetFlowStatus()
    {
        var models = new[] { typeof(Device), typeof(Sensor) };
        var status = new Dictionary<string, object>();

        foreach (var model in models)
        {
            var modelName = model.Name.ToLowerInvariant();
            status[modelName] = new
            {
                Intake = await GetStageCount(model, "intake"),
                Keyed = await GetStageCount(model, "keyed"),
                Processed = await GetStageCount(model, "processed"),
                Failed = await GetStageCount(model, "failed")
            };
        }

        return Ok(status);
    }

    private async Task<int> GetStageCount(Type modelType, string stage)
    {
        // Generic query for stage records
        return await StageRecord.CountAsync(modelType, stage);
    }
}
```

## Error Handling

### Retry Policies

```csharp
public class DeviceRetryPolicy : IFlowRetryPolicy<Device>
{
    public async Task<FlowRetryAction> ShouldRetry(Device device, Exception exception, int attemptCount)
    {
        if (exception is HttpRequestException && attemptCount < 3)
        {
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attemptCount));
            return FlowRetryAction.RetryAfter(delay);
        }

        if (exception is ValidationException)
        {
            return FlowRetryAction.DeadLetter("Validation failed");
        }

        return FlowRetryAction.Fail;
    }
}
```

### Dead Letter Handling

```csharp
public class FlowDeadLetterHandler : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await this.On<FlowDeadLetter<Device>>(async deadLetter =>
        {
            _logger.LogError("Device flow failed: {Reason} - {Data}",
                deadLetter.Reason, deadLetter.Data);

            // Send to monitoring system
            await _monitoring.RecordFailure(deadLetter);
        });
    }
}
```

## Testing

### Flow Testing

```csharp
[Test]
public async Task Should_Process_Device_Through_Pipeline()
{
    // Arrange
    var device = new Device
    {
        SerialNumber = "TEST-001",
        Model = "Test Device",
        Status = "Active"
    };

    // Act - Send through intake
    await device.Send("test-source");

    // Trigger processing
    await _flowProcessor.ProcessPendingAsync<Device>();

    // Assert - Check canonical view
    using (DataSetContext.With(FlowSets.View<Device>("canonical")))
    {
        var canonical = await CanonicalProjection<Device>
            .Query()
            .FirstOrDefaultAsync(d => d.SerialNumber == "TEST-001");

        Assert.IsNotNull(canonical);
        Assert.AreEqual("Active", canonical.Status);
    }
}
```

## API Reference

### Core Types

```csharp
public abstract class FlowEntity<T> : Entity<T> where T : class
{
    // Automatic flow entity capabilities
}

public class DynamicFlowEntity<T> : Entity<DynamicFlowEntity<T>>
{
    public string Id { get; set; } = "";
    public object Model { get; set; } = new();
}

public class StageRecord<T>
{
    public string Id { get; set; } = "";
    public string Stage { get; set; } = "";
    public object Data { get; set; } = new();
    public DateTimeOffset Created { get; set; }
}
```

### Flow Sets

```csharp
public static class FlowSets
{
    public static string Stage<T>(string stage) => $"flow.{typeof(T).Name.ToLower()}.{stage}";
    public static string View<T>(string view) => $"flow.{typeof(T).Name.ToLower()}.views.{view}";
}
```

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+
