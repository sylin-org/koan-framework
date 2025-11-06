// src/Koan.Context/Program.cs
// Console app that exposes Web API, Web UI, and MCP endpoints
// Pattern: follows g1c1.gardencoop with MCP integration

using Koan.Context.Services;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Mcp.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// ✅ ONE LINE AUTO-REGISTRATION
// Discovers: entities, controllers, MCP tools, vector adapters, orchestration evaluators
builder.Services.AddKoan();

// ✅ REGISTER FILE MONITORING & PROJECT RESOLUTION SERVICES
builder.Services.Configure<FileMonitoringOptions>(
    builder.Configuration.GetSection("Koan:Context:FileMonitoring"));
builder.Services.Configure<ProjectResolutionOptions>(
    builder.Configuration.GetSection("Koan:Context:ProjectResolution"));

builder.Services.AddSingleton<ProjectResolver>();
builder.Services.AddSingleton<IncrementalIndexingService>();
builder.Services.AddHostedService<JobRecoveryService>();
builder.Services.AddHostedService<FileMonitoringService>();
builder.Services.AddSingleton<FileMonitoringService>(sp =>
    (FileMonitoringService)sp.GetServices<IHostedService>()
        .First(s => s is FileMonitoringService));

// Build app
var app = builder.Build();

// Make services available globally (for static Entity<T> facades)
AppHost.Current ??= app.Services;

// ✅ MCP ENDPOINTS (HTTP+SSE transport for Claude Desktop / Cline)
app.MapKoanMcpEndpoints();  // Exposes /mcp/sse, /mcp/rpc, /mcp/health, /mcp/capabilities

// ✅ REST API (CRUD for projects)
app.MapControllers();  // Auto-discovers ProjectsController, etc.

// ✅ WEB UI (static HTML/JS client)
app.UseStaticFiles();  // Serves from wwwroot/
app.MapFallbackToFile("index.html");  // SPA routing

// Security: bind to localhost only by default
// Port allocation: 27500-27510 range to avoid conflicts
if (!app.Environment.IsDevelopment() && !app.Configuration.GetValue<bool>("AllowExternalAccess"))
{
    app.Urls.Clear();
    app.Urls.Add("http://localhost:27500");
}
else if (app.Urls.Count == 0)
{
    app.Urls.Add("http://localhost:27500");
}

// Startup message
app.Logger.LogInformation("Koan Context is starting on port 27500...");
app.Logger.LogInformation("MCP endpoints: http://localhost:27500/mcp/sse");
app.Logger.LogInformation("Web UI: http://localhost:27500");
app.Logger.LogInformation("API: http://localhost:27500/api/projects");
app.Logger.LogInformation("MCP Health: http://localhost:27500/mcp/health");

// ✅ WEAVIATE AUTO-PROVISIONING
// If Koan.Data.Vector.Connector.Weaviate is referenced:
// - WeaviateOrchestrationEvaluator auto-registers
// - Aspire detects vector dependency
// - Spins up Weaviate container on first vector operation
// - Endpoint: http://localhost:27501 (mapped from container's 8080)
// - Volume: koan-weaviate-data (persistent)
// - Configuration via appsettings.json or auto-detected

// Run app
await app.RunAsync();

/*
WHAT GETS AUTO-REGISTERED:

1. Entities:
   - Project (from Koan.Context/Models/Project.cs)
   - DocumentChunk (from Koan.Context/Models/DocumentChunk.cs)

2. Controllers:
   - ProjectsController (REST API for project CRUD)
   - SearchController (semantic search API)

3. MCP Tools:
   - context.resolve_library_id
   - context.get_library_docs
   - context.list_projects
   - context.project_status
   - context.reindex_project

4. Services:
   - IDocumentDiscoveryService
   - IChunkingService
   - IEmbeddingService
   - IIndexingService
   - IRetrievalService

5. Vector Adapter:
   - WeaviateVectorRepository<TEntity, TKey>
   - WeaviatePartitionMapper

6. Orchestration:
   - WeaviateOrchestrationEvaluator (auto-provisions Weaviate)

ALL DISCOVERED VIA "REFERENCE = INTENT" PATTERN
No manual registration needed!
*/
