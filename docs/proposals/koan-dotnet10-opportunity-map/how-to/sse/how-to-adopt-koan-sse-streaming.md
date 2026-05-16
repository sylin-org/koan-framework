# How to Adopt Koan SSE Helpers for Streaming Endpoints

**Audience:** Koan service developers publishing real-time HTTP streams.

**Prerequisites:**

- Koan solution targeting .NET 10 with access to `Koan.Web`, `Koan.Web.Sse`, and Koan auto-registrars.
- Existing ASP.NET Core host (controllers or minimal APIs) exposing long-lived operations.
- Ability to update project references and configuration (`appsettings.*`).

**Inputs:**

- Project file referencing the shared SSE module.
- Controller/minimal API endpoints that currently write `text/event-stream` manually.
- Configuration for `Koan:Web:Sse` (default event name and optional heartbeat).

**Outputs:**

- Consistent SSE responses emitted through `SseActionResult` / `SseResults` helpers.
- Provenance records capturing SSE defaults and heartbeat cadence.
- Reusable async streams that encapsulate business polling logic.

**Success Criteria:**

- `dotnet build` succeeds with `Koan.Web.Sse` referenced and no manual `Response.Body.WriteAsync` calls remain.
- Streaming endpoints return correctly framed SSE events with shared headers and default event names.
- Cancellation propagates cleanly; clients observe heartbeats at the configured interval.

**Failure Modes / Diagnostics:**

- Missing module reference results in `InvalidOperationException` when resolving `KoanSseOptions`; add the project reference and restore.
- Forgetting to yield `SseEnvelope` data causes silent streams; inspect enumerators and unit-test with in-memory contexts.
- Misconfigured `Koan:Web:Sse:DefaultEvent` leads to blank event names; verify appsettings and provenance output.

---

## Step 1. Reference the Module

Update your host project file so it compiles the shared SSE abstractions alongside existing web infrastructure:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\Koan.Web\Koan.Web.csproj" />
  <ProjectReference Include="..\..\Koan.Web.Sse\Koan.Web.Sse.csproj" />
</ItemGroup>
```

Run `dotnet restore` and confirm `dotnet build` succeeds before switching endpoint implementations.

## Step 2. Verify Service Registration

`Koan.Web.Sse` registers via the Koan auto-registrar pipeline. If your host keeps the default `KoanAutoRegistrar` discovery, no extra wiring is necessary. For hosts that opt out, register manually:

```csharp
builder.Services.AddKoanOptions<KoanSseOptions>("Koan:Web:Sse");
```

This ensures `KoanSseOptions` is resolved and provenance emits configuration state.

## Step 3. Refactor MVC Controllers to `SseActionResult`

Replace manual header writes and `Response.Body` usage with async envelope streams. The helper manages headers, content type, and flushing:

```csharp
[HttpGet("jobs/{jobId}")]
[Produces("text/event-stream")]
public IActionResult StreamJobProgress(string jobId, CancellationToken ct)
    => SseActionResult.StreamEnvelopes(StreamJobProgressCore(jobId, ct));

private static async IAsyncEnumerable<SseEnvelope> StreamJobProgressCore(
    string jobId,
    [EnumeratorCancellation] CancellationToken ct)
{
    var job = await Job.Get(jobId, ct);
    if (job is null)
    {
        yield return new SseEnvelope("error", JsonConvert.SerializeObject(new { message = "Job not found", jobId }));
        yield break;
    }

    yield return ToJobUpdate(job);

    var lastStatus = job.Status;
    var lastProgress = job.Progress;

    while (!ct.IsCancellationRequested)
    {
        job = await Job.Get(jobId, ct);
        if (job is null)
        {
            yield return new SseEnvelope("error", JsonConvert.SerializeObject(new { message = "Job deleted", jobId }));
            yield break;
        }

        if (job.Status != lastStatus || Math.Abs((double)(job.Progress - lastProgress)) > 0.01)
        {
            yield return ToJobUpdate(job);
            lastStatus = job.Status;
            lastProgress = job.Progress;
        }

        if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
        {
            yield return new SseEnvelope("complete", JsonConvert.SerializeObject(new { jobId, status = job.Status.ToString() }));
            yield break;
        }

        yield return new SseEnvelope(null, JsonConvert.SerializeObject(new { timestamp = DateTime.UtcNow }));
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
    }
}
```

The helper enforces Koan’s SSE defaults (no-cache headers, `X-Accel-Buffering=no`, and default event fallback). Reuse dedicated methods (e.g., `ToJobUpdate`) to keep payload serialization consistent.

## Step 4. Modernize Minimal APIs with `SseResults`

For minimal API endpoints, feed an async stream to `SseResults`:

```csharp
app.MapGet("/api/jobs", () =>
    SseResults.StreamJson(Project.AllStream(ct: default), eventName: "job-update"))
   .WithName("JobsStream")
   .Produces(StatusCodes.Status200OK);
```

`StreamJson` handles serialization with Newtonsoft.Json and automatically strips empty chunks. Use `StreamText` when streaming plain text or tokens.

## Step 5. Configure `Koan:Web:Sse`

Tune defaults via configuration following ADR-0040 naming:

```json
{
  "Koan": {
    "Web": {
      "Sse": {
        "Enabled": true,
        "DefaultEvent": "message",
        "HeartbeatInterval": "00:00:30"
      }
    }
  }
}
```

- `Enabled` gates helper registration for production lockdowns.
- `DefaultEvent` fills envelopes without explicit event names.
- `HeartbeatInterval` controls module-provided heartbeats (used by components like MCP session manager).

Review boot provenance to confirm settings, and ensure `AllowMagicInProduction` is set intentionally if exposing in restricted environments.

## Step 6. Validate with Tests and Local Clients

1. Execute focused tests (e.g., `dotnet test tests/Koan.Web.Sse.Tests/Koan.Web.Sse.Tests.csproj`) to validate formatter behaviour.
2. Exercise endpoints with `curl` or browser `EventSource` to confirm multi-line payloads and heartbeats follow expectations.
3. Inspect logs for `Koan.Web.Sse` provenance entries to verify options binding.

## Edge Cases & Hardening

- **Cancellation:** Always pass `HttpContext.RequestAborted` into async generators (`WithCancellation`) so disconnects close loops.
- **Serialization:** Use Newtonsoft for payload shaping to match Koan defaults and avoid AOT trimming warnings.
- **Backpressure:** Long-running loops should honour configurable delays and avoid `Task.Delay` inside `try/catch` blocks that yield values; wrap delay in helper methods.
- **Cross-module Consumption:** MCP transports and AI controllers share the same helpers. Keep shared utilities in dedicated methods to avoid drift between services.
- **Testing:** For controller tests, inject `DefaultHttpContext` and execute the action result to assert headers and payload framing without requiring a live server.

## Related Links

- `Koan.Web.Sse` (`src/Koan.Web.Sse`) – shared SSE module, options, results, and auto-registrar.
- `Koan.Service.KoanContext` (`src/Services/code-intelligence/Koan.Service.KoanContext`) – reference controller using `SseActionResult`.
- `Koan.Mcp` (`src/Koan.Mcp/Hosting`) – HTTP transport leveraging `SseResults.StreamEnvelopes` for session output.
- ADR `ARCH-0040` – configuration and constants naming guidance.
- `docs/guides/data/all-query-streaming-and-pager.md` – patterns for async enumerables used as SSE data sources.
