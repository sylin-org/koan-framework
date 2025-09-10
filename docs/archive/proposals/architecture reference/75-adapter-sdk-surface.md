# Adapter SDK Surface (Minimal and Meaningful)

Purpose
- Define the smallest useful API for adapter authors to implement, with platform‑provided defaults for everything else.

## Core abstractions

public interface ISourceClient {
    // Pull records in a window (incremental)
    Task<IEnumerable<SourceItem>> PullAsync(Window window, int pageSize, CancellationToken ct);

    // Stream a full seed (may internally page)
    IAsyncEnumerable<SourceItem> SeedAsync(SeedArgs args, CancellationToken ct);
}

public sealed record Window(DateTimeOffset From, DateTimeOffset Until);
public sealed record SeedArgs(DateTimeOffset? From = null, DateTimeOffset? Until = null, string? Mode = null);
public sealed record SourceItem(string Id, string? Version, DateTimeOffset OccurredAt, object Payload);

public interface IMapper {
    // Map a source item into an IntakeRecord payload; SDK will envelope and validate
    IntakeRecord Map(SourceItem item);
}

// SDK emits this to IntakeGateway or MQ after validation
public sealed record IntakeRecord(string SourceId, DateTimeOffset OccurredAt, object Payload, string? CorrelationId = null);

## Platform‑provided services (injected by SDK)

public interface IEmitClient {
    Task EmitAsync(IntakeRecord record, CancellationToken ct);
    // Optional: send a page of records efficiently in one call
    Task EmitBatchAsync(IEnumerable<IntakeRecord> records, CancellationToken ct);
}

public interface IControlClient {
    // Automatically subscribes to control queue: control.adapter.{adapterId}
    event Func<SeedCommand, CancellationToken, Task> OnSeed;
    event Func<PullWindowCommand, CancellationToken, Task> OnPull;
    event Func<SuspendCommand, CancellationToken, Task> OnSuspend;
    event Func<ResumeCommand, CancellationToken, Task> OnResume;
    event Func<ThrottleCommand, CancellationToken, Task> OnThrottle;

    Task AnnounceAsync(AdapterAnnouncement ann, CancellationToken ct);
    Task HeartbeatAsync(AdapterHeartbeat hb, CancellationToken ct);
    Task ReportProgressAsync(SeedProgress progress, CancellationToken ct);
}

public interface ICursorStore {
    Task<string?> GetAsync(CancellationToken ct);
    Task SetAsync(string value, CancellationToken ct);
}

public interface ISchemaValidator {
    void ValidateIntakeRecord(IntakeRecord record);
}

public interface IRateLimiter {
    ValueTask WaitAsync(CancellationToken ct);
}

## Minimal Program.cs (template)

// ... using directives ...
var builder = WebApplication.CreateBuilder(args);

// Read config (adapterId, sourceId, emit mode, endpoints)
// Register SourceClient and Mapper
builder.Services.AddSingleton<ISourceClient, SourceClient>();
builder.Services.AddSingleton<IMapper, Mapper>();

// Add SDK services: EmitClient, ControlClient, CursorStore, SchemaValidator, RateLimiter, OTEL, Health
builder.Services.AddAdapterSdk(builder.Configuration);

var app = builder.Build();
app.MapHealthChecks("/healthz");
app.MapGet("/readyz", () => Results.Ok());

// Wire control handlers with minimal glue
var control = app.Services.GetRequiredService<IControlClient>();
var source = app.Services.GetRequiredService<ISourceClient>();
var mapper = app.Services.GetRequiredService<IMapper>();
var emit = app.Services.GetRequiredService<IEmitClient>();

control.OnSeed += async (cmd, ct) => {
    var batch = new List<IntakeRecord>();
    var batchSize = builder.Configuration.GetValue<int>("EMIT_BATCH_SIZE", 500);
    await foreach (var item in source.SeedAsync(new SeedArgs(cmd.From, cmd.Until, cmd.Mode), ct)) {
        batch.Add(mapper.Map(item));
        if (batch.Count >= batchSize) { await emit.EmitBatchAsync(batch, ct); batch.Clear(); }
    }
    if (batch.Count > 0) { await emit.EmitBatchAsync(batch, ct); batch.Clear(); }
};

control.OnPull += async (cmd, ct) => {
    var items = await source.PullAsync(new Window(cmd.From, cmd.Until), cmd.PageSize, ct);
    var records = items.Select(mapper.Map).ToList();
    if (records.Count > 0) { await emit.EmitBatchAsync(records, ct); }
};

// Suspend/Resume/Throttle have default SDK behavior; override if needed

// Self‑announcement and heartbeat timer
var ctrl = app.Services.GetRequiredService<IControlClient>();
await ctrl.AnnounceAsync(AdapterAnnouncement.FromConfig(builder.Configuration), CancellationToken.None);
_ = Task.Run(async () => {
    while (true) { await ctrl.HeartbeatAsync(AdapterHeartbeat.FromConfig(), CancellationToken.None); await Task.Delay(TimeSpan.FromSeconds(30)); }
});

app.Run();

## Configuration keys (env)
- ADAPTER_ID, SOURCE_ID, SOURCE_API_URL, AUTH_*
- EMIT_MODE=rest|mq, INTAKE_GATEWAY_URL, RABBITMQ_URL
- EMIT_BATCH_SIZE (default 500)
- POLICY_SOURCE=db|file (for platform), OTEL_*, LOG_LEVEL

## Default behaviors (SDK)
- Control queue binding and dispatch
- Emit path (REST/MQ) with retry/backoff and rate limiting; batch emission supported with automatic chunking if necessary
- IntakeRecord schema validation and correlation headers
- Cursor get/set helpers (if adapter chooses to cache cursor locally)
- Health/metrics endpoints and OTEL spans

## Testing checklist (adapter)
- Mapping tests: SourceItem → IntakeRecord; schema valid
- Seed/Pull control handler smoke tests (emit calls counted)
- Integration test (optional): against stubbed source API

A thin adapter only implements SourceClient and Mapper; everything else is handled by the SDK for a consistent, low‑friction developer experience.
