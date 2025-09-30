using S8.Canon.Api.Entities;
using Koan.Data.Core;
using Koan.Canon.Initialization;
using Koan.Canon.Options;
using S8.Canon.Shared;
using Koan.Messaging;
using S8.Canon.Api.Adapters;
using Koan.Web.Connector.Swagger;
using Koan.Canon.Attributes;
using Koan.Canon.Core.Orchestration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Koan.Core.Logging;

var builder = WebApplication.CreateBuilder(args);

// Configure centralized Koan logging - replace ALL logging
builder.Logging.ClearProviders()
    .AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Error) // Hide port override noise  
    .AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Error) // Hide startup noise
    .AddFilter("Microsoft", LogLevel.Warning) // Reduce other Microsoft noise
    .AddFilter("System", LogLevel.Warning)    // Reduce System noise
    .AddFilter("Koan", LogLevel.Debug)        // Allow all Koan debug logs
    .SetMinimumLevel(LogLevel.Information)    // Default to Info level
    .AddKoanFormatter();

// Koan framework with auto-configuration
builder.Services.AddKoan();

// Flow interceptors and orchestrator are registered automatically via AddKoanCanon()
// All Flow entities now route through unified CanonOrchestrator - no separate transport handler needed

// Container environment requirement
if (!Koan.Core.KoanEnv.InContainer)
{
    Console.Error.WriteLine("S8.Canon.Api requires container environment. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Services.Configure<CanonOptions>(o =>
{
    // Default tags act as fallback when model has no AggregationKey attributes.
    // Our Sensor model carries [AggregationKey] on SensorId, so this isn't required,
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
builder.Services.AddKoanSwagger(builder.Configuration);

// That's it! No complex Flow orchestrator setup needed.
// Messages sent via .Send() will be automatically routed to handlers above.
// Handlers will then route to Flow intake for processing.

// Health snapshot based on recent Keyed stage records
builder.Services.AddSingleton<IAdapterHealthRegistry, S8.Canon.Api.Adapters.KeyedAdapterHealthRegistry>();


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
        logger?.LogKoanInit("Data provider test: AppSetting saved successfully");
    }
    catch (Exception ex)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetService<ILogger<Program>>();
        logger?.LogInformation("[Koan:init] Data provider test failed: {Error}", ex.Message);
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
app.UseKoanSwagger();

app.Run();

/// <summary>
/// S8.Canon API orchestrator that processes Flow entity messages from adapters.
/// This marks the API as the central orchestrator service that should run Flow background workers.
/// </summary>
[CanonOrchestrator]
public class S8CanonOrchestrator : CanonOrchestratorBase
{
    public S8CanonOrchestrator(ILogger<S8CanonOrchestrator> logger, IConfiguration configuration, IServiceProvider serviceProvider)
        : base(logger, configuration, serviceProvider)
    {
    }

    // Inherits all processing logic from CanonOrchestratorBase
    // Can override methods here for custom S8.Canon-specific processing if needed
}


