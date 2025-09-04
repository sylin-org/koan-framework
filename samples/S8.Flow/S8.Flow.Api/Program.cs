// Removed: using S8.Flow.Api; // For FlowSeeder
using S8.Flow.Api.Entities;
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

builder.Services.AddSora();
    builder.Services.AddRabbitMq();

// Container-only sample guard (must be after service registration so DI is wired)
if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Api is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

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
        // Build a normalized event bag uniformly (works for VOs and entities)
        var ev = FlowEvent.For<Reading>()
            .With(Keys.Sensor.Key, msg.SensorKey)
            .With(Keys.Reading.Value, msg.Value)
            .With(Keys.Reading.CapturedAt, msg.CapturedAt.ToString("O"));
        if (!string.IsNullOrWhiteSpace(msg.Unit)) ev.With(Keys.Sensor.Unit, msg.Unit);
        if (!string.IsNullOrWhiteSpace(msg.Source)) ev.With(Keys.Reading.Source, msg.Source);

        // Persist to Flow intake as a typed StageRecord
        var rec = new StageRecord<Reading>
        {
            Id = Guid.NewGuid().ToString("n"),
            SourceId = msg.Source ?? FlowSampleConstants.Sources.Events,
            OccurredAt = msg.CapturedAt,
            StagePayload = ev.Bag,
            CorrelationId = msg.SensorKey,
        };
        await rec.Save(FlowSets.StageShort(FlowSets.Intake));
    });
});

// Health snapshot based on recent Keyed stage records
builder.Services.AddSingleton<IAdapterHealthRegistry, S8.Flow.Api.Adapters.KeyedAdapterHealthRegistry>();


var app = builder.Build();

// Defer test entity save until the host is fully started (AppHost.Current initialized by AddSora())
app.Lifetime.ApplicationStarted.Register(async () =>
{
    try
    {
        var setting = new AppSetting { Id = "test", Value = $"Saved at {DateTime.UtcNow:O}" };
        await setting.Save();
        Console.WriteLine($"[TEST] AppSetting saved via provider 'mongo': {setting.Id} = {setting.Value}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[TEST][ERROR] Failed saving AppSetting: {ex.Message}\n{ex}");
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Only map controllers if other endpoints exist
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSoraSwagger();

app.Run();
