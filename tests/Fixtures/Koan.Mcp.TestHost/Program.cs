using Koan.Core;
using Koan.Mcp;
using Koan.Mcp.Options;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan().AsProxiedApi();
builder.Services.AddKoanWeb();

// Force code mode full exposure for tests
builder.Services.Configure<McpServerOptions>(o =>
{
    o.Exposure = McpExposureMode.Full;
    o.EnableLegacySseTransport = true; // required for /mcp/rpc
});

var app = builder.Build();

app.MapControllers();

await app.RunAsync();

namespace Koan.Mcp.TestHost
{
    public partial class Program { }
}
