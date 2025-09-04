using S8.Flow.Api.Entities;
using Sora.Data.Core;
using Sora.Flow;
using Sora.Flow.Configuration;
using Sora.Flow.Options;
using S8.Flow.Shared;
using Sora.Messaging;
using S8.Flow.Api.Adapters;
using Sora.Web.Swagger;
using Sora.Flow.Sending;

var builder = WebApplication.CreateBuilder(args);

// ✨ NEW BEAUTIFUL MESSAGING - ZERO CONFIGURATION! ✨
builder.Services.AddSora();

// ✨ BEAUTIFUL AUTO-CONFIGURED FLOW HANDLERS ✨
// Automatically registers handlers for all FlowEntity and FlowValueObject types!
// No more boilerplate - each handler logs appropriately and routes to Flow intake.
//
// BEFORE (26 lines of repetitive boilerplate):
// builder.Services.ConfigureFlow(flow =>
// {
//     flow.On<Reading>(async reading =>
//     {
//         Console.WriteLine($"📊 Received Reading: {reading.SensorKey} = {reading.Value}{reading.Unit}");
//         await reading.SendToFlowIntake();
//     });
//     flow.On<Device>(async device =>
//     {
//         Console.WriteLine($"🏭 Device registered: {device.DeviceId} ({device.Manufacturer} {device.Model})");
//         await device.SendToFlowIntake();
//     });
//     flow.On<Sensor>(async sensor =>
//     {
//         Console.WriteLine($"📡 Sensor registered: {sensor.SensorKey} ({sensor.Code}) - Unit: {sensor.Unit}");
//         await sensor.SendToFlowIntake();
//     });
// });
//
// AFTER (1 line with automatic discovery and guaranteed consistency):
builder.Services.AutoConfigureFlow(typeof(Device).Assembly);

// Keep FlowCommandMessage as direct handler (not wrapped in FlowTargetedMessage)
builder.Services.On<FlowCommandMessage>(async cmd =>
{
    Console.WriteLine($"🌱 Received command: {cmd.Command} with payload: {cmd.Payload}");
});

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

// That's it! No complex Flow orchestrator setup needed.
// Messages sent via .Send() will be automatically routed to handlers above.
// Handlers will then route to Flow intake for processing.

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
