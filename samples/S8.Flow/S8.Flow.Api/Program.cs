using S8.Flow.Api.Entities;
using Sora.Data.Core;
using Sora.Flow.Initialization;
using Sora.Flow.Options;
using S8.Flow.Shared;
using Sora.Messaging;
using S8.Flow.Api.Adapters;
using Sora.Web.Swagger;
using Sora.Flow.Attributes;
using Sora.Flow.Core.Orchestration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Sora framework with auto-configuration
builder.Services.AddSora();

// Flow interceptors and orchestrator are registered automatically via AddSoraFlow()
// All Flow entities now route through unified FlowOrchestrator - no separate transport handler needed

// Container environment requirement
if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Api requires container environment. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Services.Configure<FlowOptions>(o =>
{
    // Default tags act as fallback when model has no AggregationKey attributes.
    // Our Sensor model carries [AggregationKey] on SensorKey, so this isn't required,
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

// Test data provider functionality on startup
app.Lifetime.ApplicationStarted.Register(async () =>
{
    try
    {
        var setting = new AppSetting { Id = "test", Value = $"Saved at {DateTime.UtcNow:O}" };
        await setting.Save();
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetService<ILogger<Program>>();
        logger?.LogInformation("[API] Data provider test: AppSetting saved successfully");
    }
    catch (Exception ex)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetService<ILogger<Program>>();
        logger?.LogError(ex, "[API] Data provider test failed");
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

/// <summary>
/// S8.Flow API orchestrator that processes Flow entity messages from adapters.
/// This marks the API as the central orchestrator service that should run Flow background workers.
/// </summary>
[FlowOrchestrator]
public class S8FlowOrchestrator : FlowOrchestratorBase
{
    public S8FlowOrchestrator(ILogger<S8FlowOrchestrator> logger, IConfiguration configuration, IServiceProvider serviceProvider)
        : base(logger, configuration, serviceProvider)
    {
    }

    // Inherits all processing logic from FlowOrchestratorBase
    // Can override methods here for custom S8.Flow-specific processing if needed
}
