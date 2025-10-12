using Koan.Canon;
using Koan.Core;
using Koan.Data.Core;
using Koan.Web.Connector.Swagger;
using Koan.Web.Extensions;
using S8.Location.Core.Interceptors;
using S8.Location.Core.Models;
using S8.Location.Core.Orchestration;
using S8.Location.Core.Services;

var builder = WebApplication.CreateBuilder(args);

// Koan framework with auto-configuration
builder.Services.AddKoan();

// Enable Canon pipeline for address processing
builder.Services.AddKoanCanon();

// Register Location orchestrator with Canon.OnUpdate handlers
builder.Services.AddHostedService<LocationOrchestrator>();

// Register Location services
builder.Services.AddSingleton<IAddressResolutionService, AddressResolutionService>();
builder.Services.AddHostedService<BackgroundResolutionService>();

// LocationInterceptor is auto-registered via IKoanAutoRegistrar pattern

// Container environment requirement
if (!Koan.Core.KoanEnv.InContainer)
{
    Console.Error.WriteLine("S8.Location.Api requires container environment. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Services.AddKoanSwagger(builder.Configuration);

var app = builder.Build();

// Test data provider functionality on startup
app.Lifetime.ApplicationStarted.Register(async () =>
{
    try
    {
        var testLocation = new Location
        {
            Id = "startup-test-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            Address = "123 Main Street, Springfield, IL 62701"
            // Status removed - Canon pipeline tracks entity state
        };
        await testLocation.Save();

        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetService<ILogger<Program>>();
        logger?.LogInformation("[API] Data provider test: Location saved successfully");
    }
    catch (Exception ex)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetService<ILogger<Program>>();
        logger?.LogError(ex, "[API] Data provider test failed");
    }
});

app.UseKoanSwagger();

app.Run();
