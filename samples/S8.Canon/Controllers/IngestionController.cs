using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Canon.Infrastructure;
using Koan.Canon.Model;
using Koan.Testing.Canon;

namespace S8.Canon.Controllers;

/// <summary>
/// Sample ingestion endpoints demonstrating different Canon scenarios.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class IngestionController : ControllerBase
{
    /// <summary>
    /// Customer registration flow - simulates CRM data ingestion.
    /// </summary>
    [HttpPost("customer")]
    public async Task<IActionResult> IngestCustomer([FromBody] CustomerRegistration registration, CancellationToken ct = default)
    {
        var record = new Record
        {
            SourceId = "crm-system",
            OccurredAt = DateTimeOffset.UtcNow,
            Data = new Dictionary<string, object>
            {
                [CanonTestConstants.Keys.Email] = registration.Email,
                [CanonTestConstants.Keys.Phone] = registration.Phone ?? string.Empty,
                ["firstName"] = registration.FirstName,
                ["lastName"] = registration.LastName,
                ["company"] = registration.Company ?? string.Empty,
                ["source"] = "web-registration"
            }
        };

        using (DataSetContext.With(Constants.Sets.Intake))
        {
            await record.Save(ct);
        }

        return Ok(new { recordId = record.Id, message = "Customer data ingested for association" });
    }

    /// <summary>
    /// Social media interaction flow - simulates social platform data.
    /// </summary>
    [HttpPost("social")]
    public async Task<IActionResult> IngestSocialInteraction([FromBody] SocialInteraction interaction, CancellationToken ct = default)
    {
        var record = new Record
        {
            SourceId = $"social-{interaction.Platform}",
            OccurredAt = DateTimeOffset.UtcNow,
            Data = new Dictionary<string, object>
            {
                [CanonTestConstants.Keys.Handle] = interaction.Handle,
                [CanonTestConstants.Keys.Email] = interaction.Email ?? string.Empty,
                ["platform"] = interaction.Platform,
                ["interactionType"] = interaction.Type,
                ["content"] = interaction.Content ?? string.Empty,
                ["timestamp"] = interaction.Timestamp
            }
        };

        using (DataSetContext.With(Constants.Sets.Intake))
        {
            await record.Save(ct);
        }

        return Ok(new { recordId = record.Id, message = "Social interaction ingested for association" });
    }

    /// <summary>
    /// IoT device data flow - simulates sensor/device telemetry.
    /// </summary>
    [HttpPost("iot")]
    public async Task<IActionResult> IngestIoTEvent([FromBody] IoTEvent iotEvent, CancellationToken ct = default)
    {
        var record = new Record
        {
            SourceId = $"iot-{iotEvent.DeviceType}",
            OccurredAt = DateTimeOffset.UtcNow,
            Data = new Dictionary<string, object>
            {
                [CanonTestConstants.Keys.Phone] = iotEvent.OwnerPhone, // Phone as device owner identifier
                ["deviceId"] = iotEvent.DeviceId,
                ["deviceType"] = iotEvent.DeviceType,
                ["reading"] = iotEvent.Reading,
                ["location"] = iotEvent.Location ?? string.Empty,
                ["batteryLevel"] = iotEvent.BatteryLevel
            }
        };

        using (DataSetContext.With(Constants.Sets.Intake))
        {
            await record.Save(ct);
        }

        return Ok(new { recordId = record.Id, message = "IoT event ingested for association" });
    }

    /// <summary>
    /// Batch ingestion endpoint for testing collision scenarios.
    /// </summary>
    [HttpPost("batch")]
    public async Task<IActionResult> IngestBatch([FromBody] BatchRequest request, CancellationToken ct = default)
    {
        var results = new List<object>();

        foreach (var item in request.Items)
        {
            var record = new Record
            {
                SourceId = item.Source,
                OccurredAt = DateTimeOffset.UtcNow,
                Data = item.Payload
            };

            using (DataSetContext.With(Constants.Sets.Intake))
            {
                await record.Save(ct);
            }

            results.Add(new { recordId = record.Id, source = item.Source });
        }

        return Ok(new { message = $"Batch of {results.Count} records ingested", results });
    }
}

// DTOs for sample scenarios
public record CustomerRegistration(string Email, string FirstName, string LastName, string? Phone = null, string? Company = null);

public record SocialInteraction(string Handle, string Platform, string Type, string? Email = null, string? Content = null, DateTimeOffset? Timestamp = null);

public record IoTEvent(string DeviceId, string DeviceType, string OwnerPhone, double Reading, string? Location = null, int BatteryLevel = 100);

public record BatchRequest(IList<BatchItem> Items);

public record BatchItem(string Source, Dictionary<string, object> Payload);
