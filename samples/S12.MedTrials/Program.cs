using System.IO;
using Koan.AI.Web;
using Koan.Core.Observability;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Web.Extensions;
using Koan.Web.Swagger;
using S12.MedTrials.Infrastructure;
using S12.MedTrials.Infrastructure.Mcp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan()
    .AsWebApi()
    .AsProxiedApi()
    .WithRateLimit();

builder.Services.AddKoanObservability();
builder.Services.AddKoanDataVector();
builder.Services.AddKoanAiWeb();

builder.Services.AddMedTrialsCore();

builder.Services.AddOptions<McpBridgeOptions>()
    .Bind(builder.Configuration.GetSection(McpBridgeOptions.SectionName))
    .Validate(options => !options.Enabled || options.TryGetBaseUri() is not null,
        "S12 MedTrials MCP bridge requires a valid BaseUrl when enabled.")
    .ValidateOnStart();

builder.Services.AddHttpClient(McpHttpClientNames.McpBridge);

builder.Services.AddHostedService<MedTrialsSeedWorker>();
builder.Services.AddHostedService<McpCapabilityProbe>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    var dataPath = Path.Combine(app.Environment.ContentRootPath, "data");
    try { Directory.CreateDirectory(dataPath); } catch { /* best effort */ }
}

app.Run();

namespace S12.MedTrials
{
    public partial class Program { }
}
