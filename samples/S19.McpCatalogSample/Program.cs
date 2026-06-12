using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Mcp;
using Koan.Mcp.Extensions;
using Koan.Mcp.Options;
using Koan.Web.Extensions;
using Koan.Data.Core;
using S19.McpCatalogSample;

var builder = WebApplication.CreateBuilder(args);

// Core + minimal web bits for hosting MCP
builder.Services.AddKoan().AsProxiedApi();
builder.Services.AddKoanWeb();
builder.Services.AddKoanMcp();

// Force HttpSse transport for web interaction and expose full set of tools
builder.Services.Configure<McpServerOptions>(o =>
{
    o.Exposure = McpExposureMode.Full;
    o.EnableHttpSseTransport = true;
    o.HttpSseRoute = "/mcp";
});

var app = builder.Build();

AppHost.Current = app.Services;

// Seed initial catalog data in-memory at startup
using (var scope = app.Services.CreateScope())
{
    var p1 = new CatalogItem
    {
        Id = "p1",
        Name = "Premium Wireless Headphones",
        Description = "Noise-cancelling over-ear headphones with 40h battery life.",
        Price = 299.99m
    };
    await p1.Save();

    var p2 = new CatalogItem
    {
        Id = "p2",
        Name = "Ergonomic Mechanical Keyboard",
        Description = "Hot-swappable tactile switches with RGB backlighting.",
        Price = 149.50m
    };
    await p2.Save();

    var p3 = new CatalogItem
    {
        Id = "p3",
        Name = "Ultra-wide Productivity Monitor",
        Description = "34-inch curved IPS display with USB-C power delivery.",
        Price = 499.00m
    };
    await p3.Save();
}

app.MapGet("/", () => Results.Text("<h1>Koan MCP Catalog Sample</h1><p>MCP endpoint: <a href=\"/mcp\">/mcp</a></p>", "text/html"));
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "mcp-catalog-sample" }));

// Map MCP HTTP/SSE endpoints based on configured route
app.MapKoanMcpEndpoints();

await app.RunAsync();

namespace S19.McpCatalogSample
{
    public partial class Program { }
}
