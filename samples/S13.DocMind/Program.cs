using Koan.AI.Web;
using Koan.Core.Observability;
using Koan.Data.Core;
using Koan.Data.Mongo;
using Koan.Data.Vector;
using Koan.Mcp.Extensions;
using Koan.Web.Extensions;
using S13.DocMind.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan()
    .AsWebApi()
    .AsProxiedApi()
    .WithRateLimit();

// Register DocMind-specific processing pipeline
builder.Services.AddDocMindProcessing(builder.Configuration);

// Note: Service implementations are handled by Koan auto-registration
builder.Services.AddSingleton<IDocumentAggregationService, DocumentAggregationService>();
builder.Services.AddSingleton<IDocumentInsightsService, DocumentInsightsService>();
builder.Services.AddSingleton<IDocumentProcessingDiagnostics, DocumentProcessingDiagnostics>();
builder.Services.AddSingleton<IModelCatalogService, InMemoryModelCatalogService>();
builder.Services.AddSingleton<IModelInstallationQueue, InMemoryModelInstallationQueue>();
builder.Services.AddHostedService<ModelInstallationBackgroundService>();

// Ensure required directories exist
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "uploads"));
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "data"));

var app = builder.Build();

app.UseKoanSwagger();

app.Run();