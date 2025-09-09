using S8.Location.Core.Models;
using S8.Location.Core.Interceptors;
using S8.Location.Core.Services;
using Sora.Data.Core;
using Sora.Web.Swagger;

var builder = WebApplication.CreateBuilder(args);

// Sora framework with auto-configuration
builder.Services.AddSora();

// Register Location services
builder.Services.AddSingleton<IAddressResolutionService, AddressResolutionService>();

// Register Location interceptor
LocationInterceptor.Register();

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
            Id = "test", 
            Address = "123 Test Street, Test City, TS 12345",
            Status = LocationStatus.Pending
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