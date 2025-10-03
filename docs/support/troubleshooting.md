---
type: SUPPORT
domain: troubleshooting
title: "Koan Troubleshooting Hub"
audience: [developers, support-engineers, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.2
validation:
  date_last_tested: 2025-09-28
  status: verified
  scope: docs/support/troubleshooting.md
---

# Koan Troubleshooting Hub

## Contract

- **Inputs**: Running (or attempting to run) a Koan service, access to application logs, and optional container/host tooling.
- **Outputs**: Diagnosed root cause and next action plan covering boot, adapters, AI, Flow, and web layers.
- **Error Modes**: Skipped auto-registration, adapters remaining unhealthy, AI providers refusing connections, Flow pipelines parking records, or health checks reporting failures.
- **Success Criteria**: Service boots with expected modules, adapters report healthy, pipelines process data, AI features respond, and health endpoints return `200`.

### Edge Cases

- **Delayed infrastructure** – some adapters (Couchbase, Postgres) take >30s to warm up; configure health checks and waits accordingly.
- **Rate-limited AI providers** – throttle embedding/chat calls with batch options to avoid 429 responses.
- **Vector mismatch** – embeddings with incorrect dimensions silently fail searches; verify provider + model alignment.
- **Flow replays** – repeated requeues without idempotency cause duplicates; persist replay checkpoints.
- **Production overrides** – disabling defaults (HTTPS, auth, telemetry) without replacements leads to compliance gaps; ensure compensating controls exist.

---

## Quick Triage Checklist

1. **Confirm boot completed** – run `curl http://localhost:5000/api/health/ready`; if unhealthy, inspect logs for `Koan:modules` output and DI errors.
2. **Inspect adapter state** – for containerized stacks `docker compose ps` and `docker logs <service> --tail 50`; look for `StartupProbe` / `Healthy` markers.
3. **Verify configuration** – dump active configuration for suspect sections using `Configuration.Read` logging or `dotnet user-secrets list` (development).
4. **Check Flow backlog** – query `StageRecord<T>` counts or use Flow dashboards to ensure stages aren’t stuck in `failed`.
5. **Run smoke call** – `curl http://localhost:5000/api/todos` (or domain equivalent) and `curl http://localhost:5000/.well-known/auth/providers` to validate HTTP + auth surfaces.

When an item fails, jump to the matching section below.

---

## Boot & Auto-Registration

### Ensure Koan Loaded

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan(); // Required for module discovery
```

Missing `AddKoan()` prevents Koan from wiring controllers, Flow, and adapters. Verify the assembly containing `KoanAutoRegistrar` types is referenced by the project.

### Review Boot Reports

Enable verbose logs during development:

```csharp
builder.Logging.SetMinimumLevel(LogLevel.Debug);
if (KoanEnv.IsDevelopment)
{
    KoanEnv.DumpSnapshot(logger);
}
```

Expected output includes module elections, adapter readiness, and recipe hints:

```
[INFO] Koan:modules data→postgres web→controllers ai→ollama
```

Absence of a module line suggests missing package references.

### Bootstrap Tasks Not Running

Tasks implementing `IScheduledTask` + `IOnStartup` execute during boot. Confirm discovery and readiness:

```bash
docker logs api --follow | grep -E "(SchedulingOrchestrator|bootstrap)"
```

Author bootstrap tasks with idempotency and dependency checks:

```csharp
public sealed class ReferenceDataBootstrap : IScheduledTask, IOnStartup
{
    public string Id => "app:reference-data";

    public async Task RunAsync(CancellationToken ct)
    {
        if (await Category.Any(ct))
        {
            _logger.LogInformation("Reference data already seeded");
            return;
        }

        await new[]
        {
            new Category { Name = "Electronics" },
            new Category { Name = "Books" }
        }.Save(ct);
    }
}
```

If boot tasks depend on external services, wait for readiness:

```csharp
var health = await _healthChecks.CheckHealthAsync(ct);
if (health.Status != HealthStatus.Healthy)
{
    _logger.LogWarning("Skipping bootstrap until dependencies are healthy");
    return;
}
```

---

## Adapter & Data Connectivity

### Diagnose Connection Failures

Typical symptoms: `SocketNotAvailableException`, `Service n1ql is either not configured or cannot be reached`, empty query results.

1. **Verify container state**

   ```powershell
   docker compose ps
   docker logs couchbase --tail 50
   ```

   Look for ✅ `Cluster initialized successfully` or ❌ `Connection refused`.

2. **Check adapter readiness logs**

   ```powershell
   docker logs api | Select-String "StartupProbe: data"
   ```

3. **Probe provider health**
   ```powershell
   curl -s http://localhost:8091/pools/default               # Couchbase example
   curl -u user:pass -X POST http://localhost:8093/query/service -d "statement=SELECT 1"
   ```

### Wait for SDK Warm-Up

Insert waits inside adapter bootstrapping when necessary:

```csharp
_cluster = await Cluster.ConnectAsync(cs, options);
await _cluster.WaitUntilReadyAsync(TimeSpan.FromSeconds(30));

_bucket = await _cluster.BucketAsync(bucketName);
await _bucket.WaitUntilReadyAsync(TimeSpan.FromSeconds(10));
```

### Entity Pattern & Provider Selection

- Always use `Entity<T>` or `Entity<T, TKey>` statics; repository abstractions cause missing DI registrations.
- Inspect provider election and capabilities:
  ```csharp
  var caps = Data<Todo, string>.QueryCaps;
  _logger.LogInformation("Provider {Provider} capabilities {Flags}", caps.ProviderName, caps.Capabilities);
  ```
- Explicitly set defaults when multiple adapters exist:
  ```json
  {
    "Koan": {
      "Data": {
        "DefaultProvider": "Postgres"
      }
    }
  }
  ```

### Schema & Provisioning Delays

After auto-provisioning collections, allow the backend to settle before issuing queries:

```csharp
await manager.CreateCollectionAsync(spec);
await Task.Delay(TimeSpan.FromSeconds(2), ct); // Wait for query readiness
```

---

## Web & Authentication

### Missing Controllers / 404s

Ensure controllers inherit from `EntityController<T>` (or a custom controller) and are decorated with attribute routing:

```csharp
[Route("api/[controller]")]
public sealed class TodosController : EntityController<Todo> { }
```

### Auth Provider Diagnostics

- List configured providers: `curl http://localhost:5000/.well-known/auth/providers`.
- Confirm secrets exist via configuration hierarchy; never hardcode client IDs/secrets.
- For social providers, verify redirect URIs match portal configuration.

### Health Endpoint Failures

If `/api/health` is unhealthy, query liveness/readiness endpoints separately:

```powershell
curl http://localhost:5000/api/health/live
curl http://localhost:5000/api/health/ready
```

Implement targeted checks for critical dependencies:

```csharp
public sealed class DatabaseHealthCheck : IHealthContributor
{
    public async Task<HealthReport> CheckAsync(CancellationToken ct)
    {
        try
        {
            await Todo.FirstPage(1, ct);
            return HealthReport.Healthy("database");
        }
        catch (Exception ex)
        {
            return new HealthReport("database", false, ex.Message);
        }
    }
}
```

---

## AI & Vector Troubleshooting

### Provider Connectivity

- Ollama: `curl http://localhost:11434/api/tags` should return available models.
- Hosted providers: verify API keys and endpoints in configuration.

```json
{
  "Koan": {
    "AI": {
      "DefaultProvider": "Ollama",
      "Ollama": {
        "BaseUrl": "http://localhost:11434",
        "DefaultModel": "llama3"
      }
    }
  }
}
```

### Embeddings & Semantic Search

- Annotate vector-bearing properties with `[VectorField]`:

  ```csharp
  public class Document : Entity<Document>
  {
      public string Content { get; set; } = "";

      [VectorField]
      public float[] ContentEmbedding { get; set; } = [];
  }
  ```

- Validate dimensions before saving:
  ```csharp
  var embedding = await _ai.EmbedAsync(new AiEmbeddingRequest { Input = doc.Content });
  var vector = embedding.Embeddings.FirstOrDefault()?.Vector ?? Array.Empty<float>();
  Guard.Against.InvalidLength(vector.Length, expected: 1536);
  doc.ContentEmbedding = vector;
  await doc.Save();
  ```
- Batch heavy workloads with `AiEmbedOptions.Batch` to manage rate limits.

### Cost & Rate Limits

Set provider budgets and retry envelopes:

```json
{
  "Koan": {
    "AI": {
      "Budget": {
        "MaxTokensPerRequest": 4000,
        "MaxRequestsPerMinute": 60,
        "MaxCostPerDay": 50.0
      }
    }
  }
}
```

---

## Flow Pipelines & Automation

### Stage Diagnostics

- Count backlog per stage:
  ```csharp
  var failed = await StageRecord<Device>.CountAsync(r => r.Stage == "failed");
  var intake = await StageRecord<Device>.CountAsync(r => r.Stage == "intake");
  ```
- Inspect parked records (e.g., `NO_KEYS`) and correct aggregation attributes:
  ```csharp
  [AggregationKey("inventory.serial")]
  public string SerialNumber { get; set; } = "";
  ```

### Reprocessing & Replays

```csharp
var failedRecords = await StageRecord<Device>.Query()
    .Where(r => r.Stage == "failed")
    .ToArrayAsync();

foreach (var record in failedRecords)
{
    await record.Requeue();
}
```

### Semantic Pipeline Failures

Wrap pipeline segments with telemetry and branching:

```csharp
await Document.AllStream()
    .Pipeline()
    .Trace(env => $"Processing {env.Entity.Id}")
    .Tokenize(doc => doc.Content)
    .Embed(new AiEmbedOptions { Model = "all-minilm", Batch = 50 })
    .Branch(branch => branch
        .OnSuccess(success => success.Save())
        .OnFailure(failure => failure
            .Trace(env => $"Failed: {env.Error?.Message}")
            .ForEach(doc => doc.Status = "failed")
            .Save()))
    .ExecuteAsync();
```

If failures persist, log envelope errors and inspect provider capability mismatches.

---

## Observability & Diagnostics

- Increase logging verbosity for specific namespaces:
  ```json
  {
    "Logging": {
      "LogLevel": {
        "Koan.Data": "Debug",
        "Koan.Canon": "Debug",
        "Koan.AI": "Debug"
      }
    }
  }
  ```
- Capture metrics for adapter connection times and pipeline throughput; send to Prometheus/OpenTelemetry exporters as needed.
- Use `docker logs <service> --follow | Select-String` (PowerShell) or `grep` (bash) to filter health, provisioning, and bootstrap lines in real time.

---

## Escalate with Context

When opening a support ticket or escalating internally, include:

- Koan version and list of referenced packages.
- Full boot logs (from process start to failure) highlighting `Koan:` lines.
- Health endpoint outputs and stage counts for Flow workloads.
- Adapter configuration snippets (mask secrets) and provider health results.
- Reproduction steps or minimal failing pipeline/controller code.

Providing this context shortens triage cycles and avoids duplicate debugging.

---

Use this hub as your first stop—each pillar reference links back here, reducing duplicated guides and keeping troubleshooting knowledge in one place.
