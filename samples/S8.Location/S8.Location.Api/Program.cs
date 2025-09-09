using S8.Location.Core.Models;
using S8.Location.Core.Interceptors;
using S8.Location.Core.Orchestration;
using S8.Location.Core.Services;
using Sora.Data.Core;
using Sora.Flow;
using Sora.Web.Swagger;

var builder = WebApplication.CreateBuilder(args);

// Sora framework with auto-configuration
builder.Services.AddSora();

// Enable Flow pipeline for address processing
builder.Services.AddSoraFlow();

// Register Location orchestrator with Flow.OnUpdate handlers
builder.Services.AddHostedService<LocationOrchestrator>();

// Register Location services
builder.Services.AddSingleton<IAddressResolutionService, AddressResolutionService>();
builder.Services.AddHostedService<BackgroundResolutionService>();

// LocationInterceptor is auto-registered via ISoraAutoRegistrar pattern

// Container environment requirement
if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Location.Api requires container environment. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Services.AddControllers();
builder.Services.AddRouting();
builder.Services.AddSoraSwagger(builder.Configuration);

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
            // Status removed - Flow pipeline tracks entity state
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

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.MapControllers();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSoraSwagger();

app.Run();