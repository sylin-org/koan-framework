// ✨ BEAUTIFUL FLOW ORCHESTRATOR ✨
// This assembly is now a Flow orchestrator - it will auto-register message handlers
// for all FlowEntity and FlowValueObject types discovered in this assembly!
using S8.Flow.Api.Entities;
using Sora.Data.Core;
using Sora.Flow;
using Sora.Flow.Attributes;
using Sora.Flow.Configuration;
using Sora.Flow.Options;
using S8.Flow.Shared;
using Sora.Messaging;
using S8.Flow.Api.Adapters;
using Sora.Messaging.RabbitMq;
using Sora.Web.Swagger;

[assembly: FlowOrchestrator]


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

// ✨ BEAUTIFUL FLOW ORCHESTRATOR PATTERNS ✨
// The [FlowOrchestrator] attribute above auto-registers handlers for:
// - Device entities: Device.Send() → auto-routed to Flow intake
// - Sensor entities: Sensor.Send() → auto-routed to Flow intake  
// - Reading value objects: Reading.Send() → auto-routed to Flow intake
// 
// Custom business logic can be added via proper service registration:
builder.Services.ConfigureFlow(flow => flow
    .On<Reading>(async reading =>
    {
        // Apply custom business rules before Flow intake
        if (reading.Value < 0)
        {
            Console.WriteLine($"⚠️  Received negative reading: {reading.Value} from {reading.SensorKey}");
        }
        // The auto-registered handler will route to Flow intake automatically
    })
    .On<Device>(async device =>
    {
        Console.WriteLine($"🏭 New device registered: {device.DeviceId} ({device.Manufacturer} {device.Model})");
    })
    .On("seed", async (payload, ct) =>
    {
        Console.WriteLine($"🌱 Received seed command with payload: {payload}");
        // Handle seed commands from adapters
    })
);

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

// Map controllers and configure routing
app.MapControllers();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSoraSwagger();

app.Run();
