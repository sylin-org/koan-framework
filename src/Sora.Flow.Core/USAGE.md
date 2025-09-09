# Sora.Flow.Core — Developer Usage Guide

A comprehensive guide for building data processing pipelines with Sora.Flow, from basic concepts to advanced enterprise scenarios.

## Table of Contents

1. [Introduction](#introduction)
2. [Core Concepts](#core-concepts)
3. [Getting Started](#getting-started)
4. [Basic Pipeline Setup](#basic-pipeline-setup)
5. [Aggregation and Association](#aggregation-and-association)
6. [Projections and Views](#projections-and-views)
7. [Identity Resolution](#identity-resolution)
8. [Advanced Scenarios](#advanced-scenarios)
9. [Monitoring and Actions](#monitoring-and-actions)
10. [Production Considerations](#production-considerations)
11. [Troubleshooting](#troubleshooting)
12. [Identifier Policy: ULID and CanonicalId](#identifier-policy-ulid-and-canonicalid)

## Introduction

### Why Sora.Flow?

Sora.Flow is designed to solve common challenges in modern data processing:

- **Data Integration**: Ingest data from multiple sources with different formats
- **Identity Resolution**: Merge records from different systems that represent the same entity
- **Real-time Processing**: Process data as it arrives while maintaining consistency
- **Audit and Lineage**: Track the provenance of every piece of data
- **Scalability**: Handle growing data volumes without architectural changes

### When to Use Sora.Flow

**Perfect for:**
- Customer data platforms (CDP)
- IoT telemetry aggregation
- Multi-source data integration
- Real-time analytics pipelines
- Master data management (MDM)

**Consider alternatives for:**
- Simple batch ETL jobs
- Single-source data transformations
- Systems requiring microsecond latency
- Pure streaming analytics without entity modeling

## Core Concepts

### The Flow Pipeline

Sora.Flow processes data through a series of stages:

```
Intake → Standardize → Key → Associate → Project → Materialize
```

1. **Intake**: Raw data arrives from various sources
2. **Standardize**: Normalize formats and apply basic validation
3. **Key**: Extract aggregation keys from the data
4. **Associate**: Group records that belong to the same logical entity
5. **Project**: Create canonical and lineage views
6. **Materialize**: Store the final entity state

### Key Terminology

- **FlowEntity**: Your canonical data model (e.g., Customer, Device, Order)
- **StageRecord**: A single piece of data as it moves through the pipeline
- **ReferenceId**: Unique identifier for a logical entity across all sources
    - In Flow, ReferenceId (CanonicalId) is the business key derived from aggregation tags. A separate ULID is minted for transport/storage as Id.
- **Aggregation Tags**: Fields used to determine if records belong to the same entity
- **Canonical View**: The current, merged state of an entity
- **Lineage View**: Historical trail showing how the entity evolved

## Getting Started

### 1. Installation

Add the Sora.Flow.Core package to your project:

```xml
<PackageReference Include="Sylin.Sora.Flow.Core" Version="0.2.14" />
<PackageReference Include="Sylin.Sora.Data.Json" Version="0.2.14" /> <!-- For file-based storage -->
```

### 2. Basic Configuration

```csharp
// Program.cs
using Sora.Flow;
using Sora.Data.Core;

var builder = WebApplication.CreateBuilder(args);

// Register Sora services (auto-discovers Flow)
builder.Services.AddSora();

// Configure data provider
builder.Services.AddSoraDataCore(); // Required
builder.Services.AddJsonData();     // Simple file-based storage for getting started

var app = builder.Build();

// Essential: Set the ambient host
Sora.Core.Hosting.App.AppHost.Current = app.Services;

app.Run();
```

### 3. Configuration Options

```json
// appsettings.json
{
  "Sora": {
    "Data": {
      "Json": {
        "DirectoryPath": "./data"
      }
    },
    "Flow": {
      "BatchSize": 100,
      "PurgeEnabled": true,
      "PurgeInterval": "00:05:00",
      "AggregationTags": ["email", "phone", "customerId"]
    }
  }
}
```

## Basic Pipeline Setup

### 1. Define Your First Model

Start with a simple model representing a customer:

```csharp
using Sora.Flow.Model;
using Sora.Flow.Attributes;

public sealed class Customer : FlowEntity<Customer>
{
    [AggregationTag("email")]
    public string? Email { get; set; }
    
    [AggregationTag("phone")]
    public string? Phone { get; set; }
    
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Company { get; set; }
}
```

**Key Points:**
- Inherit from `FlowEntity<T>` where T is your model
- Use `[AggregationTag]` to mark fields used for entity resolution
- Other properties are data fields that will be aggregated

### 2. Ingest Your First Record

```csharp
using Sora.Flow.Model;
using Sora.Flow.Infrastructure;
using Sora.Data.Core;

public class CustomerController : ControllerBase
{
    [HttpPost("ingest")]
    public async Task<IActionResult> IngestCustomer([FromBody] CustomerData data)
    {
        // Create a stage record
        var record = new StageRecord<Customer>
        {
            Id = Guid.NewGuid().ToString("n"),
            SourceId = $"crm-{data.CrmId}",
            OccurredAt = DateTimeOffset.UtcNow,
            StagePayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["email"] = data.Email,
                ["phone"] = data.Phone,
                ["firstName"] = data.FirstName,
                ["lastName"] = data.LastName,
                ["company"] = data.Company
            }
        };

        // Save to intake stage
        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
        {
            await record.Save();
        }

        return Ok();
    }
}

public class CustomerData
{
    public string CrmId { get; set; } = default!;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Company { get; set; }
}
```

### 3. Query the Results

After the background workers process your data (usually within seconds):

```csharp
[HttpGet("customers")]
public async Task<IActionResult> GetCustomers(int page = 1, int size = 50)
{
    // Query canonical view (the current state of all customers)
    using (DataSetContext.With(FlowSets.ViewShort(Constants.Views.Canonical)))
    {
    var customers = await CanonicalProjection<Customer>.Page(page, size);
        return Ok(customers);
    }
}

[HttpGet("customers/{referenceId}")]
public async Task<IActionResult> GetCustomer(string referenceId)
{
    // Get a specific customer's current state
    var customer = await DynamicFlowEntity<Customer>.Get(referenceId);
    if (customer == null)
        return NotFound();
        
    return Ok(customer.Model);
}

[HttpGet("customers/{referenceId}/lineage")]
public async Task<IActionResult> GetCustomerLineage(string referenceId)
{
    // Get the historical trail of how this customer evolved
    using (DataSetContext.With(FlowSets.ViewShort(Constants.Views.Lineage)))
    {
    var lineage = await LineageProjection<Customer>.Get($"{Constants.Views.Lineage}::{referenceId}");
        return Ok(lineage?.View);
    }
}
```

## Aggregation and Association

### Understanding Aggregation Tags

Aggregation tags determine when two records represent the same entity. Records with matching tag values get the same `ReferenceId`:

```csharp
public sealed class Contact : FlowEntity<Contact>
{
    // Primary identifiers
    [AggregationTag("email")]
    public string? Email { get; set; }
    
    [AggregationTag("phone")]
    public string? Phone { get; set; }
    
    // Secondary identifiers (multiple tags on one property)
    [AggregationTag("socialSecurity")]
    [AggregationTag("taxId")]
    public string? GovernmentId { get; set; }
    
    // Nested path support
    [AggregationTag("account.number")]
    public string? AccountNumber { get; set; }
    
    // Regular data fields (not used for aggregation)
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public Address? Address { get; set; }
}

public class Address
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
}
```

### Association Rules

Sora.Flow follows strict rules when associating records:

1. **No Keys**: If no aggregation tag values are present → reject with `NO_KEYS`
2. **Single Owner**: If all keys belong to the same existing entity → use that `ReferenceId`
3. **Multi-Owner Collision**: If keys belong to different entities → reject with `MULTI_OWNER_COLLISION`
4. **New Entity**: If no keys exist → create new `ReferenceId`

When a new ReferenceId is created during Associate, Flow also mints a ULID that becomes the primary Id for the entity. Both are preserved across the stack.

### Example: Multi-Source Customer Data

Let's ingest customer data from multiple sources and watch them merge:

```csharp
// CRM system data
var crmRecord = new StageRecord<Contact>
{
    Id = Guid.NewGuid().ToString("n"),
    SourceId = "crm-12345",
    OccurredAt = DateTimeOffset.UtcNow,
    StagePayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["email"] = "john.doe@example.com",
        ["firstName"] = "John",
        ["lastName"] = "Doe",
        ["company"] = "Acme Corp"
    }
};

// Support system data (same person, different source)
var supportRecord = new StageRecord<Contact>
{
    Id = Guid.NewGuid().ToString("n"),
    SourceId = "support-67890",
    OccurredAt = DateTimeOffset.UtcNow.AddMinutes(30),
    StagePayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["email"] = "john.doe@example.com",  // Same email = same person
        ["phone"] = "+1-555-123-4567",
        ["firstName"] = "Johnny",             // Different first name
        ["preferredLanguage"] = "English"
    }
};

// Marketing system data (adds phone number)
var marketingRecord = new StageRecord<Contact>
{
    Id = Guid.NewGuid().ToString("n"),
    SourceId = "marketing-555",
    OccurredAt = DateTimeOffset.UtcNow.AddHours(1),
    StagePayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["phone"] = "+1-555-123-4567",       // Same phone = same person
        ["firstName"] = "John",
        ["lastName"] = "Doe",
        ["marketingOptIn"] = true,
        ["lastCampaign"] = "summer-2025"
    }
};

// All three records will get the same ReferenceId because they share email/phone
using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
{
    await crmRecord.Save();
    await supportRecord.Save();
    await marketingRecord.Save();
}
```

### Collision Detection

Sometimes you'll encounter conflicting data that Flow can't automatically resolve:

```csharp
// This would cause a MULTI_OWNER_COLLISION because the email belongs to one
// entity and the phone belongs to a different entity
var conflictedRecord = new StageRecord<Contact>
{
    Id = Guid.NewGuid().ToString("n"),
    SourceId = "external-import",
    OccurredAt = DateTimeOffset.UtcNow,
    StagePayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["email"] = "alice@example.com",     // Belongs to Reference-A
        ["phone"] = "+1-555-999-8888"       // Belongs to Reference-B
    }
};

// This record will be rejected and moved to the "parked" stage for manual review
```

You can query rejected records for investigation:

```csharp
[HttpGet("admin/rejections")]
public async Task<IActionResult> GetRejections()
{
    var rejections = await RejectionReport.All();
    return Ok(rejections.Select(r => new
    {
    r.Id,
    r.ReasonCode,
    r.EvidenceJson,
    r.PolicyVersion,
    r.CreatedAt
    }));
}

[HttpGet("admin/parked")]
public async Task<IActionResult> GetParkedRecords()
{
    using (DataSetContext.With(FlowSets.StageShort(FlowSets.Parked)))
    {
        var parked = await ParkedRecord<Contact>.FirstPage(100);
        return Ok(parked);
    }
}
```

### Healing Parked Records

When you need to resolve parked records (e.g., after fixing data quality issues or resolving collisions), use the semantic `.HealAsync()` extension method:

```csharp
[HttpPost("admin/heal/{parkedId}")]
public async Task<IActionResult> HealParkedRecord(string parkedId, 
    [FromServices] IFlowActions flowActions)
{
    using (DataSetContext.With(FlowSets.StageShort(FlowSets.Parked)))
    {
        var parkedRecord = await ParkedRecord<Contact>.Get(parkedId);
        if (parkedRecord == null)
            return NotFound();

        try
        {
            // Option 1: Heal with resolved properties (merges with original data)
            await parkedRecord.HealAsync(flowActions, new
            {
                Email = "corrected-email@example.com",
                Phone = "+1-555-000-0000",
                IsVerified = true
            }, 
            healingReason: "Manual correction after data quality review",
            ct: HttpContext.RequestAborted);

            return Ok(new { Message = "Record healed successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }
}

// Option 2: Background service healing with full control over data
public class AddressResolutionService : BackgroundService
{
    private readonly IFlowActions _flowActions;
    private readonly IGeocoder _geocoder;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessParkedAddresses(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
    
    private async Task ProcessParkedAddresses(CancellationToken ct)
    {
        using var context = DataSetContext.With(FlowSets.StageShort(FlowSets.Parked));
        var parkedRecords = await ParkedRecord<Location>.FirstPage(100, ct);
        var waitingRecords = parkedRecords.Where(pr => pr.ReasonCode == "WAITING_ADDRESS_RESOLVE");
        
        foreach (var parkedRecord in waitingRecords)
        {
            try
            {
                var address = parkedRecord.Data?.TryGetValue("address", out var addr) == true ? 
                             addr?.ToString() : null;
                if (string.IsNullOrEmpty(address)) continue;
                
                // Resolve the address using external service
                var geocoded = await _geocoder.GeocodeAsync(address, ct);
                
                // Heal with the resolved data using full data dictionary control
                var resolvedData = new Dictionary<string, object?>(parkedRecord.Data!, StringComparer.OrdinalIgnoreCase)
                {
                    ["canonicalAddress"] = geocoded.FormattedAddress,
                    ["latitude"] = geocoded.Latitude,
                    ["longitude"] = geocoded.Longitude,
                    ["confidence"] = geocoded.Confidence,
                    ["resolved"] = true
                };
                
                await parkedRecord.HealAsync(_flowActions, resolvedData, 
                    healingReason: $"Address resolved via geocoding service", 
                    ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to heal parked record {Id}", parkedRecord.Id);
                // Record remains parked for next cycle
            }
        }
    }
}
```

## Projections and Views

### Canonical View

The canonical view represents the current, merged state of each entity:

```csharp
[HttpGet("contacts/{referenceId}/canonical")]
public async Task<IActionResult> GetCanonicalContact(string referenceId)
{
    using (DataSetContext.With(FlowSets.ViewShort(Constants.Views.Canonical)))
    {
        var canonical = await CanonicalProjection<Contact>.Get($"{Constants.Views.Canonical}::{referenceId}");
        
        if (canonical?.Model == null)
            return NotFound();
            
        // The Model property contains the merged data from all sources
        var contact = canonical.Model as IDictionary<string, object?>;
        
        return Ok(new
        {
            ReferenceId = canonical.ReferenceId,
            Email = contact?["email"],
            Phone = contact?["phone"],
            FirstName = contact?["firstName"],
            LastName = contact?["lastName"],
            Company = contact?["company"],
            PreferredLanguage = contact?["preferredLanguage"],
            MarketingOptIn = contact?["marketingOptIn"],
            LastUpdated = canonical.UpdatedAt
        });
    }
}
```

### Lineage View

The lineage view shows the historical evolution of an entity:

```csharp
[HttpGet("contacts/{referenceId}/lineage")]
public async Task<IActionResult> GetContactLineage(string referenceId)
{
    using (DataSetContext.With(FlowSets.ViewShort(Constants.Views.Lineage)))
    {
        var lineage = await LineageProjection<Contact>.Get($"{Constants.Views.Lineage}::{referenceId}");
        
        if (lineage?.View == null)
            return NotFound();
            
        // The View property contains field -> value -> sources mapping
        var lineageData = lineage.View as IDictionary<string, object?>;
        
        return Ok(new
        {
            ReferenceId = lineage.ReferenceId,
            Fields = lineageData?.ToDictionary(
                kv => kv.Key,
                kv => ProcessLineageField(kv.Value)
            )
        });
    }
}

private object ProcessLineageField(object? fieldData)
{
    if (fieldData is IDictionary<string, object?> valueMap)
    {
        return valueMap.ToDictionary(
            kv => kv.Key, // The value
            kv => kv.Value is IEnumerable<object> sources ? sources.ToArray() : new[] { kv.Value }
        );
    }
    return fieldData ?? new { };
}
```

### Custom Views

You can create custom projection views for specific use cases:

```csharp
// Define a custom view for contact summaries
public sealed class ContactSummaryProjection<TModel> : ProjectionView<TModel, ContactSummaryView>
{
    [Index]
    public string? Email { get; set; }
    
    [Index] 
    public string? Company { get; set; }
    
    public int SourceCount { get; set; }
    public DateTime LastActivity { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
}

public class ContactSummaryView
{
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string? Company { get; set; }
    public int SourceCount { get; set; }
    public DateTime LastActivity { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
}
```

## Identity Resolution

### External System Integration

When integrating with external systems, you often need to map their IDs to your canonical entities:

```csharp
// Define envelope for external system integration
public sealed class CrmEnvelope
{
    [EntityLink(typeof(Contact), LinkKind.ExternalId)]
    public required string CrmContactId { get; init; }
    
    public required string System { get; init; } = "crm";
    public required string Adapter { get; init; } = "salesforce";
}

// Ingest data with external ID mapping
public async Task IngestFromCrm(CrmContactData data)
{
    var record = new StageRecord<Contact>
    {
        Id = Guid.NewGuid().ToString("n"),
        SourceId = $"crm-{data.Id}",
        OccurredAt = DateTimeOffset.UtcNow,
        StagePayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            // Envelope fields for identity resolution
            [Constants.Envelope.System] = "crm",
            [Constants.Envelope.Adapter] = "salesforce",
            [nameof(CrmEnvelope.CrmContactId)] = data.Id,
            
            // Actual data
            ["email"] = data.Email,
            ["firstName"] = data.FirstName,
            ["lastName"] = data.LastName,
            ["company"] = data.CompanyName
        }
    };

    using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
    {
        await record.Save();
    }
}
```

### Provisional Identity Links

When Flow encounters an external ID it hasn't seen before, it creates a provisional identity link:

```csharp
[HttpGet("identity/links")]
public async Task<IActionResult> GetIdentityLinks()
{
    var links = await IdentityLink<Contact>.FirstPage(100);
    
    return Ok(links.Select(link => new
    {
        link.Id,
        link.System,
        link.Adapter, 
        link.ExternalId,
        link.ReferenceId,
        link.Provisional,
        link.CreatedAt,
        link.ExpiresAt
    }));
}

[HttpPost("identity/links/{linkId}/confirm")]
public async Task<IActionResult> ConfirmIdentityLink(string linkId)
{
    var link = await IdentityLink<Contact>.Get(linkId);
    if (link == null)
        return NotFound();
        
    // Remove provisional status
    link.Provisional = false;
    link.ExpiresAt = null;
    await link.Save();
    
    return Ok();
}
```

## Advanced Scenarios

### Multi-Model Relationships

In complex scenarios, you might have multiple related entities:

```csharp
// Customer entity
public sealed class Customer : FlowEntity<Customer>
{
    [AggregationTag("customerId")]
    public string? CustomerId { get; set; }
    
    [AggregationTag("email")]
    public string? Email { get; set; }
    
    public string? Name { get; set; }
    public string? Company { get; set; }
}

// Order entity (related to Customer)
public sealed class Order : FlowEntity<Order>
{
    [AggregationTag("orderId")]
    public string? OrderId { get; set; }
    
    [AggregationTag("orderNumber")]
    public string? OrderNumber { get; set; }
    
    // Link to customer (not an aggregation tag)
    public string? CustomerId { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? OrderDate { get; set; }
}

// OrderItem entity (related to Order)
public sealed class OrderItem : FlowEntity<OrderItem>
{
    [AggregationTag("lineItemId")]
    public string? LineItemId { get; set; }
    
    public string? OrderId { get; set; }
    public string? ProductId { get; set; }
    public int? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
}
```

### Complex Aggregation Rules

For sophisticated business rules, use custom model names and configurations:

```csharp
[FlowModel("enterprise-customer")]
public sealed class EnterpriseCustomer : FlowEntity<EnterpriseCustomer>
{
    [AggregationTag("taxId")]
    [AggregationTag("federalId")]
    public string? TaxIdentifier { get; set; }
    
    [AggregationTag("dunsNumber")]
    public string? DunsNumber { get; set; }
    
    [AggregationTag("domain")]
    public string? EmailDomain { get; set; }
    
    public string? LegalName { get; set; }
    public string? TradeName { get; set; }
    public string? Industry { get; set; }
    public int? EmployeeCount { get; set; }
    public decimal? AnnualRevenue { get; set; }
}
```

### Time-Based Processing

Handle time-sensitive data with proper timestamps:

```csharp
public async Task IngestTimeSeriesData(SensorReading reading)
{
    var record = new StageRecord<SensorData>
    {
        Id = Guid.NewGuid().ToString("n"),
        SourceId = $"sensor-{reading.SensorId}-{reading.Timestamp:yyyyMMddHHmmss}",
        OccurredAt = reading.Timestamp, // Use actual event time
        StagePayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["sensorId"] = reading.SensorId,
            ["deviceId"] = reading.DeviceId,
            ["temperature"] = reading.Temperature,
            ["humidity"] = reading.Humidity,
            ["batteryLevel"] = reading.BatteryLevel,
            ["location"] = new 
            {
                lat = reading.Latitude,
                lng = reading.Longitude
            }
        }
    };

    using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
    {
        await record.Save();
    }
}

[FlowModel("sensor")]
public sealed class SensorData : FlowEntity<SensorData>
{
    [AggregationTag("sensorId")]
    public string? SensorId { get; set; }
    
    [AggregationTag("deviceId")]
    public string? DeviceId { get; set; }
    
    public decimal? Temperature { get; set; }
    public decimal? Humidity { get; set; }
    public decimal? BatteryLevel { get; set; }
    public object? Location { get; set; }
}
```

### Bulk Data Import

For high-volume scenarios, use batch processing:

```csharp
[HttpPost("import/customers")]
public async Task<IActionResult> BulkImportCustomers([FromBody] CustomerData[] customers)
{
    var records = customers.Select(customer => new StageRecord<Customer>
    {
        Id = Guid.NewGuid().ToString("n"),
        SourceId = $"import-{customer.Id}",
        OccurredAt = DateTimeOffset.UtcNow,
        StagePayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["email"] = customer.Email,
            ["phone"] = customer.Phone,
            ["firstName"] = customer.FirstName,
            ["lastName"] = customer.LastName,
            ["company"] = customer.Company,
            ["importBatch"] = DateTime.UtcNow.ToString("yyyyMMdd-HHmm")
        }
    }).ToArray();

    using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
    {
        await Data<StageRecord<Customer>, string>.UpsertManyAsync(records);
    }

    return Ok(new { ImportedCount = records.Length });
}
```

## Monitoring and Actions

### Flow Actions

Use the Flow actions system for programmatic control:

```csharp
public class FlowManagementController : ControllerBase
{
    private readonly IFlowActions _flowActions;
    
    public FlowManagementController(IFlowActions flowActions)
    {
        _flowActions = flowActions;
    }
    
    [HttpPost("seed/{model}")]
    public async Task<IActionResult> SeedModel(string model, [FromBody] object payload)
    {
        var referenceId = Guid.NewGuid().ToString("n");
        await _flowActions.SeedAsync(model, referenceId, payload);
        return Ok(new { ReferenceId = referenceId });
    }
    
    [HttpGet("report/{model}")]
    public async Task<IActionResult> GetModelReport(string model)
    {
        await _flowActions.ReportAsync(model, "admin-report", new { RequestedAt = DateTime.UtcNow });
        return Ok(new { Message = "Report requested" });
    }
    
    [HttpPost("ping/{model}")]
    public async Task<IActionResult> PingModel(string model)
    {
        await _flowActions.PingAsync(model);
        return Ok(new { Message = "Ping sent" });
    }
}
```

### Custom Monitoring

Implement custom business logic during projection:

```csharp
public class CustomerMonitor : IFlowMonitor<Customer>
{
    private readonly ILogger<CustomerMonitor> _logger;
    
    public CustomerMonitor(ILogger<CustomerMonitor> logger)
    {
        _logger = logger;
    }
    
    public Task OnProjectedAsync(FlowMonitorContext context, CancellationToken ct = default)
    {
        var model = context.Model;
        var policies = context.Policies;
        
        // Business rule: VIP customers get special handling
        if (IsVipCustomer(model))
        {
            policies["customerTier"] = "vip";
            policies["dataRetention"] = "extended";
            _logger.LogInformation("VIP customer detected: {Email}", model.GetValueOrDefault("email"));
        }
        
        // Data quality: normalize phone numbers
        if (model.TryGetValue("phone", out var phone) && phone is string phoneStr)
        {
            model["phone"] = NormalizePhoneNumber(phoneStr);
        }
        
        // Set last updated timestamp
        model["lastProcessed"] = DateTimeOffset.UtcNow;
        
        return Task.CompletedTask;
    }
    
    private bool IsVipCustomer(IDictionary<string, object?> model)
    {
        // Example: customers from certain companies are VIP
        var company = model.GetValueOrDefault("company")?.ToString()?.ToLowerInvariant();
        return company?.Contains("enterprise") == true || company?.Contains("corp") == true;
    }
    
    private string NormalizePhoneNumber(string phone)
    {
        // Simple normalization - remove all non-digits, add +1 prefix if US number
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length == 10 ? $"+1{digits}" : $"+{digits}";
    }
}

// Register the monitor
public void ConfigureServices(IServiceCollection services)
{
    services.AddSora();
    services.AddFlowMonitor<Customer, CustomerMonitor>();
}
```

### Health Monitoring

Monitor the health of your Flow pipeline:

```csharp
[HttpGet("health/flow")]
public async Task<IActionResult> GetFlowHealth()
{
    var health = new
    {
        Timestamp = DateTimeOffset.UtcNow,
        Stages = await GetStageHealth(),
        Rejections = await GetRejectionStats(),
        Processing = await GetProcessingStats()
    };
    
    return Ok(health);
}

private async Task<object> GetStageHealth()
{
    var intakeCount = await GetStageCount<Customer>(FlowSets.Intake);
    var keyedCount = await GetStageCount<Customer>(FlowSets.Keyed);
    var parkedCount = await GetStageCount<Customer>(FlowSets.Parked);
    
    return new
    {
        Intake = intakeCount,
        Keyed = keyedCount,
        Parked = parkedCount,
        BacklogSize = intakeCount + keyedCount
    };
}

private async Task<int> GetStageCount<TModel>(string stage)
{
    using (DataSetContext.With(FlowSets.StageShort(stage)))
    {
        // Use first-class statics + count API for accuracy and adapter correctness
    return await StageRecord<TModel>.Count();
    }
}

private async Task<object> GetRejectionStats()
{
    var rejections = await RejectionReport.FirstPage(100);
    var recentRejections = rejections.Where(r => r.CreatedAt > DateTimeOffset.UtcNow.AddHours(-1));
    
    return new
    {
        TotalCount = rejections.Count(),
        RecentCount = recentRejections.Count(),
        ReasonCodes = rejections.GroupBy(r => r.ReasonCode)
                               .ToDictionary(g => g.Key, g => g.Count())
    };
}
```

## Production Considerations

### Performance Tuning

```json
{
  "Sora": {
    "Flow": {
      "BatchSize": 500,
      "PurgeEnabled": true,
      "PurgeInterval": "00:02:00",
      "IntakeTtl": "1.00:00:00",
      "KeyedTtl": "7.00:00:00",
      "ParkedTtl": "30.00:00:00",
      "CanonicalExcludeTagPrefixes": ["temp.", "debug.", "system."]
    }
  }
}
```

### Error Handling

```csharp
public class ErrorHandlingService : IFlowMonitor<Customer>
{
    private readonly ILogger<ErrorHandlingService> _logger;
    private readonly IMessageBus _messageBus;
    
    public async Task OnProjectedAsync(FlowMonitorContext context, CancellationToken ct = default)
    {
        try
        {
            // Your business logic here
            await ProcessCustomerData(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing customer {ReferenceId}", context.ReferenceId);
            
            // Send to error handling queue
            await _messageBus.SendAsync(new CustomerProcessingError
            {
                ReferenceId = context.ReferenceId,
                Error = ex.Message,
                Timestamp = DateTimeOffset.UtcNow
            }, ct);
            
            // Don't rethrow - let the projection complete but mark for review
            context.Policies["hasError"] = "true";
            context.Policies["errorMessage"] = ex.Message;
        }
    }
}
```

### Scaling Considerations

1. **Horizontal Scaling**: Flow supports multiple instances processing different models
2. **Database Partitioning**: Use model-based partitioning for large datasets
3. **Message Queue Scaling**: Configure separate queues per model type
4. **TTL Management**: Regularly purge old data to maintain performance

```csharp
// Example: Model-specific configuration
public void ConfigureFlow(IServiceCollection services)
{
    services.Configure<FlowOptions>(o =>
    {
        // Different batch sizes per workload
        o.BatchSize = Environment.GetEnvironmentVariable("FLOW_BATCH_SIZE") switch
        {
            "large" => 1000,
            "small" => 50,
            _ => 200
        };
        
        // Enable aggressive purging in high-volume scenarios
        o.PurgeEnabled = true;
        o.PurgeInterval = TimeSpan.FromMinutes(1);
        
        // Shorter TTLs for high-volume models
        if (IsHighVolumeEnvironment())
        {
            o.IntakeTtl = TimeSpan.FromHours(4);
            o.KeyedTtl = TimeSpan.FromDays(1);
        }
    });
}
```

### Security

```csharp
[Authorize(Policy = "FlowAdmin")]
[HttpPost("admin/reproject/{referenceId}")]
public async Task<IActionResult> ReprojectEntity(string referenceId)
{
    // Audit the operation
    _logger.LogWarning("Manual reproject requested by {User} for {ReferenceId}", 
                      User.Identity?.Name, referenceId);
    
    // Implement rate limiting
    var rateLimitKey = $"reproject:{User.Identity?.Name}";
    if (!await _rateLimiter.TryAcquireAsync(rateLimitKey, 1, TimeSpan.FromMinutes(5)))
    {
        return StatusCode(429, "Rate limit exceeded");
    }
    
    // Trigger reproject
    var runtime = HttpContext.RequestServices.GetRequiredService<IFlowRuntime>();
    await runtime.ReprojectAsync(referenceId);
    
    return Ok();
}
```

## Troubleshooting

### Common Issues

#### 1. Records Not Processing

**Symptoms**: Records stuck in intake, not moving to keyed stage

**Causes & Solutions**:
```csharp
// Check for missing aggregation tags
var model = FlowRegistry.GetAggregationTags(typeof(Customer));
if (model.Length == 0)
{
    // Add [AggregationTag] attributes to your model
}

// Check rejection reports
var rejections = await RejectionReport.All();
foreach (var rejection in rejections.Where(r => r.CreatedAt > DateTimeOffset.UtcNow.AddHours(-1)))
{
    Console.WriteLine($"Rejection: {rejection.ReasonCode} - {rejection.Evidence}");
}
```

#### 2. Multi-Owner Collisions

**Symptoms**: Records being parked with `MULTI_OWNER_COLLISION`

**Investigation**:
```csharp
[HttpGet("debug/collisions")]
public async Task<IActionResult> InvestigateCollisions()
{
    using (DataSetContext.With(FlowSets.StageShort(FlowSets.Parked)))
    {
            var parked = await Data<ParkedRecord<Customer>, string>
                .Query(p => p.ReasonCode == Constants.Rejections.MultiOwnerCollision);
            
            return Ok(parked.Select(p => new
        {
            p.Id,
            p.SourceId,
            p.ReasonCode,
                p.Evidence,
                Payload = p.StagePayload
        }));
    }
}
```

#### 3. Memory Issues

**Symptoms**: High memory usage, slow processing

**Solutions**:
```csharp
// Reduce batch sizes
services.Configure<FlowOptions>(o => 
{
    o.BatchSize = 50; // Reduce from default
    o.PurgeEnabled = true;
    o.PurgeInterval = TimeSpan.FromMinutes(2);
});

// Monitor stage sizes
[HttpGet("debug/stage-sizes")]
public async Task<IActionResult> GetStageSizes()
{
    var sizes = new Dictionary<string, int>();
    
    foreach (var stage in new[] { FlowSets.Intake, FlowSets.Keyed, FlowSets.Parked })
    {
        using (DataSetContext.With(FlowSets.StageShort(stage)))
        {
            var count = await Data<StageRecord<Customer>, string>.Query().CountAsync();
            sizes[stage] = count;
        }
    }
    
    return Ok(sizes);
}
```

### Debugging Tools

#### Enable Detailed Logging

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Sora.Flow": "Debug",
      "Sora.Flow.Runtime": "Trace"
    }
  }
}
```

#### Query Processing Status

```csharp
[HttpGet("debug/processing-status/{referenceId}")]
public async Task<IActionResult> GetProcessingStatus(string referenceId)
{
    var status = new
    {
        ReferenceId = referenceId,
        ReferenceItem = await ReferenceItem<Customer>.Get(referenceId),
        CanonicalView = await GetCanonicalProjection<Customer>(referenceId),
        LineageView = await GetLineageProjection<Customer>(referenceId),
        PendingTasks = await GetPendingTasks<Customer>(referenceId),
        StageRecords = await GetStageRecords<Customer>(referenceId)
    };
    
    return Ok(status);
}

private async Task<object?> GetCanonicalProjection<TModel>(string referenceId)
{
    using (DataSetContext.With(FlowSets.ViewShort(Constants.Views.Canonical)))
    {
        return await CanonicalProjection<TModel>.Get($"{Constants.Views.Canonical}::{referenceId}");
    }
}
```

### Performance Monitoring

```csharp
public class FlowMetricsService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await LogMetrics();
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
    
    private async Task LogMetrics()
    {
        var metrics = new
        {
            Timestamp = DateTimeOffset.UtcNow,
            IntakeSize = await GetStageSize<Customer>(FlowSets.Intake),
            KeyedSize = await GetStageSize<Customer>(FlowSets.Keyed),
            ParkedSize = await GetStageSize<Customer>(FlowSets.Parked),
            CanonicalCount = await GetCanonicalCount<Customer>(),
            RecentRejections = await GetRecentRejectionCount()
        };
        
        _logger.LogInformation("Flow Metrics: {@Metrics}", metrics);
    }
}
private async Task<int> GetCanonicalCount<TModel>()
{
    using (DataSetContext.With(FlowSets.ViewShort(Constants.Views.Canonical)))
    {
        return await CanonicalProjection<TModel>.Count();
    }
}
```

## Orchestrator and Client Commands

When building distributed systems, Flow often serves as an orchestrator that needs to send commands to external clients, adapters, or services. This section covers how to implement command dispatch patterns.

### Basic Command Dispatch

First, define your command contracts:

```csharp
// Command definitions
public record ProcessOrderCommand(string OrderId, decimal Amount, string CustomerId);
public record NotifyCustomerCommand(string CustomerId, string Message, string Channel);
public record UpdateInventoryCommand(string ProductId, int Quantity, string Operation);
public record CreateShipmentCommand(string OrderId, string Address);

// Command result
public record CommandResult(bool Success, string? ErrorMessage = null, object? Data = null);
```

### Flow Action Handler with Command Dispatch

Create a custom action handler that dispatches commands and emits FlowAck/FlowReport messages. Override the default handler by replacing the registered `IMessageHandler<FlowAction>`.

```csharp
public sealed class OrderFlowActionHandler : IMessageHandler<FlowAction>
{
    private readonly ICommandDispatcher _commands;
    private readonly ILogger<OrderFlowActionHandler> _log;

    public OrderFlowActionHandler(ICommandDispatcher commands, ILogger<OrderFlowActionHandler> log)
    { _commands = commands; _log = log; }

    public async Task HandleAsync(MessageEnvelope env, FlowAction msg, CancellationToken ct)
    {
        switch ((msg.Verb ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "seed":
                await HandleSeedAsync(msg, ct);
                break;
            case "report":
                await HandleReportAsync(msg, ct);
                break;
            case "ping":
                await new FlowAck(msg.Model, "ping", msg.ReferenceId, "ok", null, msg.CorrelationId).Send(ct);
                break;
            default:
                await new FlowAck(msg.Model, msg.Verb ?? string.Empty, msg.ReferenceId, "unsupported", $"Unknown verb '{msg.Verb}'", msg.CorrelationId).Send(ct);
                break;
        }
    }

    private async Task HandleSeedAsync(FlowAction action, CancellationToken ct)
    {
        var order = ToOrder(action.Payload);
        if (order is null)
        {
            await new FlowAck(action.Model, action.Verb, action.ReferenceId, "reject", "Invalid order payload", action.CorrelationId).Send(ct);
            return;
        }

        var result = await _commands.SendAsync("payment-service",
            new ProcessOrderCommand(order.Id, order.Amount, order.CustomerId), ct);

        if (!result.Success)
        {
            _log.LogWarning("Payment processing failed for order {OrderId}: {Error}", order.Id, result.ErrorMessage);
            await new FlowAck(action.Model, action.Verb, action.ReferenceId, "reject", $"Payment failed: {result.ErrorMessage}", action.CorrelationId).Send(ct);
            return;
        }

        await new FlowAck(action.Model, action.Verb, action.ReferenceId, "ok", $"Payment initiated for order {order.Id}", action.CorrelationId).Send(ct);
    }

    private async Task HandleReportAsync(FlowAction action, CancellationToken ct)
    {
        var order = ToOrder(action.Payload);
        if (order?.Status == "Processed")
        {
            var results = await Task.WhenAll(
                _commands.SendAsync("inventory-service", new UpdateInventoryCommand(order.ProductId, order.Quantity, "reserve"), ct),
                _commands.SendAsync("notification-service", new NotifyCustomerCommand(order.CustomerId, $"Order {order.Id} confirmed", "email"), ct),
                _commands.SendAsync("shipping-service", new CreateShipmentCommand(order.Id, order.ShippingAddress), ct)
            );

            if (results.Any(r => !r.Success))
            {
                await new FlowAck(action.Model, action.Verb ?? "report", action.ReferenceId, "reject", "One or more downstream services failed", action.CorrelationId).Send(ct);
                return;
            }
        }

        await new FlowAck(action.Model, action.Verb ?? "report", action.ReferenceId, "ok", "Order status unchanged", action.CorrelationId).Send(ct);
    }

    private static Order? ToOrder(object? payload)
    {
        if (payload is IDictionary<string, object?> d)
        {
            return new Order
            {
                Id = d.TryGetValue("id", out var id) ? id?.ToString() ?? string.Empty : string.Empty,
                Amount = d.TryGetValue("amount", out var amt) && decimal.TryParse(amt?.ToString(), out var dec) ? dec : 0m,
                CustomerId = d.TryGetValue("customerId", out var cid) ? cid?.ToString() ?? string.Empty : string.Empty,
                ProductId = d.TryGetValue("productId", out var pid) ? pid?.ToString() ?? string.Empty : string.Empty,
                Quantity = d.TryGetValue("quantity", out var q) && int.TryParse(q?.ToString(), out var qi) ? qi : 0,
                ShippingAddress = d.TryGetValue("shippingAddress", out var sa) ? sa?.ToString() ?? string.Empty : string.Empty,
                Status = d.TryGetValue("status", out var st) ? st?.ToString() ?? string.Empty : string.Empty
            };
        }
        return null;
    }
}
```

### Command Dispatcher Implementation

Create a robust command dispatcher that handles retries, circuit breaking, and routing:

```csharp
public interface ICommandDispatcher
{
    Task<CommandResult> SendAsync<TCommand>(string targetService, TCommand command, 
                                          CancellationToken cancellationToken = default);
    Task<CommandResult> SendWithRetryAsync<TCommand>(string targetService, TCommand command, 
                                                   int maxRetries = 3, 
                                                   CancellationToken cancellationToken = default);
}

public class MessageBusCommandDispatcher : ICommandDispatcher
{
    private readonly IMessageBus _messageBus;
    private readonly ICircuitBreaker _circuitBreaker;
    private readonly ILogger<MessageBusCommandDispatcher> _logger;
    private readonly Dictionary<string, string> _serviceRoutes;

    public MessageBusCommandDispatcher(
        IMessageBus messageBus, 
        ICircuitBreaker circuitBreaker,
        IOptions<ServiceRoutingOptions> routingOptions,
        ILogger<MessageBusCommandDispatcher> logger)
    {
        _messageBus = messageBus;
        _circuitBreaker = circuitBreaker;
        _logger = logger;
        _serviceRoutes = routingOptions.Value.Routes;
    }

    public async Task<CommandResult> SendAsync<TCommand>(string targetService, TCommand command, 
                                                       CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString();
        
        try
        {
            await _circuitBreaker.ExecuteAsync(async () =>
            {
                var routingKey = _serviceRoutes.GetValueOrDefault(targetService, targetService);
                var message = new CommandMessage<TCommand>
                {
                    CorrelationId = correlationId,
                    Command = command,
                    Timestamp = DateTimeOffset.UtcNow,
                    TargetService = targetService
                };

                await _messageBus.PublishAsync(routingKey, message, cancellationToken);
            });

            // Wait for acknowledgment (with timeout)
            var ack = await WaitForAcknowledgment(correlationId, TimeSpan.FromSeconds(30));
            return ack ?? new CommandResult(false, "Command timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch command to {Service}: {Command}", 
                           targetService, typeof(TCommand).Name);
            return new CommandResult(false, ex.Message);
        }
    }

    public async Task<CommandResult> SendWithRetryAsync<TCommand>(string targetService, TCommand command, 
                                                                int maxRetries = 3, 
                                                                CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var result = await SendAsync(targetService, command, cancellationToken);
            if (result.Success)
            {
                return result;
            }

            if (attempt < maxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                _logger.LogWarning("Command dispatch attempt {Attempt} failed, retrying in {Delay}s: {Error}", 
                                 attempt, delay.TotalSeconds, result.ErrorMessage);
                await Task.Delay(delay, cancellationToken);
            }
        }

        return new CommandResult(false, $"All {maxRetries} attempts failed");
    }
}
```

### Advanced Orchestration Patterns

For complex workflows, materialize an orchestration view via a background projector that writes `ProjectionView<TModel,TView>` documents into a named Flow view set (e.g., `views.orchestration`).

```csharp
public sealed class OrchestrationState
{
    public string EntityId { get; set; } = string.Empty;
    public string CurrentStep { get; set; } = string.Empty;
    public List<string> CompletedSteps { get; set; } = new();
    public List<string> PendingCommands { get; set; } = new();
    public DateTimeOffset? LastCommandSent { get; set; }
    public int RetryCount { get; set; }
}

public sealed class OrderOrchestrationView : ProjectionView<Order, OrchestrationState> { }

public sealed class OrderOrchestrationProjector : BackgroundService
{
    private readonly ILogger<OrderOrchestrationProjector> _log;
    public const string ViewName = "orchestration";
    public OrderOrchestrationProjector(ILogger<OrderOrchestrationProjector> log) { _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (DataSetContext.With(FlowSets.StageShort(FlowSets.Keyed)))
                {
                    var page = await StageRecord<Order>.FirstPage(200, stoppingToken);
                    foreach (var rec in page)
                    {
                        var state = new OrchestrationState
                        {
                            EntityId = rec.CorrelationId ?? rec.SourceId,
                            CurrentStep = "validation",
                            CompletedSteps = new(),
                            PendingCommands = new() { "validate-order" }
                        };

                        var doc = new OrderOrchestrationView
                        {
                            Id = $"{ViewName}::{state.EntityId}",
                            ReferenceId = state.EntityId,
                            ViewName = ViewName,
                            View = state
                        };
                        await Data<OrderOrchestrationView, string>.UpsertAsync(doc, FlowSets.ViewShort(ViewName), stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            { _log.LogWarning(ex, "OrderOrchestrationProjector iteration failed"); }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
```

### Configuration and Registration

Wire up the command dispatch system and override the default Flow action handler; register the projector as a hosted service.

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddSora();

    // Flow actions (sender + default responder); then override responder with custom handler
    services.AddFlowActions();
    services.Replace(ServiceDescriptor.Singleton<IMessageHandler<FlowAction>, OrderFlowActionHandler>());

    // Command dispatch infrastructure
    services.AddSingleton<ICommandDispatcher, MessageBusCommandDispatcher>();
    services.AddSingleton<ICircuitBreaker, CircuitBreaker>();

    // Service routing configuration
    services.Configure<ServiceRoutingOptions>(options =>
    {
        options.Routes["payment-service"] = "commands.payment";
        options.Routes["inventory-service"] = "commands.inventory";
        options.Routes["notification-service"] = "commands.notification";
        options.Routes["shipping-service"] = "commands.shipping";
    });

    // Orchestration projector
    services.AddHostedService<OrderOrchestrationProjector>();
}
```

## Custom Business Rules Between Steps

Flow allows you to inject custom business logic at every stage of the pipeline. This enables sophisticated rule-based processing, validation, and routing.

### Rule-Based Processing Engine

Create a flexible rule engine that operates on Flow entities:

```csharp
public interface IBusinessRule<TModel>
{
    string Name { get; }
    int Priority { get; }
    Task<RuleResult> EvaluateAsync(FlowEntity<TModel> entity, RuleContext context);
}

public record RuleResult(bool Passed, string? Reason = null, object? Data = null)
{
    public static RuleResult Pass(object? data = null) => new(true, null, data);
    public static RuleResult Fail(string reason) => new(false, reason);
}

public class RuleContext
{
    public Dictionary<string, object> Properties { get; } = new();
    public string Stage { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
```

### Custom Processing Steps with Rules

Implement custom processors that evaluate business rules:

```csharp
public class CustomerValidationRule : IBusinessRule<Customer>
{
    public string Name => "CustomerValidation";
    public int Priority => 100;

    public async Task<RuleResult> EvaluateAsync(FlowEntity<Customer> entity, RuleContext context)
    {
        var customer = entity.Model;
        
        // Age validation
        if (customer.Age < 18)
            return RuleResult.Fail("Customer must be 18 or older");
            
        // Credit check for high-value customers
        if (customer.EstimatedValue > 10000)
        {
            var creditScore = await GetCreditScore(customer.Id);
            if (creditScore < 650)
                return RuleResult.Fail($"Credit score too low: {creditScore}");
        }
        
        // Geographic restrictions
        if (RestrictedRegions.Contains(customer.Region))
            return RuleResult.Fail($"Service not available in region: {customer.Region}");
            
        return RuleResult.Pass(new { CreditScore = await GetCreditScore(customer.Id) });
    }
}

public class RiskAssessmentRule : IBusinessRule<Customer>
{
    public string Name => "RiskAssessment";
    public int Priority => 200;

    public async Task<RuleResult> EvaluateAsync(FlowEntity<Customer> entity, RuleContext context)
    {
        var customer = entity.Model;
        var riskScore = await CalculateRiskScore(customer);
        
        // Set risk-based properties in context
        context.Properties["RiskScore"] = riskScore;
        context.Properties["RiskTier"] = GetRiskTier(riskScore);
        
        // High-risk customers require manual review
        if (riskScore > 80)
        {
            context.Properties["RequiresManualReview"] = true;
            return RuleResult.Fail("High risk customer requires manual review");
        }
        
        return RuleResult.Pass();
    }
    
    private async Task<int> CalculateRiskScore(Customer customer)
    {
        // Complex risk calculation logic
        var score = 0;
        
        // Check transaction history
        var recentTransactions = await GetRecentTransactions(customer.Id);
        if (recentTransactions.Count(t => t.Amount > 5000) > 3)
            score += 30;
            
        // Check identity verification
        if (!customer.IsIdentityVerified)
            score += 25;
            
        // Check against watchlists
        if (await IsOnWatchlist(customer.Id))
            score += 50;
            
        return Math.Min(score, 100);
    }
}
```

### Rule Engine Integration

Create a processor that applies rules between Flow stages:

```csharp
public class RuleBasedProcessor<TModel> : IFlowProcessor<TModel>
{
    private readonly IEnumerable<IBusinessRule<TModel>> _rules;
    private readonly ILogger<RuleBasedProcessor<TModel>> _logger;

    public RuleBasedProcessor(
        IEnumerable<IBusinessRule<TModel>> rules,
        ILogger<RuleBasedProcessor<TModel>> logger)
    {
        _rules = rules.OrderBy(r => r.Priority);
        _logger = logger;
    }

    public async Task<ProcessingResult<TModel>> ProcessAsync(FlowEntity<TModel> entity, string stage)
    {
        var context = new RuleContext { Stage = stage };
        var appliedRules = new List<string>();
        var ruleData = new Dictionary<string, object>();

        foreach (var rule in _rules)
        {
            try
            {
                var result = await rule.EvaluateAsync(entity, context);
                appliedRules.Add(rule.Name);

                if (!result.Passed)
                {
                    _logger.LogWarning("Rule {RuleName} failed for entity {ReferenceId}: {Reason}",
                                     rule.Name, entity.ReferenceId, result.Reason);
                    
                    return ProcessingResult<TModel>.Rejected(
                        entity,
                        $"Business rule violation: {result.Reason}",
                        new { 
                            FailedRule = rule.Name, 
                            AppliedRules = appliedRules,
                            RuleContext = context.Properties 
                        });
                }

                if (result.Data != null)
                {
                    ruleData[rule.Name] = result.Data;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating rule {RuleName} for entity {ReferenceId}",
                               rule.Name, entity.ReferenceId);
                
                // Continue with other rules or fail fast based on configuration
                return ProcessingResult<TModel>.Rejected(
                    entity, 
                    $"Rule evaluation error: {rule.Name}",
                    new { Exception = ex.Message });
            }
        }

        // All rules passed - enhance entity with rule results
        var enhancedEntity = entity with 
        { 
            Properties = entity.Properties
                .Concat(context.Properties)
                .Concat(ruleData.SelectMany(kv => new[] { new KeyValuePair<string, object>($"Rule_{kv.Key}", kv.Value) }))
                .ToDictionary(kv => kv.Key, kv => kv.Value)
        };

        return ProcessingResult<TModel>.Success(enhancedEntity);
    }
}

## Identifier Policy: ULID and CanonicalId {#identifier-policy-ulid-and-canonicalid}

This guide adopts the approved identity model from FLOW-0104: a dual identifier per entity.

- Id (ULID): primary identifier for transport, URLs, and storage; outward-facing and URL-friendly
- CanonicalId: stable business key derived from aggregation tags; drives association and domain joins

When a new entity is created during Associate, Flow mints a ULID and preserves both values throughout the pipeline.

### Minting and Propagation

- Mint at Associate: the first time a CanonicalId is resolved to a new entity
- Enforce uniqueness on CanonicalId in the canonical registry (unique index)
- Propagate both identifiers across stages, keyed records, and projection views

Envelope fields used across the pipeline:

- CorrelationId: use for business correlation (often your CanonicalId)
- ReferenceUlid (or EntityUlid): the minted ULID after Associate; attach for downstream consumers

### API and Storage

- Default routes should use ULID: /{model}/{id}
- Provide a secondary business-key route: /{model}/by-cid/{canonicalId}
- Store projections keyed by ULID; keep CanonicalId as a field for filtering/indexing

### Example: Dual-route controller

```csharp
[ApiController]
[Route("api/contacts")]
public sealed class ContactsController : ControllerBase
{
    // GET api/contacts/{id}  (ULID)
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        using (DataSetContext.With(FlowSets.ViewShort(Constants.Views.Canonical)))
        {
            var view = await CanonicalProjection<Contact>.Get($"{Constants.Views.Canonical}::{id}");
            return view?.Model is null ? NotFound() : Ok(view.Model);
        }
    }

    // GET api/contacts/by-cid/{canonicalId} (business key)
    [HttpGet("by-cid/{canonicalId}")]
    public async Task<IActionResult> GetByCanonicalId(string canonicalId)
    {
        // Resolve ULID via identity map / reference index, then reuse GetById
        var refItem = await ReferenceItem<Contact>.GetByCanonicalId(canonicalId);
        if (refItem is null) return NotFound();
        return await GetById(refItem.Id); // refItem.Id is ULID
    }
}
```

### Example: Enrich records after Associate

```csharp
// After Associate determines CanonicalId, enrich keyed record metadata
var keyed = await StageRecord<Contact>.FirstPage(100);
foreach (var r in keyed)
{
    // r.CorrelationId stays business-focused (often CanonicalId)
    // r.Metadata["referenceUlid"] carries the ULID for downstream consumers
    r.Metadata ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    if (!r.Metadata.ContainsKey("referenceUlid"))
    {
        var refItem = await ReferenceItem<Contact>.GetByCanonicalId(r.ReferenceId!);
        if (refItem != null)
        {
            r.Metadata["referenceUlid"] = refItem.Id; // ULID
            await r.Save();
        }
    }
}
```

### Guidance

- Prefer ULID in hyperlinks and external APIs; always include CanonicalId in payloads when relevant
- Index CanonicalId where you commonly filter or join
- For merges/splits, select a deterministic winner ULID and reproject views; maintain redirects/links for the losers

See also: `docs/decisions/FLOW-0104-ulid-primary-id-and-canonical-id.md` and `docs/decisions/ARCH-0052-core-ids-and-json-merge-policy.md`.

```csharp
public record ProcessingResult<TModel>(
    bool Success,
    FlowEntity<TModel> Entity,
    string? ErrorMessage = null,
    object? Metadata = null)
{
    public static ProcessingResult<TModel> Success(FlowEntity<TModel> entity, object? metadata = null) 
        => new(true, entity, null, metadata);
    public static ProcessingResult<TModel> Rejected(FlowEntity<TModel> entity, string error, object? metadata = null) 
        => new(false, entity, error, metadata);
}
```
### Conditional Routing Rules

Implement rules that determine entity routing and processing paths:

```csharp
public class RoutingRule : IBusinessRule<Customer>
{
    public string Name => "Routing";
    public int Priority => 50;

    public async Task<RuleResult> EvaluateAsync(FlowEntity<Customer> entity, RuleContext context)
    {
        var customer = entity.Model;
        
        // Determine processing path based on customer attributes
        var routingDecision = customer.Type switch
        {
            "Premium" => new RoutingDecision 
            { 
                Path = "premium-pipeline", 
                Priority = "high",
                RequiredApprovals = new[] { "manager" }
            },
            "Enterprise" => new RoutingDecision 
            { 
                Path = "enterprise-pipeline", 
                Priority = "critical",
                RequiredApprovals = new[] { "director", "compliance" }
            },
            _ => new RoutingDecision 
            { 
                Path = "standard-pipeline", 
                Priority = "normal",
                RequiredApprovals = Array.Empty<string>()
            }
        };

        // Apply geographical routing rules
        if (customer.Region == "EMEA")
        {
            routingDecision.AdditionalSteps.Add("gdpr-compliance");
            routingDecision.DataResidency = "eu-west-1";
        }

        context.Properties["RoutingDecision"] = routingDecision;
        return RuleResult.Pass(routingDecision);
    }
}

public class RoutingDecision
{
    public string Path { get; set; } = "";
    public string Priority { get; set; } = "";
    public string[] RequiredApprovals { get; set; } = Array.Empty<string>();
    public List<string> AdditionalSteps { get; set; } = new();
    public string? DataResidency { get; set; }
}
```

### Rule Configuration and Registration

Wire up the rule-based processing system and integrate via a Flow monitor hook (atomic with projection updates):

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddSora();

    // Register business rules
    services.AddScoped<IBusinessRule<Customer>, CustomerValidationRule>();
    services.AddScoped<IBusinessRule<Customer>, RiskAssessmentRule>();
    services.AddScoped<IBusinessRule<Customer>, RoutingRule>();

    // Register rule-based processor and monitor
    services.AddScoped<RuleBasedProcessor<Customer>>();
    services.AddFlowMonitor<Customer, CustomerRulesMonitor>();
}

public sealed class CustomerRulesMonitor : IFlowMonitor<Customer>
{
    private readonly RuleBasedProcessor<Customer> _processor;
    public CustomerRulesMonitor(RuleBasedProcessor<Customer> processor) { _processor = processor; }

    public async Task OnProjectedAsync(FlowMonitorContext context, CancellationToken ct)
    {
    // Wrap mutable root model dictionary into a simple entity shim (Expando copy)
    IDictionary<string, object?> exp = new ExpandoObject();
    foreach (var kv in context.Model) exp[kv.Key] = kv.Value;
    var shim = new DynamicFlowEntity<Customer> { ReferenceId = context.ReferenceId, Model = (ExpandoObject)exp };
        var result = await _processor.ProcessAsync(shim, "projected");
        if (!result.Success)
        {
            context.Policies["processing.status"] = "rejected";
            context.Policies["processing.reason"] = result.ErrorMessage ?? "rule-failed";
            return;
        }

        // Optionally mutate context.Model/context.Policies based on rule outputs
        if (result.Metadata is RoutingDecision rd)
        {
            context.Policies["routing.path"] = rd.Path;
            context.Policies["routing.priority"] = rd.Priority;
        }
    }
}
```

### Dynamic Rule Configuration

For enterprise scenarios, support dynamic rule configuration:

```csharp
public class ConfigurableRuleEngine<TModel>
{
    private readonly IRuleRepository _ruleRepository;
    private readonly IServiceProvider _serviceProvider;
    
    public async Task<List<IBusinessRule<TModel>>> GetActiveRulesAsync(string context)
    {
        var ruleConfigs = await _ruleRepository.GetActiveRulesAsync(typeof(TModel).Name, context);
        var rules = new List<IBusinessRule<TModel>>();
        
        foreach (var config in ruleConfigs)
        {
            var ruleType = Type.GetType(config.RuleTypeName);
            if (ruleType != null && _serviceProvider.GetService(ruleType) is IBusinessRule<TModel> rule)
            {
                // Apply configuration overrides
                if (rule is IConfigurableRule configurableRule)
                {
                    configurableRule.Configure(config.Parameters);
                }
                rules.Add(rule);
            }
        }
        
        return rules.OrderBy(r => r.Priority).ToList();
    }
}
```

---

This comprehensive guide provides developers with everything needed to build sophisticated data processing pipelines with Sora.Flow. Start with the basic concepts and gradually adopt the advanced patterns as your requirements grow.

For additional resources, see:
- [S8.Flow Sample Application](https://github.com/sylin-labs/sora-framework/tree/dev/samples/S8.Flow)
- [Sora Framework Documentation](https://github.com/sylin-labs/sora-framework/tree/dev/docs)
- [Architecture Decisions](https://github.com/sylin-labs/sora-framework/tree/dev/docs/decisions)

## Adjacent patterns

Keep these nearby patterns in mind when applying Flow in a broader app:

- Messaging commands and retries: use IMessageHandler<T> and the message bus for reliable command delivery and idempotency. See Messaging reference and samples:
    - Reference: https://github.com/sylin-labs/sora-framework/blob/dev/docs/reference/messaging.md
    - RabbitMQ adapter samples: https://github.com/sylin-labs/sora-framework/tree/dev/src/Sora.Messaging.RabbitMq
- HTTP APIs via controllers: expose routes with attribute-routed MVC controllers only; avoid inline MapGet/MapPost. Guidance and ADRs:
    - Engineering guidance: https://github.com/sylin-labs/sora-framework/blob/dev/docs/engineering/index.md
    - Web HTTP API: https://github.com/sylin-labs/sora-framework/blob/dev/docs/api/web-http-api.md
- Data access at scale: prefer FirstPage/Page or streaming (AllStream/QueryStream) for large sets; avoid unbounded All/Query. Background:
    - Guide: https://github.com/sylin-labs/sora-framework/blob/dev/docs/guides/data/all-query-streaming-and-pager.md
    - ADR: https://github.com/sylin-labs/sora-framework/blob/dev/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md
- OpenAPI/Swagger: enable discoverability and client generation with Sora.Web.Swagger.
    - Reference: https://github.com/sylin-labs/sora-framework/blob/dev/docs/api/openapi-generation.md
- Constants and options: centralize route keys, headers, and toggles; avoid magic values.
    - ADR: https://github.com/sylin-labs/sora-framework/blob/dev/docs/decisions/ARCH-0040-config-and-constants-naming.md
