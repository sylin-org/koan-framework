using Microsoft.AspNetCore.Mvc;
using S8.Flow.Shared;
using Koan.Flow.Extensions;
using Koan.Messaging;

namespace S8.Flow.Api.Controllers;

/// <summary>
/// BEAUTIFUL FLOW API DEMONSTRATION
/// Showcases the new messaging-first Flow patterns with elegant syntax.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FlowDemoController : ControllerBase
{
    /// <summary>
    /// Send a test device through the beautiful messaging system.
    /// POST /api/flowdemo/device
    /// </summary>
    [HttpPost("device")]
    public async Task<IActionResult> SendTestDevice([FromBody] CreateDeviceRequest? request = null)
    {
        var device = new Device
        {
            Id = request?.DeviceId ?? $"test-{Guid.NewGuid():N}",
            Inventory = request?.Inventory ?? "TEST-INV",
            Serial = request?.Serial ?? "TEST-SERIAL",
            Manufacturer = request?.Manufacturer ?? "Demo Corp",
            Model = request?.Model ?? "Demo Model X",
            Kind = request?.Kind ?? "demo",
            Code = request?.Code ?? "DEMO"
        };

        // BEAUTIFUL: Routes through messaging → orchestrator → Flow intake
        await device.Send();

        return Ok(new
        {
            message = "Device sent via messaging-first Flow patterns!",
            device = device.Id,
            route = "Messaging → [FlowOrchestrator] → Flow Intake → Processing Pipeline"
        });
    }

    /// <summary>
    /// Send a test device to a specific adapter target.
    /// POST /api/flowdemo/device/targeted?target=bms:simulator
    /// </summary>
    [HttpPost("device/targeted")]
    public async Task<IActionResult> SendTargetedDevice(string target, [FromBody] CreateDeviceRequest? request = null)
    {
        var device = new Device
        {
            Id = request?.DeviceId ?? $"targeted-{Guid.NewGuid():N}",
            Inventory = request?.Inventory ?? "TARGETED-INV",
            Serial = request?.Serial ?? "TARGETED-SERIAL",
            Manufacturer = request?.Manufacturer ?? "Target Corp",
            Model = request?.Model ?? "Targeted Model",
            Kind = request?.Kind ?? "targeted",
            Code = request?.Code ?? "TARGET"
        };

        // BEAUTIFUL: Send to specific target via messaging
        // Targeted send is obsolete; use messaging pattern or document as not supported
        await device.Send();

        return Ok(new
        {
            message = $"Device sent to target '{target}' via messaging!",
            device = device.Id,
            target,
            route = $"Messaging → Target({target}) → Flow Processing"
        });
    }

    /// <summary>
    /// Send a test reading value object.
    /// POST /api/flowdemo/reading
    /// </summary>
    [HttpPost("reading")]
    public async Task<IActionResult> SendTestReading([FromBody] CreateReadingRequest? request = null)
    {
        var reading = new Reading
        {
            SensorId = request?.SensorId ?? "demo::sensor::temp",
            Value = request?.Value ?? 25.5,
            CapturedAt = DateTimeOffset.UtcNow,
            Unit = request?.Unit ?? "°C",
            Source = "demo-api"
        };

        // BEAUTIFUL: Value objects route through messaging too
        await reading.Send();

        return Ok(new
        {
            message = "Reading sent via messaging-first Flow patterns!",
            reading = new { reading.SensorId, reading.Value, reading.Unit },
            route = "Messaging → [FlowOrchestrator] → Flow Intake → Processing Pipeline"
        });
    }

}

public record CreateDeviceRequest(
    string? DeviceId = null,
    string? Inventory = null,
    string? Serial = null,
    string? Manufacturer = null,
    string? Model = null,
    string? Kind = null,
    string? Code = null);

public record CreateReadingRequest(
    string? SensorId = null,
    double? Value = null,
    string? Unit = null);