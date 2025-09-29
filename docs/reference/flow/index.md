---
type: REF
domain: flow
title: "Flow Pillar Reference"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.2
validation:
  date_last_tested: 2025-09-28
  status: verified
  scope: docs/reference/flow/index.md
---

# Flow Pillar Reference

## Contract

- **Inputs**: Koan application bootstrapped with `builder.Services.AddKoan()`, Flow packages installed, and at least one storage adapter for entities and vectors when required.
- **Outputs**: Repeatable ingestion pipelines, semantic processing flows, controllers, and adapters that keep intake, enrichment, and projection concerns separate.
- **Error Modes**: Misaligned stage configuration, unbounded pipelines that ignore cancellation, provider limitations (no vector/search support), or missing adapter registration preventing intake from running.
- **Success Criteria**: Data reliably progresses from intake to projections, semantic pipelines scale with streaming, Flow controllers expose predictable APIs, and telemetry captures throughput plus failures.

### Edge Cases

- **Multi-source ingestion** – make the default adapter explicit when multiple Flow providers exist; annotate flow entities with `[DataAdapter]` when routing to specialized stores.
- **Stage replays** – use `Requeue()` sparingly and record idempotency keys to avoid duplicate events.
- **Pipeline concurrency** – throttle AI-heavy stages with `AiEmbedOptions.Batch` to respect rate limits.
- **Large lineage queries** – page or stream projection reads to avoid materializing entire audit trails in memory.
- **Long-lived adapters** – wrap background services with health checks and cancellation to prevent orphaned threads.

---

## Pillar Overview

Flow orchestrates inbound data, enrichment, and publication through orderly stages. Entities derived from `FlowEntity<T>` move through intake ➜ processing ➜ projections while semantic pipelines (`Pipeline()`) offer declarative transformations. Flow integrates directly with Data, AI, and Messaging pillars to stream entities, enrich with AI, emit events, and surface rich APIs without custom repositories.

**Core packages**

- `Koan.Flow` – runtime, entities, stages, interceptors, controllers.
- `Koan.Flow.Semantic` – semantic streaming pipeline operators.
- Adapter packages (`Koan.Data.Postgres`, `Koan.Data.Vector.Redis`, `Koan.Data.Mongo`, etc.) wired through Koan’s auto-registration.

---

## Installation

Install the Flow runtime and supporting adapters:

```powershell
pwsh -c "dotnet add package Koan.Flow"
```

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

var app = builder.Build();
app.Run();
```

Add data/vector adapters to match your providers; Koan will auto-detect them during boot.

---

## Flow Entities & Stages

### Modeling Flow Entities

```csharp
public sealed class Device : FlowEntity<Device>
{
    [AggregationKey]
    public string SerialNumber { get; set; } = "";
    public string Model { get; set; } = "";
    public string Status { get; set; } = "";
}

// Dynamic intake when payload shape varies per source
var intakeRecord = new DynamicFlowEntity<Device>
{
    Id = "device:123",
    Model = new
    {
        inventory = new { serial = "DEV-001" },
        status = "active"
    }
};

await intakeRecord.Save(FlowSets.Stage<Device>("intake"));
```

### Stage Progression

```csharp
var keyed = await StageRecord<Device>.Query()
    .Where(r => r.Stage == "keyed")
    .ToArrayAsync();

await StageRecord<Device>.Query()
    .Where(r => r.Stage == "failed")
    .ForEachAsync(record => record.Requeue());
```

### Projections & Views

```csharp
using (DataSetContext.With(FlowSets.View<Device>("canonical")))
{
    var recent = await CanonicalProjection<Device>.FirstPage(50);
}

using (DataSetContext.With(FlowSets.View<Device>("lineage")))
{
    var lineage = await LineageProjection<Device>.Query()
        .Where(r => r.SerialNumber == "DEV-001" && r.Stage == "processed")
        .ToArrayAsync();
}
```

`FlowSets.Stage<T>` and `FlowSets.View<T>` express stage and projection identifiers consistently; stay within helpers to keep providers interchangeable.

---

## Semantic Pipelines

Semantic pipelines express streaming and AI enrichment logic without bespoke orchestration code.

### Pipeline Quick Start

```csharp
await Document.AllStream()
    .Pipeline()
    .ForEach(doc =>
    {
        doc.ProcessedAt = DateTimeOffset.UtcNow;
        doc.Status = "processing";
    })
    .Tokenize(doc => doc.Content)
    .Embed(new AiEmbedOptions { Model = "all-minilm", Batch = 100 })
    .Save()
    .ExecuteAsync();
```

### Core Operations

| Operation | Purpose | Notes |
| --- | --- | --- |
| `.ForEach(...)` | Mutate entities or call services (sync/async). | Perform domain logic, metadata enrichment, or external lookups.
| `.Tokenize(...)` | Produce tokens via the configured AI provider. | Supply selector + optional `AiTokenizeOptions` (max tokens/model).
| `.Embed(...)` | Generate embeddings and persist vectors. | Configure batch size/model; works when vector adapters are installed.
| `.Branch(...)` | Split success/failure/conditional paths. | Combine `.OnSuccess`, `.OnFailure`, or `.When` clauses.
| `.Save()` | Persist entity + vector changes in one call. | Batched persistence keeps providers efficient.
| `.Notify(...)` | Emit messaging events after processing. | Integrates with Messaging pillar transports.
| `.Trace(...)` | Log structured telemetry per envelope. | Ideal for latency tracking and diagnostics.

### Branching & Error Capture

```csharp
await Document.Where(d => d.Status == "uploaded")
    .Pipeline()
    .Branch(branch => branch
        .OnSuccess(success => success
            .Tokenize(doc => doc.Content)
            .Embed(new AiEmbedOptions { Model = "all-minilm" })
            .ForEach(doc => doc.Status = "completed")
            .Save())
        .OnFailure(failure => failure
            .Trace(env => $"Failed {env.Entity.Id}: {env.Error?.Message}")
            .ForEach(doc =>
            {
                doc.Status = "failed";
                doc.ErrorMessage = env.Error?.Message;
            })
            .Save()))
    .ExecuteAsync();
```

### Performance Patterns

- Stream inputs (`AllStream`, `QueryStream`) to avoid loading entire datasets into memory.
- Batch AI calls with `AiEmbedOptions.Batch` or provider-specific configuration.
- Use `.When(...)` branches to handle content-types differently (text vs. image, etc.).
- Compose reusable extensions for standard stages:

```csharp
public static PipelineBuilder<Document> AddStandardProcessing(this PipelineBuilder<Document> pipeline) =>
    pipeline
        .ForEach(doc => doc.ProcessedAt = DateTimeOffset.UtcNow)
        .Tokenize(doc => doc.Content)
        .Embed(new AiEmbedOptions { Model = "all-minilm" });
```

---

## Adapters & Ingestion

Background adapters keep intake resilient and autonomous.

```csharp
[FlowAdapter("oem", "device-sync", DefaultSource = "oem-hub")]
public sealed class DeviceAdapter : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var snapshot = await _oemClient.GetDevicesAsync(ct);

            foreach (var payload in snapshot)
            {
                await new DynamicFlowEntity<Device>
                {
                    Id = $"device:{payload.Serial}",
                    Model = payload
                }.Send("oem-hub", ct);
            }

            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
```

For lightweight ingestion, expose HTTP endpoints that stage payloads:

```csharp
[Route("api/flow/devices")]
public sealed class DeviceIngestController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Ingest(DevicePayload[] devices, CancellationToken ct)
    {
        foreach (var device in devices)
        {
            await new DynamicFlowEntity<Device>
            {
                Id = $"device:{device.Id}",
                Model = device
            }.Save(FlowSets.Stage<Device>("intake"), ct);
        }

        return Ok(new { processed = devices.Length });
    }
}
```

---

## Controllers & APIs

`FlowEntityController<T>` exposes Flow-specific CRUD and projection endpoints without custom routing.

```csharp
[Route("api/flow/[controller]")]
public sealed class DevicesController : FlowEntityController<Device>
{
    [HttpPost("admin/reprocess-failed")]
    public async Task<IActionResult> ReprocessFailed(CancellationToken ct)
    {
        var failed = await StageRecord<Device>.Query()
            .Where(r => r.Stage == "failed")
            .ToArrayAsync(ct);

        foreach (var record in failed)
        {
            await record.Requeue(ct);
        }

        return Ok(new { requeued = failed.Length });
    }
}
```

The base controller emits:

- `GET /api/flow/device` – list canonical Flow entities.
- `GET /api/flow/device/{id}` – fetch canonical projection.
- `GET /api/flow/device/views/{view}/{id}` – retrieve projections (`canonical`, `lineage`, custom).
- `POST /api/flow/device/admin/reproject/{id}` – trigger reprojection/replay.

---

## Events & Messaging

```csharp
await FlowEvent.For<Device>()
    .WithSource("oem-hub")
    .WithData(devicePayload)
    .Send();

await this.On<FlowEvent<Device>>(async flowEvent =>
{
    if (flowEvent.Data.Status == "critical")
    {
        await _alerts.SendAsync(flowEvent.Data.SerialNumber);
    }

    await flowEvent.Continue();
});
```

Flow events connect pipelines with external systems, enabling replay, alerting, and cross-pillar workflows.

---

## Interceptors & Lifecycle

```csharp
public static class DeviceFlowInterceptors
{
    public static void Configure()
    {
        FlowInterceptors.For<Device>()
            .BeforeIntake(device =>
            {
                if (string.IsNullOrWhiteSpace(device.SerialNumber))
                {
                    return FlowIntakeAction.Reject("Serial number required");
                }

                return FlowIntakeAction.Continue;
            })
            .AfterProjection(async device =>
            {
                await new DeviceProcessed
                {
                    DeviceId = device.Id,
                    SerialNumber = device.SerialNumber,
                    ProcessedAt = DateTimeOffset.UtcNow
                }.Send();

                return FlowStageAction.Continue;
            });
    }
}
```

Interceptors resemble lifecycle hooks: validate intake, enrich intermediate data, and initiate downstream events.

---

## Configuration & Environment

```json
{
  "Koan": {
    "Flow": {
      "AutoRegister": true,
      "Adapters": {
        "AutoStart": true,
        "Include": ["oem:device-sync"],
        "Exclude": [],
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

Use per-environment overrides for adapter throttle, stage retention, or projection refresh cadence. Surface secrets (API keys, connection strings) through `IConfiguration` rather than code.

---

## Monitoring & Diagnostics

```csharp
public sealed class FlowHealthCheck : IHealthContributor
{
    public string Name => "flow-pipeline";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct)
    {
        var pending = await StageRecord<Device>.CountAsync(r => r.Stage == "intake", ct);
        var failed = await StageRecord<Device>.CountAsync(r => r.Stage == "failed", ct);

        var isHealthy = pending < 1_000 && failed < 10;
        return new HealthReport(Name, isHealthy, isHealthy ? null : $"pending={pending}, failed={failed}");
    }
}
```

Pair health checks with dashboards capturing throughput, stage latency, and adapter status to catch ingestion stalls quickly.

---

## Error Handling & Retries

```csharp
public sealed class DeviceRetryPolicy : IFlowRetryPolicy<Device>
{
    public Task<FlowRetryAction> ShouldRetry(Device device, Exception exception, int attempt)
    {
        if (exception is HttpRequestException && attempt < 3)
        {
            return Task.FromResult(FlowRetryAction.RetryAfter(TimeSpan.FromSeconds(Math.Pow(2, attempt))));
        }

        if (exception is ValidationException)
        {
            return Task.FromResult(FlowRetryAction.DeadLetter("validation-failed"));
        }

        return Task.FromResult(FlowRetryAction.Fail);
    }
}
```

Dead-letter queues preserve payloads for investigation—subscribe to `FlowDeadLetter<T>` to route alerts or manual replays.

---

## Testing

```csharp
[Test]
public async Task DevicePipeline_ShouldPersistProjection()
{
    var device = new Device { SerialNumber = "TEST-001", Status = "active" };

    await new DynamicFlowEntity<Device>
    {
        Id = device.Id,
        Model = device
    }.Save(FlowSets.Stage<Device>("intake"));

    await Device.AllStream()
        .Pipeline()
        .AddStandardProcessing()
        .ForEach(d => d.Status = "completed")
        .Save()
        .ExecuteAsync();

    using (DataSetContext.With(FlowSets.View<Device>("canonical")))
    {
        var canonical = await CanonicalProjection<Device>
            .Query()
            .FirstOrDefaultAsync(d => d.SerialNumber == "TEST-001");

        Assert.That(canonical, Is.Not.Null);
        Assert.That(canonical!.Status, Is.EqualTo("completed"));
    }
}
```

---

## Related Resources

- [Data Pillar Reference](../data/index.md)
- [AI Pillar Reference](../ai/index.md)
- [Messaging Reference](../messaging/index.md)
- [Semantic Pipelines Playbook](../../guides/semantic-pipelines.md)
- [DX-0041 Consolidation ADR](../../decisions/DX-0041-docs-consolidation.md)
