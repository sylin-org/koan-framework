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
    .AsProxiedApi();

builder.Services.AddKoanObservability();
builder.Services.AddMongoAdapter();
builder.Services.AddKoanDataVector();
builder.Services.AddKoanAiWeb();
builder.Services.AddKoanMcp(builder.Configuration);

// DocMind domain services continue to register explicitly until refactored
builder.Services.AddSingleton<IDocumentAggregationService, DocumentAggregationService>();
builder.Services.AddSingleton<IDocumentInsightsService, DocumentInsightsService>();
builder.Services.AddSingleton<IDocumentProcessingDiagnostics, DocumentProcessingDiagnostics>();
builder.Services.AddSingleton<IModelCatalogService, InMemoryModelCatalogService>();
builder.Services.AddSingleton<IModelInstallationQueue, InMemoryModelInstallationQueue>();
builder.Services.AddHostedService<ModelInstallationBackgroundService>();

Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "uploads"));
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "data"));

var app = builder.Build();

app.UseKoanSwagger();

app.Run();
