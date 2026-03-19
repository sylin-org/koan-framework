using Koan.Core.AI;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web;
using Koan.Web.Extensions;
using Koan.ZenGarden;
using Koan.ZenGarden.Extensions;
using S6.SnapVault.Configuration;
using S6.SnapVault.Services;
using S6.SnapVault.Services.AI;
using S6.SnapVault.Hubs;
using S6.SnapVault.Initialization;
using S6.SnapVault.Controllers;

[assembly: KoanApp(Name = "SnapVault", Code = "snap-vault", Description = "Photo management and AI analysis")]

var builder = WebApplication.CreateBuilder(args);

// Koan Framework - "Reference = Intent"
builder.Services
    .AddKoan()
    .AsWebApi();
builder.Services.AddKoanZenGarden(builder.Configuration);

// Configure entity lifecycle events (cascade deletes, etc.)
EntityLifecycleConfiguration.Configure();

// Configure application options from appsettings.json
builder.Services.Configure<CollectionOptions>(
    builder.Configuration.GetSection("SnapVault:Collections"));

// Register application services
builder.Services.AddScoped<IPhotoProcessingService, PhotoProcessingService>();
builder.Services.AddScoped<PhotoSetService>();
builder.Services.AddSingleton<IAnalysisPromptFactory, AnalysisPromptFactory>();

// Production monitoring and telemetry
builder.Services.AddSingleton<EmbeddingMonitoringService>();

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

// Log recommended models from orchestrator advisor (zero-config model selection)
var vision = ZenGarden.RecommendedModel(AiCapability.Vision);
var embedding = ZenGarden.RecommendedModel(AiCapability.Embed);
var chat = ZenGarden.RecommendedModel(AiCapability.Chat);
logger.LogInformation(
    "ZenGarden model advisor: vision={Vision}, embedding={Embedding}, chat={Chat}",
    vision ?? "(pending)", embedding ?? "(pending)", chat ?? "(pending)");

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
