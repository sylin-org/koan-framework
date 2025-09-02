using Sora.Data.Core;
using Sora.Flow;
using Sora.Flow.Options;
using S8.Flow.Shared;
using Sora.Messaging;
using S8.Flow.Api.Adapters;
using Sora.Flow.Model;
using Sora.Flow.Infrastructure;
using Sora.Messaging.RabbitMq;
using Sora.Web.Swagger;
using Sora.Flow.Sending; // AddFlowSender

var builder = WebApplication.CreateBuilder(args);

// Container-only sample guard
if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Api is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Services.AddSora(); // core + data + health + scheduling (Mongo registers itself via SoraAutoRegistrar)
// Messaging core is required so the bus selector exists and RabbitMQ consumers can start
builder.Services.AddMessagingCore();
// Ensure RabbitMQ provider is registered (factory + health + optional inbox discovery)
builder.Services.AddRabbitMq();
builder.Services.AddSoraFlow();
// Register FlowEvent consumer (handler is provided by AddFlowSender)
builder.Services.AddFlowSender();

builder.Services.Configure<FlowOptions>(o =>
{
    // Default tags act as fallback when model has no AggregationTag attributes.
    // Our Sensor model carries [AggregationTag(Keys.Sensor.Key)], so this isn't required,
    // but we keep a conservative default for other models.
    o.AggregationTags = new[] { Keys.Sensor.Key };
    // Enable purge for VO-heavy workloads and keep keyed retention modest by default
    o.PurgeEnabled = true;
    o.KeyedTtl = TimeSpan.FromDays(14);
    // Keep canonical clean: drop VO tags from canonical/lineage
    o.CanonicalExcludeTagPrefixes = new[] { "reading.", "sensorreading." };
});

builder.Services.AddControllers();
builder.Services.AddRouting();
builder.Services.AddSoraSwagger(builder.Configuration);

// Message-driven adapters: handle events and persist to Flow intake
builder.Services.OnMessages(h =>
{
    // Only fast-tracked readings remain as messages; device/sensor announcements are seeded by adapters via FlowAction seed.
    h.On<Reading>(async (env, msg, ct) =>
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [Keys.Sensor.Key] = msg.SensorKey,
            [Keys.Reading.Value] = msg.Value,
            [Keys.Reading.CapturedAt] = msg.CapturedAt.ToString("O"),
        };
        if (!string.IsNullOrWhiteSpace(msg.Unit)) payload[Keys.Sensor.Unit] = msg.Unit;
        if (!string.IsNullOrWhiteSpace(msg.Source)) payload[Keys.Reading.Source] = msg.Source;
        var typed = new StageRecord<Reading>
        {
            Id = Guid.NewGuid().ToString("n"),
            SourceId = msg.Source ?? "events",
            OccurredAt = msg.CapturedAt,
            StagePayload = payload.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.OrdinalIgnoreCase),
            CorrelationId = msg.SensorKey,
        };
        await typed.Save(FlowSets.StageShort(FlowSets.Intake));
    });
});

// Health snapshot based on recent Keyed stage records
builder.Services.AddSingleton<IAdapterHealthRegistry, S8.Flow.Api.Adapters.KeyedAdapterHealthRegistry>();

var app = builder.Build();

// Ensure ambient AppHost is set and runtime started before hosted services execute
Sora.Core.Hosting.App.AppHost.Current = app.Services;
try { Sora.Core.SoraEnv.TryInitialize(app.Services); } catch { }
var rt = app.Services.GetService<Sora.Core.Hosting.Runtime.IAppRuntime>();
rt?.Discover();
rt?.Start();

// Ensure the default message bus is created so subscriptions are provisioned and consumers start
try
{
    var selector = app.Services.GetService<IMessageBusSelector>();
    _ = selector?.ResolveDefault(app.Services);
}
catch { /* ignore */ }

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.MapControllers();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSoraSwagger();

app.Run();
