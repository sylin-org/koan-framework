using Koan.Mcp.Extensions;
using Koan.Web.Extensions;
using Koan.Data.Core; // for AddKoan()
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using S16.PantryPal; // reference for entity assemblies

var builder = WebApplication.CreateBuilder(args);

// Core + minimal web bits for hosting MCP over HTTP SSE
builder.Services.AddKoan();
builder.Services.AddKoanWeb();
builder.Services.AddKoanMcp(); // binds Koan:Mcp config

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "mcp-host" }));
// Map MCP HTTP/SSE endpoints based on configured route (Koan__Mcp__HttpSseRoute)
app.MapKoanMcpEndpoints();

app.Run();
