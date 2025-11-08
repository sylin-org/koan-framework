// src/Koan.Context/Program.cs
// Console app that exposes Web API, Web UI, and MCP endpoints
// Pattern: follows g1c1.gardencoop with MCP integration

using AspNetCoreRateLimit;
using Koan.Context.Middleware;
using Koan.Context.Services;
using Koan.Context.Utilities;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Mcp.Extensions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.ConfigureSampleLogging();

// ✅ ONE LINE AUTO-REGISTRATION
// Discovers: entities, controllers, MCP tools, vector adapters, orchestration evaluators
builder.Services.AddKoan();

// ✅ SECURITY: Path validation
builder.Services.AddSingleton<PathValidator>();

// ✅ SECURITY: Rate limiting
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// ✅ REGISTER FILE MONITORING & PROJECT RESOLUTION SERVICES
builder.Services.Configure<FileMonitoringOptions>(
    builder.Configuration.GetSection("Koan:Context:FileMonitoring"));
builder.Services.Configure<ProjectResolutionOptions>(
    builder.Configuration.GetSection("Koan:Context:ProjectResolution"));

builder.Services.AddSingleton<ProjectResolver>();
builder.Services.AddSingleton<TokenCounter, TokenCounter>();
builder.Services.AddSingleton<IncrementalIndexer>();
builder.Services.AddSingleton<IndexingCoordinator>();
builder.Services.AddSingleton<Metrics>();
builder.Services.AddHostedService<FileMonitoringService>();
builder.Services.AddSingleton<FileMonitoringService>(sp =>
    (FileMonitoringService)sp.GetServices<IHostedService>()
        .First(s => s is FileMonitoringService));

// Build app
var app = builder.Build();

// Make services available globally (for static Entity<T> facades)
AppHost.Current ??= app.Services;

// ✅ SECURITY: Global exception handler (must be first in pipeline)
app.UseGlobalExceptionHandler();

// ✅ SECURITY: Rate limiting middleware (must be early in pipeline)
app.UseIpRateLimiting();

// ✅ SECURITY: Security headers (CSP, X-Frame-Options, HSTS, etc.)
app.UseMiddleware<SecurityHeadersMiddleware>();

// ✅ MCP ENDPOINTS (HTTP+SSE transport for Claude Desktop / Cline)
app.MapKoanMcpEndpoints();  // Exposes /mcp/sse, /mcp/rpc, /mcp/health, /mcp/capabilities

// ✅ REST API (CRUD for projects)
app.MapControllers();  // Auto-discovers ProjectsController, etc.

// ✅ WEB UI (static HTML/JS client)
app.UseStaticFiles();  // Serves from wwwroot/
app.MapFallbackToFile("dashboard.html");  // SPA routing - new dashboard

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

// ✅ VECTOR STORE AUTO-PROVISIONING
// If a vector connector is referenced (e.g., Koan.Data.Vector.Connector.Weaviate):
// - OrchestrationEvaluator auto-registers
// - Aspire detects vector dependency
// - Spins up vector container on first vector operation
// - Endpoint: http://localhost:27501 (mapped from container's default port)
// - Volume: koan-vector-data (persistent)
// - Configuration via appsettings.json or auto-detected

// Configure sample lifecycle with browser launch
app.ConfigureSampleLifecycle(
    sampleName: "Koan Context",
    startupMessage: "Koan Context is listening on {Addresses}. MCP: http://localhost:27500/mcp/sse | API: http://localhost:27500/api/projects",
    shutdownMessage: "Koan Context shutting down.",
    launchBrowser: true);

// Run app
await app.RunAsync();

/*
WHAT GETS AUTO-REGISTERED:

1. Entities:
   - Project (from Koan.Context/Models/Project.cs)
   - Chunk (from Koan.Context/Models/Chunk.cs)

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
   - Chunker
   - Embedding
   - Indexer
   - Search

5. Vector Adapter:
   - VectorRepository<TEntity, TKey> (provider-specific implementation)
   - PartitionMapper (provider-specific implementation)

6. Orchestration:
   - OrchestrationEvaluator (auto-provisions vector store)

ALL DISCOVERED VIA "REFERENCE = INTENT" PATTERN
No manual registration needed!
*/
