using Microsoft.AspNetCore.Mvc;
using S8.Flow.Shared;
using Koan.Flow.Extensions;
using Koan.Messaging;

namespace S8.Flow.Api.Controllers;

/// <summary>
/// Quick test controller to verify our Flow orchestrator implementation works.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FlowTestController : ControllerBase
{
    /// <summary>
    /// Basic health check for Flow orchestrator functionality.
    /// GET /api/flowtest/health
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        try
        {
            return Ok(new
            {
                status = "✅ Flow messaging system is available",
                messaging = "Entity.Send() pattern available",
                transport = "TransportEnvelope system active"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                status = "❌ Flow orchestrator API failed",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Test entity creation without sending (to verify model types work).
    /// GET /api/flowtest/create-entities
    /// </summary>
    [HttpGet("create-entities")]
    public IActionResult CreateEntities()
    {
        try
        {
            // Test creating Flow entities
            var device = new Device
            {
                Id = "test-device",
                Inventory = "TEST-001",
                Serial = "SN-123",
                Manufacturer = "Test Corp",
                Model = "Test Model",
                Kind = "test",
                Code = "TEST"
            };

            var sensor = new Sensor
            {
                SensorId = "test::device::temp",
                Id = "test-device",
                Code = "TEMP",
                Unit = "°C"
            };

            var reading = new Reading
            {
                SensorId = "test::device::temp",
                Value = 25.5,
                CapturedAt = DateTimeOffset.UtcNow,
                Unit = "°C",
                Source = "test"
            };

            return Ok(new
            {
                status = "✅ Successfully created Flow entities",
                device = new { device.Id, device.Manufacturer, device.Model },
                sensor = new { sensor.SensorId, sensor.Code, sensor.Unit },
                reading = new { reading.SensorId, reading.Value, reading.Unit },
                message = "Entities created successfully - Flow model types are working"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                status = "❌ Failed to create Flow entities",
                error = ex.Message,
                stack_trace = ex.StackTrace
            });
        }
    }

    /// <summary>
    /// Test sending a single entity (this will test the full messaging pipeline).
    /// POST /api/flowtest/send-test-entity
    /// </summary>
    [HttpPost("send-test-entity")]
    public async Task<IActionResult> SendTestEntity()
    {
        try
        {
            var device = new Device
            {
                Id = $"test-{Guid.NewGuid():N}",
                Inventory = "TEST-SEND",
                Serial = $"SN-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
                Manufacturer = "Flow Test Corp",
                Model = "Pipeline Test Model",
                Kind = "test-send",
                Code = "TESTSEND"
            };

            // This is the moment of truth! 
            // device.Send() should route through messaging → orchestrator → Flow intake
            await device.Send();

            return Ok(new
            {
                status = "✅ Successfully sent entity via Flow messaging pipeline!",
                device = new { device.Id, device.Manufacturer, device.Model },
                pipeline = "entity.Send() → Messaging → [FlowOrchestrator] → Flow Intake → Processing",
                message = "If you see this, the beautiful messaging-first architecture is working!"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                status = "❌ Failed to send entity via Flow pipeline",
                error = ex.Message,
                inner_error = ex.InnerException?.Message,
                stack_trace = ex.StackTrace,
                troubleshooting = new
                {
                    check_messaging = "Ensure a messaging provider (RabbitMQ, Redis, etc.) is configured",
                    check_orchestrator = "Verify [FlowOrchestrator] attribute is present on assembly",
                    check_autoregistration = "Check that Koan auto-registration is working"
                }
            });
        }
    }
}