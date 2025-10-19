using Koan.Core;
using Koan.Web;
using Koan.Web.Extensions;
using S6.SnapVault.Configuration;
using S6.SnapVault.Services;
using S6.SnapVault.Services.AI;
using S6.SnapVault.Hubs;
using S6.SnapVault.Initialization;
using S6.SnapVault.Controllers;

var builder = WebApplication.CreateBuilder(args);

// Koan Framework - "Reference = Intent"
builder.Services
    .AddKoan()
    .AsWebApi();

// Configure entity lifecycle events (cascade deletes, etc.)
EntityLifecycleConfiguration.Configure();

// Configure application options from appsettings.json
builder.Services.Configure<CollectionOptions>(
    builder.Configuration.GetSection("SnapVault:Collections"));

// Register application services
builder.Services.AddScoped<IPhotoProcessingService, PhotoProcessingService>();
builder.Services.AddSingleton<IAnalysisPromptFactory, AnalysisPromptFactory>();

// Register background processing queue and worker
builder.Services.AddSingleton<IPhotoProcessingQueue, InMemoryPhotoProcessingQueue>();
builder.Services.AddHostedService<PhotoProcessingWorker>();

// SignalR for real-time progress updates
builder.Services.AddSignalR();

// CORS for development (allow credentials for SignalR)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Set AppHost.Current to enable entity operations before app.Run()
Koan.Core.Hosting.App.AppHost.Current ??= app.Services;

// Seed default analysis styles (S5.Recs pattern)
var logger = app.Services.GetRequiredService<ILogger<Program>>();
await AnalysisStyleSeeder.SeedDefaultStylesAsync(logger);

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    // TODO: Add Swagger package if needed
    // app.UseSwagger();
    // app.UseSwaggerUI(c =>
    // {
    //     c.SwaggerEndpoint("/swagger/v1/swagger.json", "SnapVault API v1");
    //     c.RoutePrefix = "swagger";
    // });
}

app.UseCors();
app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapFallbackToFile("index.html");

// Map SignalR hubs
app.MapHub<PhotoProcessingHub>("/hubs/processing");

app.Run();
