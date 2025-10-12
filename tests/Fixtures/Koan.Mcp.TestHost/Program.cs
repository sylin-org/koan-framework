using Koan.Core;
using Koan.Mcp;
using Koan.Mcp.Extensions;
using Koan.Mcp.Options;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan().AsProxiedApi();
builder.Services.AddKoanMcp(builder.Configuration);
builder.Services.AddKoanWeb();

// Force code mode full exposure for tests
builder.Services.Configure<McpServerOptions>(o =>
{
    o.Exposure = McpExposureMode.Full;
    o.EnableHttpSseTransport = true; // required for /mcp/rpc
});

var app = builder.Build();

app.MapKoanMcpEndpoints();
app.MapControllers();

app.Run();

namespace Koan.Mcp.TestHost
{
    public partial class Program { }
}