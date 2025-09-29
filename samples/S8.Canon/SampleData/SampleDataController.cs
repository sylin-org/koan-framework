using Microsoft.AspNetCore.Mvc;
using S8.Canon.Controllers;
using Koan.Testing.Canon;

namespace S8.Canon.SampleData;

/// <summary>
/// Sample data generator for demonstrating Canon scenarios.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SampleDataController : ControllerBase
{
    private readonly IngestionController _ingestion;

    public SampleDataController(IngestionController ingestion)
    {
        _ingestion = ingestion;
    }

    /// <summary>
    /// Generate sample customer journey data.
    /// </summary>
    [HttpPost("customer-journey")]
    public async Task<IActionResult> GenerateCustomerJourney(CancellationToken ct = default)
    {
        var results = new List<object>();

        // 1. Initial registration via web
        var webReg = await _ingestion.IngestCustomer(new CustomerRegistration(
            Email: CanonTestConstants.Samples.EmailA,
            FirstName: "Alice",
            LastName: "Johnson",
            Phone: CanonTestConstants.Samples.PhoneA,
            Company: "TechCorp"
        ), ct);
        results.Add(new { step = "web-registration", result = webReg });

        // 2. Social media interaction
        var socialPost = await _ingestion.IngestSocialInteraction(new SocialInteraction(
            Handle: CanonTestConstants.Samples.HandleA,
            Platform: "twitter",
            Type: "mention",
            Email: CanonTestConstants.Samples.EmailA,
            Content: "Great experience with @company support!",
            Timestamp: DateTimeOffset.UtcNow.AddMinutes(-30)
        ), ct);
        results.Add(new { step = "social-mention", result = socialPost });

        // 3. IoT device registration
        var iotDevice = await _ingestion.IngestIoTEvent(new IoTEvent(
            DeviceId: "smart-thermostat-001",
            DeviceType: "thermostat",
            OwnerPhone: CanonTestConstants.Samples.PhoneA,
            Reading: 72.5,
            Location: "Living Room",
            BatteryLevel: 85
        ), ct);
        results.Add(new { step = "iot-device", result = iotDevice });

        return Ok(new
        {
            message = "Customer journey data generated",
            expectedAssociation = "All records should associate to the same reference via email/phone",
            steps = results.Count,
            results
        });
    }

    /// <summary>
    /// Generate collision scenario data.
    /// </summary>
    [HttpPost("collision-scenario")]
    public async Task<IActionResult> GenerateCollisionScenario(CancellationToken ct = default)
    {
        var results = new List<object>();

        // Create two separate customer profiles first
        var customer1 = await _ingestion.IngestCustomer(new CustomerRegistration(
            Email: CanonTestConstants.Samples.EmailA,
            FirstName: "Alice",
            LastName: "Smith",
            Phone: "+1-555-0001"
        ), ct);
        results.Add(new { step = "customer1", result = customer1 });

        var customer2 = await _ingestion.IngestCustomer(new CustomerRegistration(
            Email: CanonTestConstants.Samples.EmailB,
            FirstName: "Bob",
            LastName: "Jones",
            Phone: "+1-555-0002"
        ), ct);
        results.Add(new { step = "customer2", result = customer2 });

        // Wait for association
        await Task.Delay(3000, ct);

        // Now try to ingest data with BOTH emails (should cause collision)
        var collisionData = await _ingestion.IngestBatch(new BatchRequest(new[]
        {
            new BatchItem("collision-test", new Dictionary<string, object>
            {
                [CanonTestConstants.Keys.Email] = new[] { CanonTestConstants.Samples.EmailA, CanonTestConstants.Samples.EmailB },
                ["eventType"] = "data-merge-attempt",
                ["reason"] = "duplicate-account-cleanup"
            })
        }), ct);
        results.Add(new { step = "collision-attempt", result = collisionData });

        return Ok(new
        {
            message = "Collision scenario generated",
            expectedOutcome = "Third record should be rejected with MULTI_OWNER_COLLISION",
            steps = results.Count,
            results
        });
    }

    /// <summary>
    /// Generate no-keys rejection scenario.
    /// </summary>
    [HttpPost("no-keys-scenario")]
    public async Task<IActionResult> GenerateNoKeysScenario(CancellationToken ct = default)
    {
        // Ingest data without any of the configured aggregation keys
        var result = await _ingestion.IngestBatch(new BatchRequest(new[]
        {
            new BatchItem("incomplete-data", new Dictionary<string, object>
            {
                ["sessionId"] = Guid.NewGuid().ToString(),
                ["timestamp"] = DateTimeOffset.UtcNow,
                ["action"] = "page-view",
                ["url"] = "/products/widget-123"
                // Note: No email, phone, or handle keys
            })
        }), ct);

        return Ok(new
        {
            message = "No-keys scenario generated",
            expectedOutcome = "Record should be rejected with NO_KEYS",
            result
        });
    }
}
