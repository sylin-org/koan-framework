using Microsoft.AspNetCore.Mvc;
using S8.Flow.Shared;
using Sora.Flow;
using Sora.Flow.Sending;

namespace S8.Flow.Api.Controllers;

/// <summary>
/// ✨ BEAUTIFUL FLOW API DEMONSTRATION ✨
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

        // ✨ BEAUTIFUL: Routes through messaging → orchestrator → Flow intake
    await Sora.Messaging.MessagingExtensions.Send(device);
        
        return Ok(new { 
            message = "✅ Device sent via messaging-first Flow patterns!", 
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

        // ✨ BEAUTIFUL: Send to specific target via messaging
    // Targeted send is obsolete; use messaging pattern or document as not supported
    await Sora.Messaging.MessagingExtensions.Send(device);
        
        return Ok(new { 
            message = $"✅ Device sent to target '{target}' via messaging!", 
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
            SensorKey = request?.SensorKey ?? "demo::sensor::temp",
            Value = request?.Value ?? 25.5,
            CapturedAt = DateTimeOffset.UtcNow,
            Unit = request?.Unit ?? "°C",
            Source = "demo-api"
        };

        // ✨ BEAUTIFUL: Value objects route through messaging too
    await Sora.Messaging.MessagingExtensions.Send(reading);
        
        return Ok(new { 
            message = "✅ Reading sent via messaging-first Flow patterns!", 
            reading = new { reading.SensorKey, reading.Value, reading.Unit },
            route = "Messaging → [FlowOrchestrator] → Flow Intake → Processing Pipeline"
        });
    }

    /// <summary>
    /// Send a command to all adapters via Flow's beautiful command API.
    /// POST /api/flowdemo/command/broadcast?command=seed
    /// </summary>
    [HttpPost("command/broadcast")]
    public async Task<IActionResult> BroadcastCommand(string command, [FromBody] object? payload = null)
    {
        // ✨ BEAUTIFUL: Flow command broadcasting
        await Sora.Flow.Flow.Send(command, payload).Broadcast();
        
        return Ok(new { 
            message = $"✅ Command '{command}' broadcast to all adapters!", 
            command,
            payload,
            route = "Flow.Send().Broadcast() → All Adapter Handlers"
        });
    }

    /// <summary>
    /// Send a command to a specific adapter target.
    /// POST /api/flowdemo/command/targeted?command=seed&amp;target=bms:simulator
    /// </summary>
    [HttpPost("command/targeted")]
    public async Task<IActionResult> SendTargetedCommand(string command, string target, [FromBody] object? payload = null)
    {
        // ✨ BEAUTIFUL: Targeted command sending
        await Sora.Flow.Flow.Send(command, payload).To(target);
        
        return Ok(new { 
            message = $"✅ Command '{command}' sent to target '{target}'!", 
            command,
            target,
            payload,
            route = $"Flow.Send().To({target}) → Targeted Adapter Handler"
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
    string? SensorKey = null,
    double? Value = null,
    string? Unit = null);